import { createEmailSender } from "./email";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  WEB_PASSWORD_RESET_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_PASSWORD_RESET_IP_RATE_LIMIT: RateLimit;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type EmployeeRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  email_verified_at: string | null;
  role: string;
  enabled: number;
  credential_version: number;
};

type OtpRow = {
  challenge_id: string;
  email_normalized: string;
  otp_digest: string;
  delivery_state: string;
  attempt_count: number;
  max_attempts: number;
  expires_at: string;
  consumed_at: string | null;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.2";
const PURPOSE = "employee_password_reset";
const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_COOLDOWN_MS = 60 * 1000;
const OTP_HOURLY_LIMIT = 5;
const OTP_MAX_ATTEMPTS = 5;
const PASSWORD_ITERATIONS = 210_000;
const APPLICATION_POLICIES: Record<string, ReadonlySet<string>> = {
  CYAccountingWeb: new Set(["SUPER_ADMIN", "ADMIN"]),
};

function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(supplied)) return supplied;
  return crypto.randomUUID();
}

function json(env: Env, requestId: string, status: number, body: Record<string, JsonValue>, headers?: HeadersInit): Response {
  return new Response(JSON.stringify({
    ok: status >= 200 && status < 300,
    service: SERVICE_NAME,
    cloudVersion: CLOUD_VERSION,
    apiVersion: env.API_VERSION,
    environment: env.APP_ENV,
    requestId,
    timestamp: new Date().toISOString(),
    ...body,
  }), {
    status,
    headers: {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8",
      "x-content-type-options": "nosniff",
      "x-request-id": requestId,
      ...headers,
    },
  });
}

async function readJsonObject(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
  } catch {
    return null;
  }
}

function normalizeApplication(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return Object.prototype.hasOwnProperty.call(APPLICATION_POLICIES, normalized) ? normalized : null;
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizeChallengeId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^otp_[0-9a-f-]{36}$/.test(normalized) ? normalized : null;
}

function normalizeOtp(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{6}$/.test(normalized) ? normalized : null;
}

function normalizeNewPassword(value: unknown): string | null {
  if (typeof value !== "string") return null;
  return /^[A-Za-z0-9]{8,200}$/.test(value) ? value : null;
}

async function activeWorkspace(env: Env): Promise<string | null> {
  const result = await env.DB.prepare(
    `SELECT workspace_id FROM workspaces WHERE status = 'active' ORDER BY created_at, workspace_id LIMIT 2`
  ).all<{ workspace_id: string }>();
  const rows = result.results || [];
  return rows.length === 1 ? rows[0].workspace_id : null;
}

async function employeeAuthorityReady(env: Env, workspaceId: string): Promise<boolean> {
  const row = await env.DB.prepare(
    `SELECT 1 AS ready FROM devices
      WHERE workspace_id = ?1 AND status = 'active' AND employee_authority_state = 'cloud' LIMIT 1`
  ).bind(workspaceId).first<{ ready: number }>();
  return Boolean(row);
}

async function employeeByNo(env: Env, workspaceId: string, employeeNo: string): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT employee_id, workspace_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_version
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2 LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRow>();
}

async function rateLimit(request: Request, env: Env, application: string, employeeNo: string): Promise<boolean> {
  const employeeResult = await env.WEB_PASSWORD_RESET_EMPLOYEE_RATE_LIMIT.limit({
    key: `${application}:employee:${employeeNo}`,
  });
  if (!employeeResult.success) return false;
  const ip = request.headers.get("cf-connecting-ip")?.trim() || "unknown";
  const ipResult = await env.WEB_PASSWORD_RESET_IP_RATE_LIMIT.limit({ key: `${application}:ip:${ip}` });
  return ipResult.success;
}

function randomOtpCode(): string {
  const range = 1_000_000;
  const limit = Math.floor(0x1_0000_0000 / range) * range;
  const values = new Uint32Array(1);
  do crypto.getRandomValues(values); while (values[0] >= limit);
  return (values[0] % range).toString().padStart(6, "0");
}

function maskedEmail(email: string): string {
  const at = email.lastIndexOf("@");
  if (at <= 0) return "***";
  const local = email.slice(0, at);
  const domain = email.slice(at + 1);
  const visible = local.length <= 2 ? local[0] ?? "*" : local.slice(0, 2);
  return `${visible}***@${domain}`;
}

async function hmacSha256Hex(secret: string, value: string): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value)));
  return toHex(signature);
}

function constantTimeHexEquals(left: string, right: string): boolean {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  return difference === 0;
}

function toHex(bytes: Uint8Array): string {
  return Array.from(bytes, part => part.toString(16).padStart(2, "0")).join("");
}

async function createCredentialVerifier(password: string): Promise<string> {
  const salt = new Uint8Array(16);
  crypto.getRandomValues(salt);
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
  const hash = new Uint8Array(await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: Uint8Array.from(salt).buffer, iterations: PASSWORD_ITERATIONS },
    key,
    256,
  ));
  return `pbkdf2-sha256$${PASSWORD_ITERATIONS}$${toHex(salt)}$${toHex(hash)}`;
}

function configuredEmailSender(env: Env): ReturnType<typeof createEmailSender> | null {
  try {
    return createEmailSender({
      provider: env.EMAIL_PROVIDER,
      brevoApiKey: env.BREVO_API_KEY,
      resendApiKey: env.RESEND_API_KEY,
      from: env.EMAIL_FROM,
    });
  } catch {
    return null;
  }
}

function resetScope(application: string, workspaceId: string, employeeId: string): string {
  return `${application}:${workspaceId}:${employeeId}`;
}

async function startReset(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await readJsonObject(request);
  const application = normalizeApplication(body?.application);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  if (!application || !employeeNo) {
    return json(env, requestId, 400, { error: { code: "INVALID_PASSWORD_RESET_REQUEST", message: "Password reset request is invalid." } });
  }
  if (!await rateLimit(request, env, application, employeeNo)) {
    return json(env, requestId, 429, { error: { code: "PASSWORD_RESET_RATE_LIMITED", message: "Too many password reset requests." } }, { "retry-after": "60" });
  }

  const workspaceId = await activeWorkspace(env);
  if (!workspaceId || !await employeeAuthorityReady(env, workspaceId)) {
    return json(env, requestId, 503, { error: { code: "WEB_AUTH_NOT_READY", message: "Central Employee authority is not ready." } });
  }
  const employee = await employeeByNo(env, workspaceId, employeeNo);
  const allowed = Boolean(employee
    && employee.enabled === 1
    && employee.email_verified_at
    && employee.email_normalized
    && APPLICATION_POLICIES[application].has(employee.role));
  if (!employee || !allowed) {
    return json(env, requestId, 400, { error: { code: "PASSWORD_RESET_UNAVAILABLE", message: "Password reset is not available for this account." } });
  }

  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return json(env, requestId, 503, { error: { code: "OTP_NOT_CONFIGURED", message: "Email OTP is not configured." } });
  const sender = configuredEmailSender(env);
  if (!sender) return json(env, requestId, 503, { error: { code: "EMAIL_PROVIDER_NOT_CONFIGURED", message: "Email delivery provider is not configured." } });

  const scope = resetScope(application, workspaceId, employee.employee_id);
  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after FROM email_otp_challenges
      WHERE purpose = ?1 AND scope_key = ?2 AND email_normalized = ?3
      ORDER BY created_at DESC LIMIT 1`
  ).bind(PURPOSE, scope, employee.email_normalized).first<{ resend_after: string }>();
  if (latest && latest.resend_after > nowText) {
    const seconds = Math.max(1, Math.ceil((Date.parse(latest.resend_after) - now.getTime()) / 1000));
    return json(env, requestId, 429, {
      retryAfterSeconds: seconds,
      error: { code: "OTP_RESEND_COOLDOWN", message: "Please wait before requesting another verification code." },
    }, { "retry-after": seconds.toString() });
  }
  const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS count FROM email_otp_challenges
      WHERE purpose = ?1 AND email_normalized = ?2 AND created_at >= ?3`
  ).bind(PURPOSE, employee.email_normalized, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= OTP_HOURLY_LIMIT) {
    return json(env, requestId, 429, { error: { code: "OTP_RATE_LIMITED", message: "Too many verification requests." } });
  }

  const challengeId = `otp_${crypto.randomUUID()}`;
  const code = randomOtpCode();
  const digest = await hmacSha256Hex(pepper, `${challengeId}:${code}`);
  const expiresAt = new Date(now.getTime() + OTP_TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + OTP_RESEND_COOLDOWN_MS).toISOString();
  await env.DB.batch([
    env.DB.prepare(
      `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1
        WHERE purpose = ?2 AND scope_key = ?3 AND consumed_at IS NULL`
    ).bind(nowText, PURPOSE, scope),
    env.DB.prepare(
      `INSERT INTO email_otp_challenges (
        challenge_id, purpose, scope_key, email_normalized, otp_digest,
        delivery_state, attempt_count, max_attempts, expires_at, resend_after,
        created_at, updated_at
      ) VALUES (?1, ?2, ?3, ?4, ?5, 'pending', 0, ?6, ?7, ?8, ?9, ?9)`
    ).bind(challengeId, PURPOSE, scope, employee.email_normalized, digest, OTP_MAX_ATTEMPTS, expiresAt, resendAfter, nowText),
  ]);

  try {
    await sender.send({
      to: employee.email_normalized,
      subject: "CY 員工帳號密碼重設驗證碼",
      text: `您正在重設 CY 員工帳號 ${employee.employee_no} ${employee.name} 的密碼。驗證碼是 ${code}，10 分鐘內有效。若非本人操作，請忽略此信。`,
      tags: [{ name: "purpose", value: "employee-password-reset" }],
    });
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
  } catch {
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'failed', updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
    return json(env, requestId, 503, { error: { code: "EMAIL_DELIVERY_FAILED", message: "Verification email could not be delivered." } });
  }

  return json(env, requestId, 201, {
    challenge: { challengeId, maskedEmail: maskedEmail(employee.email_normalized), expiresAt, resendAfter },
  });
}

async function confirmReset(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await readJsonObject(request);
  const application = normalizeApplication(body?.application);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const challengeId = normalizeChallengeId(body?.challengeId);
  const otp = normalizeOtp(body?.otp);
  const newPassword = normalizeNewPassword(body?.newPassword);
  if (!application || !employeeNo || !challengeId || !otp || !newPassword) {
    return json(env, requestId, 400, { error: { code: "INVALID_PASSWORD_RESET_CONFIRMATION", message: "Password reset confirmation is invalid." } });
  }
  if (!await rateLimit(request, env, application, employeeNo)) {
    return json(env, requestId, 429, { error: { code: "PASSWORD_RESET_RATE_LIMITED", message: "Too many password reset requests." } }, { "retry-after": "60" });
  }

  const workspaceId = await activeWorkspace(env);
  if (!workspaceId || !await employeeAuthorityReady(env, workspaceId)) {
    return json(env, requestId, 503, { error: { code: "WEB_AUTH_NOT_READY", message: "Central Employee authority is not ready." } });
  }
  const employee = await employeeByNo(env, workspaceId, employeeNo);
  if (!employee || employee.enabled !== 1 || !employee.email_verified_at || !APPLICATION_POLICIES[application].has(employee.role)) {
    return json(env, requestId, 400, { error: { code: "PASSWORD_RESET_UNAVAILABLE", message: "Password reset is not available for this account." } });
  }
  const scope = resetScope(application, workspaceId, employee.employee_id);
  const challenge = await env.DB.prepare(
    `SELECT challenge_id, email_normalized, otp_digest, delivery_state,
            attempt_count, max_attempts, expires_at, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2 AND scope_key = ?3 LIMIT 1`
  ).bind(challengeId, PURPOSE, scope).first<OtpRow>();

  const now = new Date();
  const nowText = now.toISOString();
  if (!challenge || challenge.email_normalized !== employee.email_normalized) {
    return json(env, requestId, 400, { error: { code: "OTP_INVALID", message: "Verification code is invalid." } });
  }
  if (challenge.consumed_at) return json(env, requestId, 409, { error: { code: "OTP_ALREADY_USED", message: "Verification code has already been used." } });
  if (challenge.delivery_state !== "sent" || challenge.expires_at <= nowText) {
    return json(env, requestId, 410, { error: { code: "OTP_EXPIRED", message: "Verification code has expired." } });
  }
  if (challenge.attempt_count >= challenge.max_attempts) {
    return json(env, requestId, 429, { error: { code: "OTP_ATTEMPTS_EXCEEDED", message: "Verification code attempts are exhausted." } });
  }
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return json(env, requestId, 503, { error: { code: "OTP_NOT_CONFIGURED", message: "Email OTP is not configured." } });
  const digest = await hmacSha256Hex(pepper, `${challengeId}:${otp}`);
  if (!constantTimeHexEquals(digest, challenge.otp_digest)) {
    const attempts = challenge.attempt_count + 1;
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET attempt_count = ?1, updated_at = ?2 WHERE challenge_id = ?3`
    ).bind(attempts, nowText, challengeId).run();
    return json(env, requestId, attempts >= challenge.max_attempts ? 429 : 400, {
      error: {
        code: attempts >= challenge.max_attempts ? "OTP_ATTEMPTS_EXCEEDED" : "OTP_INVALID",
        message: attempts >= challenge.max_attempts ? "Verification code attempts are exhausted." : "Verification code is invalid.",
      },
    });
  }

  const verifier = await createCredentialVerifier(newPassword);
  const consumeMarker = nowText;
  await env.DB.batch([
    env.DB.prepare(
      `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2 AND consumed_at IS NULL`
    ).bind(consumeMarker, challengeId),
    env.DB.prepare(
      `UPDATE cloud_employees
          SET credential_verifier = ?1,
              credential_algorithm = 'pbkdf2-sha256',
              credential_version = credential_version + 1,
              credential_updated_at = ?2,
              revision = revision + 1,
              updated_at = ?2
        WHERE workspace_id = ?3 AND employee_id = ?4
          AND EXISTS (SELECT 1 FROM email_otp_challenges WHERE challenge_id = ?5 AND consumed_at = ?2)`
    ).bind(verifier, consumeMarker, workspaceId, employee.employee_id, challengeId),
    env.DB.prepare(
      `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
        WHERE workspace_id = ?2
          AND EXISTS (SELECT 1 FROM email_otp_challenges WHERE challenge_id = ?3 AND consumed_at = ?1)`
    ).bind(consumeMarker, workspaceId, challengeId),
  ]);
  const updated = await employeeByNo(env, workspaceId, employeeNo);
  if (!updated || updated.credential_version <= employee.credential_version) {
    return json(env, requestId, 409, { error: { code: "OTP_ALREADY_USED", message: "Verification code has already been used." } });
  }
  return json(env, requestId, 200, { passwordReset: true, credentialVersion: updated.credential_version });
}

export async function handleWebPasswordRecovery(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "POST" && url.pathname === "/v1/web-auth/password-reset/challenge") {
      return await startReset(request, env, requestId);
    }
    if (request.method === "POST" && url.pathname === "/v1/web-auth/password-reset/confirm") {
      return await confirmReset(request, env, requestId);
    }
    return null;
  } catch (error) {
    console.error("web_password_recovery_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, {
      error: { code: "WEB_PASSWORD_RECOVERY_FAILED", message: "Password recovery could not be completed." },
    });
  }
}
