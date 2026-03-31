import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import {
  createAdminClient,
  handleCors,
  hashPassword,
  issueAdminToken,
  json,
  randomBase64,
  readJson,
  requireAdmin,
  sanitizeText,
} from "../_shared/common.ts";

type AdminRequest = {
  action:
    | "login"
    | "list_clients"
    | "get_telemetry"
    | "block_client"
    | "unblock_client"
    | "extend_license"
    | "update_client_access"
    | "update_notes"
    | "delete_client"
    | "get_profile"
    | "get_profiles"
    | "reset_password"
    | "get_mirror_snapshot"
    | "write_audit"
    | "block_by_ip";
  admin_password?: string;
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

  try {
    const body = await readJson<AdminRequest>(req);
    if (body.action === "login") {
      const expected = Deno.env.get("ADMIN_GATEWAY_PASSWORD") ?? "";
      if (!expected || body.admin_password !== expected) {
        return json({ ok: false, error: "invalid_admin_password" }, 401);
      }

      return json({
        ok: true,
        admin_token: await issueAdminToken("admin", 30 * 60),
      });
    }

    await requireAdmin(req);
    const admin = createAdminClient();
    const payload = body.payload ?? {};

    switch (body.action) {
      case "list_clients":
        return json({ ok: true, data: await listClients(admin) });
      case "get_telemetry":
        return json({ ok: true, data: await getTelemetry(admin, payload) });
      case "block_client":
        await blockClient(admin, payload);
        return json({ ok: true, data: true });
      case "unblock_client":
        await unblockClient(admin, payload);
        return json({ ok: true, data: true });
      case "block_by_ip":
        await blockByIp(admin, payload);
        return json({ ok: true, data: true });
      case "extend_license":
        await extendLicense(admin, payload);
        return json({ ok: true, data: true });
      case "update_client_access":
        await updateClientAccess(admin, payload);
        return json({ ok: true, data: true });
      case "update_notes":
        await updateNotes(admin, payload);
        return json({ ok: true, data: true });
      case "delete_client":
        await deleteClient(admin, payload);
        return json({ ok: true, data: true });
      case "get_profile":
        return json({ ok: true, data: await getProfile(admin, payload) });
      case "get_profiles":
        return json({ ok: true, data: await getProfiles(admin) });
      case "reset_password":
        return json({ ok: true, data: await resetPassword(admin, payload) });
      case "get_mirror_snapshot":
        return json({ ok: true, data: await getMirrorSnapshot(admin, payload) });
      case "write_audit":
        await writeAudit(admin, payload);
        return json({ ok: true, data: true });
      default:
        return json({ ok: false, error: "unknown_action" }, 400);
    }
  } catch (error) {
    return json({ ok: false, error: error instanceof Error ? error.message : "admin_gateway_failed" }, 500);
  }
});

async function listClients(admin: ReturnType<typeof createAdminClient>) {
  const telemetrySince = new Date(Date.now() - 60 * 24 * 60 * 60 * 1000).toISOString();
  const [clientsResult, profilesResult, telemetryResult] = await Promise.all([
    admin.from("clients").select("*").order("last_seen", { ascending: false }),
    admin.from("client_profiles").select("client_id,first_name,last_name"),
    admin
      .from("telemetry")
      .select("client_id,event_type,event_data,created_at,app_version")
      .gte("created_at", telemetrySince)
      .order("created_at", { ascending: false })
      .limit(4000),
  ]);

  if (clientsResult.error) throw clientsResult.error;
  if (profilesResult.error && !`${profilesResult.error.message}`.includes("does not exist")) throw profilesResult.error;
  if (telemetryResult.error) throw telemetryResult.error;

  const clients = clientsResult.data ?? [];
  const latestVersion = getLatestKnownVersion(clients);
  const profilesByClientId = new Map<string, Record<string, unknown>>();
  for (const profile of profilesResult.data ?? []) {
    profilesByClientId.set(String(profile.client_id), profile as Record<string, unknown>);
  }

  const telemetryByClientId = new Map<string, Array<Record<string, unknown>>>();
  for (const item of telemetryResult.data ?? []) {
    const clientId = String(item.client_id ?? "");
    if (!clientId) continue;
    const list = telemetryByClientId.get(clientId) ?? [];
    list.push(item as Record<string, unknown>);
    telemetryByClientId.set(clientId, list);
  }

  return clients.map((client) => {
    const clientId = String(client.id);
    const profile = profilesByClientId.get(clientId);
    const telemetry = telemetryByClientId.get(clientId) ?? [];
    const latestHeartbeat = telemetry.find((item) =>
      stringEquals(item.event_type, "heartbeat"));
    const latestStats = telemetry.find((item) => hasStats(item.event_data));
    const errorLikeCount = telemetry.filter(isErrorLikeEvent).length;
    const isOutdated = isOutdatedVersionValue(String(client.app_version ?? ""), latestVersion);
    const risk = buildRiskSummary(client as Record<string, unknown>, errorLikeCount, latestHeartbeat, isOutdated);

    return {
      ...client,
      profile_first_name: String(profile?.first_name ?? ""),
      profile_last_name: String(profile?.last_name ?? ""),
      risk_level: risk.level,
      risk_score: risk.score,
      risk_reasons: risk.reasons,
      is_outdated_version: isOutdated,
      error_like_count: errorLikeCount,
      latest_heartbeat_at: latestHeartbeat?.created_at ?? null,
      firms_count: readInt(latestStats?.event_data, "firms_count"),
      employees_count: readInt(latestStats?.event_data, "employees_count"),
    };
  });
}

async function getTelemetry(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const limit = Math.max(1, Math.min(Number(payload.limit ?? 200), 500));
  let query = admin.from("telemetry").select("*").order("created_at", { ascending: false }).limit(limit + 1);
  if (payload.client_id) {
    query = query.eq("client_id", String(payload.client_id));
  }
  if (payload.before_created_at) {
    query = query.lt("created_at", String(payload.before_created_at));
  }
  const { data, error } = await query;
  if (error) throw error;
  const items = data ?? [];
  const hasMore = items.length > limit;
  const pageItems = hasMore ? items.slice(0, limit) : items;
  const nextCursor = hasMore && pageItems.length > 0
    ? String(pageItems[pageItems.length - 1].created_at ?? "")
    : "";

  return {
    items: pageItems,
    has_more: hasMore,
    next_cursor: nextCursor,
  };
}

async function blockClient(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("clients").update({
    is_blocked: true,
    block_reason: sanitizeText(payload.reason, 1000),
  }).eq("id", String(payload.client_id ?? ""));
  if (error) throw error;
}

async function unblockClient(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("clients").update({
    is_blocked: false,
    block_reason: null,
  }).eq("id", String(payload.client_id ?? ""));
  if (error) throw error;
}

async function blockByIp(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("clients").update({
    is_blocked: true,
    block_reason: sanitizeText(payload.reason, 1000),
  }).eq("ip_address", String(payload.ip_address ?? ""));
  if (error) throw error;
}

async function extendLicense(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("clients").update({
    expires_at: payload.expires_at ?? null,
  }).eq("id", String(payload.client_id ?? ""));
  if (error) throw error;
}

async function updateClientAccess(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const clientId = String(payload.client_id ?? "");
  if (!clientId) {
    throw new Error("client_id_required");
  }

  const { error } = await admin.from("clients").update({
    plan: normalizeClientPlan(payload.plan),
    gemini_api_key: sanitizeText(payload.gemini_api_key, 2048),
  }).eq("id", clientId);
  if (error) throw error;
}

async function updateNotes(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("clients").update({
    notes: sanitizeText(payload.notes, 4000),
  }).eq("id", String(payload.client_id ?? ""));
  if (error) throw error;
}

async function deleteClient(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const clientId = String(payload.client_id ?? "");
  await admin.from("telemetry").delete().eq("client_id", clientId);
  const { error } = await admin.from("clients").delete().eq("id", clientId);
  if (error) throw error;
}

async function getProfile(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { data, error } = await admin.from("client_profiles").select("*").eq("client_id", String(payload.client_id ?? "")).limit(1).maybeSingle();
  if (error && !`${error.message}`.includes("does not exist")) throw error;
  return stripProfileSecrets(data);
}

async function getProfiles(admin: ReturnType<typeof createAdminClient>) {
  const { data, error } = await admin.from("client_profiles").select("*");
  if (error && !`${error.message}`.includes("does not exist")) throw error;
  return (data ?? []).map(stripProfileSecrets);
}

async function resetPassword(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const clientId = String(payload.client_id ?? "");
  const { data: profile, error: loadError } = await admin.from("client_profiles").select("*").eq("client_id", clientId).limit(1).maybeSingle();
  if (loadError && !`${loadError.message}`.includes("does not exist")) throw loadError;
  if (!profile) return null;

  const salt = randomBase64(16);
  const hash = await hashPassword(randomBase64(24), salt);
  const { data, error } = await admin.from("client_profiles").update({
    password_hash: hash,
    password_salt: salt,
    must_reset_password: true,
    remember_me_enabled: false,
    session_version: Number(profile.session_version ?? 1) + 1,
  }).eq("client_id", clientId).select("*").single();
  if (error) throw error;
  return stripProfileSecrets(data);
}

async function getMirrorSnapshot(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const clientId = String(payload.client_id ?? "");
  const [state, agencies, employers, addresses, positions, employees, history] = await Promise.all([
    admin.from("client_mirror_state").select("*").eq("client_id", clientId).limit(1).maybeSingle(),
    admin.from("admin_agencies").select("*").eq("client_id", clientId).order("name", { ascending: true }),
    admin.from("admin_employers").select("*").eq("client_id", clientId).order("name", { ascending: true }),
    admin.from("admin_employer_addresses").select("*").eq("client_id", clientId).order("sort_order", { ascending: true }),
    admin.from("admin_employer_positions").select("*").eq("client_id", clientId).order("sort_order", { ascending: true }),
    admin.from("admin_employees").select("*").eq("client_id", clientId).order("full_name", { ascending: true }),
    admin.from("admin_employee_firm_history").select("*").eq("client_id", clientId).order("sort_order", { ascending: true }),
  ]);

  if (state.error) throw state.error;
  if (agencies.error) throw agencies.error;
  if (employers.error) throw employers.error;
  if (addresses.error) throw addresses.error;
  if (positions.error) throw positions.error;
  if (employees.error) throw employees.error;
  if (history.error) throw history.error;

  const addressesByEmployer = new Map<string, Array<Record<string, unknown>>>();
  for (const item of addresses.data ?? []) {
    const key = String(item.employer_id);
    const list = addressesByEmployer.get(key) ?? [];
    list.push(item as Record<string, unknown>);
    addressesByEmployer.set(key, list);
  }

  const positionsByEmployer = new Map<string, Array<Record<string, unknown>>>();
  for (const item of positions.data ?? []) {
    const key = String(item.employer_id);
    const list = positionsByEmployer.get(key) ?? [];
    list.push(item as Record<string, unknown>);
    positionsByEmployer.set(key, list);
  }

  const historyByEmployee = new Map<string, Array<Record<string, unknown>>>();
  for (const item of history.data ?? []) {
    const key = String(item.employee_id);
    const list = historyByEmployee.get(key) ?? [];
    list.push(item as Record<string, unknown>);
    historyByEmployee.set(key, list);
  }

  return {
    state: state.data ?? null,
    agencies: agencies.data ?? [],
    employers: (employers.data ?? []).map((item) => ({
      ...item,
      addresses: addressesByEmployer.get(String(item.employer_id)) ?? [],
      positions: positionsByEmployer.get(String(item.employer_id)) ?? [],
    })),
    employees: (employees.data ?? []).map((item) => ({
      ...item,
      firm_history: historyByEmployee.get(String(item.employee_id)) ?? [],
    })),
  };
}

async function writeAudit(admin: ReturnType<typeof createAdminClient>, payload: Record<string, unknown>) {
  const { error } = await admin.from("admin_audit_log").insert({
    target_client_id: payload.client_id ?? null,
    action_type: sanitizeText(payload.action_type, 240),
    old_value_json: payload.old_value ?? null,
    new_value_json: payload.new_value ?? null,
    note: sanitizeText(payload.note, 2000),
    actor: sanitizeText(payload.actor, 240) || "admin-gateway",
  });
  if (error) throw error;
}

function stripProfileSecrets(profile: Record<string, unknown> | null) {
  if (!profile) return null;
  return {
    ...profile,
    password_hash: "",
    password_salt: "",
  };
}

function normalizeClientPlan(value: unknown): string {
  const normalized = String(value ?? "").trim().toLowerCase();
  return normalized === "standard" || normalized === "pro" ? normalized : "trial";
}

function hasStats(eventData: unknown): boolean {
  if (!eventData || typeof eventData !== "object") return false;
  return "firms_count" in (eventData as Record<string, unknown>)
    || "employees_count" in (eventData as Record<string, unknown>);
}

function readInt(eventData: unknown, propertyName: string): number {
  if (!eventData || typeof eventData !== "object") return 0;
  const value = (eventData as Record<string, unknown>)[propertyName];
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string") {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }
  return 0;
}

function stringEquals(value: unknown, expected: string): boolean {
  return String(value ?? "").toLowerCase() === expected.toLowerCase();
}

function isErrorLikeEvent(item: Record<string, unknown>): boolean {
  const eventType = String(item.event_type ?? "");
  const eventData = JSON.stringify(item.event_data ?? "");
  return eventType.toLowerCase().includes("error")
    || eventData.toLowerCase().includes("error")
    || eventData.toLowerCase().includes("exception");
}

function buildRiskSummary(
  client: Record<string, unknown>,
  errorLikeCount: number,
  latestHeartbeat: Record<string, unknown> | undefined,
  isOutdatedVersion: boolean,
) {
  const reasons: string[] = [];
  let score = 0;
  const daysToExpiry = getDaysUntilExpiry(client.expires_at);
  const daysSinceLastSeen = getDaysSinceLastSeen(client.last_seen);

  if (Boolean(client.is_blocked)) {
    score += 100;
    reasons.push("Клієнт заблокований");
  }

  if (daysToExpiry < 0) {
    score += 80;
    reasons.push(`Ліцензія протермінована на ${Math.abs(daysToExpiry)} дн.`);
  } else if (daysToExpiry <= 7) {
    score += 35;
    reasons.push(`Ліцензія закінчується через ${daysToExpiry} дн.`);
  } else if (daysToExpiry <= 30) {
    score += 15;
    reasons.push(`Ліцензія закінчується через ${daysToExpiry} дн.`);
  }

  if (daysSinceLastSeen === Number.MAX_SAFE_INTEGER) {
    score += 35;
    reasons.push("Немає активності");
  } else if (daysSinceLastSeen > 30) {
    score += 50;
    reasons.push(`Неактивний ${daysSinceLastSeen} дн.`);
  } else if (daysSinceLastSeen > 7) {
    score += 25;
    reasons.push(`Неактивний ${daysSinceLastSeen} дн.`);
  }

  if (isOutdatedVersion) {
    score += 20;
    reasons.push(`Стара версія ${String(client.app_version ?? "")}`);
  }

  if (errorLikeCount >= 5) {
    score += 30;
    reasons.push(`Багато error-like подій (${errorLikeCount})`);
  } else if (errorLikeCount > 0) {
    score += 10;
    reasons.push(`Є error-like події (${errorLikeCount})`);
  }

  if (!latestHeartbeat?.created_at) {
    score += 10;
    reasons.push("Немає heartbeat");
  }

  return {
    score,
    level: score >= 60 ? "High risk" : score >= 20 ? "Warning" : "OK",
    reasons: reasons.length === 0 ? ["Сигналів ризику не виявлено"] : reasons,
  };
}

function getDaysUntilExpiry(expiresAt: unknown): number {
  if (!expiresAt) return Number.MAX_SAFE_INTEGER;
  const parsed = new Date(String(expiresAt));
  if (Number.isNaN(parsed.getTime())) return Number.MAX_SAFE_INTEGER;
  return Math.floor((parsed.getTime() - Date.now()) / (24 * 60 * 60 * 1000));
}

function getDaysSinceLastSeen(lastSeen: unknown): number {
  if (!lastSeen) return Number.MAX_SAFE_INTEGER;
  const parsed = new Date(String(lastSeen));
  if (Number.isNaN(parsed.getTime())) return Number.MAX_SAFE_INTEGER;
  return Math.floor((Date.now() - parsed.getTime()) / (24 * 60 * 60 * 1000));
}

function getLatestKnownVersion(clients: Array<Record<string, unknown>>): number[] | null {
  let latest: number[] | null = null;
  for (const client of clients) {
    const parsed = parseComparableVersion(String(client.app_version ?? ""));
    if (!parsed) continue;
    if (!latest || compareVersions(parsed, latest) > 0) {
      latest = parsed;
    }
  }
  return latest;
}

function isOutdatedVersionValue(appVersion: string, latestVersion: number[] | null): boolean {
  if (!latestVersion) return false;
  const parsed = parseComparableVersion(appVersion);
  if (!parsed) return false;
  return compareVersions(parsed, latestVersion) < 0;
}

function parseComparableVersion(value: string): number[] | null {
  const trimmed = value.trim().replace(/^v/i, "");
  const comparable = trimmed.match(/^[0-9.]+/)?.[0] ?? "";
  if (!comparable) return null;
  const parts = comparable.split(".").map((part) => Number(part));
  return parts.some((part) => Number.isNaN(part)) ? null : parts;
}

function compareVersions(left: number[], right: number[]): number {
  const length = Math.max(left.length, right.length);
  for (let index = 0; index < length; index++) {
    const leftPart = left[index] ?? 0;
    const rightPart = right[index] ?? 0;
    if (leftPart !== rightPart) return leftPart - rightPart;
  }
  return 0;
}
