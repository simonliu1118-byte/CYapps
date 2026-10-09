import {
  createCredentialVerifier,
  CURRENT_CREDENTIAL_ALGORITHM,
  normalizePassword,
  sha256Hex,
  verifyCredential,
} from "./crypto";
import { createEmailSender, normalizeAddress } from "./email";
import { json, readJsonObject } from "./http";
import { reserveEmailBudget, settleEmailBudget, type EmailBudgetReservation } from "./email-budget";
import { requireWorkspaceAdmin } from "./admin-auth";
import {
  canCreateRole,
  canManageEmployeeLifecycle,
  effectiveWorkspaceRole,
  normalizeStoredEmployeeRole,
  type StoredEmployeeRole,
} from "./role-access";
import { loadWorkspaceSecurityPolicy } from "./security-policy";
import type { Env, IdentityPrincipal, JsonValue } from "./types";

const INITIAL_PASSWORD_LENGTH = 8;
const INITIAL_PASSWORD_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
const FIRST_LOGIN_TICKET_TTL_MS = 10 * 60 * 1000;
const INITIAL_PASSWORD_TTL_MS = 24 * 60 * 60 * 1000;

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
  initial_credential_present: number;
  initial_sent_at: string | null;
};

type InitialCredentialRow = {
  employee_id: string;
  algorithm: string;
  verifier: string;
  exchange_token_digest: string | null;
  exchange_expires_at: string | null;
  issued_at: string;
  expires_at: string;
  sent_at: string | null;
  verified_at: string | null;
  revision: number;
};

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

function normalizedApplicationId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  return /^[A-Z0-9][A-Z0-9_-]{1,63}$/.test(normalized) ? normalized : null;
}

function coreApplicationId(env: Env): string | null {
  return normalizedApplicationId(env.CORE_ACCOUNT_APPLICATION_ID);
}

function randomString(length: number, alphabet: string): string {
  const output: string[] = [];
  const values = new Uint32Array(length);
  crypto.getRandomValues(values);
  for (let index = 0; index < length; index += 1) output.push(alphabet[values[index] % alphabet.length]);
  return output.join("");
}

function createInitialPassword(): string {
  return randomString(INITIAL_PASSWORD_LENGTH, INITIAL_PASSWORD_ALPHABET);
}

function createFirstLoginToken(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return `cyif_${Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("")}`;
}

function validFirstLoginToken(value: unknown): value is string {
  return typeof value === "string" && /^cyif_[0-9a-f]{64}$/.test(value);
}

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
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

async function managedEmployee(env: Env, workspaceId: string, employeeId: string): Promise<ManagedEmployeeRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id, e.workspace_id, e.employee_no, e.name, e.email_normalized,
            e.email_verified_at, e.enabled, e.role_key, e.identity_admin, e.activated_at,
            e.revision, w.status AS workspace_status, w.super_admin_employee_id,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present,
            i.sent_at AS initial_sent_at
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
       LEFT JOIN employee_initial_credentials i ON i.employee_id = e.employee_id
      WHERE e.workspace_id = ?1 AND e.employee_id = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeId).first<ManagedEmployeeRow>();
}

function targetRole(row: ManagedEmployeeRow) {
  return effectiveWorkspaceRole(row.employee_id, row.super_admin_employee_id, row.role_key);
}

function actorCanManage(actor: IdentityPrincipal, row: ManagedEmployeeRow): boolean {
  const role = targetRole(row);
  if (role === "SUPER_ADMIN") return actor.workspaceRole === "SUPER_ADMIN" && actor.employeeId === row.employee_id;
  return canManageEmployeeLifecycle(actor, role, row.identity_admin === 1);
}

export async function issueInitialCredential(
  env: Env,
  employee: Pick<ManagedEmployeeRow, "workspace_id" | "employee_id" | "employee_no" | "email_normalized" | "initial_sent_at">,
  options: { bypassCooldown?: boolean } = {},
): Promise<{ expiresAt: string }> {
  const policy = await loadWorkspaceSecurityPolicy(env, employee.workspace_id);
  if (!options.bypassCooldown && employee.initial_sent_at) {
    const nextAllowed = new Date(employee.initial_sent_at).getTime() + policy.otpResendCooldownSeconds * 1000;
    if (Number.isFinite(nextAllowed) && nextAllowed > Date.now()) throw new Error("OTP_RESEND_COOLDOWN");
  }

  const password = createInitialPassword();
  const verifier = await createCredentialVerifier(password);
  const now = new Date();
  const expiresAt = new Date(now.getTime() + INITIAL_PASSWORD_TTL_MS).toISOString();
  const stored = await env.DB.prepare(
    `INSERT INTO employee_initial_credentials(
       employee_id, algorithm, verifier, exchange_token_digest, exchange_expires_at,
       issued_at, expires_at, sent_at, verified_at, revision
     ) VALUES(?1, ?2, ?3, NULL, NULL, ?4, ?5, NULL, NULL, 1)
     ON CONFLICT(employee_id) DO UPDATE SET
       algorithm = excluded.algorithm,
       verifier = excluded.verifier,
       exchange_token_digest = NULL,
       exchange_expires_at = NULL,
       issued_at = excluded.issued_at,
       expires_at = excluded.expires_at,
       sent_at = NULL,
       verified_at = NULL,
       revision = employee_initial_credentials.revision + 1
     RETURNING revision`
  ).bind(employee.employee_id, CURRENT_CREDENTIAL_ALGORITHM, verifier, now.toISOString(), expiresAt)
   .first<{ revision: number }>();
  if (!stored) throw new Error("INITIAL_CREDENTIAL_WRITE_FAILED");

  let reservation: EmailBudgetReservation;
  try {
    reservation = await reserveEmailBudget(env, employee.workspace_id, now);
  } catch (error) {
    await env.DB.prepare(
      `DELETE FROM employee_initial_credentials
        WHERE employee_id = ?1 AND revision = ?2 AND sent_at IS NULL`
    ).bind(employee.employee_id, stored.revision).run();
    throw error;
  }

  let sent = false;
  try {
    await emailSenderFrom(env).send({
      to: employee.email_normalized,
      subject: "CY Web 帳號 Email 驗證",
      text: [
        "您的 CY Web 帳號已建立。",
        "",
        `帳號：${employee.employee_no}`,
        `一次性首次登入密碼：${password}`,
        "",
        "此密碼僅可使用一次，並於 24 小時後失效。",
        "請前往 CY Web，使用上述帳號與首次登入密碼登入。",
        "登入後請在 10 分鐘內設定新的正式密碼。",
        "設定完成後系統會回到登入頁，請使用新密碼重新登入。",
        "",
        "若首次登入密碼或驗證流程已逾期，請聯絡有權限的管理員重寄驗證 Email。",
        "若您未預期收到此信，請聯絡系統管理員。",
      ].join("\n"),
    });
    sent = true;
    await env.DB.prepare(
      `UPDATE employee_initial_credentials
          SET sent_at = ?3
        WHERE employee_id = ?1 AND revision = ?2 AND sent_at IS NULL`
    ).bind(employee.employee_id, stored.revision, new Date().toISOString()).run();
    return { expiresAt };
  } catch (error) {
    await env.DB.prepare(
      `DELETE FROM employee_initial_credentials
        WHERE employee_id = ?1 AND revision = ?2 AND sent_at IS NULL`
    ).bind(employee.employee_id, stored.revision).run();
    throw error;
  } finally {
    await settleEmailBudget(env, reservation, sent);
  }
}

function deliveryCode(error: unknown): string {
  const code = error instanceof Error ? error.message : "ACTIVATION_EMAIL_FAILED";
  return code === "OTP_RESEND_COOLDOWN"
    || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
    || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
    ? code
    : "ACTIVATION_EMAIL_FAILED";
}

function deliveryStatus(code: string): number {
  return code === "OTP_RESEND_COOLDOWN"
    || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
    || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED" ? 429 : 503;
}

export async function handleCreateEmployee(request: Request, env: Env, requestId: string): Promise<Response> {
  const actor = await requireWorkspaceAdmin(request, env);
  if (!actor) return json(env, requestId, 403, { error: { code: "WORKSPACE_ADMIN_REQUIRED", message: "Workspace administrator authority is required." } });
  const body = await readJsonObject(request);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const displayName = normalizeDisplayName(body?.displayName);
  const roleKey = normalizeStoredEmployeeRole(body?.roleKey ?? "USER");
  let email: string | null = null;
  try { if (typeof body?.email === "string") email = normalizeAddress(body.email); } catch { email = null; }
  if (!employeeNo || !displayName || !email || !roleKey) return json(env, requestId, 400, { error: { code: "INVALID_EMPLOYEE", message: "Employee input is invalid." } });
  if (!canCreateRole(actor, roleKey)) return json(env, requestId, 403, { error: { code: "EMPLOYEE_ROLE_NOT_ALLOWED", message: "The requested Employee role cannot be created by this administrator." } });

  const employeeId = `emp_${crypto.randomUUID()}`;
  const now = new Date().toISOString();
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employees(employee_id, workspace_id, employee_no, name, email_normalized,
          email_verified_at, enabled, role_key, identity_admin, activated_at, revision, created_at, updated_at)
         VALUES(?1, ?2, ?3, ?4, ?5, NULL, 0, ?6, 0, NULL, 1, ?7, ?7)`
      ).bind(employeeId, actor.workspaceId, employeeNo, displayName, email, roleKey, now),
      env.DB.prepare(
        `INSERT INTO identity_audit_events(event_id, workspace_id, actor_employee_id, target_employee_id,
          event_type, detail_json, created_at)
         VALUES(?1, ?2, ?3, ?4, 'employee_created_pending_activation', ?5, ?6)`
      ).bind(`audit_${crypto.randomUUID()}`, actor.workspaceId, actor.employeeId, employeeId, JSON.stringify({ employeeNo, roleKey }), now),
    ]);
  } catch {
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_CONFLICT", message: "Employee No or Email already exists in this Workspace." } });
  }

  const created = await managedEmployee(env, actor.workspaceId, employeeId);
  if (!created) throw new Error("EMPLOYEE_CREATE_READBACK_FAILED");
  let emailVerificationDelivery: JsonValue;
  try {
    const issued = await issueInitialCredential(env, created);
    emailVerificationDelivery = { sent: true, expiresAt: issued.expiresAt };
    await audit(env, { workspaceId: actor.workspaceId, actorEmployeeId: actor.employeeId, targetEmployeeId: employeeId, eventType: "employee_activation_email_sent", detail: { method: "initial_password" } });
  } catch (error) {
    const errorCode = deliveryCode(error);
    emailVerificationDelivery = { sent: false, errorCode };
    await audit(env, { workspaceId: actor.workspaceId, actorEmployeeId: actor.employeeId, targetEmployeeId: employeeId, eventType: "employee_activation_email_failed", detail: { errorCode, method: "initial_password" } });
  }

  return json(env, requestId, 201, {
    employee: {
      employeeId, employeeNo, displayName, email, roleKey,
      isIdentityAdmin: false, emailVerified: false, enabled: false, activated: false,
      pendingEmailVerification: true,
      pendingActivation: true,
      revision: 1,
    },
    emailVerificationDelivery,
  });
}

export async function handleResendInitialEmailVerification(request: Request, env: Env, requestId: string, employeeIdRaw: string): Promise<Response> {
  const actor = await requireWorkspaceAdmin(request, env);
  if (!actor) return json(env, requestId, 403, { error: { code: "WORKSPACE_ADMIN_REQUIRED", message: "Workspace administrator authority is required." } });
  const employeeId = normalizeEmployeeId(employeeIdRaw);
  const employee = employeeId ? await managedEmployee(env, actor.workspaceId, employeeId) : null;
  if (!employee) return json(env, requestId, 404, { error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." } });
  if (!actorCanManage(actor, employee) || targetRole(employee) === "SUPER_ADMIN") return json(env, requestId, 403, { error: { code: "EMPLOYEE_MANAGEMENT_NOT_ALLOWED", message: "This Employee cannot be managed by the current administrator." } });
  if (employee.activated_at || employee.credential_present !== 0) return json(env, requestId, 409, { error: { code: "EMPLOYEE_ALREADY_ACTIVATED", message: "This Employee has already completed first activation." } });
  try {
    const issued = await issueInitialCredential(env, employee);
    await audit(env, { workspaceId: actor.workspaceId, actorEmployeeId: actor.employeeId, targetEmployeeId: employee.employee_id, eventType: "employee_activation_email_resent", detail: { method: "initial_password" } });
    const delivery = { sent: true, expiresAt: issued.expiresAt };
    return json(env, requestId, 202, {
      emailVerificationDelivery: delivery,
    });
  } catch (error) {
    const code = deliveryCode(error);
    return json(env, requestId, deliveryStatus(code), { error: { code, message: "Email verification could not be sent." } });
  }
}

export async function exchangeInitialPassword(
  env: Env, requestId: string,
  employee: { employee_id: string; workspace_id: string; employee_no: string; name: string; workspace_status: string; enabled: number },
  applicationId: string, password: string,
): Promise<Response> {
  const coreApp = coreApplicationId(env);
  const workspaceId = employee.workspace_id;
  if (!coreApp || applicationId !== coreApp || employee.workspace_status !== "active" || employee.enabled !== 0) {
    return json(env, requestId, 401, { error: { code: "AUTHENTICATION_FAILED", message: "Authentication failed." } });
  }
  const initial = await env.DB.prepare(
    `SELECT employee_id, algorithm, verifier, exchange_token_digest, exchange_expires_at,
            issued_at, expires_at, sent_at, verified_at, revision
       FROM employee_initial_credentials WHERE employee_id = ?1 LIMIT 1`
  ).bind(employee.employee_id).first<InitialCredentialRow>();
  if (!initial || initial.sent_at === null || initial.verified_at !== null
      || !await verifyCredential(password, initial.algorithm, initial.verifier)) {
    return json(env, requestId, 401, { error: { code: "AUTHENTICATION_FAILED", message: "Authentication failed." } });
  }
  const initialExpiresAt = new Date(initial.expires_at).getTime();
  if (!Number.isFinite(initialExpiresAt) || initialExpiresAt <= Date.now()) {
    return json(env, requestId, 401, {
      error: { code: "FIRST_LOGIN_PASSWORD_EXPIRED", message: "Email verification has expired. Ask an authorized administrator to resend the verification Email." },
    });
  }

  const token = createFirstLoginToken();
  const digest = await sha256Hex(token);
  const now = new Date();
  const expiresAt = new Date(now.getTime() + FIRST_LOGIN_TICKET_TTL_MS).toISOString();
  const consumed = await env.DB.prepare(
    `UPDATE employee_initial_credentials
        SET exchange_token_digest = ?2, exchange_expires_at = ?3, verified_at = ?4,
            revision = revision + 1
      WHERE employee_id = ?1 AND revision = ?5 AND verified_at IS NULL
        AND sent_at IS NOT NULL AND expires_at > ?4
      RETURNING revision`
  ).bind(employee.employee_id, digest, expiresAt, now.toISOString(), initial.revision)
   .first<{ revision: number }>();
  if (!consumed) {
    return json(env, requestId, 401, { error: { code: "AUTHENTICATION_FAILED", message: "Authentication failed." } });
  }
  await audit(env, { workspaceId, targetEmployeeId: employee.employee_id, eventType: "employee_initial_password_verified" });
  return json(env, requestId, 200, {
    passwordChangeRequired: true,
    firstLogin: { token, employeeNo: employee.employee_no, displayName: employee.name, expiresAt },
  });
}

export async function handleCompleteFirstLogin(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const applicationId = normalizedApplicationId(body?.applicationId);
  const token = body?.token;
  const password = normalizePassword(body?.password);
  const coreApp = coreApplicationId(env);
  if (!workspaceId || !applicationId || applicationId !== coreApp || !validFirstLoginToken(token) || !password) {
    return json(env, requestId, 400, { error: { code: "INVALID_FIRST_LOGIN", message: "First login completion is invalid." } });
  }
  const digest = await sha256Hex(token);
  const now = new Date();
  const row = await env.DB.prepare(
    `SELECT e.employee_id, e.employee_no, e.name, e.email_normalized, e.role_key,
            e.activated_at, e.enabled, w.status AS workspace_status,
            i.exchange_expires_at,
            CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present
       FROM employee_initial_credentials i
       JOIN employees e ON e.employee_id = i.employee_id
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1 AND i.exchange_token_digest = ?2
      LIMIT 1`
  ).bind(workspaceId, digest).first<any>();
  if (!row || row.workspace_status !== "active" || row.activated_at || row.enabled !== 0 || row.credential_present !== 0
      || !row.exchange_expires_at || new Date(row.exchange_expires_at).getTime() <= now.getTime()) {
    return json(env, requestId, 400, { error: { code: "FIRST_LOGIN_EXPIRED", message: "First login verification is no longer valid." } });
  }

  const verifier = await createCredentialVerifier(password);
  const timestamp = now.toISOString();
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version, updated_at)
         VALUES(?1, ?2, ?3, 1, ?4)`
      ).bind(row.employee_id, CURRENT_CREDENTIAL_ALGORITHM, verifier, timestamp),
      env.DB.prepare(
        `UPDATE employees
            SET email_verified_at = ?3, activated_at = ?3, enabled = 1,
                revision = revision + 1, updated_at = ?3
          WHERE workspace_id = ?1 AND employee_id = ?2 AND activated_at IS NULL AND enabled = 0`
      ).bind(workspaceId, row.employee_id, timestamp),
      env.DB.prepare(`DELETE FROM employee_initial_credentials WHERE employee_id = ?1`).bind(row.employee_id),
      env.DB.prepare(
        `INSERT INTO identity_audit_events(event_id, workspace_id, actor_employee_id, target_employee_id,
          event_type, detail_json, created_at)
         VALUES(?1, ?2, NULL, ?3, 'employee_activation_completed', ?4, ?5)`
      ).bind(`audit_${crypto.randomUUID()}`, workspaceId, row.employee_id, JSON.stringify({ employeeNo: row.employee_no, roleKey: row.role_key, method: "initial_password" }), timestamp),
    ]);
  } catch {
    return json(env, requestId, 409, { error: { code: "FIRST_LOGIN_CONFLICT", message: "First login could not be completed." } });
  }

  return json(env, requestId, 200, {
    emailVerified: true,
    passwordChanged: true,
    reloginRequired: true,
  });
}
