import { createEmailSender } from "./email";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };
type Device = { workspace_id: string; employee_authority_state: string };
type Employee = {
  employee_id: string;
  employee_no: string;
  email_normalized: string;
  email_verified_at: string | null;
  enabled: number;
  credential_version: number;
};
type Challenge = {
  email_normalized: string;
  otp_digest: string;
  delivery_state: string;
  attempt_count: number;
  max_attempts: number;
  expires_at: string;
  consumed_at: string | null;
};

const PURPOSE = "employee_password_recovery";
const TTL_MS = 10 * 60 * 1000;
const COOLDOWN_MS = 60 * 1000;
const MAX_PER_HOUR = 5;
const MAX_ATTEMPTS = 5;

function requestId(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  return supplied && supplied.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(supplied)
    ? supplied : crypto.randomUUID();
}

function json(env: Env, id: string, status: number, body: Record<string, JsonValue>, headers?: HeadersInit): Response {
  return new Response(JSON.stringify({
    ok: status >= 200 && status < 300,
    service: "cyinvoice-cloud",
    cloudVersion: "0.8.4",
    apiVersion: env.API_VERSION,
    environment: env.APP_ENV,
    requestId: id,
    timestamp: new Date().toISOString(),
    ...body,
  }), {
    status,
    headers: {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8",
      "x-content-type-options": "nosniff",
      "x-request-id": id,
      ...headers,
    },
  });
}

function error(env: Env, id: string, status: number, code: string, message: string): Response {
  return json(env, id, status, { error: { code, message } });
}

async function sha256Hex(value: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

async function hmacHex(secret: string, value: string): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const digest = new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

function equalHex(left: string, right: string): boolean {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1)
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  return difference === 0;
}

async function deviceFor(request: Request, env: Env): Promise<Device | null> {
  const token = /^Bearer\s+(cydev_[0-9a-f]{64})$/i.exec(request.headers.get("authorization")?.trim() ?? "")?.[1]?.toLowerCase();
  if (!token) return null;
  return env.DB.prepare(
    `SELECT d.workspace_id, d.employee_authority_state
       FROM devices d JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1 AND d.status = 'active' AND w.status = 'active' LIMIT 1`
  ).bind(await sha256Hex(token)).first<Device>();
}

async function employeeFor(env: Env, device: Device, employeeNo: string): Promise<Employee | null> {
  return env.DB.prepare(
    `SELECT employee_id, employee_no, email_normalized, email_verified_at, enabled, credential_version
       FROM cloud_employees WHERE workspace_id = ?1 AND employee_no = ?2 LIMIT 1`
  ).bind(device.workspace_id, employeeNo).first<Employee>();
}

async function bodyFor(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    return value && typeof value === "object" && !Array.isArray(value)
      ? value as Record<string, unknown> : null;
  } catch { return null; }
}

function employeeNo(value: unknown): string | null {
  const normalized = typeof value === "string" ? value.trim() : "";
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function emailAddress(value: unknown): string | null {
  const normalized = typeof value === "string" ? value.trim().toLowerCase() : "";
  return normalized.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized)
    ? normalized : null;
}

function verifier(value: unknown): string | null {
  const normalized = typeof value === "string" ? value.trim().toLowerCase() : "";
  const match = /^pbkdf2-sha256\$(\d+)\$([0-9a-f]+)\$([0-9a-f]{64})$/.exec(normalized);
  if (!match) return null;
  const iterations = Number(match[1]);
  return Number.isInteger(iterations) && iterations >= 100_000 && iterations <= 2_000_000 &&
    match[2].length >= 32 && match[2].length <= 128 && match[2].length % 2 === 0 ? normalized : null;
}

function scope(device: Device, employee: Employee): string {
  return `employee_password_recovery:${device.workspace_id}:${employee.employee_id}:${employee.credential_version}`;
}

function masked(email: string): string {
  const at = email.lastIndexOf("@");
  const local = email.slice(0, at);
  return `${local.slice(0, local.length <= 2 ? 1 : 2)}***@${email.slice(at + 1)}`;
}

function otpCode(): string {
  const range = 1_000_000;
  const limit = Math.floor(0x1_0000_0000 / range) * range;
  const values = new Uint32Array(1);
  do crypto.getRandomValues(values); while (values[0] >= limit);
  return (values[0] % range).toString().padStart(6, "0");
}

async function start(request: Request, env: Env, id: string): Promise<Response> {
  const device = await deviceFor(request, env);
  if (!device) return error(env, id, 401, "UNAUTHORIZED", "Device authentication failed.");
  if (device.employee_authority_state !== "cloud")
    return error(env, id, 409, "EMPLOYEE_AUTHORITY_NOT_READY", "Cloud Employee authority is not ready.");
  const body = await bodyFor(request);
  const number = employeeNo(body?.employeeNo);
  const email = emailAddress(body?.email);
  if (!number || !email) return error(env, id, 400, "INVALID_PASSWORD_RECOVERY_REQUEST", "Employee No or Email is invalid.");
  const employee = await employeeFor(env, device, number);
  if (!employee || employee.enabled !== 1 || !employee.email_verified_at || employee.email_normalized !== email)
    return error(env, id, 404, "RECOVERY_ACCOUNT_NOT_FOUND", "Employee No and verified Email do not match an enabled account.");
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return error(env, id, 503, "OTP_NOT_CONFIGURED", "Email verification is not configured.");
  let sender: ReturnType<typeof createEmailSender>;
  try {
    sender = createEmailSender({
      provider: env.EMAIL_PROVIDER,
      brevoApiKey: env.BREVO_API_KEY,
      resendApiKey: env.RESEND_API_KEY,
      from: env.EMAIL_FROM,
    });
  } catch {
    return error(env, id, 503, "EMAIL_PROVIDER_NOT_CONFIGURED", "Email delivery is not configured.");
  }

  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after FROM email_otp_challenges
      WHERE purpose = ?1 AND scope_key = ?2 ORDER BY created_at DESC LIMIT 1`
  ).bind(PURPOSE, scope(device, employee)).first<{ resend_after: string }>();
  if (latest && latest.resend_after > nowText) {
    const seconds = Math.max(1, Math.ceil((Date.parse(latest.resend_after) - now.getTime()) / 1000));
    return json(env, id, 429, {
      retryAfterSeconds: seconds,
      error: { code: "OTP_RESEND_COOLDOWN", message: "Please wait before requesting another verification code." },
    }, { "retry-after": seconds.toString() });
  }
  const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS count FROM email_otp_challenges
      WHERE purpose = ?1 AND email_normalized = ?2 AND created_at >= ?3`
  ).bind(PURPOSE, employee.email_normalized, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= MAX_PER_HOUR)
    return error(env, id, 429, "OTP_RATE_LIMITED", "Too many verification requests. Please try again later.");

  const challengeId = `otp_${crypto.randomUUID()}`;
  const code = otpCode();
  const expiresAt = new Date(now.getTime() + TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + COOLDOWN_MS).toISOString();
  await env.DB.prepare(
    `INSERT INTO email_otp_challenges (
        challenge_id, purpose, scope_key, email_normalized, otp_digest,
        delivery_state, attempt_count, max_attempts, expires_at, resend_after,
        created_at, updated_at
     ) VALUES (?1, ?2, ?3, ?4, ?5, 'pending', 0, ?6, ?7, ?8, ?9, ?9)`
  ).bind(challengeId, PURPOSE, scope(device, employee), employee.email_normalized,
    await hmacHex(pepper, `${challengeId}:${code}`), MAX_ATTEMPTS, expiresAt, resendAfter, nowText).run();
  try {
    await sender.send({
      to: employee.email_normalized,
      subject: "CYInvoice 密碼復原驗證碼",
      text: `您正在重設 CYInvoice 帳號 ${employee.employee_no} 的密碼。驗證碼是 ${code}，10 分鐘內有效。若非本人操作，請忽略此信。`,
      tags: [{ name: "purpose", value: "employee-password-recovery" }],
    });
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
  } catch {
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'failed', updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
    return error(env, id, 503, "EMAIL_DELIVERY_FAILED", "Verification Email could not be delivered.");
  }
  return json(env, id, 201, {
    challenge: { challengeId, maskedEmail: masked(employee.email_normalized), expiresAt, resendAfter },
  });
}

async function confirm(request: Request, env: Env, id: string): Promise<Response> {
  const device = await deviceFor(request, env);
  if (!device) return error(env, id, 401, "UNAUTHORIZED", "Device authentication failed.");
  if (device.employee_authority_state !== "cloud")
    return error(env, id, 409, "EMPLOYEE_AUTHORITY_NOT_READY", "Cloud Employee authority is not ready.");
  const body = await bodyFor(request);
  const number = employeeNo(body?.employeeNo);
  const challengeId = typeof body?.challengeId === "string" && /^otp_[0-9a-f-]{36}$/.test(body.challengeId.trim().toLowerCase())
    ? body.challengeId.trim().toLowerCase() : null;
  const code = typeof body?.otp === "string" && /^\d{6}$/.test(body.otp.trim()) ? body.otp.trim() : null;
  const credential = verifier(body?.credentialVerifier);
  if (!number || !challengeId || !code || !credential)
    return error(env, id, 400, "INVALID_PASSWORD_RECOVERY_REQUEST", "Password recovery request is invalid.");
  const employee = await employeeFor(env, device, number);
  if (!employee || employee.enabled !== 1 || !employee.email_verified_at)
    return error(env, id, 404, "RECOVERY_ACCOUNT_NOT_FOUND", "An enabled Employee with verified Email was not found.");
  const challenge = await env.DB.prepare(
    `SELECT email_normalized, otp_digest, delivery_state, attempt_count,
            max_attempts, expires_at, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2 AND scope_key = ?3 LIMIT 1`
  ).bind(challengeId, PURPOSE, scope(device, employee)).first<Challenge>();
  const now = new Date().toISOString();
  if (!challenge || challenge.email_normalized !== employee.email_normalized)
    return error(env, id, 400, "OTP_INVALID", "Verification code is invalid.");
  if (challenge.consumed_at)
    return error(env, id, 409, "OTP_ALREADY_USED", "Verification code has already been used.");
  if (challenge.delivery_state !== "sent" || challenge.expires_at <= now)
    return error(env, id, 410, "OTP_EXPIRED", "Verification code has expired.");
  if (challenge.attempt_count >= challenge.max_attempts)
    return error(env, id, 429, "OTP_ATTEMPTS_EXHAUSTED", "Verification code attempts are exhausted.");
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return error(env, id, 503, "OTP_NOT_CONFIGURED", "Email verification is not configured.");
  if (!equalHex(await hmacHex(pepper, `${challengeId}:${code}`), challenge.otp_digest)) {
    const nextAttempts = challenge.attempt_count + 1;
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET attempt_count = ?1, updated_at = ?2 WHERE challenge_id = ?3`
    ).bind(nextAttempts, now, challengeId).run();
    return error(env, id, nextAttempts >= challenge.max_attempts ? 429 : 400,
      nextAttempts >= challenge.max_attempts ? "OTP_ATTEMPTS_EXHAUSTED" : "OTP_INVALID",
      nextAttempts >= challenge.max_attempts ? "Verification code attempts are exhausted." : "Verification code is invalid.");
  }

  const results = await env.DB.batch([
    env.DB.prepare(
      `UPDATE cloud_employees
          SET credential_verifier = ?1, credential_algorithm = 'pbkdf2-sha256',
              credential_version = credential_version + 1, credential_updated_at = ?2,
              revision = revision + 1, updated_at = ?2
        WHERE workspace_id = ?3 AND employee_id = ?4 AND credential_version = ?5
          AND email_normalized = ?6 AND email_verified_at IS NOT NULL AND enabled = 1
          AND EXISTS (
            SELECT 1 FROM email_otp_challenges
             WHERE challenge_id = ?7 AND consumed_at IS NULL AND delivery_state = 'sent'
               AND expires_at > ?2 AND attempt_count < max_attempts)`
    ).bind(credential, now, device.workspace_id, employee.employee_id,
      employee.credential_version, employee.email_normalized, challengeId),
    env.DB.prepare(
      `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2 AND consumed_at IS NULL`
    ).bind(now, challengeId),
    env.DB.prepare(
      `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
        WHERE workspace_id = ?2 AND EXISTS (
          SELECT 1 FROM cloud_employees WHERE employee_id = ?3
            AND credential_version = ?4 AND credential_updated_at = ?1)`
    ).bind(now, device.workspace_id, employee.employee_id, employee.credential_version + 1),
  ]);
  if (results[0].meta.changes !== 1)
    return error(env, id, 409, "RECOVERY_STATE_CHANGED", "Account or verification state changed. Start recovery again.");
  return json(env, id, 200, { passwordReset: true });
}

export async function handleEmployeePasswordRecovery(request: Request, env: Env): Promise<Response | null> {
  const path = new URL(request.url).pathname;
  if (request.method !== "POST" || !path.startsWith("/v1/employees/password-recovery/")) return null;
  const id = requestId(request);
  try {
    if (path === "/v1/employees/password-recovery/challenge") return await start(request, env, id);
    if (path === "/v1/employees/password-recovery/confirm") return await confirm(request, env, id);
    return error(env, id, 404, "NOT_FOUND", "Route not found.");
  } catch (problem) {
    console.error("employee_password_recovery_failed", {
      requestId: id,
      path,
      error: problem instanceof Error ? problem.message : "unknown_error",
    });
    return error(env, id, 500, "EMPLOYEE_PASSWORD_RECOVERY_FAILED", "Password recovery could not be completed.");
  }
}
