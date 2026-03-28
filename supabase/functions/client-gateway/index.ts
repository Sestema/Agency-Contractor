import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import {
  ClientRow,
  createAdminClient,
  extractIp,
  getPendingCommands,
  getPolicyForClient,
  handleCors,
  json,
  readJson,
  resolveClientByMachineId,
  updateClientHeartbeat,
} from "../_shared/common.ts";

type GatewayRequest = {
  action: "startup" | "heartbeat" | "track_event" | "ack_command" | "migrate_legacy_license";
  machine_id: string;
  machine_name?: string;
  app_version?: string;
  ip_address?: string;
  event_type?: string;
  event_data?: Record<string, unknown>;
  command_id?: string;
  command_status?: string;
  command_result?: Record<string, unknown> | null;
  command_error?: string | null;
  plan?: string;
  expires_on?: string;
  activated_on?: string;
  is_unlimited?: boolean;
  source?: string;
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
    const body = await readJson<GatewayRequest>(req);
    if (!body.machine_id) {
      return json({ ok: false, error: "machine_id_required" }, 400);
    }

    const admin = createAdminClient();
    const ipAddress = body.ip_address || extractIp(req);
    const createIfMissing = body.action === "startup" || body.action === "heartbeat";
    const client = await resolveClientByMachineId(
      admin,
      body.machine_id,
      body.machine_name ?? "",
      body.app_version ?? "",
      ipAddress,
      createIfMissing,
    );

    if (!client) {
      return json({ ok: false, error: "client_not_found" }, 404);
    }

    if (body.action === "startup" || body.action === "heartbeat") {
      await updateClientHeartbeat(admin, client.id, body.machine_name ?? client.machine_name, body.app_version ?? client.app_version, ipAddress);
      await insertTelemetry(admin, client.id, body.machine_id, ipAddress, body.app_version ?? client.app_version, body.event_type ?? body.action, body.event_data);
      const refreshed = await resolveClientByMachineId(admin, body.machine_id, "", "", "", false) as ClientRow;
      const policy = await getPolicyForClient(admin, client.id);
      const commands = await getPendingCommands(admin, client.id);

      return json({
        ok: true,
        client_id: refreshed.id,
        is_blocked: refreshed.is_blocked,
        expires_at: refreshed.expires_at,
        policy,
        pending_commands: commands,
      });
    }

    switch (body.action) {
      case "migrate_legacy_license":
        return await migrateLegacyLicense(admin, client, body, ipAddress);

      case "track_event":
        if (client.is_blocked) {
          return json({ ok: false, error: "client_blocked" }, 403);
        }
        await insertTelemetry(admin, client.id, body.machine_id, ipAddress, body.app_version ?? client.app_version, body.event_type ?? "event", body.event_data);
        return json({ ok: true, client_id: client.id, is_blocked: client.is_blocked });

      case "ack_command":
        if (client.is_blocked) {
          return json({ ok: false, error: "client_blocked" }, 403);
        }
        return await acknowledgeCommand(admin, client.id, body);

      default:
        return json({ ok: false, error: "unknown_action" }, 400);
    }
  } catch (error) {
    return json({
      ok: false,
      error: error instanceof Error ? error.message : "gateway_failed",
    }, 500);
  }
});

async function insertTelemetry(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineId: string,
  ipAddress: string,
  appVersion: string,
  eventType: string,
  eventData?: Record<string, unknown>,
) {
  const payload: Record<string, unknown> = {
    client_id: clientId,
    machine_id: machineId,
    ip_address: ipAddress,
    app_version: appVersion,
    event_type: eventType,
  };

  if (eventData && Object.keys(eventData).length > 0) {
    payload.event_data = eventData;
  }

  const { error } = await admin.from("telemetry").insert(payload);
  if (error) {
    throw error;
  }
}

async function acknowledgeCommand(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  body: GatewayRequest,
) {
  if (!body.command_id) {
    return json({ ok: false, error: "command_id_required" }, 400);
  }

  const { data: command, error } = await admin
    .from("admin_commands")
    .select("id,client_id")
    .eq("id", body.command_id)
    .maybeSingle();

  if (error) {
    throw error;
  }

  if (!command || command.client_id !== clientId) {
    return json({ ok: false, error: "command_not_owned" }, 403);
  }

  const { error: updateError } = await admin
    .from("admin_commands")
    .update({
      status: body.command_status ?? "ack",
      executed_at: new Date().toISOString(),
      result_json: body.command_result ?? null,
      error_text: body.command_error ?? null,
    })
    .eq("id", body.command_id);

  if (updateError) {
    throw updateError;
  }

  return json({ ok: true, client_id: clientId, is_blocked: false });
}

async function migrateLegacyLicense(
  admin: ReturnType<typeof createAdminClient>,
  client: ClientRow,
  body: GatewayRequest,
  ipAddress: string,
) {
  const source = String(body.source ?? "local_file");
  const isUnlimited = Boolean(body.is_unlimited) || String(body.expires_on ?? "").startsWith("9999");
  const normalizedExpiresAt = normalizeClaimedExpiry(body.expires_on, isUnlimited);
  const normalizedActivatedAt = normalizeClaimedActivatedAt(body.activated_on);
  const migrationResult = client.is_blocked
    ? "blocked"
    : isClaimStrongerThanServer(normalizedExpiresAt, client.expires_at, isUnlimited)
      ? "migrated"
      : "noop";

  if (client.is_blocked) {
    await insertTelemetry(admin, client.id, body.machine_id, ipAddress, body.app_version ?? client.app_version, "legacy_license_migrated", {
      old_expires_at: client.expires_at,
      new_expires_at: normalizedExpiresAt,
      plan: body.plan ?? "",
      source,
      is_unlimited: isUnlimited,
      migrated: false,
      migration_result: migrationResult,
    });
    return json({ ok: false, error: "client_blocked" }, 403);
  }

  if (migrationResult === "migrated") {
    const { error } = await admin
      .from("clients")
      .update({
        expires_at: normalizedExpiresAt,
        activated_at: normalizedActivatedAt,
      })
      .eq("id", client.id);
    if (error) {
      throw error;
    }
  }

  const refreshed = await resolveClientByMachineId(admin, body.machine_id, "", "", "", false);
  if (!refreshed) {
    return json({ ok: false, error: "client_not_found" }, 404);
  }

  await insertTelemetry(admin, refreshed.id, body.machine_id, ipAddress, body.app_version ?? client.app_version, "legacy_license_migrated", {
    old_expires_at: client.expires_at,
    new_expires_at: refreshed.expires_at,
    plan: body.plan ?? "",
    source,
    is_unlimited: isUnlimited,
    migrated: migrationResult === "migrated",
    migration_result: migrationResult,
  });

  const policy = await getPolicyForClient(admin, refreshed.id);
  const commands = await getPendingCommands(admin, refreshed.id);
  return json({
    ok: true,
    client_id: refreshed.id,
    is_blocked: refreshed.is_blocked,
    expires_at: refreshed.expires_at,
    migration_result: migrationResult,
    policy,
    pending_commands: commands,
  });
}

function normalizeClaimedExpiry(expiresOn: string | undefined, isUnlimited: boolean): string {
  if (isUnlimited) {
    return "2099-12-31T00:00:00Z";
  }

  const fallback = "2099-12-31T00:00:00Z";
  if (!expiresOn) {
    return fallback;
  }

  const parsed = new Date(expiresOn);
  return Number.isNaN(parsed.getTime()) ? fallback : parsed.toISOString();
}

function normalizeClaimedActivatedAt(activatedOn: string | undefined): string {
  if (!activatedOn) {
    return new Date().toISOString();
  }

  const parsed = new Date(activatedOn);
  return Number.isNaN(parsed.getTime()) ? new Date().toISOString() : parsed.toISOString();
}

function isClaimStrongerThanServer(
  claimedExpiresAt: string,
  serverExpiresAt: string | null,
  isUnlimited: boolean,
): boolean {
  if (!serverExpiresAt) {
    return true;
  }

  const claimed = new Date(claimedExpiresAt);
  const server = new Date(serverExpiresAt);
  if (Number.isNaN(claimed.getTime())) {
    return false;
  }
  if (Number.isNaN(server.getTime())) {
    return true;
  }

  const serverLooksUnlimited = server >= new Date("2099-01-01T00:00:00Z");
  if (isUnlimited && !serverLooksUnlimited) {
    return true;
  }

  return claimed > server;
}
