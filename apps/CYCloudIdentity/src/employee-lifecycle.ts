import { createEmailSender, normalizeAddress } from "./email";
import { json, readJsonObject } from "./http";
import { issueEmailOtp, type OtpIssueResult } from "./otp";
import { requireIdentityAdministrator, requireWorkspaceAdmin, requireWorkspaceSuperAdmin } from "./admin-auth";
import {
  canManageEmployeeLifecycle,
  effectiveWorkspaceRole,
  normalizeStoredEmployeeRole,
  type StoredEmployeeRole,
} from "./role-access";
import type { Env, IdentityPrincipal, JsonValue } from "./types";

type ManagedEmployeeRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  email_verified_at: string | null;
  enabled: number;
  role_key: StoredEmployeeRole;
  identity_admin: number;
  activated_at: string | null;
  revision: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  credential_present: number;
};

const OTP_TTL_MS = 10 * 60 * 1000;
const OTP_RESEND_FALLBACK_MS = 60 * 1000;

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

function emailVerificationScope(workspaceId: string, employeeId: string): string {
  return `${workspaceId}:${employeeId}`;
}

function fakeOtpIssue(): OtpIssueResult {
  const now = Date.now();
  return {
    challengeId: `otp_${crypto.randomUUID()}`,
    expiresAt: new Date(now + OTP_TTL_MS).toISOString(),
    resendAfter: new Date(now + OTP_RESEND_FALLBACK_MS).toISOString(),
  };
}

function portalUrl(env: Env, params: Record<string, string>): string {
  const configured = env.ACCOUNT_PORTAL_URL?.trim() ?? "";
  if (!configured) throw new Error("ACCOUNT_PORTAL_URL_NOT_CONFIGURED");
  let url: URL;
  try {
    url = new URL(configured);
  } catch {
    throw new Error("ACCOUNT_PORTAL_URL_INVALID");
  }
  if (url.protocol !== "https:" && url.protocol !== "http:") throw new Error("ACCOUNT_PORTAL_URL_INVALID");
  for (const [key, value] of Object.entries(params)) url.searchParams.set(key, value);
  return url.toString();
}

function publicDeliveryError(error: unknown): string {
  const code = error instanceof Error ? error.message : "ACTIVATION_EMAIL_FAILED";
  if (code === "OTP_RESEND_COOLDOWN"
      || code === "OTP_RATE_LIMITED"
      || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
      || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
      || code === "ACCOUNT_PORTAL_URL_NOT_CONFIGURED"
      || code === "ACCOUNT_PORTAL_URL_INVALID") return code;
  return "ACTIVATION_EMAIL_FAILED";
}

function deliveryStatus(code: string): number {
  return code === "OTP_RESEND_COOLDOWN"
    || code === "OTP_RATE_LIMITED"
    || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
    || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
    ? 429
    : 503;
}

async function audit(
  env: Env,
  input: {
    workspaceId: string;
    actorEmployeeId?: string | null;
    targetEmployeeId?: string | null;
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
    input.targetEmployeeId ?? null,
    input.eventType,
    input.detail ? JSON.stringify(input.detail) : null,
    new Date().toISOString(),
  ).run();
}

async function managedEmployee(
  env: Env,
  workspaceId: string,
  employeeId: string,
): Promise<ManagedEmployeeRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id,
            e.workspace_id,
            e.employee_no,
            e.name,
            e.email_normalized,
            e.email_verified_at,
            e.enabled,
            e.role_key,
            e.identity_admin,
            e.activated_at,
            e.revision,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_id = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeId).first<ManagedEmployeeRow>();
}

async function pendingEmployee(
  env: Env,
  workspaceId: string,
  employeeNo: string,
): Promise<ManagedEmployeeRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id,
            e.workspace_id,
            e.employee_no,
            e.name,
            e.email_normalized,
            e.email_verified_at,
            e.enabled,
            e.role_key,
            e.identity_admin,
            e.activated_at,
            e.revision,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<ManagedEmployeeRow>();
}

function targetRole(row: ManagedEmployeeRow) {
  return effectiveWorkspaceRole(row.employee_id, row.super_admin_employee_id, row.role_key);
}

function actorCanManage(actor: IdentityPrincipal, row: ManagedEmployeeRow): boolean {
  const role = targetRole(row);
  if (role === "SUPER_ADMIN") return actor.workspaceRole === "SUPER_ADMIN" && actor.employeeId === row.employee_id;
  return canManageEmployeeLifecycle(actor, role, row.identity_admin === 1);
}

async function sendActivationEmail(env: Env, employee: ManagedEmployeeRow): Promise<OtpIssueResult> {
  const link = portalUrl(env, { activate: "1", employeeNo: employee.employee_no });
  return issueEmailOtp(env, emailSenderFrom(env), {
    purpose: "employee_email_verification",
    scopeKey: activationScope(employee.workspace_id, employee.employee_id),
    workspaceId: employee.workspace_id,
    email: employee.email_normalized,
    subject: "CY Identity 帳號啟用",
    textPrefix: "您的 CY Identity 帳號已建立。請使用下方驗證碼完成 Email 驗證並設定第一次登入密碼。",
    textSuffix: `開啟 CY Web 完成帳號啟用：${link}`,
  });
}

async function sendActivatedEmailVerification(env: Env, employee: ManagedEmployeeRow): Promise<OtpIssueResult> {
  const link = portalUrl(env, { verifyEmail: "1" });
  return issueEmailOtp(env, emailSenderFrom(env), {
    purpose: "employee_email_verification",
    scopeKey: emailVerificationScope(employee.workspace_id, employee.employee_id),
    workspaceId: employee.workspace_id,
    email: employee.email_normalized,
    subject: "CY Identity Email 重新驗證",
    textPrefix: "您的 CY Identity Email 已由身分管理員更新，請完成新 Email 驗證。原有密碼不變。",
    textSuffix: `開啟 CY Web 完成 Email 驗證：${link}`,
  });
}

export async function handleDeletePendingEmployee(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_ADMIN_REQUIRED", message: "Workspace administrator authority is required." },
    });
  }

  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const employee = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!employee) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (targetRole(employee) === "SUPER_ADMIN" || !actorCanManage(actor, employee)) {
    return json(env, requestId, 403, {
      error: { code: "EMPLOYEE_MANAGEMENT_NOT_ALLOWED", message: "This Employee cannot be deleted by the current administrator." },
    });
  }
  if (employee.activated_at || employee.credential_present !== 0) {
    return json(env, requestId, 409, {
      error: {
        code: "EMPLOYEE_DELETE_NOT_ALLOWED",
        message: "Only an Employee that has never completed first activation can be deleted.",
      },
    });
  }

  const deleted = await env.DB.prepare(
    `DELETE FROM employees
      WHERE workspace_id = ?1
        AND employee_id = ?2
        AND activated_at IS NULL
        AND NOT EXISTS (
          SELECT 1 FROM employee_credentials c WHERE c.employee_id = employees.employee_id
        )
    RETURNING employee_id, employee_no`
  ).bind(actor.workspaceId, employee.employee_id).first<{ employee_id: string; employee_no: string }>();
  if (!deleted) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_DELETE_NOT_ALLOWED", message: "Employee activation state changed and the account was not deleted." },
    });
  }

  const now = new Date().toISOString();
  await env.DB.batch([
    env.DB.prepare(
      `DELETE FROM email_otp_challenges
        WHERE purpose = 'employee_email_verification'
          AND scope_key = ?1`
    ).bind(activationScope(actor.workspaceId, employee.employee_id)),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, NULL, 'employee_pending_activation_deleted', ?4, ?5)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      actor.workspaceId,
      actor.employeeId,
      JSON.stringify({ employeeId: deleted.employee_id, employeeNo: deleted.employee_no }),
      now,
    ),
  ]);

  return json(env, requestId, 200, {
    deleted: true,
    employeeId: deleted.employee_id,
    employeeNo: deleted.employee_no,
  });
}

export async function handleUpdateEmployee(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_ADMIN_REQUIRED", message: "Workspace administrator authority is required." },
    });
  }
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const existing = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!existing) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }

  const body = await readJsonObject(request);
  const employeeNo = body?.employeeNo === undefined
    ? existing.employee_no
    : normalizeEmployeeNo(body.employeeNo);
  const displayName = body?.displayName === undefined
    ? existing.name
    : normalizeDisplayName(body.displayName);
  const enabled = body?.enabled === undefined
    ? existing.enabled === 1
    : typeof body.enabled === "boolean" ? body.enabled : null;
  const roleKey = body?.roleKey === undefined
    ? existing.role_key
    : normalizeStoredEmployeeRole(body.roleKey);
  const revision = typeof body?.revision === "number" && Number.isInteger(body.revision) && body.revision >= 1
    ? body.revision
    : null;
  let email = existing.email_normalized;
  if (body?.email !== undefined) {
    try {
      if (typeof body.email !== "string") throw new Error("invalid");
      email = normalizeAddress(body.email);
    } catch {
      return json(env, requestId, 400, {
        error: { code: "INVALID_EMPLOYEE", message: "Employee Email is invalid." },
      });
    }
  }
  if (!employeeNo || !displayName || enabled === null || !roleKey || !revision) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMPLOYEE", message: "Employee update is invalid." },
    });
  }

  const existingEffectiveRole = targetRole(existing);
  const isSuperSelf = existingEffectiveRole === "SUPER_ADMIN" && actor.employeeId === existing.employee_id
    && actor.workspaceRole === "SUPER_ADMIN";
  if (!isSuperSelf && !actorCanManage(actor, existing)) {
    return json(env, requestId, 403, {
      error: { code: "EMPLOYEE_MANAGEMENT_NOT_ALLOWED", message: "This Employee cannot be managed by the current administrator." },
    });
  }
  if (existingEffectiveRole === "SUPER_ADMIN") {
    if (!isSuperSelf || !enabled || roleKey !== existing.role_key || email !== existing.email_normalized) {
      return json(env, requestId, 409, {
        error: { code: "SUPER_ADMIN_PROTECTED", message: "Transfer Super Admin authority before changing protected account state." },
      });
    }
  }

  const roleChanged = roleKey !== existing.role_key;
  if (roleChanged && actor.workspaceRole !== "SUPER_ADMIN" && !actor.isIdentityAdmin) {
    return json(env, requestId, 403, {
      error: { code: "EMPLOYEE_ROLE_CHANGE_NOT_ALLOWED", message: "Identity administration authority is required to change Employee role." },
    });
  }
  if (existing.identity_admin === 1 && roleKey !== "ADMIN") {
    return json(env, requestId, 409, {
      error: { code: "IDENTITY_ADMIN_REVOKE_REQUIRED", message: "Remove Identity Admin capability before changing this Employee to USER." },
    });
  }

  const pendingFirstActivation = existing.activated_at === null && existing.credential_present === 0;
  if (pendingFirstActivation && enabled) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_NOT_ACTIVATED", message: "Employee must complete first activation before being enabled." },
    });
  }
  if (!pendingFirstActivation && enabled && existing.credential_present !== 1) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_CREDENTIAL_MISSING", message: "Activated Employee credential is missing." },
    });
  }

  const emailChanged = email !== existing.email_normalized;
  if (emailChanged && !pendingFirstActivation) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_CHANGE_REQUIRES_RECOVERY", message: "Activated Employee Email changes require the controlled Email recovery flow." },
    });
  }

  const now = new Date().toISOString();
  let row: {
    employee_id: string;
    employee_no: string;
    name: string;
    email_normalized: string;
    email_verified_at: string | null;
    enabled: number;
    role_key: StoredEmployeeRole;
    identity_admin: number;
    activated_at: string | null;
    revision: number;
  } | null = null;
  try {
    row = await env.DB.prepare(
      `UPDATE employees
          SET employee_no = ?3,
              name = ?4,
              email_normalized = ?5,
              email_verified_at = CASE WHEN ?9 = 1 THEN NULL ELSE email_verified_at END,
              enabled = ?6,
              role_key = ?7,
              revision = revision + 1,
              updated_at = ?8
        WHERE workspace_id = ?1
          AND employee_id = ?2
          AND revision = ?10
      RETURNING employee_id, employee_no, name, email_normalized,
                email_verified_at, enabled, role_key, identity_admin, activated_at, revision`
    ).bind(
      actor.workspaceId,
      existing.employee_id,
      employeeNo,
      displayName,
      email,
      enabled ? 1 : 0,
      roleKey,
      now,
      emailChanged ? 1 : 0,
      revision,
    ).first<typeof row extends infer _T ? any : never>();
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_CONFLICT", message: "Employee could not be updated." },
    });
  }
  if (!row) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_REVISION_CONFLICT", message: "Employee revision no longer matches." },
    });
  }

  const stateChanged = roleChanged || enabled !== (existing.enabled === 1);
  const statements: D1PreparedStatement[] = [];
  if (stateChanged) {
    statements.push(
      env.DB.prepare(
        `UPDATE identity_sessions
            SET revoked_at = COALESCE(revoked_at, ?2)
          WHERE employee_id = ?1`
      ).bind(existing.employee_id, now),
    );
  }
  if (emailChanged) {
    statements.push(
      env.DB.prepare(
        `DELETE FROM email_otp_challenges
          WHERE purpose = 'employee_email_verification'
            AND scope_key = ?1`
      ).bind(activationScope(actor.workspaceId, existing.employee_id)),
    );
  }
  statements.push(
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?4, 'employee_updated', ?5, ?6)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      actor.workspaceId,
      actor.employeeId,
      existing.employee_id,
      JSON.stringify({
        employeeNo: row.employee_no,
        enabled: row.enabled === 1,
        roleKey: row.role_key,
        emailChanged,
        revision: row.revision,
      }),
      now,
    ),
  );
  await env.DB.batch(statements);

  let activationDelivery: JsonValue | undefined;
  if (emailChanged && pendingFirstActivation) {
    const refreshed = await managedEmployee(env, actor.workspaceId, existing.employee_id);
    if (refreshed) {
      try {
        const issued = await sendActivationEmail(env, refreshed);
        activationDelivery = { sent: true, ...issued };
      } catch (error) {
        activationDelivery = { sent: false, errorCode: publicDeliveryError(error) };
      }
    }
  }

  return json(env, requestId, 200, {
    employee: {
      employeeId: row.employee_id,
      employeeNo: row.employee_no,
      displayName: row.name,
      email: row.email_normalized,
      emailVerified: Boolean(row.email_verified_at),
      enabled: row.enabled === 1,
      roleKey: row.role_key,
      isIdentityAdmin: row.identity_admin === 1,
      activated: Boolean(row.activated_at),
      revision: row.revision,
    },
    ...(activationDelivery ? { activationDelivery } : {}),
  });
}

export async function handleSetEmployeeIdentityAdmin(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Super Admin authority is required." },
    });
  }
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const target = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!target) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (targetRole(target) === "SUPER_ADMIN") {
    return json(env, requestId, 409, {
      error: { code: "SUPER_ADMIN_PROTECTED", message: "Identity Admin capability is not applied to the protected Super Admin account." },
    });
  }
  const body = await readJsonObject(request);
  const enabled = typeof body?.enabled === "boolean" ? body.enabled : null;
  if (enabled === null) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_IDENTITY_ADMIN_UPDATE", message: "Identity Admin update is invalid." },
    });
  }
  if (enabled && target.role_key !== "ADMIN") {
    return json(env, requestId, 409, {
      error: { code: "IDENTITY_ADMIN_REQUIRES_ADMIN", message: "Identity Admin capability requires ADMIN role." },
    });
  }

  const now = new Date().toISOString();
  const updated = await env.DB.prepare(
    `UPDATE employees
        SET identity_admin = ?3,
            revision = revision + 1,
            updated_at = ?4
      WHERE workspace_id = ?1
        AND employee_id = ?2
    RETURNING employee_id, role_key, identity_admin, revision`
  ).bind(actor.workspaceId, target.employee_id, enabled ? 1 : 0, now).first<{
    employee_id: string;
    role_key: StoredEmployeeRole;
    identity_admin: number;
    revision: number;
  }>();
  if (!updated) throw new Error("IDENTITY_ADMIN_UPDATE_FAILED");

  await env.DB.batch([
    env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?2)
        WHERE employee_id = ?1`
    ).bind(target.employee_id, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?4, 'employee_identity_admin_updated', ?5, ?6)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      actor.workspaceId,
      actor.employeeId,
      target.employee_id,
      JSON.stringify({ enabled, revision: updated.revision }),
      now,
    ),
  ]);

  return json(env, requestId, 200, {
    employee: {
      employeeId: updated.employee_id,
      roleKey: updated.role_key,
      isIdentityAdmin: updated.identity_admin === 1,
      revision: updated.revision,
    },
    sessionsRevoked: true,
  });
}

export async function handleForceEmployeeEmailRecovery(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireIdentityAdministrator(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "IDENTITY_ADMIN_REQUIRED", message: "Identity administration authority is required." },
    });
  }
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const target = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!target) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (targetRole(target) === "SUPER_ADMIN" || target.employee_id === actor.employeeId) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_RECOVERY_NOT_ALLOWED", message: "This account must use its protected or self-service Email flow." },
    });
  }
  if (!target.activated_at || target.credential_present !== 1) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_RECOVERY_NOT_ALLOWED", message: "Forced Email recovery is only available after first activation." },
    });
  }

  const body = await readJsonObject(request);
  let email: string | null = null;
  try {
    if (typeof body?.email === "string") email = normalizeAddress(body.email);
  } catch {
    email = null;
  }
  if (!email) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_EMAIL_RECOVERY", message: "Replacement Email is invalid." },
    });
  }
  if (email === target.email_normalized && target.email_verified_at) {
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
  ).bind(actor.workspaceId, email, target.employee_id).first<{ present: number }>();
  if (conflict) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_ALREADY_IN_USE", message: "Email is already used in this Workspace." },
    });
  }

  const now = new Date().toISOString();
  try {
    await env.DB.batch([
      env.DB.prepare(
        `UPDATE employees
            SET email_normalized = ?3,
                email_verified_at = NULL,
                revision = revision + 1,
                updated_at = ?4
          WHERE workspace_id = ?1
            AND employee_id = ?2`
      ).bind(actor.workspaceId, target.employee_id, email, now),
      env.DB.prepare(
        `UPDATE identity_sessions
            SET revoked_at = COALESCE(revoked_at, ?2)
          WHERE employee_id = ?1`
      ).bind(target.employee_id, now),
      env.DB.prepare(
        `DELETE FROM email_otp_challenges
          WHERE purpose = 'employee_email_verification'
            AND scope_key IN (?1, ?2)`
      ).bind(
        activationScope(actor.workspaceId, target.employee_id),
        emailVerificationScope(actor.workspaceId, target.employee_id),
      ),
      env.DB.prepare(
        `INSERT INTO identity_audit_events(
           event_id, workspace_id, actor_employee_id, target_employee_id,
           event_type, detail_json, created_at
         ) VALUES(?1, ?2, ?3, ?4, 'employee_email_recovery_forced', ?5, ?6)`
      ).bind(
        `audit_${crypto.randomUUID()}`,
        actor.workspaceId,
        actor.employeeId,
        target.employee_id,
        JSON.stringify({ emailChanged: email !== target.email_normalized }),
        now,
      ),
    ]);
  } catch {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_UPDATE_CONFLICT", message: "Replacement Email could not be applied." },
    });
  }

  const refreshed = await managedEmployee(env, actor.workspaceId, target.employee_id);
  if (!refreshed) throw new Error("EMAIL_RECOVERY_READBACK_FAILED");

  let verificationDelivery: JsonValue;
  try {
    const issued = await sendActivatedEmailVerification(env, refreshed);
    verificationDelivery = { sent: true, ...issued };
  } catch (error) {
    verificationDelivery = { sent: false, errorCode: publicDeliveryError(error) };
  }

  return json(env, requestId, 200, {
    recovered: true,
    employee: {
      employeeId: refreshed.employee_id,
      email: refreshed.email_normalized,
      emailVerified: false,
      enabled: refreshed.enabled === 1,
      activated: true,
      revision: refreshed.revision,
    },
    verificationDelivery,
    sessionsRevoked: true,
  });
}

export async function handleResendActivatedEmailVerification(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireIdentityAdministrator(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "IDENTITY_ADMIN_REQUIRED", message: "Identity administration authority is required." },
    });
  }
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const target = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!target) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (targetRole(target) === "SUPER_ADMIN" || target.employee_id === actor.employeeId
      || !target.activated_at || target.credential_present !== 1 || target.email_verified_at) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_VERIFICATION_RESEND_NOT_ALLOWED", message: "Email verification cannot be resent for this Employee." },
    });
  }
  try {
    const issued = await sendActivatedEmailVerification(env, target);
    return json(env, requestId, 202, { verificationDelivery: { sent: true, ...issued } as unknown as JsonValue });
  } catch (error) {
    const code = publicDeliveryError(error);
    return json(env, requestId, deliveryStatus(code), {
      error: { code, message: "Email verification could not be sent." },
    });
  }
}
