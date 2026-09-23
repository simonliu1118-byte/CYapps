import { createEmailSender } from "./email";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceRow = {
  device_id: string;
  workspace_id: string;
  employee_authority_state: string;
  recovery_email: string | null;
  recovery_email_verified_at: string | null;
};

type EmployeeRow = {
  employee_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  email_verified_at: string | null;
  role: string;
  enabled: number;
  credential_verifier: string | null;
  credential_algorithm: string | null;
  credential_version: number;
  revision: number;
};

type OtpRow = {
  challenge_id: string;
  email_normalized: string;
  otp_digest: string;
  delivery_state: string;
  attempt_count: number;
  max_attempts: number;
  expires_at: string;
  resend_after: string;
  consumed_at: string | null;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.2";
const TRANSFER_PURPOSE = "super_admin_transfer_authorization";
const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_COOLDOWN_MS = 60 * 1000;
const OTP_HOURLY_LIMIT = 5;
const OTP_MAX_ATTEMPTS = 5;

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

async function sha256Hex(value: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

async function hmacSha256Hex(secret: string, value: string): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]);
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value)));
  return Array.from(signature, part => part.toString(16).padStart(2, "0")).join("");
}

function constantTimeHexEquals(left: string, right: string): boolean {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1)
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  return difference === 0;
}

function bearerToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  const token = match?.[1]?.trim().toLowerCase() ?? "";
  return /^cydev_[0-9a-f]{64}$/.test(token) ? token : null;
}

async function authenticateDevice(request: Request, env: Env): Promise<DeviceRow | null> {
  const token = bearerToken(request);
  if (!token) return null;
  const tokenHash = await sha256Hex(token);
  return env.DB.prepare(
    `SELECT d.device_id, d.workspace_id, d.employee_authority_state,
            w.recovery_email, w.recovery_email_verified_at
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1 AND d.status = 'active' AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<DeviceRow>();
}

async function readJsonObject(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    return value as Record<string, unknown>;
  } catch {
    return null;
  }
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizePassword(value: unknown): string | null {
  if (typeof value !== "string" || value.length < 1 || value.length > 200) return null;
  return value;
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

function fromHex(value: string): Uint8Array | null {
  if (value.length === 0 || value.length % 2 !== 0 || !/^[0-9a-f]+$/i.test(value)) return null;
  const bytes = new Uint8Array(value.length / 2);
  for (let index = 0; index < bytes.length; index += 1) {
    const parsed = Number.parseInt(value.slice(index * 2, index * 2 + 2), 16);
    if (!Number.isFinite(parsed)) return null;
    bytes[index] = parsed;
  }
  return bytes;
}

async function verifyPassword(password: string, verifier: string): Promise<boolean> {
  const parts = verifier.split("$");
  if (parts.length !== 4 || parts[0] !== "pbkdf2-sha256") return false;
  const iterations = Number.parseInt(parts[1], 10);
  const salt = fromHex(parts[2]);
  const expected = fromHex(parts[3]);
  if (!Number.isInteger(iterations) || iterations < 100_000 || iterations > 2_000_000
      || !salt || salt.length < 16 || !expected || expected.length !== 32) return false;

  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"]);
  const actual = new Uint8Array(await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: Uint8Array.from(salt).buffer, iterations },
    key,
    expected.length * 8));
  let difference = 0;
  for (let index = 0; index < expected.length; index += 1)
    difference |= actual[index] ^ expected[index];
  return difference === 0;
}

async function employeeByNo(env: Env, workspaceId: string, employeeNo: string): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_algorithm,
            credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRow>();
}

async function requireCurrentSuperAdmin(
  env: Env,
  device: DeviceRow,
  employeeNo: string,
  password: string,
): Promise<EmployeeRow | null> {
  if (device.employee_authority_state !== "cloud") return null;
  const employee = await employeeByNo(env, device.workspace_id, employeeNo);
  if (!employee || employee.role !== "SUPER_ADMIN" || employee.enabled !== 1
      || !employee.email_verified_at
      || employee.credential_algorithm !== "pbkdf2-sha256" || !employee.credential_verifier)
    return null;
  return await verifyPassword(password, employee.credential_verifier) ? employee : null;
}

async function requireTargetAdmin(env: Env, device: DeviceRow, employeeNo: string): Promise<EmployeeRow | null> {
  const employee = await employeeByNo(env, device.workspace_id, employeeNo);
  if (!employee || employee.role !== "ADMIN" || employee.enabled !== 1
      || !employee.email_verified_at
      || employee.credential_algorithm !== "pbkdf2-sha256" || !employee.credential_verifier
      || employee.credential_version < 1)
    return null;
  return employee;
}

function employeeJson(employee: EmployeeRow): Record<string, JsonValue> {
  return {
    employeeId: employee.employee_id,
    employeeNo: employee.employee_no,
    name: employee.name,
    email: employee.email_normalized,
    emailVerified: Boolean(employee.email_verified_at),
    role: employee.role,
    enabled: employee.enabled === 1,
    credentialReady: Boolean(employee.credential_verifier),
    credentialVersion: employee.credential_version,
    revision: employee.revision,
  };
}

function maskedEmail(email: string): string {
  const at = email.lastIndexOf("@");
  if (at <= 0) return "***";
  const local = email.slice(0, at);
  const domain = email.slice(at + 1);
  const visible = local.length <= 2 ? local[0] ?? "*" : local.slice(0, 2);
  return `${visible}***@${domain}`;
}

function randomOtpCode(): string {
  const range = 1_000_000;
  const limit = Math.floor(0x1_0000_0000 / range) * range;
  const values = new Uint32Array(1);
  do crypto.getRandomValues(values); while (values[0] >= limit);
  return (values[0] % range).toString().padStart(6, "0");
}

function transferScope(device: DeviceRow, actor: EmployeeRow, target: EmployeeRow): string {
  return `${device.workspace_id}:${actor.employee_id}:${target.employee_id}`;
}

async function configuredEmailSender(env: Env): Promise<ReturnType<typeof createEmailSender> | null> {
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

async function startTransferChallenge(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });

  const body = await readJsonObject(request);
  const actorNo = normalizeEmployeeNo(body?.actorEmployeeNo);
  const password = normalizePassword(body?.actorPassword);
  const targetNo = normalizeEmployeeNo(body?.targetEmployeeNo);
  if (!actorNo || !password || !targetNo)
    return json(env, requestId, 400, { error: { code: "INVALID_TRANSFER_REQUEST", message: "SUPER_ADMIN credentials and target Employee No are required." } });
  if (actorNo === targetNo)
    return json(env, requestId, 409, { error: { code: "TRANSFER_TARGET_INVALID", message: "SUPER_ADMIN cannot transfer the role to the same Employee." } });

  const actor = await requireCurrentSuperAdmin(env, device, actorNo, password);
  if (!actor)
    return json(env, requestId, 403, { error: { code: "SUPER_ADMIN_REQUIRED", message: "Current Workspace SUPER_ADMIN authentication failed." } });
  const target = await requireTargetAdmin(env, device, targetNo);
  if (!target)
    return json(env, requestId, 409, { error: { code: "TRANSFER_TARGET_NOT_READY", message: "Target must be an enabled ADMIN with verified Email and a ready credential." } });

  if (!device.recovery_email || !device.recovery_email_verified_at
      || device.recovery_email.trim().toLowerCase() !== actor.email_normalized) {
    return json(env, requestId, 409, {
      error: {
        code: "SUPER_ADMIN_RECOVERY_EMAIL_MISMATCH",
        message: "Workspace Recovery Email must belong to the current verified SUPER_ADMIN before transfer."
      }
    });
  }

  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper)
    return json(env, requestId, 503, { error: { code: "OTP_NOT_CONFIGURED", message: "Email verification is not configured." } });
  const sender = await configuredEmailSender(env);
  if (!sender)
    return json(env, requestId, 503, { error: { code: "EMAIL_PROVIDER_NOT_CONFIGURED", message: "Email delivery is not configured." } });

  const scope = transferScope(device, actor, target);
  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after FROM email_otp_challenges
      WHERE purpose = ?1 AND scope_key = ?2 AND email_normalized = ?3
      ORDER BY created_at DESC LIMIT 1`
  ).bind(TRANSFER_PURPOSE, scope, actor.email_normalized).first<{ resend_after: string }>();
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
      WHERE purpose = ?1 AND scope_key = ?2 AND email_normalized = ?3 AND created_at >= ?4`
  ).bind(TRANSFER_PURPOSE, scope, actor.email_normalized, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= OTP_HOURLY_LIMIT)
    return json(env, requestId, 429, { error: { code: "OTP_RATE_LIMITED", message: "Too many verification requests. Please try again later." } });

  const challengeId = `otp_${crypto.randomUUID()}`;
  const code = randomOtpCode();
  const digest = await hmacSha256Hex(pepper, `${challengeId}:${code}`);
  const expiresAt = new Date(now.getTime() + OTP_TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + OTP_RESEND_COOLDOWN_MS).toISOString();

  await env.DB.prepare(
    `INSERT INTO email_otp_challenges (
        challenge_id, purpose, scope_key, email_normalized, otp_digest,
        delivery_state, attempt_count, max_attempts, expires_at, resend_after,
        created_at, updated_at
     ) VALUES (?1, ?2, ?3, ?4, ?5, 'pending', 0, ?6, ?7, ?8, ?9, ?9)`
  ).bind(challengeId, TRANSFER_PURPOSE, scope, actor.email_normalized, digest,
    OTP_MAX_ATTEMPTS, expiresAt, resendAfter, nowText).run();

  try {
    await sender.send({
      to: actor.email_normalized,
      subject: "CYInvoice 超級管理員移交驗證碼",
      text: `您正在將 CYInvoice Workspace 的超級管理員權限移交給 ${target.employee_no} ${target.name}。驗證碼是 ${code}，10 分鐘內有效。若非本人操作，請勿提供此驗證碼。`,
      tags: [{ name: "purpose", value: "super-admin-transfer" }],
    });
    await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
  } catch {
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'failed', updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
    return json(env, requestId, 503, { error: { code: "EMAIL_DELIVERY_FAILED", message: "Verification Email could not be delivered." } });
  }

  return json(env, requestId, 201, {
    challenge: {
      challengeId,
      maskedEmail: maskedEmail(actor.email_normalized),
      targetEmployee: employeeJson(target),
      expiresAt,
      resendAfter,
    },
  });
}

async function confirmTransfer(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });

  const body = await readJsonObject(request);
  const actorNo = normalizeEmployeeNo(body?.actorEmployeeNo);
  const password = normalizePassword(body?.actorPassword);
  const targetNo = normalizeEmployeeNo(body?.targetEmployeeNo);
  const suppliedChallengeId = normalizeChallengeId(body?.challengeId);
  const suppliedOtp = normalizeOtp(body?.otp);
  if (!actorNo || !password || !targetNo || !suppliedChallengeId || !suppliedOtp)
    return json(env, requestId, 400, { error: { code: "INVALID_TRANSFER_CONFIRMATION", message: "Transfer confirmation request is invalid." } });

  const actor = await requireCurrentSuperAdmin(env, device, actorNo, password);
  if (!actor)
    return json(env, requestId, 403, { error: { code: "SUPER_ADMIN_REQUIRED", message: "Current Workspace SUPER_ADMIN authentication failed." } });
  const target = await requireTargetAdmin(env, device, targetNo);
  if (!target)
    return json(env, requestId, 409, { error: { code: "TRANSFER_TARGET_NOT_READY", message: "Target must still be an enabled ADMIN with verified Email and a ready credential." } });

  if (!device.recovery_email || !device.recovery_email_verified_at
      || device.recovery_email.trim().toLowerCase() !== actor.email_normalized) {
    return json(env, requestId, 409, {
      error: { code: "SUPER_ADMIN_RECOVERY_EMAIL_MISMATCH", message: "Workspace Recovery Email no longer matches the current SUPER_ADMIN." },
    });
  }

  const scope = transferScope(device, actor, target);
  const challenge = await env.DB.prepare(
    `SELECT challenge_id, email_normalized, otp_digest, delivery_state,
            attempt_count, max_attempts, expires_at, resend_after, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2 AND scope_key = ?3
      LIMIT 1`
  ).bind(suppliedChallengeId, TRANSFER_PURPOSE, scope).first<OtpRow>();
  const now = new Date();
  const nowText = now.toISOString();
  if (!challenge || challenge.email_normalized !== actor.email_normalized)
    return json(env, requestId, 400, { error: { code: "OTP_INVALID", message: "Verification code is invalid." } });
  if (challenge.consumed_at)
    return json(env, requestId, 409, { error: { code: "OTP_ALREADY_USED", message: "Verification code has already been used." } });
  if (challenge.delivery_state !== "sent" || challenge.expires_at <= nowText)
    return json(env, requestId, 410, { error: { code: "OTP_EXPIRED", message: "Verification code has expired." } });
  if (challenge.attempt_count >= challenge.max_attempts)
    return json(env, requestId, 429, { error: { code: "OTP_ATTEMPTS_EXHAUSTED", message: "Verification code attempts are exhausted." } });

  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper)
    return json(env, requestId, 503, { error: { code: "OTP_NOT_CONFIGURED", message: "Email verification is not configured." } });
  const digest = await hmacSha256Hex(pepper, `${suppliedChallengeId}:${suppliedOtp}`);
  if (!constantTimeHexEquals(digest, challenge.otp_digest)) {
    const nextAttempts = challenge.attempt_count + 1;
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET attempt_count = ?1, updated_at = ?2 WHERE challenge_id = ?3`
    ).bind(nextAttempts, nowText, suppliedChallengeId).run();
    return json(env, requestId, nextAttempts >= challenge.max_attempts ? 429 : 400, {
      error: {
        code: nextAttempts >= challenge.max_attempts ? "OTP_ATTEMPTS_EXHAUSTED" : "OTP_INVALID",
        message: nextAttempts >= challenge.max_attempts ? "Verification code attempts are exhausted." : "Verification code is invalid."
      },
    });
  }

  const batch = await env.DB.batch([
    env.DB.prepare(
      `UPDATE cloud_employees
          SET role = 'ADMIN', revision = revision + 1, updated_at = ?1
        WHERE employee_id = ?2 AND workspace_id = ?3 AND role = 'SUPER_ADMIN' AND enabled = 1`
    ).bind(nowText, actor.employee_id, device.workspace_id),
    env.DB.prepare(
      `UPDATE cloud_employees
          SET role = 'SUPER_ADMIN', revision = revision + 1, updated_at = ?1
        WHERE employee_id = ?2 AND workspace_id = ?3 AND role = 'ADMIN' AND enabled = 1
          AND email_verified_at IS NOT NULL AND credential_verifier IS NOT NULL`
    ).bind(nowText, target.employee_id, device.workspace_id),
    env.DB.prepare(
      `UPDATE workspaces
          SET recovery_email = ?1,
              recovery_email_verified_at = ?2,
              employee_revision = employee_revision + 1,
              updated_at = ?3
        WHERE workspace_id = ?4 AND recovery_email = ?5`
    ).bind(target.email_normalized, target.email_verified_at, nowText, device.workspace_id, actor.email_normalized),
    env.DB.prepare(
      `UPDATE email_otp_challenges
          SET consumed_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2 AND consumed_at IS NULL`
    ).bind(nowText, suppliedChallengeId),
  ]);

  if (batch.some(result => !result.success))
    return json(env, requestId, 503, { error: { code: "SUPER_ADMIN_TRANSFER_FAILED", message: "SUPER_ADMIN transfer could not be committed." } });

  const [former, successor] = await Promise.all([
    employeeByNo(env, device.workspace_id, actor.employee_no),
    employeeByNo(env, device.workspace_id, target.employee_no),
  ]);
  if (!former || former.role !== "ADMIN" || !successor || successor.role !== "SUPER_ADMIN")
    return json(env, requestId, 500, { error: { code: "SUPER_ADMIN_TRANSFER_STATE_INVALID", message: "SUPER_ADMIN transfer state could not be verified." } });

  return json(env, requestId, 200, {
    transfer: {
      formerSuperAdmin: employeeJson(former),
      newSuperAdmin: employeeJson(successor),
      recoveryEmail: successor.email_normalized,
      completedAt: nowText,
    },
  });
}

export async function handleSuperAdminTransfer(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "POST" && url.pathname === "/v1/employees/super-admin-transfer/challenge")
      return await startTransferChallenge(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employees/super-admin-transfer/confirm")
      return await confirmTransfer(request, env, requestId);
    return null;
  } catch (error) {
    console.error("super_admin_transfer_request_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, {
      error: { code: "SUPER_ADMIN_TRANSFER_REQUEST_FAILED", message: "SUPER_ADMIN transfer request could not be completed." },
    });
  }
}
