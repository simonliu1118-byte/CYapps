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
  pairing_id: string | null;
  employee_authority_state: string;
  employee_transition_snapshot_hash: string | null;
  recovery_email: string | null;
  recovery_email_verified_at: string | null;
};

type TransitionItemRow = {
  transition_item_id: string;
  local_employee_no: string;
  local_name: string;
  local_email_normalized: string;
  local_role: string;
  local_enabled: number;
  suggested_cloud_role: string;
  state: string;
  match_kind: string;
  matched_employee_id: string | null;
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
const CLOUD_VERSION = "0.8.5";
const EMAIL_PURPOSE = "employee_email_verification";
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
    `SELECT d.device_id, d.workspace_id, d.pairing_id, d.employee_authority_state,
            d.employee_transition_snapshot_hash,
            w.recovery_email, w.recovery_email_verified_at
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
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

function employeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function snapshotHash(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^[0-9a-f]{64}$/.test(normalized) ? normalized : null;
}

function challengeId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^otp_[0-9a-f-]{36}$/.test(normalized) ? normalized : null;
}

function otp(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{6}$/.test(normalized) ? normalized : null;
}

function credentialVerifier(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  const match = /^pbkdf2-sha256\$(\d+)\$([0-9a-f]+)\$([0-9a-f]{64})$/.exec(normalized);
  if (!match) return null;
  const iterations = Number(match[1]);
  if (!Number.isInteger(iterations) || iterations < 100_000 || iterations > 2_000_000) return null;
  if (match[2].length < 32 || match[2].length > 128 || match[2].length % 2 !== 0) return null;
  return normalized;
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

function scopeKey(device: DeviceRow, localEmployeeNo: string): string {
  return `${device.workspace_id}:${device.device_id}:${localEmployeeNo}`;
}

async function loadTransitionItem(env: Env, deviceId: string, localEmployeeNo: string): Promise<TransitionItemRow | null> {
  return env.DB.prepare(
    `SELECT transition_item_id, local_employee_no, local_name, local_email_normalized,
            local_role, local_enabled, suggested_cloud_role, state, match_kind,
            matched_employee_id
       FROM employee_transition_items
      WHERE device_id = ?1 AND local_employee_no = ?2
      LIMIT 1`
  ).bind(deviceId, localEmployeeNo).first<TransitionItemRow>();
}

async function loadEmployee(env: Env, employeeId: string | null): Promise<EmployeeRow | null> {
  if (!employeeId) return null;
  return env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_algorithm,
            credential_version, revision
       FROM cloud_employees
      WHERE employee_id = ?1
      LIMIT 1`
  ).bind(employeeId).first<EmployeeRow>();
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
    credentialVersion: Number(employee.credential_version ?? 0),
    revision: Number(employee.revision ?? 1),
  };
}

function verifySnapshot(device: DeviceRow, supplied: string | null): boolean {
  return Boolean(supplied && device.employee_transition_snapshot_hash === supplied);
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

async function beginEmailChallenge(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  if (device.employee_authority_state !== "transitioning")
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_AUTHORITY_ALREADY_CLOUD", message: "Employee transition is already complete." } });

  const body = await readJsonObject(request);
  if (!body) return json(env, requestId, 400, { error: { code: "INVALID_JSON", message: "A JSON object is required." } });
  const localEmployeeNo = employeeNo(body.employeeNo);
  const suppliedSnapshot = snapshotHash(body.snapshotHash);
  if (!localEmployeeNo || !verifySnapshot(device, suppliedSnapshot))
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED", message: "Run transition inspection again before continuing." } });

  const item = await loadTransitionItem(env, device.device_id, localEmployeeNo);
  if (!item) return json(env, requestId, 404, { error: { code: "TRANSITION_ITEM_NOT_FOUND", message: "Transition item was not found." } });
  if (item.state === "conflict" || item.state === "bootstrap_owner_pending" || item.state === "ready")
    return json(env, requestId, 409, { error: { code: "EMAIL_VERIFICATION_NOT_REQUIRED", message: "This transition item cannot start Employee Email verification in its current state." } });

  const matched = await loadEmployee(env, item.matched_employee_id);
  if (matched) {
    if (matched.email_normalized !== item.local_email_normalized)
      return json(env, requestId, 409, { error: { code: "TRANSITION_IDENTITY_CHANGED", message: "The matched Cloud Employee no longer matches this transition item." } });
    if (matched.email_verified_at)
      return json(env, requestId, 409, { error: { code: "EMAIL_ALREADY_VERIFIED", message: "The matched Cloud Employee Email is already verified." } });
  } else if (item.state !== "new_email_pending") {
    return json(env, requestId, 409, { error: { code: "TRANSITION_ITEM_NOT_READY", message: "This transition item must be inspected again." } });
  }

  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return json(env, requestId, 503, { error: { code: "OTP_NOT_CONFIGURED", message: "Email verification is not configured." } });
  const sender = await configuredEmailSender(env);
  if (!sender) return json(env, requestId, 503, { error: { code: "EMAIL_PROVIDER_NOT_CONFIGURED", message: "Email delivery is not configured." } });

  const scope = scopeKey(device, localEmployeeNo);
  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after FROM email_otp_challenges
      WHERE purpose = ?1 AND scope_key = ?2 AND email_normalized = ?3
      ORDER BY created_at DESC LIMIT 1`
  ).bind(EMAIL_PURPOSE, scope, item.local_email_normalized).first<{ resend_after: string }>();
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
  ).bind(EMAIL_PURPOSE, scope, item.local_email_normalized, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= OTP_HOURLY_LIMIT)
    return json(env, requestId, 429, { error: { code: "OTP_RATE_LIMITED", message: "Too many verification requests. Please try again later." } });

  const id = `otp_${crypto.randomUUID()}`;
  const code = randomOtpCode();
  const digest = await hmacSha256Hex(pepper, `${id}:${code}`);
  const expiresAt = new Date(now.getTime() + OTP_TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + OTP_RESEND_COOLDOWN_MS).toISOString();

  await env.DB.prepare(
    `INSERT INTO email_otp_challenges (
        challenge_id, purpose, scope_key, email_normalized, otp_digest,
        delivery_state, attempt_count, max_attempts, expires_at, resend_after,
        created_at, updated_at
     ) VALUES (?1, ?2, ?3, ?4, ?5, 'pending', 0, ?6, ?7, ?8, ?9, ?9)`
  ).bind(id, EMAIL_PURPOSE, scope, item.local_email_normalized, digest, OTP_MAX_ATTEMPTS, expiresAt, resendAfter, nowText).run();

  try {
    await sender.send({
      to: item.local_email_normalized,
      subject: "CYInvoice Email 驗證碼",
      text: `您的 CYInvoice Email 驗證碼是 ${code}。驗證碼 10 分鐘內有效；若您沒有進行帳號轉換，請忽略此信。`,
      tags: [{ name: "purpose", value: "employee-verification" }],
    });
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2 AND delivery_state = 'pending'`
    ).bind(new Date().toISOString(), id).run();
  } catch (error) {
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'failed', updated_at = ?1
        WHERE challenge_id = ?2 AND delivery_state = 'pending'`
    ).bind(new Date().toISOString(), id).run();
    console.warn("employee_transition_email_delivery_failed", {
      requestId,
      deviceId: device.device_id,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 502, { error: { code: "EMAIL_DELIVERY_FAILED", message: "Verification Email could not be delivered." } });
  }

  return json(env, requestId, 201, {
    challenge: {
      challengeId: id,
      maskedEmail: maskedEmail(item.local_email_normalized),
      expiresAt,
      resendAfter,
    },
  });
}

async function readValidChallenge(
  env: Env,
  device: DeviceRow,
  item: TransitionItemRow,
  challenge: string,
): Promise<OtpRow | null> {
  return env.DB.prepare(
    `SELECT challenge_id, email_normalized, otp_digest, delivery_state,
            attempt_count, max_attempts, expires_at, resend_after, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2 AND scope_key = ?3 AND email_normalized = ?4
      LIMIT 1`
  ).bind(challenge, EMAIL_PURPOSE, scopeKey(device, item.local_employee_no), item.local_email_normalized).first<OtpRow>();
}

async function verifyChallengeCode(
  env: Env,
  row: OtpRow,
  code: string,
): Promise<"ok" | "expired" | "used" | "attempts" | "invalid" | "unavailable"> {
  if (row.delivery_state !== "sent") return "unavailable";
  if (row.consumed_at) return "used";
  if (row.expires_at <= new Date().toISOString()) return "expired";
  if (row.attempt_count >= row.max_attempts) return "attempts";
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return "unavailable";
  const digest = await hmacSha256Hex(pepper, `${row.challenge_id}:${code}`);
  if (constantTimeHexEquals(digest, row.otp_digest)) return "ok";
  await env.DB.prepare(
    `UPDATE email_otp_challenges SET attempt_count = attempt_count + 1, updated_at = ?1
      WHERE challenge_id = ?2 AND consumed_at IS NULL`
  ).bind(new Date().toISOString(), row.challenge_id).run();
  return row.attempt_count + 1 >= row.max_attempts ? "attempts" : "invalid";
}

function otpError(env: Env, requestId: string, state: string): Response {
  const mapping: Record<string, [number, string, string]> = {
    expired: [409, "OTP_EXPIRED", "Email verification code has expired."],
    used: [409, "OTP_ALREADY_USED", "Email verification code has already been used."],
    attempts: [429, "OTP_ATTEMPTS_EXCEEDED", "Email verification attempts have been exceeded."],
    invalid: [400, "OTP_INVALID", "Email verification code is invalid."],
    unavailable: [503, "OTP_UNAVAILABLE", "Email verification is temporarily unavailable."],
  };
  const [status, code, message] = mapping[state] ?? mapping.unavailable;
  return json(env, requestId, status, { error: { code, message } });
}

async function completeBootstrapOwner(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  if (device.employee_authority_state !== "transitioning")
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_AUTHORITY_ALREADY_CLOUD", message: "Employee transition is already complete." } });

  const body = await readJsonObject(request);
  if (!body) return json(env, requestId, 400, { error: { code: "INVALID_JSON", message: "A JSON object is required." } });
  const localEmployeeNo = employeeNo(body.employeeNo);
  const suppliedSnapshot = snapshotHash(body.snapshotHash);
  const verifier = credentialVerifier(body.credentialVerifier);
  if (!localEmployeeNo || !verifier)
    return json(env, requestId, 400, { error: { code: "INVALID_TRANSITION_CREDENTIAL", message: "Employee No or local credential verifier is invalid." } });
  if (!verifySnapshot(device, suppliedSnapshot))
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED", message: "Run transition inspection again before continuing." } });

  const item = await loadTransitionItem(env, device.device_id, localEmployeeNo);
  if (!item) return json(env, requestId, 404, { error: { code: "TRANSITION_ITEM_NOT_FOUND", message: "Transition item was not found." } });
  if (item.state === "ready" && item.matched_employee_id) {
    const existing = await loadEmployee(env, item.matched_employee_id);
    if (existing) return json(env, requestId, 200, { transitionItem: { state: "ready", employee: employeeJson(existing) } });
  }
  if (item.state !== "bootstrap_owner_pending" || item.match_kind !== "bootstrap_owner" || device.pairing_id !== null)
    return json(env, requestId, 409, { error: { code: "BOOTSTRAP_OWNER_NOT_PENDING", message: "This Device does not have a pending bootstrap owner." } });
  if (!item.local_enabled || !device.recovery_email_verified_at || device.recovery_email !== item.local_email_normalized)
    return json(env, requestId, 409, { error: { code: "BOOTSTRAP_OWNER_EMAIL_MISMATCH", message: "Bootstrap owner must match the verified Workspace recovery Email." } });

  const count = await env.DB.prepare("SELECT COUNT(*) AS count FROM cloud_employees WHERE workspace_id = ?1")
    .bind(device.workspace_id).first<{ count: number }>();
  if (Number(count?.count ?? 0) !== 0)
    return json(env, requestId, 409, { error: { code: "BOOTSTRAP_OWNER_CONFLICT", message: "Workspace already contains central Employees; inspect again." } });

  const now = new Date().toISOString();
  const id = `emp_${crypto.randomUUID()}`;
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO cloud_employees (
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, role, enabled, source_device_id,
            credential_verifier, credential_algorithm, credential_version,
            credential_updated_at, revision, created_at, updated_at
         ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'SUPER_ADMIN', 1, ?7, ?8,
                   'pbkdf2-sha256', 1, ?9, 1, ?9, ?9)`
      ).bind(id, device.workspace_id, item.local_employee_no, item.local_name, item.local_email_normalized,
             device.recovery_email_verified_at, device.device_id, verifier, now),
      env.DB.prepare(
        `INSERT INTO device_employee_links (device_id, employee_id, local_employee_no, linked_at)
         VALUES (?1, ?2, ?3, ?4)`
      ).bind(device.device_id, id, item.local_employee_no, now),
      env.DB.prepare(
        `UPDATE employee_transition_items
            SET state = 'ready', matched_employee_id = ?1, resolved_at = ?2, updated_at = ?2
          WHERE transition_item_id = ?3 AND state = 'bootstrap_owner_pending'`
      ).bind(id, now, item.transition_item_id),
      env.DB.prepare(
        `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
          WHERE workspace_id = ?2`
      ).bind(now, device.workspace_id),
    ]);
  } catch (error) {
    console.warn("bootstrap_owner_completion_conflict", { requestId, deviceId: device.device_id, error: error instanceof Error ? error.message : "unknown_error" });
    return json(env, requestId, 409, { error: { code: "BOOTSTRAP_OWNER_CONFLICT", message: "Bootstrap owner could not be created; inspect again." } });
  }

  const created = await loadEmployee(env, id);
  if (!created) throw new Error("bootstrap_owner_readback_failed");
  return json(env, requestId, 200, { transitionItem: { state: "ready", employee: employeeJson(created) } });
}

async function verifyEmployeeEmail(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  if (device.employee_authority_state !== "transitioning")
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_AUTHORITY_ALREADY_CLOUD", message: "Employee transition is already complete." } });

  const body = await readJsonObject(request);
  if (!body) return json(env, requestId, 400, { error: { code: "INVALID_JSON", message: "A JSON object is required." } });
  const localEmployeeNo = employeeNo(body.employeeNo);
  const suppliedSnapshot = snapshotHash(body.snapshotHash);
  const suppliedChallenge = challengeId(body.challengeId);
  const suppliedOtp = otp(body.otp);
  const verifier = body.credentialVerifier === undefined || body.credentialVerifier === null
    ? null
    : credentialVerifier(body.credentialVerifier);
  if (!localEmployeeNo || !suppliedChallenge || !suppliedOtp || (body.credentialVerifier !== undefined && !verifier))
    return json(env, requestId, 400, { error: { code: "INVALID_EMAIL_VERIFICATION", message: "Employee Email verification input is invalid." } });
  if (!verifySnapshot(device, suppliedSnapshot))
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED", message: "Run transition inspection again before continuing." } });

  const item = await loadTransitionItem(env, device.device_id, localEmployeeNo);
  if (!item) return json(env, requestId, 404, { error: { code: "TRANSITION_ITEM_NOT_FOUND", message: "Transition item was not found." } });
  if (item.state === "ready" && item.matched_employee_id) {
    const existing = await loadEmployee(env, item.matched_employee_id);
    if (existing) return json(env, requestId, 200, { transitionItem: { state: "ready", employee: employeeJson(existing) } });
  }
  if (item.state === "conflict" || item.state === "bootstrap_owner_pending")
    return json(env, requestId, 409, { error: { code: "TRANSITION_ITEM_CONFLICT", message: "This Employee identity requires another transition action." } });

  const challenge = await readValidChallenge(env, device, item, suppliedChallenge);
  if (!challenge) return json(env, requestId, 404, { error: { code: "OTP_NOT_FOUND", message: "Email verification challenge was not found." } });
  const verification = await verifyChallengeCode(env, challenge, suppliedOtp);
  if (verification !== "ok") return otpError(env, requestId, verification);

  const now = new Date().toISOString();
  const matched = await loadEmployee(env, item.matched_employee_id);

  if (!matched) {
    if (item.state !== "new_email_pending" || item.suggested_cloud_role === "SUPER_ADMIN" || !verifier)
      return json(env, requestId, 409, { error: { code: "TRANSITION_ITEM_NOT_READY", message: "New Employee transition data is incomplete." } });

    const id = `emp_${crypto.randomUUID()}`;
    try {
      await env.DB.batch([
        env.DB.prepare(
          `INSERT INTO cloud_employees (
              employee_id, workspace_id, employee_no, name, email_normalized,
              email_verified_at, role, enabled, source_device_id,
              credential_verifier, credential_algorithm, credential_version,
              credential_updated_at, revision, created_at, updated_at
           ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10,
                     'pbkdf2-sha256', 1, ?6, 1, ?6, ?6)`
        ).bind(id, device.workspace_id, item.local_employee_no, item.local_name, item.local_email_normalized,
               now, item.suggested_cloud_role, item.local_enabled, device.device_id, verifier),
        env.DB.prepare(
          `INSERT INTO device_employee_links (device_id, employee_id, local_employee_no, linked_at)
           VALUES (?1, ?2, ?3, ?4)`
        ).bind(device.device_id, id, item.local_employee_no, now),
        env.DB.prepare(
          `UPDATE employee_transition_items
              SET state = 'ready', matched_employee_id = ?1, resolved_at = ?2, updated_at = ?2
            WHERE transition_item_id = ?3 AND state = 'new_email_pending'`
        ).bind(id, now, item.transition_item_id),
        env.DB.prepare(
          `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1
            WHERE challenge_id = ?2 AND consumed_at IS NULL`
        ).bind(now, suppliedChallenge),
        env.DB.prepare(
          `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
            WHERE workspace_id = ?2`
        ).bind(now, device.workspace_id),
      ]);
    } catch (error) {
      console.warn("new_employee_transition_conflict", { requestId, deviceId: device.device_id, error: error instanceof Error ? error.message : "unknown_error" });
      return json(env, requestId, 409, { error: { code: "EMPLOYEE_IDENTITY_CONFLICT", message: "Employee identity changed while Email was being verified; inspect again." } });
    }

    const created = await loadEmployee(env, id);
    if (!created) throw new Error("new_employee_readback_failed");
    return json(env, requestId, 200, { transitionItem: { state: "ready", employee: employeeJson(created) } });
  }

  if (matched.employee_no !== item.local_employee_no || matched.email_normalized !== item.local_email_normalized)
    return json(env, requestId, 409, { error: { code: "TRANSITION_IDENTITY_CHANGED", message: "The matched Cloud Employee no longer matches this transition item." } });

  const shouldAdoptCredential = !matched.credential_verifier && verifier;
  await env.DB.batch([
    env.DB.prepare(
      `UPDATE cloud_employees
          SET email_verified_at = COALESCE(email_verified_at, ?1),
              credential_verifier = CASE WHEN credential_verifier IS NULL AND ?2 IS NOT NULL THEN ?2 ELSE credential_verifier END,
              credential_algorithm = CASE WHEN credential_verifier IS NULL AND ?2 IS NOT NULL THEN 'pbkdf2-sha256' ELSE credential_algorithm END,
              credential_version = CASE WHEN credential_verifier IS NULL AND ?2 IS NOT NULL THEN MAX(1, credential_version + 1) ELSE credential_version END,
              credential_updated_at = CASE WHEN credential_verifier IS NULL AND ?2 IS NOT NULL THEN ?1 ELSE credential_updated_at END,
              revision = revision + 1,
              updated_at = ?1
        WHERE employee_id = ?3`
    ).bind(now, shouldAdoptCredential ? verifier : null, matched.employee_id),
    env.DB.prepare(
      `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1
        WHERE challenge_id = ?2 AND consumed_at IS NULL`
    ).bind(now, suppliedChallenge),
    env.DB.prepare(
      `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
        WHERE workspace_id = ?2`
    ).bind(now, device.workspace_id),
  ]);

  const updated = await loadEmployee(env, matched.employee_id);
  if (!updated) throw new Error("verified_employee_readback_failed");
  const nextState = updated.email_verified_at && updated.credential_verifier ? "ready" : "credential_pending";
  await env.DB.prepare(
    `UPDATE employee_transition_items
        SET state = ?1, resolved_at = CASE WHEN ?1 = 'ready' THEN ?2 ELSE NULL END, updated_at = ?2
      WHERE transition_item_id = ?3`
  ).bind(nextState, now, item.transition_item_id).run();
  return json(env, requestId, 200, { transitionItem: { state: nextState, employee: employeeJson(updated) } });
}

async function completeExistingCredential(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  if (device.employee_authority_state !== "transitioning")
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_AUTHORITY_ALREADY_CLOUD", message: "Employee transition is already complete." } });

  const body = await readJsonObject(request);
  if (!body) return json(env, requestId, 400, { error: { code: "INVALID_JSON", message: "A JSON object is required." } });
  const localEmployeeNo = employeeNo(body.employeeNo);
  const suppliedSnapshot = snapshotHash(body.snapshotHash);
  const verifier = credentialVerifier(body.credentialVerifier);
  if (!localEmployeeNo || !verifier)
    return json(env, requestId, 400, { error: { code: "INVALID_TRANSITION_CREDENTIAL", message: "Employee No or local credential verifier is invalid." } });
  if (!verifySnapshot(device, suppliedSnapshot))
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED", message: "Run transition inspection again before continuing." } });

  const item = await loadTransitionItem(env, device.device_id, localEmployeeNo);
  if (!item || !item.matched_employee_id)
    return json(env, requestId, 404, { error: { code: "MATCHED_EMPLOYEE_NOT_FOUND", message: "Matched Cloud Employee was not found for this transition item." } });
  const matched = await loadEmployee(env, item.matched_employee_id);
  if (!matched) return json(env, requestId, 404, { error: { code: "MATCHED_EMPLOYEE_NOT_FOUND", message: "Matched Cloud Employee was not found." } });
  if (matched.employee_no !== item.local_employee_no || matched.email_normalized !== item.local_email_normalized)
    return json(env, requestId, 409, { error: { code: "TRANSITION_IDENTITY_CHANGED", message: "The matched Cloud Employee no longer matches this transition item." } });
  if (!matched.email_verified_at)
    return json(env, requestId, 409, { error: { code: "EMAIL_VERIFICATION_REQUIRED", message: "Matched Cloud Employee Email must be verified first." } });

  if (!matched.credential_verifier) {
    const now = new Date().toISOString();
    await env.DB.batch([
      env.DB.prepare(
        `UPDATE cloud_employees
            SET credential_verifier = ?1, credential_algorithm = 'pbkdf2-sha256',
                credential_version = MAX(1, credential_version + 1), credential_updated_at = ?2,
                revision = revision + 1, updated_at = ?2
          WHERE employee_id = ?3 AND credential_verifier IS NULL`
      ).bind(verifier, now, matched.employee_id),
      env.DB.prepare(
        `UPDATE workspaces SET employee_revision = employee_revision + 1, updated_at = ?1
          WHERE workspace_id = ?2`
      ).bind(now, device.workspace_id),
    ]);
  }

  const updated = await loadEmployee(env, matched.employee_id);
  if (!updated?.credential_verifier) throw new Error("credential_adoption_failed");
  const now = new Date().toISOString();
  await env.DB.prepare(
    `UPDATE employee_transition_items SET state = 'ready', resolved_at = ?1, updated_at = ?1
      WHERE transition_item_id = ?2`
  ).bind(now, item.transition_item_id).run();
  return json(env, requestId, 200, { transitionItem: { state: "ready", employee: employeeJson(updated) } });
}

export async function handleEmployeeTransitionActions(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/email-challenge")
      return await beginEmailChallenge(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/email-verify")
      return await verifyEmployeeEmail(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/bootstrap-owner/complete")
      return await completeBootstrapOwner(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/credential/complete")
      return await completeExistingCredential(request, env, requestId);
    return null;
  } catch (error) {
    console.error("employee_transition_action_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, { error: { code: "EMPLOYEE_TRANSITION_ACTION_FAILED", message: "Employee transition action could not be completed." } });
  }
}
