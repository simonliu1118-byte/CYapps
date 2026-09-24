import { createEmailSender } from "./email";
import { verifyPassword } from "./web-auth";
import { recordSecurityEvent, securityEventStatement } from "./security-audit";
import {
  BOOTSTRAP_DEVICE_INSERT_SQL,
  BOOTSTRAP_WORKSPACE_INSERT_SQL,
} from "./bootstrap-sql";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  BOOTSTRAP_KEY?: string;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
  WEB_LOGIN_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_LOGIN_IP_RATE_LIMIT: RateLimit;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceIdentity = {
  deviceId: string;
  workspaceId: string;
  deviceDisplayName: string;
};

type BootstrapDeviceRow = {
  device_id: string;
  workspace_id: string;
  device_display_name: string;
  workspace_display_name: string;
};

type PairingDeviceRow = {
  device_id: string;
  workspace_id: string;
  device_display_name: string;
};

type OtpChallengeRow = {
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
const MAX_REQUEST_ID_LENGTH = 128;
const MAX_DISPLAY_NAME_LENGTH = 120;
const MAX_CLIENT_VERSION_LENGTH = 64;
const MAX_EMAIL_LENGTH = 320;
const PAIRING_TTL_MS = 10 * 60 * 1000;
const INVITATION_TTL_MS = 72 * 60 * 60 * 1000;
const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_COOLDOWN_MS = 60 * 1000;
const OTP_HOURLY_LIMIT = 5;
const OTP_MAX_ATTEMPTS = 5;
const WORKSPACE_BOOTSTRAP_PURPOSE = "workspace_bootstrap";
const DEVICE_PAIRING_PURPOSE_PREFIX = "device_pairing_authorization";


function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= MAX_REQUEST_ID_LENGTH && /^[A-Za-z0-9._:-]+$/.test(supplied)) {
    return supplied;
  }
  return crypto.randomUUID();
}

function json(env: Env, requestId: string, status: number, body: Record<string, JsonValue>, extraHeaders?: HeadersInit): Response {
  const headers = new Headers(extraHeaders);
  headers.set("cache-control", "no-store");
  headers.set("content-type", "application/json; charset=utf-8");
  headers.set("x-content-type-options", "nosniff");
  headers.set("x-request-id", requestId);

  return new Response(
    JSON.stringify({
      ok: status >= 200 && status < 300,
      service: SERVICE_NAME,
      cloudVersion: CLOUD_VERSION,
      apiVersion: env.API_VERSION,
      environment: env.APP_ENV,
      requestId,
      timestamp: new Date().toISOString(),
      ...body,
    }),
    { status, headers }
  );
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

function validDisplayName(value: unknown): value is string {
  return typeof value === "string" && value.trim().length >= 1 && value.trim().length <= MAX_DISPLAY_NAME_LENGTH;
}

function validClientVersion(value: unknown): value is string {
  return typeof value === "string" && value.trim().length >= 1 && value.trim().length <= MAX_CLIENT_VERSION_LENGTH;
}

function normalizeEmail(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const email = value.trim().toLowerCase();
  if (email.length < 3 || email.length > MAX_EMAIL_LENGTH || /[\r\n\s]/.test(email)) return null;
  const at = email.lastIndexOf("@");
  if (at <= 0 || at >= email.length - 1 || email.indexOf("@") !== at) return null;
  const domain = email.slice(at + 1);
  if (!domain.includes(".") || domain.startsWith(".") || domain.endsWith(".")) return null;
  return email;
}

function randomToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, part => part.toString(16).padStart(2, "0")).join("");
}

function validDeviceToken(value: unknown): value is string {
  return typeof value === "string" && /^cydev_[0-9a-f]{64}$/.test(value.trim().toLowerCase());
}

function normalizeDeviceToken(value: string): string {
  return value.trim().toLowerCase();
}

function validPairingCode(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{20}$/.test(value.trim().toLowerCase());
}

function randomPairingCode(): string {
  const bytes = new Uint8Array(10);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, part => part.toString(16).padStart(2, "0")).join("");
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

function errorResponse(env: Env, requestId: string, status: number, code: string, message: string, extra?: Record<string, JsonValue>): Response {
  return json(env, requestId, status, {
    ...(extra ?? {}),
    error: { code, message },
  });
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

async function createEmailChallenge(
  env: Env,
  requestId: string,
  purpose: string,
  email: string,
  subject: string,
  text: (code: string) => string,
  scopeKey: string | null = null,
): Promise<Response> {
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return errorResponse(env, requestId, 503, "OTP_NOT_CONFIGURED", "Email OTP is not configured.");
  const sender = await configuredEmailSender(env);
  if (!sender) return errorResponse(env, requestId, 503, "EMAIL_PROVIDER_NOT_CONFIGURED", "Email delivery provider is not configured.");

  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after FROM email_otp_challenges
      WHERE purpose = ?1 AND scope_key IS ?2 AND email_normalized = ?3
      ORDER BY created_at DESC LIMIT 1`
  ).bind(purpose, scopeKey, email).first<{ resend_after: string }>();
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
  ).bind(purpose, email, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= OTP_HOURLY_LIMIT)
    return errorResponse(env, requestId, 429, "OTP_RATE_LIMITED", "Too many verification requests. Please try again later.");

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
  ).bind(challengeId, purpose, scopeKey, email, digest, OTP_MAX_ATTEMPTS, expiresAt, resendAfter, nowText).run();

  try {
    await sender.send({
      to: email,
      subject,
      text: text(code),
      tags: [{ name: "purpose", value: purpose }],
    });
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
  } catch {
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET delivery_state = 'failed', updated_at = ?1 WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
    return errorResponse(env, requestId, 503, "EMAIL_DELIVERY_FAILED", "Verification email could not be delivered.");
  }

  return json(env, requestId, 201, {
    challenge: { challengeId, maskedEmail: maskedEmail(email), expiresAt, resendAfter },
  });
}

async function consumeEmailChallenge(
  env: Env,
  requestId: string,
  purpose: string,
  challengeId: string,
  suppliedOtp: string,
  scopeKey: string | null = null,
): Promise<{ email: string } | Response> {
  const challenge = await env.DB.prepare(
    `SELECT challenge_id, email_normalized, otp_digest, delivery_state,
            attempt_count, max_attempts, expires_at, resend_after, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2 AND scope_key IS ?3
      LIMIT 1`
  ).bind(challengeId, purpose, scopeKey).first<OtpChallengeRow>();

  const now = new Date();
  const nowText = now.toISOString();
  if (!challenge) return errorResponse(env, requestId, 400, "OTP_INVALID", "Verification code is invalid.");
  if (challenge.consumed_at) return errorResponse(env, requestId, 409, "OTP_ALREADY_USED", "Verification code has already been used.");
  if (challenge.delivery_state !== "sent") return errorResponse(env, requestId, 503, "OTP_NOT_DELIVERED", "Verification code delivery did not complete.");
  if (challenge.expires_at <= nowText) return errorResponse(env, requestId, 410, "OTP_EXPIRED", "Verification code has expired.");
  if (challenge.attempt_count >= challenge.max_attempts)
    return errorResponse(env, requestId, 429, "OTP_ATTEMPTS_EXCEEDED", "Verification code attempts are exhausted.");

  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return errorResponse(env, requestId, 503, "OTP_NOT_CONFIGURED", "Email OTP is not configured.");
  const digest = await hmacSha256Hex(pepper, `${challengeId}:${suppliedOtp}`);
  if (!constantTimeHexEquals(digest, challenge.otp_digest)) {
    const nextAttempts = challenge.attempt_count + 1;
    await env.DB.prepare(
      `UPDATE email_otp_challenges SET attempt_count = ?1, updated_at = ?2 WHERE challenge_id = ?3`
    ).bind(nextAttempts, nowText, challengeId).run();
    return errorResponse(
      env,
      requestId,
      nextAttempts >= challenge.max_attempts ? 429 : 400,
      nextAttempts >= challenge.max_attempts ? "OTP_ATTEMPTS_EXCEEDED" : "OTP_INVALID",
      nextAttempts >= challenge.max_attempts ? "Verification code attempts are exhausted." : "Verification code is invalid."
    );
  }

  await env.DB.prepare(
    `UPDATE email_otp_challenges SET consumed_at = ?1, updated_at = ?1 WHERE challenge_id = ?2 AND consumed_at IS NULL`
  ).bind(nowText, challengeId).run();
  return { email: challenge.email_normalized };
}

async function storageHealth(env: Env): Promise<boolean> {
  try {
    const row = await env.DB.prepare("SELECT 1 AS ok").first<{ ok: number }>();
    return row?.ok === 1;
  } catch {
    return false;
  }
}

async function workspaceCount(env: Env): Promise<number> {
  const row = await env.DB.prepare("SELECT COUNT(*) AS count FROM workspaces").first<{ count: number }>();
  return Number(row?.count ?? 0);
}

function bearerToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  return match?.[1]?.trim() || null;
}

async function authenticateDevice(request: Request, env: Env): Promise<DeviceIdentity | null> {
  const token = bearerToken(request);
  if (!token || !validDeviceToken(token)) return null;
  const tokenHash = await sha256Hex(normalizeDeviceToken(token));
  const row = await env.DB.prepare(
    `SELECT d.device_id, d.workspace_id, d.display_name
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1 AND d.status = 'active' AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<{ device_id: string; workspace_id: string; display_name: string }>();
  return row ? { deviceId: row.device_id, workspaceId: row.workspace_id, deviceDisplayName: row.display_name } : null;
}

async function onboardingStatus(env: Env, requestId: string): Promise<Response> {
  const healthy = await storageHealth(env);
  if (!healthy) return errorResponse(env, requestId, 503, "STORAGE_UNAVAILABLE", "Backend storage health check failed.", { storage: "unavailable" });
  const count = await workspaceCount(env);
  return json(env, requestId, 200, {
    onboarding: {
      state: count === 0 ? "uninitialized" : "initialized",
      workspaceInitialized: count > 0,
    },
  });
}

async function bootstrapEmailChallenge(request: Request, env: Env, requestId: string): Promise<Response> {
  const bootstrapKey = env.BOOTSTRAP_KEY?.trim() ?? "";
  const supplied = request.headers.get("x-bootstrap-key")?.trim() ?? "";
  if (!bootstrapKey || supplied !== bootstrapKey)
    return errorResponse(env, requestId, 403, "BOOTSTRAP_FORBIDDEN", "Bootstrap authorization failed.");
  if (await workspaceCount(env) !== 0)
    return errorResponse(env, requestId, 409, "WORKSPACE_ALREADY_INITIALIZED", "Workspace has already been initialized.");

  const body = await request.json<Record<string, unknown>>().catch(() => null);
  const email = normalizeEmail(body?.email);
  if (!email) return errorResponse(env, requestId, 400, "INVALID_EMAIL", "A valid Email is required.");
  return createEmailChallenge(
    env,
    requestId,
    WORKSPACE_BOOTSTRAP_PURPOSE,
    email,
    "CYInvoice 雲端初始化驗證碼",
    code => `您正在建立第一個 CYInvoice Workspace。驗證碼是 ${code}，10 分鐘內有效。若非本人操作，請忽略此信。`
  );
}

async function bootstrapWorkspace(request: Request, env: Env, requestId: string): Promise<Response> {
  const bootstrapKey = env.BOOTSTRAP_KEY?.trim() ?? "";
  const supplied = request.headers.get("x-bootstrap-key")?.trim() ?? "";
  if (!bootstrapKey || supplied !== bootstrapKey)
    return errorResponse(env, requestId, 403, "BOOTSTRAP_FORBIDDEN", "Bootstrap authorization failed.");

  const body = await request.json<Record<string, unknown>>().catch(() => null);
  if (!body || !validDisplayName(body.workspaceDisplayName) || !validDisplayName(body.deviceDisplayName)
      || !validClientVersion(body.clientVersion) || !validDeviceToken(body.deviceToken)) {
    return errorResponse(env, requestId, 400, "INVALID_BOOTSTRAP_REQUEST", "Bootstrap request is invalid.");
  }
  const challengeId = typeof body.emailChallengeId === "string" ? body.emailChallengeId.trim() : "";
  const emailOtp = typeof body.emailOtp === "string" ? body.emailOtp.trim() : "";
  if (!/^otp_[0-9a-f-]{36}$/.test(challengeId.toLowerCase()) || !/^\d{6}$/.test(emailOtp))
    return errorResponse(env, requestId, 400, "INVALID_OTP_REQUEST", "Email verification is required.");

  const normalizedToken = normalizeDeviceToken(body.deviceToken as string);
  const tokenHash = await sha256Hex(normalizedToken);
  const recovered = await env.DB.prepare(
    `SELECT d.device_id, d.workspace_id, d.display_name AS device_display_name, w.display_name AS workspace_display_name
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
      LIMIT 1`
  ).bind(tokenHash).first<BootstrapDeviceRow>();
  if (recovered) {
    return json(env, requestId, 200, {
      recovered: true,
      workspace: { workspaceId: recovered.workspace_id, displayName: recovered.workspace_display_name },
      device: { deviceId: recovered.device_id, displayName: recovered.device_display_name },
    });
  }

  if (await workspaceCount(env) !== 0)
    return errorResponse(env, requestId, 409, "WORKSPACE_ALREADY_INITIALIZED", "Workspace has already been initialized.");

  const verified = await consumeEmailChallenge(env, requestId, WORKSPACE_BOOTSTRAP_PURPOSE, challengeId.toLowerCase(), emailOtp);
  if (verified instanceof Response) return verified;

  const now = new Date().toISOString();
  const workspaceId = `ws_${crypto.randomUUID()}`;
  const deviceId = `dev_${crypto.randomUUID()}`;
  try {
    await env.DB.batch([
      env.DB.prepare(BOOTSTRAP_WORKSPACE_INSERT_SQL)
        .bind(workspaceId, (body.workspaceDisplayName as string).trim(), verified.email, now),
      env.DB.prepare(BOOTSTRAP_DEVICE_INSERT_SQL)
        .bind(deviceId, workspaceId, (body.deviceDisplayName as string).trim(), tokenHash, (body.clientVersion as string).trim(), now),
    ]);
  } catch (error) {
    const byToken = await env.DB.prepare(
      `SELECT d.device_id, d.workspace_id, d.display_name AS device_display_name, w.display_name AS workspace_display_name
         FROM devices d JOIN workspaces w ON w.workspace_id = d.workspace_id
        WHERE d.token_hash = ?1 LIMIT 1`
    ).bind(tokenHash).first<BootstrapDeviceRow>();
    if (byToken) {
      return json(env, requestId, 200, {
        recovered: true,
        workspace: { workspaceId: byToken.workspace_id, displayName: byToken.workspace_display_name },
        device: { deviceId: byToken.device_id, displayName: byToken.device_display_name },
      });
    }
    const workspaceExists = await workspaceCount(env).catch(() => 0) !== 0;
    console.error("bootstrap_insert_failed", { requestId, error: error instanceof Error ? error.message : "unknown_error" });
    return workspaceExists
      ? errorResponse(env, requestId, 409, "WORKSPACE_ALREADY_INITIALIZED", "Workspace initialization conflicted with existing state.")
      : errorResponse(env, requestId, 500, "BOOTSTRAP_FAILED", "Workspace initialization failed. No Workspace or Device was created.");
  }

  return json(env, requestId, 201, {
    workspace: { workspaceId, displayName: (body.workspaceDisplayName as string).trim() },
    device: { deviceId, displayName: (body.deviceDisplayName as string).trim() },
  });
}

async function pairingAuthorizationChallenge(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  const owner = await authorizedOwner(request, env, device.workspaceId, body);
  if (!owner) return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");

  const scope = `${device.workspaceId}:${device.deviceId}:${owner.employee_id}`;
  const response = await createEmailChallenge(
    env,
    requestId,
    DEVICE_PAIRING_PURPOSE_PREFIX,
    owner.email_normalized,
    "CYInvoice 新裝置授權驗證碼",
    code => `您正在授權 CYInvoice Workspace 加入一台新裝置。驗證碼是 ${code}，10 分鐘內有效。若非本人操作，請忽略此信。`,
    scope
  );
  if (response.ok) await recordSecurityEvent(env.DB, { workspaceId: device.workspaceId,
    type: "pairing_email", outcome: "success", actorDeviceId: device.deviceId,
    actorEmployeeId: owner.employee_id, requestId });
  return response;
}

async function createPairing(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");

  const body = await request.json<Record<string, unknown>>().catch(() => null);
  const owner = await authorizedOwner(request, env, device.workspaceId, body);
  if (!owner) return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  const challengeId = typeof body?.emailChallengeId === "string" ? body.emailChallengeId.trim().toLowerCase() : "";
  const emailOtp = typeof body?.emailOtp === "string" ? body.emailOtp.trim() : "";
  if (!/^otp_[0-9a-f-]{36}$/.test(challengeId) || !/^\d{6}$/.test(emailOtp))
    return errorResponse(env, requestId, 400, "INVALID_OTP_REQUEST", "Email authorization is required.");

  const scope = `${device.workspaceId}:${device.deviceId}:${owner.employee_id}`;
  const verified = await consumeEmailChallenge(env, requestId, DEVICE_PAIRING_PURPOSE_PREFIX, challengeId, emailOtp, scope);
  if (verified instanceof Response) return verified;

  const code = randomPairingCode();
  const codeHash = await sha256Hex(code);
  const now = new Date();
  const expiresAt = new Date(now.getTime() + PAIRING_TTL_MS).toISOString();
  const pairingId = `pair_${crypto.randomUUID()}`;
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO device_pairing_codes (
        pairing_id, workspace_id, created_by_device_id, code_hash, expires_at, created_at
      ) VALUES (?1, ?2, ?3, ?4, ?5, ?6)`
    ).bind(pairingId, device.workspaceId, device.deviceId, codeHash, expiresAt, now.toISOString()),
    securityEventStatement(env.DB, { workspaceId: device.workspaceId, type: "pairing_issued",
      outcome: "success", actorDeviceId: device.deviceId, actorEmployeeId: owner.employee_id,
      pairingId, requestId }),
  ]);
  return json(env, requestId, 201, { pairing: { pairingId, code, expiresAt } });
}

async function directJoinWorkspace(env: Env, workspaceId: string): Promise<{ display_name: string } | null> {
  const workspace = await env.DB.prepare(
    "SELECT display_name FROM workspaces WHERE workspace_id = ?1 AND status = 'active' LIMIT 1"
  ).bind(workspaceId).first<{ display_name: string }>();
  if (!workspace) return null;
  const ready = await env.DB.prepare(
    `SELECT COUNT(*) AS total,
            SUM(CASE WHEN role = 'SUPER_ADMIN' AND enabled = 1 THEN 1 ELSE 0 END) AS owners,
            SUM(CASE WHEN email_verified_at IS NULL OR credential_verifier IS NULL
                      OR credential_algorithm <> 'pbkdf2-sha256' OR credential_version < 1
                     THEN 1 ELSE 0 END) AS invalid
       FROM cloud_employees WHERE workspace_id = ?1`
  ).bind(workspaceId).first<{ total: number; owners: number; invalid: number }>();
  return ready && ready.total > 0 && ready.owners === 1 && ready.invalid === 0 ? workspace : null;
}

async function previewPairing(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  if (!validPairingCode(body?.code))
    return errorResponse(env, requestId, 400, "INVALID_PAIRING_CLAIM", "Pairing code is invalid.");
  const hash = await sha256Hex(body.code.trim().toLowerCase());
  const pairing = await env.DB.prepare(
    `SELECT pairing_id, workspace_id FROM device_pairing_codes
      WHERE code_hash = ?1 AND used_at IS NULL AND expires_at > ?2 LIMIT 1`
  ).bind(hash, new Date().toISOString()).first<{ pairing_id: string; workspace_id: string }>();
  const workspace = pairing && await directJoinWorkspace(env, pairing.workspace_id);
  if (!workspace) return errorResponse(env, requestId, 404, "PAIRING_NOT_FOUND", "Pairing code is invalid or Workspace is not ready.");
  await recordSecurityEvent(env.DB, { workspaceId: pairing!.workspace_id, type: "pairing_verified",
    outcome: "success", pairingId: pairing!.pairing_id, requestId });
  return json(env, requestId, 200, { workspace: { workspaceId: pairing!.workspace_id, displayName: workspace.display_name } });
}

async function directJoinOwner(env: Env, workspaceId: string, employeeNo: string) {
  return env.DB.prepare(
    `SELECT employee_id, email_normalized, email_verified_at, credential_verifier,
            credential_algorithm, credential_version
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2 AND role = 'SUPER_ADMIN' AND enabled = 1 LIMIT 1`
  ).bind(workspaceId, employeeNo).first<{
    employee_id: string; email_normalized: string; email_verified_at: string | null;
    credential_verifier: string | null; credential_algorithm: string | null; credential_version: number;
  }>();
}

async function authorizedOwner(request: Request, env: Env, workspaceId: string,
  body: Record<string, unknown> | null): Promise<{ employee_id: string; email_normalized: string } | null> {
  const employeeNo = typeof body?.employeeNo === "string" ? body.employeeNo.trim() : "";
  const password = body?.password;
  if (!/^\d{4}$/.test(employeeNo) || typeof password !== "string" || password.length < 1 || password.length > 200)
    return null;
  const employeeLimit = await env.WEB_LOGIN_EMPLOYEE_RATE_LIMIT.limit({ key: `onboard:${workspaceId}:${employeeNo}` });
  const ip = request.headers.get("cf-connecting-ip")?.trim() || "unknown";
  const ipLimit = await env.WEB_LOGIN_IP_RATE_LIMIT.limit({ key: `onboard:ip:${ip}` });
  if (!employeeLimit.success || !ipLimit.success) return null;
  const owner = await directJoinOwner(env, workspaceId, employeeNo);
  if (!owner?.email_verified_at || owner.credential_algorithm !== "pbkdf2-sha256" ||
      !owner.credential_verifier || !await verifyPassword(password, owner.credential_verifier)) return null;
  return { employee_id: owner.employee_id, email_normalized: owner.email_normalized };
}

function invitationCode(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{40}$/.test(value.trim().toLowerCase());
}

function newInvitationCode(): string {
  const bytes = new Uint8Array(20);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

type InvitationRow = {
  invitation_id: string; workspace_id: string; delivery_state: string; expires_at: string;
  revoked_at: string | null; consumed_at: string | null; consumed_by_device_id: string | null;
};

async function findInvitation(env: Env, code: string): Promise<InvitationRow | null> {
  return env.DB.prepare(`SELECT invitation_id, workspace_id, delivery_state, expires_at,
      revoked_at, consumed_at, consumed_by_device_id FROM device_invitations WHERE code_hash = ?1 LIMIT 1`)
    .bind(await sha256Hex(code.trim().toLowerCase())).first<InvitationRow>();
}

function invitationProblem(invitation: InvitationRow, now: string): string | null {
  if (invitation.delivery_state !== "sent") return "INVITATION_NOT_DELIVERED";
  if (invitation.revoked_at) return "INVITATION_REVOKED";
  if (invitation.consumed_at) return "INVITATION_ALREADY_USED";
  if (invitation.expires_at <= now) return "INVITATION_EXPIRED";
  return null;
}

async function issueInvitation(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  const owner = await authorizedOwner(request, env, device.workspaceId, body);
  if (!owner) return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  if (!await directJoinWorkspace(env, device.workspaceId))
    return errorResponse(env, requestId, 409, "WORKSPACE_NOT_READY", "Central Employee authority is not ready.");
  const sender = await configuredEmailSender(env);
  if (!sender) return errorResponse(env, requestId, 503, "EMAIL_PROVIDER_NOT_CONFIGURED", "Email delivery provider is not configured.");
  const code = newInvitationCode();
  const invitationId = `inv_${crypto.randomUUID()}`;
  const now = new Date();
  const expiresAt = new Date(now.getTime() + INVITATION_TTL_MS).toISOString();
  await env.DB.prepare(`INSERT INTO device_invitations (
    invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    delivery_state, expires_at, created_at) VALUES (?1, ?2, ?3, ?4, ?5, 'pending', ?6, ?7)`)
    .bind(invitationId, device.workspaceId, await sha256Hex(code), device.deviceId,
      owner.employee_id, expiresAt, now.toISOString()).run();
  try {
    await sender.send({ to: owner.email_normalized, subject: "CYInvoice 新裝置邀請",
      text: `CYInvoice 新裝置連線設定\nCloud API 網址：${new URL(request.url).origin}\n開通碼：${code}\n有效至：${expiresAt}\n僅限使用一次。若非本人操作，請通知管理員撤銷邀請。`,
      tags: [{ name: "purpose", value: "device_invitation" }] });
  } catch {
    await env.DB.batch([
      env.DB.prepare("UPDATE device_invitations SET delivery_state = 'failed' WHERE invitation_id = ?1").bind(invitationId),
      securityEventStatement(env.DB, { workspaceId: device.workspaceId, type: "invitation_delivery_failed",
        outcome: "failed", actorDeviceId: device.deviceId, actorEmployeeId: owner.employee_id,
        invitationId, reasonCode: "EMAIL_DELIVERY_FAILED", requestId }),
    ]);
    return errorResponse(env, requestId, 503, "EMAIL_DELIVERY_FAILED", "Invitation email could not be delivered.");
  }
  await env.DB.batch([
    env.DB.prepare("UPDATE device_invitations SET delivery_state = 'sent', sent_at = ?1 WHERE invitation_id = ?2")
      .bind(new Date().toISOString(), invitationId),
    securityEventStatement(env.DB, { workspaceId: device.workspaceId, type: "invitation_issued",
      outcome: "success", actorDeviceId: device.deviceId, actorEmployeeId: owner.employee_id,
      invitationId, requestId }),
  ]);
  return json(env, requestId, 201, { invitation: { invitationId, expiresAt, deliveryState: "sent" } });
}

async function previewInvitation(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  if (!invitationCode(body?.code)) return errorResponse(env, requestId, 400, "INVALID_INVITATION", "Invitation code is invalid.");
  const invitation = await findInvitation(env, body.code);
  if (!invitation) return errorResponse(env, requestId, 404, "INVITATION_NOT_FOUND", "Invitation code is invalid.");
  const problem = invitationProblem(invitation, new Date().toISOString());
  if (problem) {
    await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "invitation_claim_denied",
      outcome: "denied", invitationId: invitation.invitation_id, reasonCode: problem, requestId });
    return errorResponse(env, requestId, 409, problem, "Invitation is unavailable.");
  }
  const owner = await authorizedOwner(request, env, invitation.workspace_id, body);
  if (!owner) {
    await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "invitation_claim_denied",
      outcome: "denied", invitationId: invitation.invitation_id, reasonCode: "SUPER_ADMIN_AUTH_FAILED", requestId });
    return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  }
  const workspace = await directJoinWorkspace(env, invitation.workspace_id);
  if (!workspace) return errorResponse(env, requestId, 409, "WORKSPACE_NOT_READY", "Workspace is not ready.");
  await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "invitation_verified",
    outcome: "success", actorEmployeeId: owner.employee_id, invitationId: invitation.invitation_id, requestId });
  return json(env, requestId, 200, { workspace: { workspaceId: invitation.workspace_id, displayName: workspace.display_name } });
}

async function claimInvitation(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  if (!invitationCode(body?.code) || !validDisplayName(body?.deviceDisplayName) ||
      !validClientVersion(body?.clientVersion) || !validDeviceToken(body?.deviceToken))
    return errorResponse(env, requestId, 400, "INVALID_INVITATION_CLAIM", "Invitation claim is invalid.");
  const invitation = await findInvitation(env, body.code);
  if (!invitation) return errorResponse(env, requestId, 404, "INVITATION_NOT_FOUND", "Invitation code is invalid.");
  const tokenHash = await sha256Hex(normalizeDeviceToken(body.deviceToken));
  const existing = await env.DB.prepare(`SELECT device_id, workspace_id, display_name AS device_display_name,
    invitation_id FROM devices WHERE token_hash = ?1 LIMIT 1`).bind(tokenHash)
    .first<PairingDeviceRow & { invitation_id: string | null }>();
  if (existing) {
    if (existing.invitation_id !== invitation.invitation_id)
      return errorResponse(env, requestId, 409, "DEVICE_TOKEN_ALREADY_USED", "Device token belongs to another claim.");
    return json(env, requestId, 200, { recovered: true,
      workspace: { workspaceId: existing.workspace_id },
      device: { deviceId: existing.device_id, displayName: existing.device_display_name } });
  }
  const problem = invitationProblem(invitation, new Date().toISOString());
  if (problem) {
    await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "invitation_claim_denied",
      outcome: "denied", invitationId: invitation.invitation_id, reasonCode: problem, requestId });
    return errorResponse(env, requestId, 409, problem, "Invitation is unavailable.");
  }
  const owner = await authorizedOwner(request, env, invitation.workspace_id, body);
  if (!owner) {
    await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "invitation_claim_denied",
      outcome: "denied", invitationId: invitation.invitation_id, reasonCode: "SUPER_ADMIN_AUTH_FAILED", requestId });
    return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  }
  const workspace = await directJoinWorkspace(env, invitation.workspace_id);
  if (!workspace) return errorResponse(env, requestId, 409, "WORKSPACE_NOT_READY", "Workspace is not ready.");
  const now = new Date().toISOString();
  const deviceId = `dev_${crypto.randomUUID()}`;
  try {
    const result = await env.DB.batch([
      env.DB.prepare(`INSERT INTO devices (device_id, workspace_id, display_name, token_hash,
        client_version, status, invitation_id, employee_authority_state,
        employee_transition_completed_at, paired_at, created_at, updated_at)
        SELECT ?1, ?2, ?3, ?4, ?5, 'active', ?6, 'cloud', ?7, ?7, ?7, ?7
        FROM device_invitations WHERE invitation_id = ?6 AND delivery_state = 'sent'
          AND consumed_at IS NULL AND revoked_at IS NULL AND expires_at > ?7`)
        .bind(deviceId, invitation.workspace_id, body.deviceDisplayName.trim(), tokenHash,
          body.clientVersion.trim(), invitation.invitation_id, now),
      env.DB.prepare(`UPDATE device_invitations SET consumed_at = ?1, consumed_by_device_id = ?2
        WHERE invitation_id = ?3 AND consumed_at IS NULL AND revoked_at IS NULL AND expires_at > ?1
          AND EXISTS (SELECT 1 FROM devices WHERE device_id = ?2 AND invitation_id = ?3)`)
        .bind(now, deviceId, invitation.invitation_id),
    ]);
    if (result[0].meta.changes !== 1 || result[1].meta.changes !== 1)
      return errorResponse(env, requestId, 409, "INVITATION_CLAIM_CONFLICT", "Invitation is no longer active.");
    await recordSecurityEvent(env.DB, { workspaceId: invitation.workspace_id, type: "device_joined",
      outcome: "success", actorEmployeeId: owner.employee_id, targetDeviceId: deviceId,
      invitationId: invitation.invitation_id, requestId });
  } catch {
    return errorResponse(env, requestId, 409, "INVITATION_CLAIM_CONFLICT", "Device join could not complete.");
  }
  return json(env, requestId, 201, { workspace: { workspaceId: invitation.workspace_id,
    displayName: workspace.display_name }, device: { deviceId, displayName: body.deviceDisplayName.trim() } });
}

async function revokeInvitation(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  const invitationId = typeof body?.invitationId === "string" ? body.invitationId.trim() : "";
  if (!/^inv_[0-9a-f-]{36}$/.test(invitationId))
    return errorResponse(env, requestId, 400, "INVALID_INVITATION", "Invitation ID is invalid.");
  const owner = await authorizedOwner(request, env, device.workspaceId, body);
  if (!owner) return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  const now = new Date().toISOString();
  const result = await env.DB.prepare(`UPDATE device_invitations SET revoked_at = ?1
    WHERE invitation_id = ?2 AND workspace_id = ?3 AND revoked_at IS NULL AND consumed_at IS NULL`)
    .bind(now, invitationId, device.workspaceId).run();
  if (result.meta.changes !== 1)
    return errorResponse(env, requestId, 409, "INVITATION_NOT_ACTIVE", "Invitation is no longer active.");
  await recordSecurityEvent(env.DB, { workspaceId: device.workspaceId, type: "invitation_revoked",
    outcome: "success", actorDeviceId: device.deviceId, actorEmployeeId: owner.employee_id,
    invitationId, requestId });
  return json(env, requestId, 200, { invitation: { invitationId, status: "revoked" } });
}

async function onboardingTicketStatus(request: Request, env: Env, requestId: string,
  kind: "pairing" | "invitation", id: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const table = kind === "pairing" ? "device_pairing_codes" : "device_invitations";
  const idColumn = kind === "pairing" ? "pairing_id" : "invitation_id";
  const creatorColumn = kind === "pairing" ? "created_by_device_id" : "issued_by_device_id";
  const usedColumn = kind === "pairing" ? "used_at" : "consumed_at";
  const claimedColumn = kind === "pairing" ? "claimed_device_id" : "consumed_by_device_id";
  const stateColumns = kind === "invitation" ? "t.revoked_at, t.delivery_state" : "NULL AS revoked_at, 'sent' AS delivery_state";
  const row = await env.DB.prepare(`SELECT t.expires_at, t.${usedColumn} AS used_at, ${stateColumns},
    t.${claimedColumn} AS joined_device_id, d.display_name AS joined_device_name
    FROM ${table} t LEFT JOIN devices d ON d.device_id = t.${claimedColumn}
    WHERE t.${idColumn} = ?1 AND t.workspace_id = ?2 AND t.${creatorColumn} = ?3 LIMIT 1`)
    .bind(id, device.workspaceId, device.deviceId).first<{
      expires_at: string; used_at: string | null; joined_device_id: string | null; joined_device_name: string | null;
      revoked_at: string | null; delivery_state: string;
    }>();
  if (!row) return errorResponse(env, requestId, 404, "TICKET_NOT_FOUND", "Join ticket not found.");
  return json(env, requestId, 200, { ticket: { status: row.used_at ? "joined" : row.revoked_at ? "revoked" :
      row.delivery_state === "failed" ? "failed" : row.expires_at <= new Date().toISOString() ? "expired" : "pending",
    joinedDeviceName: row.joined_device_name ?? "", joinedAt: row.used_at ?? "" } });
}

async function recentJoinTickets(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const pairing = await env.DB.prepare(`SELECT p.pairing_id AS id, p.expires_at, p.used_at AS joined_at,
    d.display_name AS joined_device_name FROM device_pairing_codes p
    LEFT JOIN devices d ON d.device_id = p.claimed_device_id
    WHERE p.workspace_id = ?1 AND p.created_by_device_id = ?2 ORDER BY p.created_at DESC LIMIT 1`)
    .bind(device.workspaceId, device.deviceId).first<{
      id: string; expires_at: string; joined_at: string | null; joined_device_name: string | null;
    }>();
  const invitation = await env.DB.prepare(`SELECT i.invitation_id AS id, i.expires_at,
    i.consumed_at AS joined_at, i.revoked_at, i.delivery_state,
    d.display_name AS joined_device_name FROM device_invitations i
    LEFT JOIN devices d ON d.device_id = i.consumed_by_device_id
    WHERE i.workspace_id = ?1 AND i.issued_by_device_id = ?2 ORDER BY i.created_at DESC LIMIT 1`)
    .bind(device.workspaceId, device.deviceId).first<{
      id: string; expires_at: string; joined_at: string | null; revoked_at: string | null;
      delivery_state: string; joined_device_name: string | null;
    }>();
  const now = new Date().toISOString();
  const status = (row: { id: string; expires_at: string; joined_at: string | null;
    joined_device_name: string | null; revoked_at?: string | null; delivery_state?: string }): Record<string, JsonValue> => ({
      id: row.id,
      status: row.joined_at ? "joined" : row.revoked_at ? "revoked" : row.delivery_state === "failed" ? "failed" :
        row.expires_at <= now ? "expired" : "pending",
      expiresAt: row.expires_at,
      joinedAt: row.joined_at ?? "",
      joinedDeviceName: row.joined_device_name ?? "",
    });
  return json(env, requestId, 200, { pairing: pairing ? status(pairing) : null,
    invitation: invitation ? status(invitation) : null });
}

async function claimPairing(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await request.json<Record<string, unknown>>().catch(() => null);
  if (!body || !validPairingCode(body.code) || !validDisplayName(body.deviceDisplayName)
      || !validClientVersion(body.clientVersion) || !validDeviceToken(body.deviceToken)) {
    return errorResponse(env, requestId, 400, "INVALID_PAIRING_CLAIM", "Pairing claim is invalid.");
  }
  const normalizedCode = (body.code as string).trim().toLowerCase();
  const directJoin = body.directJoin === true;
  const normalizedToken = normalizeDeviceToken(body.deviceToken as string);
  const codeHash = await sha256Hex(normalizedCode);
  const tokenHash = await sha256Hex(normalizedToken);

  const byToken = await env.DB.prepare(
    `SELECT device_id, workspace_id, display_name AS device_display_name
       FROM devices WHERE token_hash = ?1 LIMIT 1`
  ).bind(tokenHash).first<PairingDeviceRow>();
  if (byToken) {
    const matchingConsumed = await env.DB.prepare(
      `SELECT pairing_id FROM device_pairing_codes
        WHERE code_hash = ?1 AND claimed_device_id = ?2
        LIMIT 1`
    ).bind(codeHash, byToken.device_id).first<{ pairing_id: string }>();
    if (!matchingConsumed)
      return errorResponse(env, requestId, 409, "DEVICE_TOKEN_ALREADY_USED", "Device token already belongs to another device claim.");
    return json(env, requestId, 200, {
      recovered: true,
      workspace: { workspaceId: byToken.workspace_id },
      device: { deviceId: byToken.device_id, displayName: byToken.device_display_name },
    });
  }

  const now = new Date().toISOString();
  const pairing = await env.DB.prepare(
    `SELECT pairing_id, workspace_id, used_at, claimed_device_id, expires_at
       FROM device_pairing_codes
      WHERE code_hash = ?1
      LIMIT 1`
  ).bind(codeHash).first<{
    pairing_id: string;
    workspace_id: string;
    used_at: string | null;
    claimed_device_id: string | null;
    expires_at: string;
  }>();
  if (!pairing) return errorResponse(env, requestId, 404, "PAIRING_NOT_FOUND", "Pairing code is invalid.");
  if (pairing.used_at) {
    await recordSecurityEvent(env.DB, { workspaceId: pairing.workspace_id, type: "pairing_claim_denied",
      outcome: "denied", pairingId: pairing.pairing_id, reasonCode: "PAIRING_ALREADY_USED", requestId });
    return errorResponse(env, requestId, 409, "PAIRING_ALREADY_USED", "Pairing code has already been used.");
  }
  if (pairing.expires_at <= now) {
    await recordSecurityEvent(env.DB, { workspaceId: pairing.workspace_id, type: "pairing_claim_denied",
      outcome: "denied", pairingId: pairing.pairing_id, reasonCode: "PAIRING_EXPIRED", requestId });
    return errorResponse(env, requestId, 410, "PAIRING_EXPIRED", "Pairing code has expired.");
  }
  if (directJoin && !await directJoinWorkspace(env, pairing.workspace_id))
    return errorResponse(env, requestId, 409, "DIRECT_JOIN_NOT_READY", "Central Employee authority is not ready.");

  const deviceId = `dev_${crypto.randomUUID()}`;
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO devices (
          device_id, workspace_id, display_name, token_hash, client_version,
          status, pairing_id, employee_authority_state, employee_transition_completed_at,
          paired_at, created_at, updated_at
        ) VALUES (?1, ?2, ?3, ?4, ?5, 'active', ?6, ?8, ?9, ?7, ?7, ?7)`
      ).bind(
        deviceId,
        pairing.workspace_id,
        (body.deviceDisplayName as string).trim(),
        tokenHash,
        (body.clientVersion as string).trim(),
        pairing.pairing_id,
        now, directJoin ? "cloud" : "transitioning", directJoin ? now : null),
      env.DB.prepare(
        `UPDATE device_pairing_codes
            SET used_at = ?1, claimed_device_id = ?2
          WHERE pairing_id = ?3 AND used_at IS NULL`
      ).bind(now, deviceId, pairing.pairing_id),
      securityEventStatement(env.DB, { workspaceId: pairing.workspace_id, type: "device_joined",
        outcome: "success", targetDeviceId: deviceId, pairingId: pairing.pairing_id, requestId }),
    ]);
  } catch (error) {
    const recovered = await env.DB.prepare(
      `SELECT d.device_id, d.workspace_id, d.display_name AS device_display_name
         FROM devices d
         JOIN device_pairing_codes p ON p.claimed_device_id = d.device_id
        WHERE d.token_hash = ?1 AND p.code_hash = ?2
        LIMIT 1`
    ).bind(tokenHash, codeHash).first<PairingDeviceRow>();
    if (recovered) {
      return json(env, requestId, 200, {
        recovered: true,
        workspace: { workspaceId: recovered.workspace_id },
        device: { deviceId: recovered.device_id, displayName: recovered.device_display_name },
      });
    }
    console.error("pairing_claim_failed", { requestId, error: error instanceof Error ? error.message : "unknown_error" });
    return errorResponse(env, requestId, 409, "PAIRING_CLAIM_CONFLICT", "Pairing claim conflicted with existing state.");
  }

  return json(env, requestId, 201, {
    workspace: { workspaceId: pairing.workspace_id },
    device: { deviceId, displayName: (body.deviceDisplayName as string).trim() },
  });
}

async function currentDevice(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  return json(env, requestId, 200, {
    workspace: { workspaceId: device.workspaceId },
    device: { deviceId: device.deviceId, displayName: device.deviceDisplayName },
  });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const requestId = requestIdFrom(request);
    const url = new URL(request.url);
    try {
      if (request.method === "GET" && url.pathname === "/v1/health") {
        const healthy = await storageHealth(env);
        return json(env, requestId, healthy ? 200 : 503, {
          storage: healthy ? "ok" : "unavailable",
          schemaVersion: env.SCHEMA_VERSION,
          ...(healthy ? {} : { error: { code: "STORAGE_UNAVAILABLE", message: "Backend storage health check failed." } }),
        });
      }
      if (request.method === "GET" && (url.pathname === "/v1/health/storage" || url.pathname === "/v1/health/db")) {
        const healthy = await storageHealth(env);
        return json(env, requestId, healthy ? 200 : 503, {
          storage: healthy ? "ok" : "unavailable",
          schemaVersion: env.SCHEMA_VERSION,
          ...(healthy ? {} : { error: { code: "STORAGE_UNAVAILABLE", message: "Backend storage health check failed." } }),
        });
      }
      if (request.method === "GET" && url.pathname === "/v1/onboarding/status")
        return await onboardingStatus(env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/onboarding/bootstrap-email")
        return await bootstrapEmailChallenge(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/bootstrap")
        return await bootstrapWorkspace(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-pairings/authorization-email")
        return await pairingAuthorizationChallenge(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-pairings")
        return await createPairing(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-pairings/claim")
        return await claimPairing(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-pairings/preview")
        return await previewPairing(request, env, requestId);
      if (request.method === "GET" && url.pathname.startsWith("/v1/device-pairings/status/"))
        return await onboardingTicketStatus(request, env, requestId, "pairing", url.pathname.slice(27));
      if (request.method === "POST" && url.pathname === "/v1/device-invitations")
        return await issueInvitation(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-invitations/preview")
        return await previewInvitation(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-invitations/claim")
        return await claimInvitation(request, env, requestId);
      if (request.method === "POST" && url.pathname === "/v1/device-invitations/revoke")
        return await revokeInvitation(request, env, requestId);
      if (request.method === "GET" && url.pathname.startsWith("/v1/device-invitations/status/"))
        return await onboardingTicketStatus(request, env, requestId, "invitation", url.pathname.slice(30));
      if (request.method === "GET" && url.pathname === "/v1/device-join-tickets")
        return await recentJoinTickets(request, env, requestId);
      if (request.method === "GET" && url.pathname === "/v1/device")
        return await currentDevice(request, env, requestId);
      return errorResponse(env, requestId, 404, "NOT_FOUND", "Route not found.");
    } catch (error) {
      console.error("cloud_request_failed", {
        requestId,
        path: url.pathname,
        error: error instanceof Error ? error.message : "unknown_error",
      });
      return errorResponse(env, requestId, 500, "INTERNAL_ERROR", "Request could not be completed.");
    }
  },
} satisfies ExportedHandler<Env>;
