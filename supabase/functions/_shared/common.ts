import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

export interface ClientRow {
  id: string;
  machine_id: string;
  machine_name: string;
  ip_address: string;
  app_version: string;
  is_blocked: boolean;
  expires_at: string | null;
}

const encoder = new TextEncoder();
const decoder = new TextDecoder();

export function getEnv(name: string): string {
  const value = Deno.env.get(name);
  if (!value) {
    throw new Error(`Missing env var: ${name}`);
  }

  return value;
}

function getAdminJwtSecret(): string {
  const value = Deno.env.get("ADMIN_JWT_SECRET")
    ?? Deno.env.get("JWT_SECRET")
    ?? Deno.env.get("SUPABASE_JWT_SECRET");

  if (!value) {
    throw new Error("Missing env var: ADMIN_JWT_SECRET");
  }

  return value;
}

export function createAdminClient() {
  return createClient(getEnv("SUPABASE_URL"), getEnv("SUPABASE_SERVICE_ROLE_KEY"));
}

export function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
      "Access-Control-Allow-Methods": "POST, OPTIONS",
    },
  });
}

export function handleCors(req: Request): Response | null {
  if (req.method === "OPTIONS") {
    return json({ ok: true }, 200);
  }

  return null;
}

export async function readJson<T>(req: Request): Promise<T> {
  return await req.json() as T;
}

export function extractIp(req: Request): string {
  const forwarded = req.headers.get("x-forwarded-for") ?? "";
  if (forwarded.includes(",")) {
    return forwarded.split(",")[0].trim();
  }

  return forwarded.trim();
}

export async function resolveClientByMachineId(
  admin: ReturnType<typeof createAdminClient>,
  machineId: string,
  machineName = "",
  appVersion = "",
  ipAddress = "",
  createIfMissing = false,
): Promise<ClientRow | null> {
  const { data: existing, error } = await admin
    .from("clients")
    .select("id,machine_id,machine_name,ip_address,app_version,is_blocked,expires_at")
    .eq("machine_id", machineId)
    .limit(1)
    .maybeSingle();

  if (error) {
    throw error;
  }

  if (existing) {
    return existing as ClientRow;
  }

  if (!createIfMissing) {
    return null;
  }

  const now = new Date().toISOString();
  const expiresAt = new Date(Date.now() + 14 * 24 * 60 * 60 * 1000).toISOString();
  const licenseKey = `AC-${machineId}-${Date.now()}`;
  const { data: created, error: createError } = await admin
    .from("clients")
    .insert({
      license_key: licenseKey,
      machine_id: machineId,
      machine_name: machineName,
      ip_address: ipAddress,
      app_version: appVersion,
      activated_at: now,
      expires_at: expiresAt,
      last_seen: now,
      is_blocked: false,
    })
    .select("id,machine_id,machine_name,ip_address,app_version,is_blocked,expires_at")
    .single();

  if (createError) {
    throw createError;
  }

  return created as ClientRow;
}

export async function updateClientHeartbeat(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineName: string,
  appVersion: string,
  ipAddress: string,
) {
  const { error } = await admin
    .from("clients")
    .update({
      machine_name: machineName,
      app_version: appVersion,
      ip_address: ipAddress,
      last_seen: new Date().toISOString(),
    })
    .eq("id", clientId);

  if (error) {
    throw error;
  }
}

export async function getPolicyForClient(admin: ReturnType<typeof createAdminClient>, clientId: string) {
  const { data, error } = await admin
    .from("client_policies")
    .select("*")
    .eq("client_id", clientId)
    .limit(1)
    .maybeSingle();

  if (error && !`${error.message}`.includes("does not exist")) {
    throw error;
  }

  return data ?? null;
}

export async function getPendingCommands(admin: ReturnType<typeof createAdminClient>, clientId: string) {
  const { data, error } = await admin
    .from("admin_commands")
    .select("*")
    .eq("client_id", clientId)
    .eq("status", "pending")
    .order("created_at", { ascending: true });

  if (error && !`${error.message}`.includes("does not exist")) {
    throw error;
  }

  return data ?? [];
}

export function sanitizeText(value: unknown, maxLength: number): string {
  if (value == null) {
    return "";
  }

  const text = String(value).trim();
  if (/^[a-zA-Z]:\\/.test(text) || text.startsWith("file://")) {
    return "";
  }

  if (/^[A-Za-z0-9+/=]{800,}$/.test(text)) {
    return "";
  }

  return text.slice(0, maxLength);
}

export function ensureText(value: unknown, field: string, maxLength: number, required = false): string {
  const text = sanitizeText(value, maxLength);
  if (required && !text) {
    throw new Error(`${field} is required`);
  }

  return text;
}

export async function logMirrorSync(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  machineId: string,
  operation: string,
  success: boolean,
  errorText: string | null,
  rowsAffected = 0,
) {
  await admin.from("mirror_sync_log").insert({
    client_id: clientId,
    machine_id: machineId,
    operation,
    success,
    error_text: errorText,
    rows_affected: rowsAffected,
  });
}

export async function upsertMirrorState(
  admin: ReturnType<typeof createAdminClient>,
  clientId: string,
  schemaVersion: string,
  isFullSync: boolean,
  errorText: string | null = null,
) {
  const now = new Date().toISOString();
  const payload: Record<string, unknown> = {
    client_id: clientId,
    schema_version: schemaVersion,
    last_delta_sync_at: now,
    last_error_text: errorText,
  };

  if (isFullSync) {
    payload.last_full_sync_at = now;
  }

  const { error } = await admin
    .from("client_mirror_state")
    .upsert(payload, { onConflict: "client_id" });

  if (error) {
    throw error;
  }
}

function base64UrlEncode(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

function base64UrlDecode(value: string): Uint8Array {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized.padEnd(normalized.length + ((4 - normalized.length % 4) % 4), "=");
  return Uint8Array.from(atob(padded), (char) => char.charCodeAt(0));
}

async function sign(message: string, secret: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(message));
  return base64UrlEncode(new Uint8Array(signature));
}

export async function issueAdminToken(subject: string, lifetimeSeconds: number): Promise<string> {
  const secret = getAdminJwtSecret();
  const header = base64UrlEncode(encoder.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const now = Math.floor(Date.now() / 1000);
  const payload = base64UrlEncode(encoder.encode(JSON.stringify({
    sub: subject,
    role: "admin_gateway",
    iat: now,
    exp: now + lifetimeSeconds,
  })));
  const signature = await sign(`${header}.${payload}`, secret);
  return `${header}.${payload}.${signature}`;
}

export async function verifyAdminToken(token: string): Promise<Record<string, unknown> | null> {
  const secret = getAdminJwtSecret();
  const parts = token.split(".");
  if (parts.length !== 3) {
    return null;
  }

  const [header, payload, signature] = parts;
  const expected = await sign(`${header}.${payload}`, secret);
  if (expected !== signature) {
    return null;
  }

  const parsed = JSON.parse(decoder.decode(base64UrlDecode(payload))) as Record<string, unknown>;
  const exp = typeof parsed.exp === "number" ? parsed.exp : 0;
  if (exp <= Math.floor(Date.now() / 1000)) {
    return null;
  }

  return parsed;
}

export async function requireAdmin(req: Request): Promise<Record<string, unknown>> {
  const header = req.headers.get("authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice("Bearer ".length) : "";
  const payload = token ? await verifyAdminToken(token) : null;
  if (!payload) {
    throw new Error("admin_unauthorized");
  }

  return payload;
}

export function randomBase64(bytes = 32): string {
  const buffer = new Uint8Array(bytes);
  crypto.getRandomValues(buffer);
  return btoa(String.fromCharCode(...buffer));
}

export async function hashPassword(password: string, saltBase64: string): Promise<string> {
  const salt = Uint8Array.from(atob(saltBase64), (char) => char.charCodeAt(0));
  const keyMaterial = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({
    name: "PBKDF2",
    hash: "SHA-256",
    salt,
    iterations: 100_000,
  }, keyMaterial, 256);
  return btoa(String.fromCharCode(...new Uint8Array(bits)));
}
