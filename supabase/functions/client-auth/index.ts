import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import {
  createAdminClient,
  extractIp,
  handleCors,
  hashPassword,
  json,
  randomBase64,
  readJson,
  resolveClientByMachineId,
  sanitizeText,
} from "../_shared/common.ts";

type AuthRequest = {
  action: "check" | "create" | "login" | "update_name" | "update_remember" | "change_password" | "forced_reset";
  machine_id: string;
  client_id?: string;
  first_name?: string;
  last_name?: string;
  password?: string;
  current_password?: string;
  new_password?: string;
  remember_me_enabled?: boolean;
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
    const body = await readJson<AuthRequest>(req);
    if (!body.machine_id) {
      return json({ ok: false, error: "machine_id_required" }, 400);
    }

    const admin = createAdminClient();
    const client = await resolveClientByMachineId(admin, body.machine_id, "", "", extractIp(req), false);
    if (!client) {
      return json({ ok: false, error: "client_not_found" }, 404);
    }
    if (client.is_blocked) {
      return json({ ok: false, error: "client_blocked" }, 403);
    }

    switch (body.action) {
      case "check":
        return await checkProfile(admin, client.id);
      case "create":
        return await createProfile(admin, client.id, body);
      case "login":
        return await login(admin, client.id, body, extractIp(req), body.machine_id);
      case "update_name":
        return await updateName(admin, client.id, body);
      case "update_remember":
        return await updateRemember(admin, client.id, body);
      case "change_password":
        return await changePassword(admin, client.id, body);
      case "forced_reset":
        return await forcedReset(admin, client.id, body);
      default:
        return json({ ok: false, error: "unknown_action" }, 400);
    }
  } catch (error) {
    return json({ ok: false, error: error instanceof Error ? error.message : "client_auth_failed" }, 500);
  }
});

async function getProfile(admin: ReturnType<typeof createAdminClient>, clientId: string) {
  const { data, error } = await admin
    .from("client_profiles")
    .select("*")
    .eq("client_id", clientId)
    .limit(1)
    .maybeSingle();
  if (error && !`${error.message}`.includes("does not exist")) {
    throw error;
  }

  return data;
}

function profileResponse(profile: Record<string, unknown> | null) {
  if (!profile) {
    return null;
  }

  return {
    id: profile.id,
    client_id: profile.client_id,
    first_name: profile.first_name ?? "",
    last_name: profile.last_name ?? "",
    must_reset_password: !!profile.must_reset_password,
    remember_me_enabled: !!profile.remember_me_enabled,
    session_version: Number(profile.session_version ?? 1),
    created_at: profile.created_at ?? null,
    updated_at: profile.updated_at ?? null,
    password_hash: "",
    password_salt: "",
  };
}

async function checkProfile(admin: ReturnType<typeof createAdminClient>, clientId: string) {
  const profile = await getProfile(admin, clientId);
  return json({ ok: true, exists: !!profile, profile: profileResponse(profile as Record<string, unknown> | null) });
}

async function createProfile(admin: ReturnType<typeof createAdminClient>, clientId: string, body: AuthRequest) {
  const existing = await getProfile(admin, clientId);
  if (existing) {
    return json({ ok: false, error: "profile_exists" }, 409);
  }

  const password = body.password ?? "";
  if (!password) {
    return json({ ok: false, error: "password_required" }, 400);
  }

  const salt = randomBase64(16);
  const hash = await hashPassword(password, salt);
  const { data, error } = await admin
    .from("client_profiles")
    .insert({
      client_id: clientId,
      first_name: sanitizeText(body.first_name, 120),
      last_name: sanitizeText(body.last_name, 120),
      password_hash: hash,
      password_salt: salt,
      must_reset_password: false,
      remember_me_enabled: false,
      session_version: 1,
    })
    .select("*")
    .single();

  if (error) throw error;
  return json({ ok: true, profile: profileResponse(data as Record<string, unknown>) });
}

async function login(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  body: AuthRequest,
  ipAddress: string,
  machineId: string,
) {
  const rate = await getRateLimit(admin, clientId, machineId, ipAddress);
  if (rate.cooldownSeconds > 0) {
    return json({ ok: false, error: "cooldown_active", cooldown_seconds: rate.cooldownSeconds }, 429);
  }

  const profile = await getProfile(admin, clientId);
  if (!profile) {
    return json({ ok: false, error: "profile_not_found" }, 404);
  }

  const expected = await hashPassword(body.password ?? "", String(profile.password_salt ?? ""));
  if (expected !== String(profile.password_hash ?? "")) {
    await registerFailedAttempt(admin, clientId, machineId, ipAddress, rate.failedAttempts + 1);
    return json({ ok: false, error: "wrong_password" }, 401);
  }

  await clearFailedAttempts(admin, clientId, machineId, ipAddress);
  return json({ ok: true, profile: profileResponse(profile as Record<string, unknown>) });
}

async function updateName(admin: ReturnType<typeof createAdminClient>, clientId: string, body: AuthRequest) {
  const { data, error } = await admin
    .from("client_profiles")
    .update({
      first_name: sanitizeText(body.first_name, 120),
      last_name: sanitizeText(body.last_name, 120),
    })
    .eq("client_id", clientId)
    .select("*")
    .single();
  if (error) throw error;
  return json({ ok: true, profile: profileResponse(data as Record<string, unknown>) });
}

async function updateRemember(admin: ReturnType<typeof createAdminClient>, clientId: string, body: AuthRequest) {
  const { data, error } = await admin
    .from("client_profiles")
    .update({
      remember_me_enabled: !!body.remember_me_enabled,
    })
    .eq("client_id", clientId)
    .select("*")
    .single();
  if (error) throw error;
  return json({ ok: true, profile: profileResponse(data as Record<string, unknown>) });
}

async function changePassword(admin: ReturnType<typeof createAdminClient>, clientId: string, body: AuthRequest) {
  const profile = await getProfile(admin, clientId);
  if (!profile) {
    return json({ ok: false, error: "profile_not_found" }, 404);
  }

  const currentHash = await hashPassword(body.current_password ?? "", String(profile.password_salt ?? ""));
  if (currentHash !== String(profile.password_hash ?? "")) {
    return json({ ok: false, error: "current_password_wrong" }, 401);
  }

  const newSalt = randomBase64(16);
  const newHash = await hashPassword(body.new_password ?? "", newSalt);
  const { data, error } = await admin
    .from("client_profiles")
    .update({
      password_hash: newHash,
      password_salt: newSalt,
      must_reset_password: false,
      session_version: Number(profile.session_version ?? 1) + 1,
    })
    .eq("client_id", clientId)
    .select("*")
    .single();
  if (error) throw error;
  return json({ ok: true, profile: profileResponse(data as Record<string, unknown>) });
}

async function forcedReset(admin: ReturnType<typeof createAdminClient>, clientId: string, body: AuthRequest) {
  const profile = await getProfile(admin, clientId);
  if (!profile) {
    return json({ ok: false, error: "profile_not_found" }, 404);
  }

  const newSalt = randomBase64(16);
  const newHash = await hashPassword(body.new_password ?? "", newSalt);
  const { data, error } = await admin
    .from("client_profiles")
    .update({
      password_hash: newHash,
      password_salt: newSalt,
      must_reset_password: false,
      remember_me_enabled: false,
      session_version: Number(profile.session_version ?? 1) + 1,
    })
    .eq("client_id", clientId)
    .select("*")
    .single();
  if (error) throw error;
  return json({ ok: true, profile: profileResponse(data as Record<string, unknown>) });
}

async function getRateLimit(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineId: string,
  ipAddress: string,
) {
  const { data } = await admin
    .from("client_auth_attempts")
    .select("*")
    .eq("client_id", clientId)
    .eq("machine_id", machineId)
    .eq("ip_address", ipAddress)
    .limit(1)
    .maybeSingle();

  const failedAttempts = Number(data?.failed_attempts ?? 0);
  const lastFailedAt = data?.last_failed_at ? new Date(String(data.last_failed_at)) : null;
  let cooldownMs = 0;
  if (failedAttempts >= 20) cooldownMs = 30 * 60 * 1000;
  else if (failedAttempts >= 10) cooldownMs = 5 * 60 * 1000;
  else if (failedAttempts >= 5) cooldownMs = 60 * 1000;

  if (!lastFailedAt || cooldownMs === 0) {
    return { failedAttempts, cooldownSeconds: 0 };
  }

  const remaining = cooldownMs - (Date.now() - lastFailedAt.getTime());
  return {
    failedAttempts,
    cooldownSeconds: remaining > 0 ? Math.ceil(remaining / 1000) : 0,
  };
}

async function registerFailedAttempt(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineId: string,
  ipAddress: string,
  failedAttempts: number,
) {
  await admin.from("client_auth_attempts").upsert({
    client_id: clientId,
    machine_id: machineId,
    ip_address: ipAddress,
    failed_attempts: failedAttempts,
    last_failed_at: new Date().toISOString(),
  }, { onConflict: "client_id,machine_id,ip_address" });
}

async function clearFailedAttempts(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineId: string,
  ipAddress: string,
) {
  await admin.from("client_auth_attempts").delete()
    .eq("client_id", clientId)
    .eq("machine_id", machineId)
    .eq("ip_address", ipAddress);
}
