import { createCredentialVerifier, CURRENT_CREDENTIAL_ALGORITHM, normalizePassword } from "./crypto";
import { createEmailSender, normalizeAddress } from "./email";
import { json, readJsonObject } from "./http";
import { issueEmailOtp, verifyEmailOtp, consumeVerifiedOtp } from "./otp";
import { requireWorkspaceSuperAdmin } from "./admin-auth";
import type { Env, JsonValue } from "./types";

type PendingEmployeeRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  email_verified_at: string | null;
  enabled: number;
  revision: number;
  workspace_status: string;
  credential_present: number;
};

const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_FALLBACK_MS = 60 * 1000;

function normalizeWorkspaceId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 5 && normalized.length <= 80 ? normalized : null;
}

function normalizeEmployeeId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 5 && normalized.length <= 80 ? normalized : null;
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizeDisplayName(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 1 && normalized.length <= 120 ? normalized : null;
}

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
}

function activationScope(workspaceId: string, employeeId: string): string {
  return `activate:${workspaceId}:${employeeId}`;
}

function fakeOtpIssue() {
  const now = Date.now();
  return {
    challengeId: `otp_${crypto.randomUUID()}`,
    expiresAt: new Date(now + OTP_TTL_MS).toISOString(),
    resendAfter: new Date(now + OTP_RESEND_FALLBACK_MS).toISOString(),
  };
}

async function pendingEmployee(
  env: Env,
  workspaceId: string,
  employeeNo: string,
): Promise<PendingEmployeeRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id,
            e.workspace_id,
            e.employee_no,
            e.name,
            e.email_normalized,
            e.email_verified_at,
            e.enabled,
            e.revision,
            w.status AS workspace_status,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<PendingEmployeeRow>();
}

export async function handleCreateEmployee(
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
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const displayName = normalizeDisplayName(body?.displayName);
  let email: string | null = null;
  try {
    if (typeof body?.email === "string") email = normalizeAddress(body.email);
  } catch {
    email = null;
  }
  if (!employeeNo || !displayName || !email) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMPLOYEE", message: "Employee input is invalid." },
    });
  }

  const employeeId = `emp_${crypto.randomUUID()}`;
  const now = new Date().toISOString();
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employees(
           employee_id, workspace_id, employee_no, name, email_normalized,
           email_verified_at, enabled, revision, created_at, updated_at
         ) VALUES(?1, ?2, ?3, ?4, ?5, NULL, 0, 1, ?6, ?6)`
      ).bind(employeeId, actor.workspaceId, employeeNo, displayName, email, now),
      env.DB.prepare(
        `INSERT INTO identity_audit_events(
           event_id, workspace_id, actor_employee_id, target_employee_id,
           event_type, detail_json, created_at
         ) VALUES(?1, ?2, ?3, ?4, 'employee_created_pending_activation', ?5, ?6)`
      ).bind(
        `audit_${crypto.randomUUID()}`,
        actor.workspaceId,
        actor.employeeId,
        employeeId,
        JSON.stringify({ employeeNo }),
        now,
      ),
    ]);
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_CONFLICT", message: "Employee No or Email already exists in this Workspace." },
    });
  }

  return json(env, requestId, 201, {
    employee: {
      employeeId,
      employeeNo,
      displayName,
      email,
      emailVerified: false,
      enabled: false,
      pendingActivation: true,
      revision: 1,
    },
  });
}

export async function handleUpdateEmployee(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const body = await readJsonObject(request);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const displayName = normalizeDisplayName(body?.displayName);
  const enabled = typeof body?.enabled === "boolean" ? body.enabled : null;
  const revision = typeof body?.revision === "number" && Number.isInteger(body.revision) && body.revision >= 1
    ? body.revision
    : null;
  if (!employeeId || !employeeNo || !displayName || enabled === null || !revision) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMPLOYEE", message: "Employee update is invalid." },
    });
  }

  if (enabled) {
    const ready = await env.DB.prepare(
      `SELECT e.email_verified_at,
              CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
         FROM employees e
         LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
        WHERE e.workspace_id = ?1
          AND e.employee_id = ?2
        LIMIT 1`
    ).bind(actor.workspaceId, employeeId).first<{ email_verified_at: string | null; credential_present: number }>();
    if (!ready || !ready.email_verified_at || ready.credential_present !== 1) {
      return json(env, requestId, 409, {
        error: { code: "EMPLOYEE_NOT_ACTIVATED", message: "Employee must complete activation before being enabled." },
      });
    }
  }

  const now = new Date().toISOString();
  try {
    const row = await env.DB.prepare(
      `UPDATE employees
          SET employee_no = ?3,
              name = ?4,
              enabled = ?5,
              revision = revision + 1,
              updated_at = ?6
        WHERE workspace_id = ?1
          AND employee_id = ?2
          AND revision = ?7
      RETURNING employee_id, employee_no, name, email_normalized,
                email_verified_at, enabled, revision`
    ).bind(
      actor.workspaceId,
      employeeId,
      employeeNo,
      displayName,
      enabled ? 1 : 0,
      now,
      revision,
    ).first<{
      employee_id: string;
      employee_no: string;
      name: string;
      email_normalized: string;
      email_verified_at: string | null;
      enabled: number;
      revision: number;
    }>();
    if (!row) {
      return json(env, requestId, 409, {
        error: { code: "EMPLOYEE_REVISION_CONFLICT", message: "Employee revision no longer matches." },
      });
    }

    if (!enabled) {
      await env.DB.prepare(
        `UPDATE identity_sessions
            SET revoked_at = COALESCE(revoked_at, ?2)
          WHERE employee_id = ?1`
      ).bind(employeeId, now).run();
    }
    await env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?4, 'employee_updated', ?5, ?6)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      actor.workspaceId,
      actor.employeeId,
      employeeId,
      JSON.stringify({ employeeNo: row.employee_no, enabled: row.enabled === 1, revision: row.revision }),
      now,
    ).run();

    return json(env, requestId, 200, {
      employee: {
        employeeId: row.employee_id,
        employeeNo: row.employee_no,
        displayName: row.name,
        email: row.email_normalized,
        emailVerified: Boolean(row.email_verified_at),
        enabled: row.enabled === 1,
        revision: row.revision,
      },
    });
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_CONFLICT", message: "Employee could not be updated." },
    });
  }
}

export async function handleStartEmployeeActivation(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  if (!workspaceId || !employeeNo) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMPLOYEE_ACTIVATION", message: "Employee activation request is invalid." },
    });
  }

  const employee = await pendingEmployee(env, workspaceId, employeeNo);
  let otp = fakeOtpIssue();
  if (employee && employee.workspace_status === "active" && employee.enabled === 0
      && !employee.email_verified_at && employee.credential_present === 0) {
    try {
      otp = await issueEmailOtp(env, emailSenderFrom(env), {
        purpose: "employee_email_verification",
        scopeKey: activationScope(workspaceId, employee.employee_id),
        workspaceId,
        email: employee.email_normalized,
        subject: "CY Identity 帳號啟用驗證碼",
        textPrefix: "正在啟用您的 CY Identity 帳號並設定登入密碼。",
      });
    } catch {
      otp = fakeOtpIssue();
    }
  }

  return json(env, requestId, 202, {
    activation: otp as unknown as JsonValue,
    message: "If an account is pending activation, a verification code has been sent.",
  });
}

export async function handleConfirmEmployeeActivation(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const challengeId = typeof body?.challengeId === "string" ? body.challengeId.trim() : "";
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  const password = normalizePassword(body?.password);
  if (!workspaceId || !employeeNo || !challengeId || !/^\d{6}$/.test(code) || !password) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMPLOYEE_ACTIVATION", message: "Employee activation confirmation is invalid." },
    });
  }

  const employee = await pendingEmployee(env, workspaceId, employeeNo);
  if (!employee || employee.workspace_status !== "active" || employee.enabled !== 0
      || employee.email_verified_at || employee.credential_present !== 0) {
    return json(env, requestId, 400, {
      error: { code: "EMPLOYEE_ACTIVATION_FAILED", message: "Employee activation verification failed." },
    });
  }

  const challenge = await verifyEmailOtp(env, {
    challengeId,
    purpose: "employee_email_verification",
    scopeKey: activationScope(workspaceId, employee.employee_id),
    code,
  });
  if (!challenge || challenge.email_normalized !== employee.email_normalized) {
    return json(env, requestId, 400, {
      error: { code: "EMPLOYEE_ACTIVATION_FAILED", message: "Employee activation verification failed." },
    });
  }

  const verifier = await createCredentialVerifier(password);
  const now = new Date().toISOString();
  if (!await consumeVerifiedOtp(env, challengeId, now)) {
    return json(env, requestId, 400, {
      error: { code: "EMPLOYEE_ACTIVATION_FAILED", message: "Employee activation verification failed." },
    });
  }

  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employee_credentials(
           employee_id, algorithm, verifier, credential_version, updated_at
         ) VALUES(?1, ?2, ?3, 1, ?4)`
      ).bind(employee.employee_id, CURRENT_CREDENTIAL_ALGORITHM, verifier, now),
      env.DB.prepare(
        `UPDATE employees
            SET email_verified_at = ?3,
                enabled = 1,
                revision = revision + 1,
                updated_at = ?3
          WHERE workspace_id = ?1
            AND employee_id = ?2
            AND enabled = 0
            AND email_verified_at IS NULL`
      ).bind(workspaceId, employee.employee_id, now),
      env.DB.prepare(
        `INSERT INTO identity_audit_events(
           event_id, workspace_id, actor_employee_id, target_employee_id,
           event_type, detail_json, created_at
         ) VALUES(?1, ?2, NULL, ?3, 'employee_activation_completed', ?4, ?5)`
      ).bind(
        `audit_${crypto.randomUUID()}`,
        workspaceId,
        employee.employee_id,
        JSON.stringify({ employeeNo }),
        now,
      ),
    ]);
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_ACTIVATION_CONFLICT", message: "Employee activation could not be completed." },
    });
  }

  return json(env, requestId, 200, {
    activated: true,
    employee: {
      employeeId: employee.employee_id,
      employeeNo: employee.employee_no,
      displayName: employee.name,
      email: employee.email_normalized,
    },
  });
}
