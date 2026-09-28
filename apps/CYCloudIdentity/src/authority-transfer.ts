import { normalizePassword, verifyCredential } from "./crypto";
import { createEmailSender } from "./email";
import { json, readJsonObject } from "./http";
import { consumeVerifiedOtp, issueEmailOtp, verifyEmailOtp } from "./otp";
import { requireWorkspaceSuperAdmin } from "./admin-auth";
import type { Env, JsonValue } from "./types";

type CurrentAuthorityRow = {
  email_normalized: string;
  email_verified_at: string | null;
  algorithm: string;
  verifier: string;
};

type TransferTargetRow = {
  employee_id: string;
  email_normalized: string;
  email_verified_at: string | null;
  enabled: number;
  credential_present: number;
};

function normalizeEmployeeId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 5 && normalized.length <= 80 ? normalized : null;
}

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
}

function transferScope(workspaceId: string, actorEmployeeId: string, targetEmployeeId: string): string {
  return `${workspaceId}:${actorEmployeeId}:${targetEmployeeId}`;
}

async function targetForTransfer(
  env: Env,
  workspaceId: string,
  targetEmployeeId: string,
): Promise<TransferTargetRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id,
            e.email_normalized,
            e.email_verified_at,
            e.enabled,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
       FROM employees e
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_id = ?2
      LIMIT 1`
  ).bind(workspaceId, targetEmployeeId).first<TransferTargetRow>();
}

export async function handleStartHighestAuthorityTransfer(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const body = await readJsonObject(request);
  const targetEmployeeId = normalizeEmployeeId(body?.targetEmployeeId);
  const currentPassword = normalizePassword(body?.currentPassword);
  if (!targetEmployeeId || !currentPassword || targetEmployeeId === actor.employeeId) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_AUTHORITY_TRANSFER", message: "Highest-authority transfer request is invalid." },
    });
  }

  const current = await env.DB.prepare(
    `SELECT e.email_normalized,
            e.email_verified_at,
            c.algorithm,
            c.verifier
       FROM employees e
       JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_id = ?2
      LIMIT 1`
  ).bind(actor.workspaceId, actor.employeeId).first<CurrentAuthorityRow>();
  if (!current || !current.email_verified_at
      || !await verifyCredential(currentPassword, current.algorithm, current.verifier)) {
    return json(env, requestId, 401, {
      error: { code: "CURRENT_PASSWORD_INVALID", message: "Current highest-authority credential verification failed." },
    });
  }

  const target = await targetForTransfer(env, actor.workspaceId, targetEmployeeId);
  if (!target || target.enabled !== 1 || !target.email_verified_at || target.credential_present !== 1) {
    return json(env, requestId, 409, {
      error: { code: "AUTHORITY_TRANSFER_TARGET_NOT_READY", message: "Target Employee is not ready for highest authority." },
    });
  }

  try {
    const otp = await issueEmailOtp(env, emailSenderFrom(env), {
      purpose: "super_admin_transfer_authorization",
      scopeKey: transferScope(actor.workspaceId, actor.employeeId, targetEmployeeId),
      workspaceId: actor.workspaceId,
      email: current.email_normalized,
      subject: "CY Identity 最高管理權移交驗證碼",
      textPrefix: "正在移交 Workspace 最高管理權。若非本人操作，請勿提供此驗證碼。",
    });
    return json(env, requestId, 202, {
      transfer: {
        targetEmployeeId,
        challengeId: otp.challengeId,
        expiresAt: otp.expiresAt,
        resendAfter: otp.resendAfter,
      } as unknown as JsonValue,
    });
  } catch (error) {
    const code = error instanceof Error ? error.message : "AUTHORITY_TRANSFER_OTP_FAILED";
    const status = code === "OTP_RESEND_COOLDOWN" || code === "OTP_RATE_LIMITED"
      || code === "EMAIL_DAILY_BUDGET_EXHAUSTED" || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
      ? 429
      : 503;
    return json(env, requestId, status, {
      error: { code, message: "Highest-authority transfer verification code could not be issued." },
    });
  }
}

export async function handleConfirmHighestAuthorityTransfer(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const body = await readJsonObject(request);
  const targetEmployeeId = normalizeEmployeeId(body?.targetEmployeeId);
  const challengeId = typeof body?.challengeId === "string" ? body.challengeId.trim() : "";
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  if (!targetEmployeeId || targetEmployeeId === actor.employeeId || !challengeId || !/^\d{6}$/.test(code)) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_AUTHORITY_TRANSFER", message: "Highest-authority transfer confirmation is invalid." },
    });
  }

  const current = await env.DB.prepare(
    `SELECT email_normalized, email_verified_at
       FROM employees
      WHERE workspace_id = ?1
        AND employee_id = ?2
      LIMIT 1`
  ).bind(actor.workspaceId, actor.employeeId).first<{ email_normalized: string; email_verified_at: string | null }>();
  const target = await targetForTransfer(env, actor.workspaceId, targetEmployeeId);
  if (!current?.email_verified_at || !target || target.enabled !== 1
      || !target.email_verified_at || target.credential_present !== 1) {
    return json(env, requestId, 409, {
      error: { code: "AUTHORITY_TRANSFER_TARGET_NOT_READY", message: "Highest-authority transfer cannot be completed." },
    });
  }

  const challenge = await verifyEmailOtp(env, {
    challengeId,
    purpose: "super_admin_transfer_authorization",
    scopeKey: transferScope(actor.workspaceId, actor.employeeId, targetEmployeeId),
    code,
  });
  if (!challenge || challenge.email_normalized !== current.email_normalized) {
    return json(env, requestId, 400, {
      error: { code: "AUTHORITY_TRANSFER_VERIFICATION_FAILED", message: "Highest-authority transfer verification failed." },
    });
  }

  const now = new Date().toISOString();
  if (!await consumeVerifiedOtp(env, challengeId, now)) {
    return json(env, requestId, 400, {
      error: { code: "AUTHORITY_TRANSFER_VERIFICATION_FAILED", message: "Highest-authority transfer verification failed." },
    });
  }

  const changed = await env.DB.prepare(
    `UPDATE workspaces
        SET super_admin_employee_id = ?3,
            recovery_email_normalized = ?4,
            recovery_email_verified_at = ?5,
            revision = revision + 1,
            updated_at = ?6
      WHERE workspace_id = ?1
        AND super_admin_employee_id = ?2
    RETURNING workspace_id, revision`
  ).bind(
    actor.workspaceId,
    actor.employeeId,
    targetEmployeeId,
    target.email_normalized,
    target.email_verified_at,
    now,
  ).first<{ workspace_id: string; revision: number }>();
  if (!changed) {
    return json(env, requestId, 409, {
      error: { code: "AUTHORITY_TRANSFER_CONFLICT", message: "Workspace highest authority changed during this request." },
    });
  }

  await env.DB.batch([
    env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?2)
        WHERE employee_id = ?1`
    ).bind(actor.employeeId, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?4, 'workspace_super_admin_transferred', ?5, ?6)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      actor.workspaceId,
      actor.employeeId,
      targetEmployeeId,
      JSON.stringify({ workspaceRevision: changed.revision, previousEmployeeId: actor.employeeId }),
      now,
    ),
  ]);

  return json(env, requestId, 200, {
    transferred: true,
    workspaceId: actor.workspaceId,
    previousEmployeeId: actor.employeeId,
    superAdminEmployeeId: targetEmployeeId,
    recoveryEmail: target.email_normalized,
    previousAuthoritySessionsRevoked: true,
  });
}
