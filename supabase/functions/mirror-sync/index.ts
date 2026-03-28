import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import {
  createAdminClient,
  ensureText,
  handleCors,
  json,
  logMirrorSync,
  readJson,
  resolveClientByMachineId,
  sanitizeText,
  upsertMirrorState,
} from "../_shared/common.ts";

type MirrorRequest = {
  machine_id: string;
  app_version?: string;
  schema_version?: string;
  operation:
    | "full_resync"
    | "full_resync_start"
    | "full_resync_employers_batch"
    | "full_resync_employees_batch"
    | "full_resync_finish"
    | "employer_upsert"
    | "employer_delete"
    | "employee_upsert"
    | "employee_delete";
  payload?: Record<string, unknown>;
};

serve(async (req) => {
  const cors = handleCors(req);
  if (cors) {
    return cors;
  }

  if (req.method !== "POST") {
    return json({ ok: false, error: "method_not_allowed" }, 405);
  }

  let clientId = "";
  let machineId = "";
  let operation = "";
  const schemaVersion = "1";

  try {
    const body = await readJson<MirrorRequest>(req);
    machineId = body.machine_id ?? "";
    operation = body.operation ?? "";
    if (!machineId || !operation) {
      return json({ ok: false, error: "machine_id_and_operation_required" }, 400);
    }

    const admin = createAdminClient();
    const client = await resolveClientByMachineId(admin, machineId, "", body.app_version ?? "", "", false);
    if (!client) {
      return json({ ok: false, error: "client_not_found" }, 404);
    }

    clientId = client.id;
    if (client.is_blocked) {
      return json({ ok: false, error: "client_blocked" }, 403);
    }

    switch (body.operation) {
      case "full_resync":
        await handleFullResync(admin, clientId, body.payload ?? {});
        break;
      case "full_resync_start":
        await handleFullResyncStart(admin, clientId);
        break;
      case "full_resync_employers_batch":
        await handleFullResyncEmployersBatch(admin, clientId, body.payload ?? {});
        break;
      case "full_resync_employees_batch":
        await handleFullResyncEmployeesBatch(admin, clientId, body.payload ?? {});
        break;
      case "full_resync_finish":
        await handleFullResyncFinish();
        break;
      case "employer_upsert":
        await handleEmployerUpsert(admin, clientId, body.payload ?? {});
        break;
      case "employer_delete":
        await handleEmployerDelete(admin, clientId, body.payload ?? {});
        break;
      case "employee_upsert":
        await handleEmployeeUpsert(admin, clientId, body.payload ?? {});
        break;
      case "employee_delete":
        await handleEmployeeDelete(admin, clientId, body.payload ?? {});
        break;
      default:
        return json({ ok: false, error: "unknown_operation" }, 400);
    }

    await upsertMirrorState(
      admin,
      clientId,
      body.schema_version ?? schemaVersion,
      body.operation === "full_resync" || body.operation === "full_resync_finish",
    );
    await logMirrorSync(admin, clientId, machineId, body.operation, true, null);
    return json({ ok: true });
  } catch (error) {
    if (clientId && operation) {
      const admin = createAdminClient();
      await logMirrorSync(
        admin,
        clientId,
        machineId,
        operation,
        false,
        error instanceof Error ? error.message : "mirror_sync_failed",
      );
      await upsertMirrorState(admin, clientId, schemaVersion, false, error instanceof Error ? error.message : "mirror_sync_failed");
    }

    return json({
      ok: false,
      error: error instanceof Error ? error.message : "mirror_sync_failed",
    }, 500);
  }
});

async function handleFullResync(admin: ReturnType<typeof createAdminClient>, clientId: string, payload: Record<string, unknown>) {
  await handleFullResyncStart(admin, clientId);

  const employers = Array.isArray(payload.employers) ? payload.employers : [];
  const employees = Array.isArray(payload.employees) ? payload.employees : [];

  await handleFullResyncEmployersBatch(admin, clientId, { employers });
  await handleFullResyncEmployeesBatch(admin, clientId, { employees });
  await handleFullResyncFinish();
}

async function handleFullResyncStart(admin: ReturnType<typeof createAdminClient>, clientId: string) {
  const now = new Date().toISOString();
  await admin.from("admin_agencies").update({ is_deleted: true, deleted_at: now }).eq("client_id", clientId);
  await admin.from("admin_employers").update({ is_deleted: true, deleted_at: now }).eq("client_id", clientId);
  await admin.from("admin_employees").update({ is_deleted: true, deleted_at: now }).eq("client_id", clientId);
  await admin.from("admin_employer_addresses").delete().eq("client_id", clientId);
  await admin.from("admin_employer_positions").delete().eq("client_id", clientId);
  await admin.from("admin_employee_firm_history").delete().eq("client_id", clientId);
}

async function handleFullResyncEmployersBatch(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  payload: Record<string, unknown>,
) {
  const employers = Array.isArray(payload.employers) ? payload.employers : [];
  for (const employer of employers) {
    await handleEmployerUpsert(admin, clientId, employer as Record<string, unknown>);
  }
}

async function handleFullResyncEmployeesBatch(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  payload: Record<string, unknown>,
) {
  const employees = Array.isArray(payload.employees) ? payload.employees : [];
  for (const employee of employees) {
    await handleEmployeeUpsert(admin, clientId, employee as Record<string, unknown>);
  }
}

async function handleFullResyncFinish() {
  await Promise.resolve();
}

async function handleEmployerUpsert(admin: ReturnType<typeof createAdminClient>, clientId: string, payload: Record<string, unknown>) {
  const agency = (payload.agency ?? {}) as Record<string, unknown>;
  const employer = (payload.employer ?? {}) as Record<string, unknown>;
  const agencyId = ensureText(agency.agency_id, "agency_id", 120, true);
  const employerId = ensureText(employer.employer_id, "employer_id", 120, true);
  const now = new Date().toISOString();

  const { error: agencyError } = await admin.from("admin_agencies").upsert({
    client_id: clientId,
    agency_id: agencyId,
    name: ensureText(agency.name, "agency.name", 500),
    ico: ensureText(agency.ico, "agency.ico", 50),
    full_address: ensureText(agency.full_address, "agency.full_address", 2000),
    source_updated_at: agency.source_updated_at ?? null,
    last_synced_at: now,
    is_deleted: false,
    deleted_at: null,
  }, { onConflict: "client_id,agency_id" });
  if (agencyError) throw agencyError;

  const { error: employerError } = await admin.from("admin_employers").upsert({
    client_id: clientId,
    employer_id: employerId,
    agency_id: agencyId,
    name: ensureText(employer.name, "employer.name", 500),
    ico: ensureText(employer.ico, "employer.ico", 50),
    legal_address: ensureText(employer.legal_address, "employer.legal_address", 2000),
    weekly_work_hours: Number(employer.weekly_work_hours ?? 0),
    daily_work_hours: Number(employer.daily_work_hours ?? 0),
    shift_count: Number(employer.shift_count ?? 0),
    hidden_from_year: Number(employer.hidden_from_year ?? 0),
    hidden_from_month: Number(employer.hidden_from_month ?? 0),
    created_at: employer.created_at ?? null,
    source_updated_at: employer.source_updated_at ?? null,
    last_synced_at: now,
    is_deleted: false,
    deleted_at: null,
  }, { onConflict: "client_id,employer_id" });
  if (employerError) throw employerError;

  await admin.from("admin_employer_addresses").delete().eq("client_id", clientId).eq("employer_id", employerId);
  const addresses = Array.isArray(employer.addresses) ? employer.addresses : [];
  if (addresses.length > 0) {
    const { error } = await admin.from("admin_employer_addresses").insert(addresses.map((item, index) => {
      const address = item as Record<string, unknown>;
      return {
        client_id: clientId,
        employer_id: employerId,
        sort_order: index,
        street: ensureText(address.street, "address.street", 500),
        number: ensureText(address.number, "address.number", 120),
        city: ensureText(address.city, "address.city", 240),
        zip_code: ensureText(address.zip_code, "address.zip_code", 50),
        last_synced_at: now,
      };
    }));
    if (error) throw error;
  }

  await admin.from("admin_employer_positions").delete().eq("client_id", clientId).eq("employer_id", employerId);
  const positions = Array.isArray(employer.positions) ? employer.positions : [];
  if (positions.length > 0) {
    const { error } = await admin.from("admin_employer_positions").insert(positions.map((item, index) => {
      const position = item as Record<string, unknown>;
      return {
        client_id: clientId,
        employer_id: employerId,
        sort_order: index,
        title: ensureText(position.title, "position.title", 500),
        position_number: ensureText(position.position_number, "position.position_number", 120),
        monthly_salary_brutto: Number(position.monthly_salary_brutto ?? 0),
        hourly_salary: Number(position.hourly_salary ?? 0),
        last_synced_at: now,
      };
    }));
    if (error) throw error;
  }
}

async function handleEmployerDelete(admin: ReturnType<typeof createAdminClient>, clientId: string, payload: Record<string, unknown>) {
  const employerId = ensureText(payload.employer_id, "employer_id", 120, true);
  const agencyId = ensureText(payload.agency_id, "agency_id", 120);
  const shouldKeepAgency = !!payload.agency_still_referenced;
  const now = new Date().toISOString();

  await admin.from("admin_employers").update({
    is_deleted: true,
    deleted_at: now,
    last_synced_at: now,
  }).eq("client_id", clientId).eq("employer_id", employerId);
  await admin.from("admin_employer_addresses").delete().eq("client_id", clientId).eq("employer_id", employerId);
  await admin.from("admin_employer_positions").delete().eq("client_id", clientId).eq("employer_id", employerId);

  if (agencyId && !shouldKeepAgency) {
    await admin.from("admin_agencies").update({
      is_deleted: true,
      deleted_at: now,
      last_synced_at: now,
    }).eq("client_id", clientId).eq("agency_id", agencyId);
  }
}

async function handleEmployeeUpsert(admin: ReturnType<typeof createAdminClient>, clientId: string, payload: Record<string, unknown>) {
  const employee = (payload.employee ?? {}) as Record<string, unknown>;
  const employeeId = ensureText(employee.employee_id, "employee_id", 120, true);
  const now = new Date().toISOString();

  const { error } = await admin.from("admin_employees").upsert({
    client_id: clientId,
    employee_id: employeeId,
    employer_id: employee.employer_id ?? null,
    full_name: ensureText(employee.full_name, "employee.full_name", 500),
    first_name: ensureText(employee.first_name, "employee.first_name", 240),
    last_name: ensureText(employee.last_name, "employee.last_name", 240),
    birth_date: ensureText(employee.birth_date, "employee.birth_date", 120),
    employee_type: ensureText(employee.employee_type, "employee.employee_type", 120),
    eu_document_type: ensureText(employee.eu_document_type, "employee.eu_document_type", 120),
    visa_doc_type: ensureText(employee.visa_doc_type, "employee.visa_doc_type", 120),
    gender: ensureText(employee.gender, "employee.gender", 120),
    passport_number: ensureText(employee.passport_number, "employee.passport_number", 240),
    passport_city: ensureText(employee.passport_city, "employee.passport_city", 240),
    passport_country: ensureText(employee.passport_country, "employee.passport_country", 240),
    passport_expiry: ensureText(employee.passport_expiry, "employee.passport_expiry", 120),
    visa_number: ensureText(employee.visa_number, "employee.visa_number", 240),
    visa_type: ensureText(employee.visa_type, "employee.visa_type", 240),
    visa_expiry: ensureText(employee.visa_expiry, "employee.visa_expiry", 120),
    insurance_company_short: ensureText(employee.insurance_company_short, "employee.insurance_company_short", 240),
    insurance_number: ensureText(employee.insurance_number, "employee.insurance_number", 240),
    insurance_expiry: ensureText(employee.insurance_expiry, "employee.insurance_expiry", 120),
    work_permit_name: ensureText(employee.work_permit_name, "employee.work_permit_name", 240),
    work_permit_number: ensureText(employee.work_permit_number, "employee.work_permit_number", 240),
    work_permit_type: ensureText(employee.work_permit_type, "employee.work_permit_type", 240),
    work_permit_issue_date: ensureText(employee.work_permit_issue_date, "employee.work_permit_issue_date", 120),
    work_permit_expiry: ensureText(employee.work_permit_expiry, "employee.work_permit_expiry", 120),
    work_permit_authority: ensureText(employee.work_permit_authority, "employee.work_permit_authority", 500),
    address_local_street: ensureText(employee.address_local_street, "employee.address_local_street", 500),
    address_local_number: ensureText(employee.address_local_number, "employee.address_local_number", 120),
    address_local_city: ensureText(employee.address_local_city, "employee.address_local_city", 240),
    address_local_zip: ensureText(employee.address_local_zip, "employee.address_local_zip", 120),
    address_abroad_street: ensureText(employee.address_abroad_street, "employee.address_abroad_street", 500),
    address_abroad_number: ensureText(employee.address_abroad_number, "employee.address_abroad_number", 120),
    address_abroad_city: ensureText(employee.address_abroad_city, "employee.address_abroad_city", 240),
    address_abroad_zip: ensureText(employee.address_abroad_zip, "employee.address_abroad_zip", 120),
    work_address_tag: ensureText(employee.work_address_tag, "employee.work_address_tag", 240),
    position_tag: ensureText(employee.position_tag, "employee.position_tag", 240),
    position_number: ensureText(employee.position_number, "employee.position_number", 120),
    monthly_salary_brutto: Number(employee.monthly_salary_brutto ?? 0),
    hourly_salary: Number(employee.hourly_salary ?? 0),
    contract_type: ensureText(employee.contract_type, "employee.contract_type", 120),
    phone: ensureText(employee.phone, "employee.phone", 240),
    email: ensureText(employee.email, "employee.email", 240),
    department: ensureText(employee.department, "employee.department", 240),
    status: ensureText(employee.status, "employee.status", 120),
    start_date: ensureText(employee.start_date, "employee.start_date", 120),
    contract_sign_date: ensureText(employee.contract_sign_date, "employee.contract_sign_date", 120),
    end_date: ensureText(employee.end_date, "employee.end_date", 120),
    is_archived: !!employee.is_archived,
    archived_from_firm: ensureText(employee.archived_from_firm, "employee.archived_from_firm", 500),
    source_updated_at: employee.source_updated_at ?? null,
    last_synced_at: now,
    is_deleted: false,
    deleted_at: null,
  }, { onConflict: "client_id,employee_id" });
  if (error) throw error;

  await admin.from("admin_employee_firm_history").delete().eq("client_id", clientId).eq("employee_id", employeeId);
  const history = Array.isArray(employee.firm_history) ? employee.firm_history : [];
  if (history.length > 0) {
    const { error: historyError } = await admin.from("admin_employee_firm_history").insert(history.map((item, index) => {
      const row = item as Record<string, unknown>;
      return {
        client_id: clientId,
        employee_id: employeeId,
        sort_order: index,
        firm_name: sanitizeText(row.firm_name, 500),
        start_date: sanitizeText(row.start_date, 120),
        end_date: sanitizeText(row.end_date, 120),
        last_synced_at: now,
      };
    }));
    if (historyError) throw historyError;
  }
}

async function handleEmployeeDelete(admin: ReturnType<typeof createAdminClient>, clientId: string, payload: Record<string, unknown>) {
  const employeeId = ensureText(payload.employee_id, "employee_id", 120, true);
  const now = new Date().toISOString();
  await admin.from("admin_employees").update({
    employer_id: payload.employer_id ?? null,
    is_deleted: true,
    deleted_at: now,
    last_synced_at: now,
  }).eq("client_id", clientId).eq("employee_id", employeeId);
  await admin.from("admin_employee_firm_history").delete().eq("client_id", clientId).eq("employee_id", employeeId);
}
