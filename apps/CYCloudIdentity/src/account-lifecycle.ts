import {
  createCredentialVerifier,
  CURRENT_CREDENTIAL_ALGORITHM,
  normalizePassword,
  verifyCredential,
} from "./crypto";
import { createEmailSender, normalizeAddress } from "./email";
import { json, readJsonObject } from "./http";
import { consumeVerifiedOtp, issueEmailOtp, verifyEmailOtp } from "./otp";
import { requireIdentitySession } from "./session-auth";
import type { Env, JsonValue } from "./types";

type CredentialRow = {
  algorithm: string;
  verifier: string;
  credential_version: number;
};

type EmployeeRecoveryRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  email_normalized: string;
  email_verified_at: string | null;
  enabled: number;
  workspace_status: string;
};

type EmployeeEmailRow = {
  email_normalized: string;
  email_verified_at: string | null;
  super_admin_employee_id: string | null;
};

const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_FALLBACK_MS = 60 * 1000;

function normalizeWorkspaceId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 5 && normalized.length <= 80 ? normalized : null;
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
}

function fakeOtpIssue() {
  const now = Date.now();
  return {
    challengeId: `otp_${crypto.randomUUID()}`,
    expiresAt: new Date(now + OTP_TTL_MS).toISOString(),
    resendAfter: new Date(now + OTP_RESEND_FALLBACK_MS).toISOString(),
  };
}

async function credentialForEmployee(env: Env, employeeId: string): Promise<CredentialRow | null> {
  return env.DB.prepare(
    `SELECT algorithm, verifier, credential_version
       FROM employee_credentials
      WHERE employee_id = ?1
      LIMIT 1`
  ).bind(employeeId).first<CredentialRow>();
}

async function audit(
  env: Env,
  input: {
    workspaceId: string;
    actorEmployeeId?: string | null;
    targetEmployeeId: string;
    eventType: string;
    detail?: Record<string, JsonValue>;
  },
): Promise<void> {
  await env.DB.prepare(
    `INSERT INTO identity_audit_events(
       event_id, workspace_id, actor_employee_id, target_employee_id,
       event_type, detail_json, created_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7)`
  ).bind(
    `audit_${crypto.randomUUID()}`,
    input.workspaceId,
    input.actorEmployeeId ?? null,
    input.targetEmployeeId,
    input.eventType,
    input.detail ? JSON.stringify(input.detail) : null,
    new Date().toISOString(),
  ).run();
}

export async function handleChangeOwnPassword(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireIdentitySession(request, env);
  if (!principal) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }
  const body = await readJsonObject(request);
  const currentPassword = normalizePassword(body?.currentPassword);
  const newPassword = normalizePassword(body?.newPassword);
  if (!currentPassword || !newPassword) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_PASSWORD_CHANGE", message: "Password change request is invalid." },
    });
  }

  const credential = await credentialForEmployee(env, principal.employeeId);
  if (!credential || !await verifyCredential(currentPassword, credential.algorithm, credential.verifier)) {
    return json(env, requestId, 401, {
      error: { code: "CURRENT_PASSWORD_INVALID", message: "Current password verification failed." },
    });
  }
  if (await verifyCredential(newPassword, credential.algorithm, credential.verifier)) {
    return json(env, requestId, 409, {
      error: { code: "PASSWORD_UNCHANGED", message: "New password must be different from the current password." },
    });
  }

  const verifier = await createCredentialVerifier(newPassword);
  const now = new Date().toISOString();
  const updated = await env.DB.prepare(
    `UPDATE employee_credentials
        SET algorithm = ?2,
            verifier = ?3,
            credential_version = credential_version + 1,
            updated_at = ?4
      WHERE employee_id = ?1
        AND credential_version = ?5
    RETURNING credential_version`
  ).bind(
    principal.employeeId,
    CURRENT_CREDENTIAL_ALGORITHM,
    verifier,
    now,
    credential.credential_version,
  ).first<{ credential_version: number }>();
  if (!updated) {
    return json(env, requestId, 409, {
      error: { code: "CREDENTIAL_VERSION_CONFLICT", message: "Credential changed during this request." },
    });
  }

  await env.DB.batch([
    env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?2)
        WHERE employee_id = ?1`
    ).bind(principal.employeeId, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?3, 'employee_password_changed', ?4, ?5)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      principal.workspaceId,
      principal.employeeId,
      JSON.stringify({ credentialVersion: updated.credential_version }),
      now,
    ),
  ]);

  return json(env, requestId, 200, {
    changed: true,
    credentialVersion: updated.credential_version,
    sessionRevoked: true,
  });
}

export async function handleStartOwnEmailChange(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireIdentitySession(request, env);
  if (!principal) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }
  const body = await readJsonObject(request);
  const currentPassword = normalizePassword(body?.currentPassword);
  let email: string | null = null;
  try {
    if (typeof body?.email === "string") email = normalizeAddress(body.email);
  } catch {
    email = null;
  }
  if (!currentPassword || !email) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMAIL_CHANGE", message: "Email change request is invalid." },
    });
  }

  const credential = await credentialForEmployee(env, principal.employeeId);
  if (!credential || !await verifyCredential(currentPassword, credential.algorithm, credential.verifier)) {
    return json(env, requestId, 401, {
      error: { code: "CURRENT_PASSWORD_INVALID", message: "Current password verification failed." },
    });
  }

  const current = await env.DB.prepare(
    `SELECT e.email_normalized, e.email_verified_at, w.super_admin_employee_id
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
      WHERE e.workspace_id = ?1
        AND e.employee_id = ?2
      LIMIT 1`
  ).bind(principal.workspaceId, principal.employeeId).first<EmployeeEmailRow>();
  if (!current) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (current.email_normalized === email && current.email_verified_at) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_ALREADY_VERIFIED", message: "This Email is already verified." },
    });
  }

  const conflict = await env.DB.prepare(
    `SELECT 1 AS present
       FROM employees
      WHERE workspace_id = ?1
        AND email_normalized = ?2
        AND employee_id <> ?3
      LIMIT 1`
  ).bind(principal.workspaceId, email, principal.employeeId).first<{ present: number }>();
  if (conflict) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_ALREADY_IN_USE", message: "Email is already used in this Workspace." },
    });
  }

  try {
    const otp = await issueEmailOtp(env, emailSenderFrom(env), {
      purpose: "employee_email_verification",
      scopeKey: `${principal.workspaceId}:${principal.employeeId}`,
      workspaceId: principal.workspaceId,
      email,
      subject: "CY Identity Email 驗證碼",
      textPrefix: "正在驗證您的 CY Identity Email。",
    });
    return json(env, requestId, 202, { verification: otp as unknown as JsonValue });
  } catch (error) {
    const code = error instanceof Error ? error.message : "EMAIL_VERIFICATION_FAILED";
    const status = code === "OTP_RESEND_COOLDOWN" || code === "OTP_RATE_LIMITED"
      || code === "EMAIL_DAILY_BUDGET_EXHAUSTED" || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
      ? 429
      : 503;
    return json(env, requestId, status, {
      error: { code, message: "Email verification code could not be issued." },
    });
  }
}

export async function handleConfirmOwnEmailChange(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireIdentitySession(request, env);
  if (!principal) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }
  const body = await readJsonObject(request);
  const challengeId = typeof body?.challengeId === "string" ? body.challengeId.trim() : "";
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  if (!challengeId || !/^\d{6}$/.test(code)) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMAIL_CONFIRMATION", message: "Email verification request is invalid." },
    });
  }

  const challenge = await verifyEmailOtp(env, {
    challengeId,
    purpose: "employee_email_verification",
    scopeKey: `${principal.workspaceId}:${principal.employeeId}`,
    code,
  });
  if (!challenge) {
    return json(env, requestId, 400, {
      error: { code: "EMAIL_VERIFICATION_FAILED", message: "Email verification failed." },
    });
  }

  const conflict = await env.DB.prepare(
    `SELECT 1 AS present
       FROM employees
      WHERE workspace_id = ?1
        AND email_normalized = ?2
        AND employee_id <> ?3
      LIMIT 1`
  ).bind(principal.workspaceId, challenge.email_normalized, principal.employeeId).first<{ present: number }>();
  if (conflict) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_ALREADY_IN_USE", message: "Email is already used in this Workspace." },
    });
  }

  const now = new Date().toISOString();
  if (!await consumeVerifiedOtp(env, challengeId, now)) {
    return json(env, requestId, 400, {
      error: { code: "EMAIL_VERIFICATION_FAILED", message: "Email verification failed." },
    });
  }

  const statements: D1PreparedStatement[] = [
    env.DB.prepare(
      `UPDATE employees
          SET email_normalized = ?3,
              email_verified_at = ?4,
              revision = revision + 1,
              updated_at = ?4
        WHERE workspace_id = ?1
          AND employee_id = ?2`
    ).bind(principal.workspaceId, principal.employeeId, challenge.email_normalized, now),
    env.DB.prepare(
      `UPDATE workspaces
          SET recovery_email_normalized = ?3,
              recovery_email_verified_at = ?4,
              revision = revision + 1,
              updated_at = ?4
        WHERE workspace_id = ?1
          AND super_admin_employee_id = ?2`
    ).bind(principal.workspaceId, principal.employeeId, challenge.email_normalized, now),
    env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?2)
        WHERE employee_id = ?1`
    ).bind(principal.employeeId, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?3, 'employee_email_verified', ?4, ?5)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      principal.workspaceId,
      principal.employeeId,
      JSON.stringify({ emailChanged: true }),
      now,
    ),
  ];
  try {
    await env.DB.batch(statements);
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_UPDATE_CONFLICT", message: "Verified Email could not be applied." },
    });
  }

  return json(env, requestId, 200, {
    verified: true,
    email: challenge.email_normalized,
    sessionRevoked: true,
  });
}

export async function handleStartPasswordRecovery(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  if (!workspaceId || !employeeNo) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_PASSWORD_RECOVERY", message: "Password recovery request is invalid." },
    });
  }

  const employee = await env.DB.prepare(
    `SELECT e.employee_id, e.workspace_id, e.employee_no, e.email_normalized,
            e.email_verified_at, e.enabled, w.status AS workspace_status
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRecoveryRow>();

  let result = fakeOtpIssue();
  if (employee && employee.enabled === 1 && employee.workspace_status === "active" && employee.email_verified_at) {
    try {
      result = await issueEmailOtp(env, emailSenderFrom(env), {
        purpose: "employee_password_reset",
        scopeKey: `${workspaceId}:${employee.employee_id}`,
        workspaceId,
        email: employee.email_normalized,
        subject: "CY Identity 密碼重設驗證碼",
        textPrefix: "正在重設您的 CY Identity 密碼。",
      });
    } catch {
      // Keep the public response non-enumerating. The caller receives the same
      // accepted shape even when no recoverable account exists or delivery fails.
      result = fakeOtpIssue();
    }
  }

  return json(env, requestId, 202, {
    recovery: result as unknown as JsonValue,
    message: "If a recoverable account exists, a verification code has been sent.",
  });
}

export async function handleConfirmPasswordRecovery(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const challengeId = typeof body?.challengeId === "string" ? body.challengeId.trim() : "";
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  const newPassword = normalizePassword(body?.newPassword);
  if (!workspaceId || !employeeNo || !challengeId || !/^\d{6}$/.test(code) || !newPassword) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_PASSWORD_RECOVERY", message: "Password recovery confirmation is invalid." },
    });
  }

  const employee = await env.DB.prepare(
    `SELECT e.employee_id, e.workspace_id, e.employee_no, e.email_normalized,
            e.email_verified_at, e.enabled, w.status AS workspace_status
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRecoveryRow>();
  if (!employee || employee.enabled !== 1 || employee.workspace_status !== "active" || !employee.email_verified_at) {
    return json(env, requestId, 400, {
      error: { code: "PASSWORD_RECOVERY_FAILED", message: "Password recovery verification failed." },
    });
  }

  const challenge = await verifyEmailOtp(env, {
    challengeId,
    purpose: "employee_password_reset",
    scopeKey: `${workspaceId}:${employee.employee_id}`,
    code,
  });
  if (!challenge || challenge.email_normalized !== employee.email_normalized) {
    return json(env, requestId, 400, {
      error: { code: "PASSWORD_RECOVERY_FAILED", message: "Password recovery verification failed." },
    });
  }

  const verifier = await createCredentialVerifier(newPassword);
  const now = new Date().toISOString();
  if (!await consumeVerifiedOtp(env, challengeId, now)) {
    return json(env, requestId, 400, {
      error: { code: "PASSWORD_RECOVERY_FAILED", message: "Password recovery verification failed." },
    });
  }

  await env.DB.batch([
    env.DB.prepare(
      `UPDATE employee_credentials
          SET algorithm = ?2,
              verifier = ?3,
              credential_version = credential_version + 1,
              updated_at = ?4
        WHERE employee_id = ?1`
    ).bind(employee.employee_id, CURRENT_CREDENTIAL_ALGORITHM, verifier, now),
    env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?2)
        WHERE employee_id = ?1`
    ).bind(employee.employee_id, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, NULL, ?3, 'employee_password_recovered', NULL, ?4)`
    ).bind(`audit_${crypto.randomUUID()}`, workspaceId, employee.employee_id, now),
  ]);

  return json(env, requestId, 200, { reset: true, sessionsRevoked: true });
}
