import { requireWorkspaceSuperAdmin } from "./admin-auth";
import { json, readJsonObject } from "./http";
import type { Env, JsonValue } from "./types";

export type WorkspaceSecurityPolicy = {
  otpResendCooldownSeconds: number;
  otpMaxAttempts: number;
  otpMaxSentPerEmailPurposeHour: number;
  emailDailyLimit: number;
  revision: number;
};

type SecurityPolicyRow = {
  otp_resend_cooldown_seconds: number;
  otp_max_attempts: number;
  otp_max_sent_per_email_purpose_hour: number;
  email_daily_limit: number;
  revision: number;
};

const DEFAULT_OTP_RESEND_COOLDOWN_SECONDS = 60;
const DEFAULT_OTP_MAX_ATTEMPTS = 5;
const DEFAULT_OTP_MAX_SENT_PER_EMAIL_PURPOSE_HOUR = 5;
const DEFAULT_WORKSPACE_EMAIL_DAILY_LIMIT = 100;
const MAX_GLOBAL_EMAIL_DAILY_BUDGET = 10_000;

export function globalEmailDailyCeiling(env: Env): number {
  const value = Number(env.EMAIL_DAILY_BUDGET ?? "");
  if (!Number.isInteger(value) || value < 1 || value > MAX_GLOBAL_EMAIL_DAILY_BUDGET) {
    throw new Error("EMAIL_DAILY_BUDGET_NOT_CONFIGURED");
  }
  return value;
}

export async function loadWorkspaceSecurityPolicy(
  env: Env,
  workspaceId: string,
): Promise<WorkspaceSecurityPolicy> {
  const row = await env.DB.prepare(
    `SELECT otp_resend_cooldown_seconds,
            otp_max_attempts,
            otp_max_sent_per_email_purpose_hour,
            email_daily_limit,
            revision
       FROM identity_security_policies
      WHERE workspace_id = ?1
      LIMIT 1`
  ).bind(workspaceId).first<SecurityPolicyRow>();

  if (row) {
    return {
      otpResendCooldownSeconds: row.otp_resend_cooldown_seconds,
      otpMaxAttempts: row.otp_max_attempts,
      otpMaxSentPerEmailPurposeHour: row.otp_max_sent_per_email_purpose_hour,
      emailDailyLimit: Math.min(row.email_daily_limit, globalEmailDailyCeiling(env)),
      revision: row.revision,
    };
  }

  return {
    otpResendCooldownSeconds: DEFAULT_OTP_RESEND_COOLDOWN_SECONDS,
    otpMaxAttempts: DEFAULT_OTP_MAX_ATTEMPTS,
    otpMaxSentPerEmailPurposeHour: DEFAULT_OTP_MAX_SENT_PER_EMAIL_PURPOSE_HOUR,
    emailDailyLimit: Math.min(DEFAULT_WORKSPACE_EMAIL_DAILY_LIMIT, globalEmailDailyCeiling(env)),
    revision: 0,
  };
}

function integerInRange(value: unknown, min: number, max: number): number | null {
  return Number.isInteger(value) && Number(value) >= min && Number(value) <= max ? Number(value) : null;
}

export async function handleGetSecurityPolicy(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireWorkspaceSuperAdmin(request, env);
  if (!principal) {
    return json(env, requestId, 403, {
      error: { code: "HIGHEST_AUTHORITY_REQUIRED", message: "Workspace highest authority is required." },
    });
  }

  const policy = await loadWorkspaceSecurityPolicy(env, principal.workspaceId);
  return json(env, requestId, 200, {
    policy: policy as unknown as JsonValue,
    systemEmailDailyCeiling: globalEmailDailyCeiling(env),
  });
}

export async function handleUpdateSecurityPolicy(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireWorkspaceSuperAdmin(request, env);
  if (!principal) {
    return json(env, requestId, 403, {
      error: { code: "HIGHEST_AUTHORITY_REQUIRED", message: "Workspace highest authority is required." },
    });
  }

  const body = await readJsonObject(request);
  const cooldown = integerInRange(body?.otpResendCooldownSeconds, 30, 600);
  const maxAttempts = integerInRange(body?.otpMaxAttempts, 3, 10);
  const perHour = integerInRange(body?.otpMaxSentPerEmailPurposeHour, 1, 20);
  const globalCeiling = globalEmailDailyCeiling(env);
  const dailyLimit = integerInRange(body?.emailDailyLimit, 1, globalCeiling);

  if (cooldown === null || maxAttempts === null || perHour === null || dailyLimit === null) {
    return json(env, requestId, 400, {
      error: {
        code: "INVALID_SECURITY_POLICY",
        message: "Security policy values are outside the allowed safety bounds.",
      },
      allowed: {
        otpResendCooldownSeconds: { min: 30, max: 600 },
        otpMaxAttempts: { min: 3, max: 10 },
        otpMaxSentPerEmailPurposeHour: { min: 1, max: 20 },
        emailDailyLimit: { min: 1, max: globalCeiling },
      },
    });
  }

  const before = await loadWorkspaceSecurityPolicy(env, principal.workspaceId);
  const now = new Date().toISOString();

  const updated = await env.DB.prepare(
    `INSERT INTO identity_security_policies(
       workspace_id,
       otp_resend_cooldown_seconds,
       otp_max_attempts,
       otp_max_sent_per_email_purpose_hour,
       email_daily_limit,
       revision,
       updated_by_employee_id,
       created_at,
       updated_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, 1, ?6, ?7, ?7)
     ON CONFLICT(workspace_id) DO UPDATE SET
       otp_resend_cooldown_seconds = excluded.otp_resend_cooldown_seconds,
       otp_max_attempts = excluded.otp_max_attempts,
       otp_max_sent_per_email_purpose_hour = excluded.otp_max_sent_per_email_purpose_hour,
       email_daily_limit = excluded.email_daily_limit,
       revision = identity_security_policies.revision + 1,
       updated_by_employee_id = excluded.updated_by_employee_id,
       updated_at = excluded.updated_at
     RETURNING otp_resend_cooldown_seconds,
               otp_max_attempts,
               otp_max_sent_per_email_purpose_hour,
               email_daily_limit,
               revision`
  ).bind(
    principal.workspaceId,
    cooldown,
    maxAttempts,
    perHour,
    dailyLimit,
    principal.employeeId,
    now,
  ).first<SecurityPolicyRow>();

  if (!updated) throw new Error("SECURITY_POLICY_UPDATE_FAILED");

  const after: WorkspaceSecurityPolicy = {
    otpResendCooldownSeconds: updated.otp_resend_cooldown_seconds,
    otpMaxAttempts: updated.otp_max_attempts,
    otpMaxSentPerEmailPurposeHour: updated.otp_max_sent_per_email_purpose_hour,
    emailDailyLimit: updated.email_daily_limit,
    revision: updated.revision,
  };

  await env.DB.prepare(
    `INSERT INTO identity_audit_events(
       event_id, workspace_id, actor_employee_id, target_employee_id,
       event_type, detail_json, created_at
     ) VALUES(?1, ?2, ?3, ?3, 'identity_security_policy_updated', ?4, ?5)`
  ).bind(
    `audit_${crypto.randomUUID()}`,
    principal.workspaceId,
    principal.employeeId,
    JSON.stringify({ before, after }),
    now,
  ).run();

  return json(env, requestId, 200, {
    policy: after as unknown as JsonValue,
    systemEmailDailyCeiling: globalCeiling,
  });
}
