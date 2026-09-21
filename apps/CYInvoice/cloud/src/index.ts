import { createEmailSender } from "./email";

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
const CLOUD_VERSION = "0.5.0";
const MAX_REQUEST_ID_LENGTH = 128;
const MAX_DISPLAY_NAME_LENGTH = 120;
const MAX_CLIENT_VERSION_LENGTH = 64;
const MAX_EMAIL_LENGTH = 320;
const PAIRING_TTL_MS = 10 * 60 * 1000;
const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_COOLDOWN_MS = 60 * 1000;
const OTP_HOURLY_LIMIT = 5;
const OTP_MAX_ATTEMPTS = 5;
const WORKSPACE_BOOTSTRAP_PURPOSE = "workspace_bootstrap";

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

function methodNotAllowed(env: Env, requestId: string, allow: string): Response {
  return json(
    env,
    requestId,
    405,
    {
      error: {
        code: "METHOD_NOT_ALLOWED",
        message: "Method not allowed."
      }
    },
    { allow }
  );
}

function badRequest(env: Env, requestId: string, code: string, message: string): Response {
  return json(env, requestId, 400, {
    error: { code, message }
  });
}

function unauthorized(env: Env, requestId: string): Response {
  return json(env, requestId, 401, {
    error: {
      code: "UNAUTHORIZED",
      message: "Device authentication failed."
    }
  });
}

function randomHex(byteLength: number): string {
  const bytes = new Uint8Array(byteLength);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
}

function randomOtpCode(): string {
  const range = 1_000_000;
  const limit = Math.floor(0x1_0000_0000 / range) * range;
  const values = new Uint32Array(1);
  do {
    crypto.getRandomValues(values);
  } while (values[0] >= limit);
  return (values[0] % range).toString().padStart(6, "0");
}

async function sha256Hex(value: string): Promise<string> {
  const bytes = new TextEncoder().encode(value);
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

async function hmacSha256Hex(secret: string, value: string): Promise<string> {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value)));
  return Array.from(signature, part => part.toString(16).padStart(2, "0")).join("");
}

async function constantTimeSecretEquals(left: string, right: string): Promise<boolean> {
  const [leftHash, rightHash] = await Promise.all([sha256Hex(left), sha256Hex(right)]);
  let difference = 0;
  for (let index = 0; index < leftHash.length; index += 1) {
    difference |= leftHash.charCodeAt(index) ^ rightHash.charCodeAt(index);
  }
  return difference === 0;
}

async function requireBootstrapAuthorization(request: Request, env: Env): Promise<"ok" | "disabled" | "denied"> {
  if (!env.BOOTSTRAP_KEY) return "disabled";
  const suppliedKey = request.headers.get("x-bootstrap-key") ?? "";
  if (!suppliedKey || !(await constantTimeSecretEquals(suppliedKey, env.BOOTSTRAP_KEY))) return "denied";
  return "ok";
}

function bootstrapAuthorizationError(env: Env, requestId: string, state: "disabled" | "denied"): Response {
  if (state === "disabled") {
    return json(env, requestId, 503, {
      error: {
        code: "BOOTSTRAP_DISABLED",
        message: "Workspace bootstrap is not configured."
      }
    });
  }
  return json(env, requestId, 401, {
    error: {
      code: "BOOTSTRAP_AUTH_FAILED",
      message: "Workspace bootstrap authentication failed."
    }
  });
}

function bearerToken(request: Request): string | null {
  const authorization = request.headers.get("authorization")?.trim();
  if (!authorization) return null;
  const match = /^Bearer\s+(.+)$/i.exec(authorization);
  return match?.[1]?.trim() || null;
}

function normalizedDisplayName(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (normalized.length < 1 || normalized.length > MAX_DISPLAY_NAME_LENGTH) return null;
  return normalized;
}

function normalizedClientVersion(value: unknown): string | null {
  if (value === undefined || value === null || value === "") return null;
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (normalized.length < 1 || normalized.length > MAX_CLIENT_VERSION_LENGTH) return null;
  return normalized;
}

function normalizedDeviceToken(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^cydev_[0-9a-f]{64}$/.test(normalized) ? normalized : null;
}

function normalizedEmail(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  if (normalized.length < 3 || normalized.length > MAX_EMAIL_LENGTH || /[\r\n\s]/.test(normalized)) return null;
  const at = normalized.lastIndexOf("@");
  if (at <= 0 || at >= normalized.length - 1 || normalized.indexOf("@") !== at) return null;
  const domain = normalized.slice(at + 1);
  if (!domain.includes(".") || domain.startsWith(".") || domain.endsWith(".")) return null;
  return normalized;
}

function maskedEmail(email: string): string {
  const at = email.lastIndexOf("@");
  if (at <= 0) return "***";
  const local = email.slice(0, at);
  const domain = email.slice(at + 1);
  const visibleLocal = local.length <= 2 ? local[0] ?? "*" : local.slice(0, 2);
  return `${visibleLocal}***@${domain}`;
}

function normalizedChallengeId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^otp_[0-9a-f-]{36}$/.test(normalized) ? normalized : null;
}

function normalizedOtp(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{6}$/.test(normalized) ? normalized : null;
}

async function otpDigest(env: Env, challengeId: string, otp: string): Promise<string | null> {
  const pepper = env.OTP_PEPPER?.trim() ?? "";
  if (!pepper) return null;
  return hmacSha256Hex(pepper, `${challengeId}:${otp}`);
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

async function storageHealth(env: Env, requestId: string): Promise<Response> {
  try {
    await env.DB.prepare("SELECT 1 AS ok FROM workspaces LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM devices LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM device_pairing_codes LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM email_otp_challenges LIMIT 1").first();

    return json(env, requestId, 200, {
      storage: "ok",
      schemaVersion: env.SCHEMA_VERSION
    });
  } catch (error) {
    console.error("storage_health_failed", {
      requestId,
      error: error instanceof Error ? error.message : "unknown_error"
    });

    return json(env, requestId, 503, {
      storage: "unavailable",
      schemaVersion: env.SCHEMA_VERSION,
      error: {
        code: "STORAGE_UNAVAILABLE",
        message: "Backend storage health check failed."
      }
    });
  }
}

async function onboardingStatus(env: Env, requestId: string): Promise<Response> {
  const row = await env.DB.prepare("SELECT COUNT(*) AS count FROM workspaces").first<{ count: number }>();
  const initialized = Number(row?.count ?? 0) > 0;

  return json(env, requestId, 200, {
    onboarding: {
      state: initialized ? "initialized" : "uninitialized",
      workspaceInitialized: initialized
    }
  });
}

async function startBootstrapEmailChallenge(request: Request, env: Env, requestId: string): Promise<Response> {
  const auth = await requireBootstrapAuthorization(request, env);
  if (auth !== "ok") return bootstrapAuthorizationError(env, requestId, auth);

  const workspaceCount = await env.DB.prepare("SELECT COUNT(*) AS count FROM workspaces").first<{ count: number }>();
  if (Number(workspaceCount?.count ?? 0) !== 0) {
    return json(env, requestId, 409, {
      error: {
        code: "WORKSPACE_ALREADY_INITIALIZED",
        message: "A workspace already exists."
      }
    });
  }

  if (!env.OTP_PEPPER?.trim()) {
    return json(env, requestId, 503, {
      error: {
        code: "OTP_NOT_CONFIGURED",
        message: "Email verification is not configured."
      }
    });
  }

  const body = await readJsonObject(request);
  if (!body) return badRequest(env, requestId, "INVALID_JSON", "A JSON object is required.");
  const email = normalizedEmail(body.email);
  if (!email) return badRequest(env, requestId, "INVALID_EMAIL", "Email address is invalid.");

  const now = new Date();
  const nowText = now.toISOString();
  const latest = await env.DB.prepare(
    `SELECT resend_after
       FROM email_otp_challenges
      WHERE purpose = ?1 AND email_normalized = ?2
      ORDER BY created_at DESC
      LIMIT 1`
  ).bind(WORKSPACE_BOOTSTRAP_PURPOSE, email).first<{ resend_after: string }>();

  if (latest && latest.resend_after > nowText) {
    const retryAfterSeconds = Math.max(1, Math.ceil((Date.parse(latest.resend_after) - now.getTime()) / 1000));
    return json(env, requestId, 429, {
      retryAfterSeconds,
      error: {
        code: "OTP_RESEND_COOLDOWN",
        message: "Please wait before requesting another verification code."
      }
    }, { "retry-after": retryAfterSeconds.toString() });
  }

  const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS count
       FROM email_otp_challenges
      WHERE purpose = ?1 AND email_normalized = ?2 AND created_at >= ?3`
  ).bind(WORKSPACE_BOOTSTRAP_PURPOSE, email, hourAgo).first<{ count: number }>();
  if (Number(recent?.count ?? 0) >= OTP_HOURLY_LIMIT) {
    return json(env, requestId, 429, {
      error: {
        code: "OTP_RATE_LIMITED",
        message: "Too many verification requests. Please try again later."
      }
    });
  }

  let sender;
  try {
    sender = createEmailSender({
      provider: env.EMAIL_PROVIDER,
      brevoApiKey: env.BREVO_API_KEY,
      resendApiKey: env.RESEND_API_KEY,
      from: env.EMAIL_FROM
    });
  } catch {
    return json(env, requestId, 503, {
      error: {
        code: "EMAIL_PROVIDER_NOT_CONFIGURED",
        message: "Email delivery is not configured."
      }
    });
  }

  const challengeId = `otp_${crypto.randomUUID()}`;
  const otp = randomOtpCode();
  const digest = await otpDigest(env, challengeId, otp);
  if (!digest) {
    return json(env, requestId, 503, {
      error: {
        code: "OTP_NOT_CONFIGURED",
        message: "Email verification is not configured."
      }
    });
  }

  const expiresAt = new Date(now.getTime() + OTP_TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + OTP_RESEND_COOLDOWN_MS).toISOString();

  await env.DB.prepare(
    `INSERT INTO email_otp_challenges (
        challenge_id, purpose, email_normalized, otp_digest, delivery_state,
        attempt_count, max_attempts, expires_at, resend_after, created_at, updated_at
     ) VALUES (?1, ?2, ?3, ?4, 'pending', 0, ?5, ?6, ?7, ?8, ?8)`
  ).bind(
    challengeId,
    WORKSPACE_BOOTSTRAP_PURPOSE,
    email,
    digest,
    OTP_MAX_ATTEMPTS,
    expiresAt,
    resendAfter,
    nowText
  ).run();

  try {
    await sender.send({
      to: email,
      subject: "CYInvoice 雲端驗證碼",
      text: `您的 CYInvoice 雲端驗證碼是 ${otp}。驗證碼 10 分鐘內有效，請勿提供給其他人。`,
      html: `<p>您的 CYInvoice 雲端驗證碼是：</p><p style="font-size:28px;font-weight:700;letter-spacing:6px">${otp}</p><p>驗證碼 10 分鐘內有效，請勿提供給其他人。</p>`
    });
  } catch (error) {
    await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET delivery_state = 'failed', updated_at = ?1
        WHERE challenge_id = ?2`
    ).bind(new Date().toISOString(), challengeId).run();
    console.warn("otp_email_delivery_failed", {
      requestId,
      error: error instanceof Error ? error.message : "email_delivery_failed"
    });
    return json(env, requestId, 503, {
      error: {
        code: "EMAIL_DELIVERY_UNAVAILABLE",
        message: "Verification email could not be sent."
      }
    });
  }

  const sentAt = new Date().toISOString();
  await env.DB.prepare(
    `UPDATE email_otp_challenges
        SET delivery_state = 'sent', sent_at = ?1, updated_at = ?1
      WHERE challenge_id = ?2`
  ).bind(sentAt, challengeId).run();

  return json(env, requestId, 201, {
    challenge: {
      challengeId,
      maskedEmail: maskedEmail(email),
      expiresAt,
      resendAfter
    }
  });
}

async function authenticateDevice(request: Request, env: Env): Promise<DeviceIdentity | null> {
  const token = bearerToken(request);
  if (!token || token.length > 160 || !token.startsWith("cydev_")) return null;

  const tokenHash = await sha256Hex(token);
  const row = await env.DB.prepare(
    `SELECT d.device_id, d.workspace_id, d.display_name
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<{ device_id: string; workspace_id: string; display_name: string }>();

  if (!row) return null;

  try {
    await env.DB.prepare(
      "UPDATE devices SET last_seen_at = ?1, updated_at = ?1 WHERE device_id = ?2"
    ).bind(new Date().toISOString(), row.device_id).run();
  } catch (error) {
    console.warn("device_last_seen_update_failed", {
      deviceId: row.device_id,
      error: error instanceof Error ? error.message : "unknown_error"
    });
  }

  return {
    deviceId: row.device_id,
    workspaceId: row.workspace_id,
    deviceDisplayName: row.display_name
  };
}

async function findBootstrapDeviceByTokenHash(env: Env, tokenHash: string): Promise<BootstrapDeviceRow | null> {
  return env.DB.prepare(
    `SELECT d.device_id,
            d.workspace_id,
            d.display_name AS device_display_name,
            w.display_name AS workspace_display_name
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<BootstrapDeviceRow>();
}

function bootstrapIdentityResponse(env: Env, requestId: string, status: number, row: BootstrapDeviceRow): Response {
  return json(env, requestId, status, {
    workspace: {
      workspaceId: row.workspace_id,
      displayName: row.workspace_display_name
    },
    device: {
      deviceId: row.device_id,
      displayName: row.device_display_name
    }
  });
}

async function validateBootstrapOtp(
  env: Env,
  requestId: string,
  challengeId: string,
  otp: string
): Promise<{ email: string } | Response> {
  if (!env.OTP_PEPPER?.trim()) {
    return json(env, requestId, 503, {
      error: {
        code: "OTP_NOT_CONFIGURED",
        message: "Email verification is not configured."
      }
    });
  }

  const row = await env.DB.prepare(
    `SELECT challenge_id, email_normalized, otp_digest, delivery_state,
            attempt_count, max_attempts, expires_at, resend_after, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1 AND purpose = ?2
      LIMIT 1`
  ).bind(challengeId, WORKSPACE_BOOTSTRAP_PURPOSE).first<OtpChallengeRow>();

  const now = new Date().toISOString();
  if (!row || row.delivery_state !== "sent" || row.consumed_at || row.expires_at <= now) {
    return badRequest(env, requestId, "OTP_INVALID", "Verification code is invalid or expired.");
  }
  if (Number(row.attempt_count) >= Number(row.max_attempts)) {
    return json(env, requestId, 429, {
      error: {
        code: "OTP_ATTEMPTS_EXHAUSTED",
        message: "Verification attempts have been exhausted."
      }
    });
  }

  const suppliedDigest = await otpDigest(env, challengeId, otp);
  if (!suppliedDigest || !(await constantTimeSecretEquals(row.otp_digest, suppliedDigest))) {
    const result = await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET attempt_count = attempt_count + 1, updated_at = ?1
        WHERE challenge_id = ?2 AND consumed_at IS NULL AND attempt_count < max_attempts`
    ).bind(now, challengeId).run();
    const attempts = Number(row.attempt_count) + (result.meta.changes > 0 ? 1 : 0);
    if (attempts >= Number(row.max_attempts)) {
      return json(env, requestId, 429, {
        error: {
          code: "OTP_ATTEMPTS_EXHAUSTED",
          message: "Verification attempts have been exhausted."
        }
      });
    }
    return badRequest(env, requestId, "OTP_INVALID", "Verification code is invalid or expired.");
  }

  return { email: row.email_normalized };
}

async function bootstrapWorkspace(request: Request, env: Env, requestId: string): Promise<Response> {
  const auth = await requireBootstrapAuthorization(request, env);
  if (auth !== "ok") return bootstrapAuthorizationError(env, requestId, auth);

  const body = await readJsonObject(request);
  if (!body) return badRequest(env, requestId, "INVALID_JSON", "A JSON object is required.");

  const workspaceDisplayName = normalizedDisplayName(body.workspaceDisplayName);
  const deviceDisplayName = normalizedDisplayName(body.deviceDisplayName);
  const clientVersion = normalizedClientVersion(body.clientVersion);
  const deviceToken = normalizedDeviceToken(body.deviceToken);
  const emailChallengeId = normalizedChallengeId(body.emailChallengeId);
  const emailOtp = normalizedOtp(body.emailOtp);
  if (!workspaceDisplayName || !deviceDisplayName || !deviceToken || !emailChallengeId || !emailOtp
      || (body.clientVersion !== undefined && clientVersion === null)) {
    return badRequest(env, requestId, "INVALID_BOOTSTRAP_INPUT", "Workspace, device, or email verification information is invalid.");
  }

  const tokenHash = await sha256Hex(deviceToken);
  const retry = await findBootstrapDeviceByTokenHash(env, tokenHash);
  if (retry) return bootstrapIdentityResponse(env, requestId, 200, retry);

  const countRow = await env.DB.prepare("SELECT COUNT(*) AS count FROM workspaces").first<{ count: number }>();
  if (Number(countRow?.count ?? 0) !== 0) {
    return json(env, requestId, 409, {
      error: {
        code: "WORKSPACE_ALREADY_INITIALIZED",
        message: "A workspace already exists."
      }
    });
  }

  const otpValidation = await validateBootstrapOtp(env, requestId, emailChallengeId, emailOtp);
  if (otpValidation instanceof Response) return otpValidation;

  const workspaceId = `ws_${crypto.randomUUID()}`;
  const deviceId = `dev_${crypto.randomUUID()}`;
  const now = new Date().toISOString();

  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO workspaces (
            workspace_id, display_name, status, recovery_email,
            recovery_email_verified_at, created_at, updated_at
         ) VALUES (?1, ?2, 'active', ?3, ?4, ?4, ?4)`
      ).bind(workspaceId, workspaceDisplayName, otpValidation.email, now),
      env.DB.prepare(
        `INSERT INTO devices (
            device_id, workspace_id, display_name, status, client_version,
            paired_at, last_seen_at, created_at, updated_at, token_hash, token_created_at
         ) VALUES (?1, ?2, ?3, 'active', ?4, ?5, ?5, ?5, ?5, ?6, ?5)`
      ).bind(deviceId, workspaceId, deviceDisplayName, clientVersion, now, tokenHash),
      env.DB.prepare(
        `UPDATE email_otp_challenges
            SET consumed_at = ?1, updated_at = ?1
          WHERE challenge_id = ?2
            AND purpose = ?3
            AND consumed_at IS NULL`
      ).bind(now, emailChallengeId, WORKSPACE_BOOTSTRAP_PURPOSE)
    ]);
  } catch (error) {
    const recovered = await findBootstrapDeviceByTokenHash(env, tokenHash);
    if (recovered) return bootstrapIdentityResponse(env, requestId, 200, recovered);

    const afterCount = await env.DB.prepare("SELECT COUNT(*) AS count FROM workspaces").first<{ count: number }>();
    if (Number(afterCount?.count ?? 0) !== 0) {
      return json(env, requestId, 409, {
        error: {
          code: "WORKSPACE_ALREADY_INITIALIZED",
          message: "A workspace already exists."
        }
      });
    }

    throw error;
  }

  return bootstrapIdentityResponse(env, requestId, 201, {
    device_id: deviceId,
    workspace_id: workspaceId,
    device_display_name: deviceDisplayName,
    workspace_display_name: workspaceDisplayName
  });
}

async function createDevicePairing(request: Request, env: Env, requestId: string): Promise<Response> {
  const identity = await authenticateDevice(request, env);
  if (!identity) return unauthorized(env, requestId);

  const pairingId = `pair_${crypto.randomUUID()}`;
  const pairingCode = randomHex(10);
  const codeHash = await sha256Hex(pairingCode);
  const now = new Date();
  const expiresAt = new Date(now.getTime() + PAIRING_TTL_MS).toISOString();

  await env.DB.prepare(
    `INSERT INTO device_pairing_codes (
        pairing_id, workspace_id, code_hash, created_by_device_id, expires_at, created_at
     ) VALUES (?1, ?2, ?3, ?4, ?5, ?6)`
  ).bind(pairingId, identity.workspaceId, codeHash, identity.deviceId, expiresAt, now.toISOString()).run();

  return json(env, requestId, 201, {
    pairing: {
      code: pairingCode,
      expiresAt
    }
  });
}

async function claimDevicePairing(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await readJsonObject(request);
  if (!body) return badRequest(env, requestId, "INVALID_JSON", "A JSON object is required.");

  const code = typeof body.code === "string" ? body.code.trim().toLowerCase() : "";
  const deviceDisplayName = normalizedDisplayName(body.deviceDisplayName);
  const clientVersion = normalizedClientVersion(body.clientVersion);
  if (!/^[0-9a-f]{20}$/.test(code) || !deviceDisplayName || (body.clientVersion !== undefined && clientVersion === null)) {
    return badRequest(env, requestId, "INVALID_PAIRING_INPUT", "Pairing information is invalid.");
  }

  const codeHash = await sha256Hex(code);
  const now = new Date().toISOString();
  const pairing = await env.DB.prepare(
    `SELECT p.pairing_id, p.workspace_id
       FROM device_pairing_codes p
       JOIN workspaces w ON w.workspace_id = p.workspace_id
      WHERE p.code_hash = ?1
        AND p.used_at IS NULL
        AND p.expires_at > ?2
        AND w.status = 'active'
      LIMIT 1`
  ).bind(codeHash, now).first<{ pairing_id: string; workspace_id: string }>();

  if (!pairing) {
    return json(env, requestId, 400, {
      error: {
        code: "PAIRING_CODE_INVALID",
        message: "Pairing code is invalid or expired."
      }
    });
  }

  const deviceId = `dev_${crypto.randomUUID()}`;
  const deviceToken = `cydev_${randomHex(32)}`;
  const tokenHash = await sha256Hex(deviceToken);

  try {
    await env.DB.prepare(
      `INSERT INTO devices (
          device_id, workspace_id, display_name, status, client_version,
          paired_at, last_seen_at, created_at, updated_at, token_hash,
          token_created_at, pairing_id
       )
       SELECT ?1, p.workspace_id, ?2, 'active', ?3,
              ?4, ?4, ?4, ?4, ?5, ?4, p.pairing_id
         FROM device_pairing_codes p
        WHERE p.pairing_id = ?6
          AND p.used_at IS NULL
          AND p.expires_at > ?4`
    ).bind(deviceId, deviceDisplayName, clientVersion, now, tokenHash, pairing.pairing_id).run();
  } catch (error) {
    console.warn("device_pairing_insert_failed", {
      pairingId: pairing.pairing_id,
      error: error instanceof Error ? error.message : "unknown_error"
    });
    return json(env, requestId, 409, {
      error: {
        code: "PAIRING_CODE_USED",
        message: "Pairing code has already been used."
      }
    });
  }

  const created = await env.DB.prepare(
    "SELECT device_id FROM devices WHERE device_id = ?1 AND pairing_id = ?2 LIMIT 1"
  ).bind(deviceId, pairing.pairing_id).first<{ device_id: string }>();

  if (!created) {
    return json(env, requestId, 409, {
      error: {
        code: "PAIRING_CODE_USED",
        message: "Pairing code has already been used."
      }
    });
  }

  await env.DB.prepare(
    `UPDATE device_pairing_codes
        SET used_at = ?1, claimed_device_id = ?2
      WHERE pairing_id = ?3 AND used_at IS NULL`
  ).bind(now, deviceId, pairing.pairing_id).run();

  return json(env, requestId, 201, {
    workspace: {
      workspaceId: pairing.workspace_id
    },
    device: {
      deviceId,
      displayName: deviceDisplayName,
      token: deviceToken
    }
  });
}

async function currentDevice(request: Request, env: Env, requestId: string): Promise<Response> {
  const identity = await authenticateDevice(request, env);
  if (!identity) return unauthorized(env, requestId);

  return json(env, requestId, 200, {
    workspace: {
      workspaceId: identity.workspaceId
    },
    device: {
      deviceId: identity.deviceId,
      displayName: identity.deviceDisplayName
    }
  });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const requestId = requestIdFrom(request);
    const url = new URL(request.url);

    try {
      switch (url.pathname) {
        case "/":
        case "/health":
          if (request.method !== "GET") return methodNotAllowed(env, requestId, "GET");
          return json(env, requestId, 200, {
            status: "ok",
            schemaVersion: env.SCHEMA_VERSION
          });

        case "/v1/health":
        case "/v1/health/storage":
        case "/v1/health/db":
          if (request.method !== "GET") return methodNotAllowed(env, requestId, "GET");
          return storageHealth(env, requestId);

        case "/v1/version":
          if (request.method !== "GET") return methodNotAllowed(env, requestId, "GET");
          return json(env, requestId, 200, {
            schemaVersion: env.SCHEMA_VERSION
          });

        case "/v1/onboarding/status":
          if (request.method !== "GET") return methodNotAllowed(env, requestId, "GET");
          return onboardingStatus(env, requestId);

        case "/v1/onboarding/bootstrap-email":
          if (request.method !== "POST") return methodNotAllowed(env, requestId, "POST");
          return startBootstrapEmailChallenge(request, env, requestId);

        case "/v1/bootstrap":
          if (request.method !== "POST") return methodNotAllowed(env, requestId, "POST");
          return bootstrapWorkspace(request, env, requestId);

        case "/v1/device-pairings":
          if (request.method !== "POST") return methodNotAllowed(env, requestId, "POST");
          return createDevicePairing(request, env, requestId);

        case "/v1/device-pairings/claim":
          if (request.method !== "POST") return methodNotAllowed(env, requestId, "POST");
          return claimDevicePairing(request, env, requestId);

        case "/v1/device":
          if (request.method !== "GET") return methodNotAllowed(env, requestId, "GET");
          return currentDevice(request, env, requestId);

        default:
          return json(env, requestId, 404, {
            error: {
              code: "NOT_FOUND",
              message: "Route not found."
            }
          });
      }
    } catch (error) {
      console.error("request_failed", {
        requestId,
        path: url.pathname,
        error: error instanceof Error ? error.message : "unknown_error"
      });

      return json(env, requestId, 500, {
        error: {
          code: "INTERNAL_ERROR",
          message: "Request could not be completed."
        }
      });
    }
  }
} satisfies ExportedHandler<Env>;
