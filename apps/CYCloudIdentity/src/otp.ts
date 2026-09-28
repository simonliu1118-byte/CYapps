import { normalizeAddress, type EmailSender } from "./email";
import type { Env } from "./types";

export type OtpPurpose =
  | "workspace_bootstrap"
  | "workspace_recovery"
  | "employee_email_verification"
  | "employee_password_reset"
  | "super_admin_transfer_authorization"
  | "recovery_email_change";

export type OtpIssueResult = {
  challengeId: string;
  expiresAt: string;
  resendAfter: string;
};

export type OtpChallengeRow = {
  challenge_id: string;
  purpose: OtpPurpose;
  scope_key: string | null;
  email_normalized: string;
  otp_digest: string;
  delivery_state: "pending" | "sent" | "failed";
  attempt_count: number;
  max_attempts: number;
  expires_at: string;
  resend_after: string;
  sent_at: string | null;
  consumed_at: string | null;
};

const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_COOLDOWN_MS = 60 * 1000;
const OTP_MAX_ATTEMPTS = 5;
const OTP_MAX_SENT_PER_EMAIL_PURPOSE_HOUR = 5;
const MAX_EMAIL_DAILY_BUDGET = 10_000;
const OTP_SPACE = 1_000_000;
const UINT32_ACCEPT_BELOW = Math.floor(0x1_0000_0000 / OTP_SPACE) * OTP_SPACE;

function hex(bytes: Uint8Array): string {
  return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

function secureHexEquals(left: string, right: string): boolean {
  if (left.length !== right.length || !/^[0-9a-f]+$/i.test(left) || !/^[0-9a-f]+$/i.test(right)) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

function otpPepper(env: Env): string {
  const value = env.OTP_PEPPER?.trim() ?? "";
  if (new TextEncoder().encode(value).length < 32) throw new Error("OTP_PEPPER_NOT_CONFIGURED");
  return value;
}

function emailDailyBudget(env: Env): number {
  const value = Number(env.EMAIL_DAILY_BUDGET ?? "");
  if (!Number.isInteger(value) || value < 1 || value > MAX_EMAIL_DAILY_BUDGET) {
    throw new Error("EMAIL_DAILY_BUDGET_NOT_CONFIGURED");
  }
  return value;
}

function utcUsageDate(now: Date): string {
  return now.toISOString().slice(0, 10);
}

async function reserveEmailBudget(env: Env, now: Date): Promise<string | null> {
  const usageDate = utcUsageDate(now);
  const budget = emailDailyBudget(env);
  const row = await env.DB.prepare(
    `INSERT INTO email_delivery_budget(usage_date_utc, reserved_count, sent_count, updated_at)
     VALUES(?1, 1, 0, ?2)
     ON CONFLICT(usage_date_utc) DO UPDATE SET
       reserved_count = reserved_count + 1,
       updated_at = excluded.updated_at
     WHERE email_delivery_budget.sent_count + email_delivery_budget.reserved_count < ?3
     RETURNING reserved_count, sent_count`
  ).bind(usageDate, now.toISOString(), budget).first<{ reserved_count: number; sent_count: number }>();
  return row ? usageDate : null;
}

async function releaseEmailBudget(env: Env, usageDate: string, now: Date): Promise<void> {
  await env.DB.prepare(
    `UPDATE email_delivery_budget
        SET reserved_count = CASE WHEN reserved_count > 0 THEN reserved_count - 1 ELSE 0 END,
            updated_at = ?2
      WHERE usage_date_utc = ?1`
  ).bind(usageDate, now.toISOString()).run();
}

async function commitEmailBudget(env: Env, usageDate: string, now: Date): Promise<void> {
  await env.DB.prepare(
    `UPDATE email_delivery_budget
        SET reserved_count = CASE WHEN reserved_count > 0 THEN reserved_count - 1 ELSE 0 END,
            sent_count = sent_count + 1,
            updated_at = ?2
      WHERE usage_date_utc = ?1`
  ).bind(usageDate, now.toISOString()).run();
}

function randomOtpCode(): string {
  const values = new Uint32Array(1);
  for (;;) {
    crypto.getRandomValues(values);
    const value = values[0];
    if (value < UINT32_ACCEPT_BELOW) return String(value % OTP_SPACE).padStart(6, "0");
  }
}

async function otpDigest(
  env: Env,
  challengeId: string,
  purpose: OtpPurpose,
  scopeKey: string | null,
  email: string,
  code: string,
): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(otpPepper(env)),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const payload = [challengeId, purpose, scopeKey ?? "", email, code].join("\n");
  return hex(new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload))));
}

async function enforceOtpFrequency(
  env: Env,
  purpose: OtpPurpose,
  scopeKey: string | null,
  email: string,
  now: Date,
): Promise<void> {
  const latest = await env.DB.prepare(
    `SELECT resend_after
       FROM email_otp_challenges
      WHERE purpose = ?1
        AND ((scope_key = ?2) OR (scope_key IS NULL AND ?2 IS NULL))
        AND email_normalized = ?3
        AND consumed_at IS NULL
        AND delivery_state IN ('pending', 'sent')
      ORDER BY created_at DESC
      LIMIT 1`
  ).bind(purpose, scopeKey, email).first<{ resend_after: string }>();

  if (latest && latest.resend_after > now.toISOString()) throw new Error("OTP_RESEND_COOLDOWN");

  const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS count
       FROM email_otp_challenges
      WHERE purpose = ?1
        AND email_normalized = ?2
        AND sent_at >= ?3`
  ).bind(purpose, email, hourAgo).first<{ count: number }>();

  if ((recent?.count ?? 0) >= OTP_MAX_SENT_PER_EMAIL_PURPOSE_HOUR) throw new Error("OTP_RATE_LIMITED");
}

export async function issueEmailOtp(
  env: Env,
  sender: EmailSender,
  input: {
    purpose: OtpPurpose;
    scopeKey: string | null;
    email: string;
    subject: string;
    textPrefix: string;
  },
): Promise<OtpIssueResult> {
  const now = new Date();
  const email = normalizeAddress(input.email);
  await enforceOtpFrequency(env, input.purpose, input.scopeKey, email, now);

  const budgetUsageDate = await reserveEmailBudget(env, now);
  if (!budgetUsageDate) throw new Error("EMAIL_DAILY_BUDGET_EXHAUSTED");

  const challengeId = `otp_${crypto.randomUUID()}`;
  const code = randomOtpCode();
  const expiresAt = new Date(now.getTime() + OTP_TTL_MS).toISOString();
  const resendAfter = new Date(now.getTime() + OTP_RESEND_COOLDOWN_MS).toISOString();
  const digest = await otpDigest(env, challengeId, input.purpose, input.scopeKey, email, code);

  try {
    await env.DB.prepare(
      `INSERT INTO email_otp_challenges(
         challenge_id, purpose, scope_key, email_normalized, otp_digest,
         delivery_state, attempt_count, max_attempts, expires_at, resend_after,
         created_at, updated_at
       ) VALUES(?1, ?2, ?3, ?4, ?5, 'pending', 0, ?6, ?7, ?8, ?9, ?9)`
    ).bind(
      challengeId,
      input.purpose,
      input.scopeKey,
      email,
      digest,
      OTP_MAX_ATTEMPTS,
      expiresAt,
      resendAfter,
      now.toISOString(),
    ).run();
  } catch (error) {
    await releaseEmailBudget(env, budgetUsageDate, now);
    throw error;
  }

  try {
    await sender.send({
      to: email,
      subject: input.subject,
      text: `${input.textPrefix}\n\n驗證碼：${code}\n\n此驗證碼 10 分鐘內有效。請勿將驗證碼提供給其他人。`,
    });
    const sentAt = new Date();
    await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET delivery_state = 'sent', sent_at = ?2, updated_at = ?2
        WHERE challenge_id = ?1 AND delivery_state = 'pending'`
    ).bind(challengeId, sentAt.toISOString()).run();
    await commitEmailBudget(env, budgetUsageDate, sentAt);
  } catch (error) {
    const failedAt = new Date();
    await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET delivery_state = 'failed', updated_at = ?2
        WHERE challenge_id = ?1 AND delivery_state = 'pending'`
    ).bind(challengeId, failedAt.toISOString()).run();
    await releaseEmailBudget(env, budgetUsageDate, failedAt);
    throw error;
  }

  return { challengeId, expiresAt, resendAfter };
}

export async function verifyEmailOtp(
  env: Env,
  input: {
    challengeId: string;
    purpose: OtpPurpose;
    scopeKey: string | null;
    code: string;
  },
): Promise<OtpChallengeRow | null> {
  if (!/^\d{6}$/.test(input.code)) return null;
  const row = await env.DB.prepare(
    `SELECT challenge_id, purpose, scope_key, email_normalized, otp_digest,
            delivery_state, attempt_count, max_attempts, expires_at, resend_after,
            sent_at, consumed_at
       FROM email_otp_challenges
      WHERE challenge_id = ?1
        AND purpose = ?2
        AND ((scope_key = ?3) OR (scope_key IS NULL AND ?3 IS NULL))
      LIMIT 1`
  ).bind(input.challengeId, input.purpose, input.scopeKey).first<OtpChallengeRow>();

  const now = new Date().toISOString();
  if (!row || row.delivery_state !== "sent" || row.consumed_at || row.expires_at <= now) return null;
  if (row.attempt_count >= row.max_attempts) return null;

  const expected = await otpDigest(
    env,
    row.challenge_id,
    row.purpose,
    row.scope_key,
    row.email_normalized,
    input.code,
  );
  if (!secureHexEquals(expected, row.otp_digest)) {
    await env.DB.prepare(
      `UPDATE email_otp_challenges
          SET attempt_count = attempt_count + 1, updated_at = ?2
        WHERE challenge_id = ?1
          AND consumed_at IS NULL
          AND attempt_count < max_attempts`
    ).bind(row.challenge_id, now).run();
    return null;
  }
  return row;
}

export async function consumeVerifiedOtp(
  env: Env,
  challengeId: string,
  consumedAt: string,
): Promise<boolean> {
  const row = await env.DB.prepare(
    `UPDATE email_otp_challenges
        SET consumed_at = ?2, updated_at = ?2
      WHERE challenge_id = ?1
        AND consumed_at IS NULL
        AND delivery_state = 'sent'
        AND expires_at > ?2
        AND attempt_count < max_attempts
      RETURNING challenge_id`
  ).bind(challengeId, consumedAt).first<{ challenge_id: string }>();
  return Boolean(row);
}
