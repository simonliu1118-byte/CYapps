// Single sign-on portal handoff (SSO_PORTAL_DESIGN.md §4, AUTH_CONTRACT.md §13).
//
// issue:  the core account portal (CY Web) presents its own valid Session and
//         asks for a one-time code for a target App. The code is returned once,
//         together with the App's registered return URL; only its SHA-256 is kept.
// redeem: the target App presents the code through its own Service Binding.
//         The code is consumed atomically, current authority is re-checked and a
//         new App Session is created exactly as login does.
//
// Neither the code nor any Session token is ever written to D1 in raw form, to
// Audit, to an error message or to console output.

import { createSession, type SessionSubject } from "./auth";
import { sha256Hex } from "./crypto";
import { json, readJsonObject } from "./http";
import { hasApplicationEntry, isCoreAccountApplication, type StoredEmployeeRole } from "./role-access";
import { bearerSessionToken, resolveIdentitySession } from "./session-auth";
import { ssoReturnUrlMatches } from "./sso-return-url";
import type { Env, JsonValue } from "./types";

export const SSO_HANDOFF_TTL_MS = 60 * 1000;
const SSO_HANDOFF_PURGE_BATCH = 100;
const CODE_PATTERN = /^[A-Za-z0-9_-]{43}$/;

type IssueFailureReason =
  | "SSO_PORTAL_REQUIRED"
  | "SESSION_INVALID"
  | "INVALID_SSO_HANDOFF_REQUEST"
  | "FIRST_LOGIN_REQUIRED"
  | "SSO_APPLICATION_UNAVAILABLE"
  | "SSO_RETURN_URL_MISMATCH"
  | "APPLICATION_ACCESS_DENIED";

type RedeemFailureReason =
  | "MALFORMED"
  | "UNKNOWN"
  | "EXPIRED"
  | "USED"
  | "WRONG_APPLICATION"
  | "PORTAL_SESSION_INVALID"
  | "EMPLOYEE_INVALID"
  | "CREDENTIAL_CHANGED"
  | "APPLICATION_UNAVAILABLE"
  | "ACCESS_DENIED";

type HandoffAuthorityRow = {
  workspace_id: string;
  employee_id: string;
  application_id: string;
  handoff_credential_version: number;
  expires_at: string;
  used_at: string | null;
  employee_no: string | null;
  name: string | null;
  email_verified_at: string | null;
  enabled: number | null;
  role_key: StoredEmployeeRole | null;
  identity_admin: number | null;
  revision: number | null;
  activated_at: string | null;
  initial_credential_present: number;
  workspace_status: string | null;
  super_admin_employee_id: string | null;
  credential_version: number | null;
  application_status: string | null;
  sso_return_url: string | null;
  workspace_application_enabled: number | null;
  portal_revoked_at: string | null;
  portal_expires_at: string | null;
  portal_credential_version: number | null;
};

function normalizeApplicationId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 2 || normalized.length > 64 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

/** 32 random bytes, base64url without padding (43 characters). */
export function createHandoffCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  let binary = "";
  for (const value of bytes) binary += String.fromCharCode(value);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/**
 * Bounded removal of expired handoffs (used rows expire within the same 60 s).
 * It runs inside every issue so the table cannot grow beyond the codes issued
 * in the last minute plus one batch, without a Cron trigger.
 */
export function purgeExpiredSsoHandoffsStatement(env: Env, nowIso: string): D1PreparedStatement {
  return env.DB.prepare(
    `DELETE FROM sso_handoffs
      WHERE code_hash IN (
        SELECT code_hash FROM sso_handoffs
         WHERE expires_at <= ?1
         ORDER BY expires_at
         LIMIT ?2)`
  ).bind(nowIso, SSO_HANDOFF_PURGE_BATCH);
}

// Audit columns are resolved through sub-selects so that an unknown or
// caller-supplied identifier is stored as NULL instead of violating a foreign key.
function auditStatement(
  env: Env,
  eventType: string,
  input: {
    workspaceId?: string | null;
    actorEmployeeId?: string | null;
    targetEmployeeId?: string | null;
    applicationId?: string | null;
    detail: Record<string, JsonValue>;
  },
): D1PreparedStatement {
  return env.DB.prepare(
    `INSERT INTO identity_audit_events(
       event_id, workspace_id, actor_employee_id, target_employee_id,
       application_id, event_type, detail_json, created_at)
     SELECT ?1,
            (SELECT workspace_id FROM workspaces WHERE workspace_id = ?2),
            (SELECT employee_id FROM employees WHERE employee_id = ?3),
            (SELECT employee_id FROM employees WHERE employee_id = ?4),
            (SELECT application_id FROM applications WHERE application_id = ?5),
            ?6, ?7, ?8`
  ).bind(
    `audit_${crypto.randomUUID()}`,
    input.workspaceId ?? null,
    input.actorEmployeeId ?? null,
    input.targetEmployeeId ?? null,
    input.applicationId ?? null,
    eventType,
    JSON.stringify(input.detail),
    new Date().toISOString(),
  );
}

const ISSUE_FAILURES: Record<IssueFailureReason, { status: number; message: string }> = {
  SSO_PORTAL_REQUIRED: { status: 403, message: "Only the core account portal may issue single sign-on handoffs." },
  SESSION_INVALID: { status: 401, message: "Identity session is missing, invalid, or no longer authorized." },
  INVALID_SSO_HANDOFF_REQUEST: { status: 400, message: "Single sign-on handoff request is invalid." },
  FIRST_LOGIN_REQUIRED: { status: 403, message: "Email verification and permanent password setup must be completed first." },
  SSO_APPLICATION_UNAVAILABLE: { status: 404, message: "Application is not available for single sign-on." },
  SSO_RETURN_URL_MISMATCH: { status: 400, message: "Return URL does not match the registered value." },
  APPLICATION_ACCESS_DENIED: { status: 403, message: "Employee does not have access to this application." },
};

export async function handleIssueSsoHandoff(request: Request, env: Env, requestId: string): Promise<Response> {
  try {
    return await issue(request, env, requestId);
  } catch {
    // Never propagate storage/runtime detail that could carry request material.
    throw new Error("sso_handoff_issue_failed");
  }
}

async function issue(request: Request, env: Env, requestId: string): Promise<Response> {
  const callerApplicationId = normalizeApplicationId(request.headers.get("x-identity-application"));
  const body = await readJsonObject(request);
  const targetApplicationId = normalizeApplicationId(body?.applicationId);

  const refuse = async (
    reason: IssueFailureReason,
    known: { workspaceId?: string; employeeId?: string } = {},
  ): Promise<Response> => {
    const detail: Record<string, JsonValue> = { result: "failure", reason };
    if (callerApplicationId) detail.callerApplicationId = callerApplicationId;
    if (targetApplicationId) detail.targetApplicationId = targetApplicationId;
    await auditStatement(env, "sso_handoff_issue_failed", {
      workspaceId: known.workspaceId,
      actorEmployeeId: known.employeeId,
      targetEmployeeId: known.employeeId,
      applicationId: targetApplicationId,
      detail,
    }).run();
    const failure = ISSUE_FAILURES[reason];
    return json(env, requestId, failure.status, { error: { code: reason, message: failure.message } });
  };

  // Only the configured core account application (CY Web) may issue codes.
  if (!callerApplicationId || !isCoreAccountApplication(env, callerApplicationId)) {
    return refuse("SSO_PORTAL_REQUIRED");
  }

  const portal = await resolveIdentitySession(request, env);
  const portalToken = bearerSessionToken(request);
  if (!portal || !portalToken) return refuse("SESSION_INVALID");
  const principal = portal.principal;
  const known = { workspaceId: principal.workspaceId, employeeId: principal.employeeId };

  const returnUrlSupplied = Boolean(body && Object.prototype.hasOwnProperty.call(body, "returnUrl"));
  if (!body || !targetApplicationId
      || Object.keys(body).some(key => key !== "applicationId" && key !== "returnUrl")
      || (returnUrlSupplied && typeof body.returnUrl !== "string")) {
    return refuse("INVALID_SSO_HANDOFF_REQUEST", known);
  }

  // A pending (first-login) Employee never holds a normal Session, but refuse
  // defensively if lifecycle state says the first-login flow is incomplete.
  const lifecycle = await env.DB.prepare(
    `SELECT e.activated_at,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present
       FROM employees e
       LEFT JOIN employee_initial_credentials i ON i.employee_id = e.employee_id
      WHERE e.employee_id = ?1 AND e.workspace_id = ?2
      LIMIT 1`
  ).bind(principal.employeeId, principal.workspaceId)
    .first<{ activated_at: string | null; initial_credential_present: number }>();
  if (!lifecycle || !lifecycle.activated_at || lifecycle.initial_credential_present === 1) {
    return refuse("FIRST_LOGIN_REQUIRED", known);
  }

  const target = isCoreAccountApplication(env, targetApplicationId) ? null : await env.DB.prepare(
    `SELECT a.sso_return_url
       FROM applications a
       JOIN workspace_applications wa ON wa.application_id = a.application_id
      WHERE a.application_id = ?1
        AND wa.workspace_id = ?2
        AND a.status = 'active'
        AND wa.enabled = 1
      LIMIT 1`
  ).bind(targetApplicationId, principal.workspaceId).first<{ sso_return_url: string | null }>();
  if (!target || !target.sso_return_url) return refuse("SSO_APPLICATION_UNAVAILABLE", known);
  const returnUrl = target.sso_return_url;

  // The portal never chooses the destination. An optional assertion must equal
  // the registered value exactly; it can only cause a refusal.
  if (returnUrlSupplied && !ssoReturnUrlMatches(returnUrl, body.returnUrl)) {
    return refuse("SSO_RETURN_URL_MISMATCH", known);
  }

  if (!await hasApplicationEntry(
    env, principal.workspaceId, principal.employeeId, targetApplicationId, principal.isWorkspaceSuperAdmin,
  )) {
    return refuse("APPLICATION_ACCESS_DENIED", known);
  }

  const code = createHandoffCode();
  const now = new Date();
  const nowIso = now.toISOString();
  const expiresAt = new Date(now.getTime() + SSO_HANDOFF_TTL_MS).toISOString();
  await env.DB.batch([
    purgeExpiredSsoHandoffsStatement(env, nowIso),
    env.DB.prepare(
      `INSERT INTO sso_handoffs(
         code_hash, workspace_id, employee_id, application_id, portal_application_id,
         issued_session_hash, credential_version, created_at, expires_at)
       VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)`
    ).bind(
      await sha256Hex(code),
      principal.workspaceId,
      principal.employeeId,
      targetApplicationId,
      callerApplicationId,
      await sha256Hex(portalToken),
      principal.credentialVersion,
      nowIso,
      expiresAt,
    ),
    auditStatement(env, "sso_handoff_issued", {
      ...known,
      actorEmployeeId: principal.employeeId,
      targetEmployeeId: principal.employeeId,
      applicationId: targetApplicationId,
      detail: { result: "success", callerApplicationId },
    }),
  ]);

  return json(env, requestId, 200, { code, returnUrl, expiresAt });
}

type PreviewFailureReason = "SSO_PORTAL_REQUIRED" | "SESSION_INVALID" | "INVALID_SSO_HANDOFF_REQUEST" | "SSO_APPLICATION_UNAVAILABLE";

const PREVIEW_FAILURES: Record<PreviewFailureReason, { status: number; message: string }> = {
  SSO_PORTAL_REQUIRED: ISSUE_FAILURES.SSO_PORTAL_REQUIRED,
  SESSION_INVALID: ISSUE_FAILURES.SESSION_INVALID,
  INVALID_SSO_HANDOFF_REQUEST: ISSUE_FAILURES.INVALID_SSO_HANDOFF_REQUEST,
  SSO_APPLICATION_UNAVAILABLE: ISSUE_FAILURES.SSO_APPLICATION_UNAVAILABLE,
};

/**
 * Read-only preview for the portal confirmation screen (AUTH_CONTRACT.md §13.5).
 * It applies the same caller, Session, target and access rules as issue, but it
 * creates no code, writes no row and no Audit event, and never returns the
 * return URL.
 */
export async function handlePreviewSsoHandoff(request: Request, env: Env, requestId: string): Promise<Response> {
  try {
    return await preview(request, env, requestId);
  } catch {
    throw new Error("sso_handoff_preview_failed");
  }
}

async function preview(request: Request, env: Env, requestId: string): Promise<Response> {
  const refuse = (reason: PreviewFailureReason): Response => {
    const failure = PREVIEW_FAILURES[reason];
    return json(env, requestId, failure.status, { error: { code: reason, message: failure.message } });
  };

  const callerApplicationId = normalizeApplicationId(request.headers.get("x-identity-application"));
  if (!callerApplicationId || !isCoreAccountApplication(env, callerApplicationId)) {
    return refuse("SSO_PORTAL_REQUIRED");
  }
  const portal = await resolveIdentitySession(request, env);
  if (!portal || !bearerSessionToken(request)) return refuse("SESSION_INVALID");
  const principal = portal.principal;

  const body = await readJsonObject(request);
  const targetApplicationId = normalizeApplicationId(body?.applicationId);
  if (!body || !targetApplicationId || Object.keys(body).some(key => key !== "applicationId")) {
    return refuse("INVALID_SSO_HANDOFF_REQUEST");
  }

  const target = isCoreAccountApplication(env, targetApplicationId) ? null : await env.DB.prepare(
    `SELECT a.display_name, a.display_name_zh, a.sso_return_url
       FROM applications a
       JOIN workspace_applications wa ON wa.application_id = a.application_id
      WHERE a.application_id = ?1
        AND wa.workspace_id = ?2
        AND a.status = 'active'
        AND wa.enabled = 1
      LIMIT 1`
  ).bind(targetApplicationId, principal.workspaceId)
    .first<{ display_name: string; display_name_zh: string | null; sso_return_url: string | null }>();
  if (!target || !target.sso_return_url) return refuse("SSO_APPLICATION_UNAVAILABLE");

  const lifecycle = await env.DB.prepare(
    `SELECT e.activated_at,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present
       FROM employees e
       LEFT JOIN employee_initial_credentials i ON i.employee_id = e.employee_id
      WHERE e.employee_id = ?1 AND e.workspace_id = ?2
      LIMIT 1`
  ).bind(principal.employeeId, principal.workspaceId)
    .first<{ activated_at: string | null; initial_credential_present: number }>();

  let access: "allowed" | "denied" | "first_login_required";
  if (!lifecycle || !lifecycle.activated_at || lifecycle.initial_credential_present === 1) {
    access = "first_login_required";
  } else {
    access = await hasApplicationEntry(
      env, principal.workspaceId, principal.employeeId, targetApplicationId, principal.isWorkspaceSuperAdmin,
    ) ? "allowed" : "denied";
  }

  return json(env, requestId, 200, {
    // Only the site origin (scheme + host) of the registered callback: lets the portal pick the App icon;
    // the full return URL stays undisclosed.
    application: { applicationId: targetApplicationId, displayName: target.display_name, displayNameZh: target.display_name_zh ?? null, origin: new URL(target.sso_return_url).origin },
    access,
  });
}

/**
 * Read-only list of the Apps the current Employee may enter through the portal
 * (AUTH_CONTRACT.md §13.6). Same caller and Session rules as preview; no code,
 * no write, no Audit event, and only the origin of each return URL is returned.
 */
export async function handleListSsoApplications(request: Request, env: Env, requestId: string): Promise<Response> {
  try {
    return await listApplications(request, env, requestId);
  } catch {
    throw new Error("sso_application_list_failed");
  }
}

async function listApplications(request: Request, env: Env, requestId: string): Promise<Response> {
  const refuse = (reason: PreviewFailureReason): Response => {
    const failure = PREVIEW_FAILURES[reason];
    return json(env, requestId, failure.status, { error: { code: reason, message: failure.message } });
  };
  const callerApplicationId = normalizeApplicationId(request.headers.get("x-identity-application"));
  if (!callerApplicationId || !isCoreAccountApplication(env, callerApplicationId)) {
    return refuse("SSO_PORTAL_REQUIRED");
  }
  const portal = await resolveIdentitySession(request, env);
  if (!portal || !bearerSessionToken(request)) return refuse("SESSION_INVALID");
  const principal = portal.principal;

  const body = await readJsonObject(request);
  if (!body || Object.keys(body).length > 0) return refuse("INVALID_SSO_HANDOFF_REQUEST");

  const lifecycle = await env.DB.prepare(
    `SELECT e.activated_at,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present
       FROM employees e
       LEFT JOIN employee_initial_credentials i ON i.employee_id = e.employee_id
      WHERE e.employee_id = ?1 AND e.workspace_id = ?2
      LIMIT 1`
  ).bind(principal.employeeId, principal.workspaceId)
    .first<{ activated_at: string | null; initial_credential_present: number }>();
  if (!lifecycle || !lifecycle.activated_at || lifecycle.initial_credential_present === 1) {
    return json(env, requestId, 200, { applications: [] });
  }

  const rows = await env.DB.prepare(
    `SELECT a.application_id, a.display_name, a.display_name_zh, a.sso_return_url
       FROM applications a
       JOIN workspace_applications wa ON wa.application_id = a.application_id
      WHERE wa.workspace_id = ?1
        AND a.status = 'active'
        AND wa.enabled = 1
        AND a.sso_return_url IS NOT NULL
      ORDER BY a.display_name, a.application_id`
  ).bind(principal.workspaceId).all<{
    application_id: string; display_name: string; display_name_zh: string | null; sso_return_url: string;
  }>();

  const applications: Array<{ applicationId: string; displayName: string; displayNameZh: string | null; origin: string }> = [];
  for (const row of rows.results ?? []) {
    if (isCoreAccountApplication(env, row.application_id)) continue;
    if (!await hasApplicationEntry(
      env, principal.workspaceId, principal.employeeId, row.application_id, principal.isWorkspaceSuperAdmin,
    )) continue;
    applications.push({
      applicationId: row.application_id,
      displayName: row.display_name,
      displayNameZh: row.display_name_zh ?? null,
      origin: new URL(row.sso_return_url).origin,
    });
  }
  return json(env, requestId, 200, { applications });
}

export async function handleRedeemSsoHandoff(request: Request, env: Env, requestId: string): Promise<Response> {
  try {
    return await redeem(request, env, requestId);
  } catch {
    throw new Error("sso_handoff_redeem_failed");
  }
}

async function redeem(request: Request, env: Env, requestId: string): Promise<Response> {
  const limiter = env.LOGIN_RATE_LIMITER;
  if (!limiter) {
    return json(env, requestId, 503, {
      error: { code: "AUTH_RATE_LIMITER_UNAVAILABLE", message: "Authentication protection is not configured." },
    });
  }
  const applicationId = normalizeApplicationId(request.headers.get("x-identity-application"));

  const invalid = async (
    reason: RedeemFailureReason,
    row?: Pick<HandoffAuthorityRow, "workspace_id" | "employee_id" | "application_id"> | null,
  ): Promise<Response> => {
    // Failures are counted per calling application with the existing limiter.
    const limit = await limiter.limit({ key: await sha256Hex(`sso-redeem-failure:${applicationId ?? "malformed"}`) });
    if (!limit.success) {
      // Bounded: no Audit write while the limit is exceeded.
      console.warn("sso_handoff_redeem_rate_limited", { requestId, applicationId });
      return json(env, requestId, 429, {
        error: { code: "AUTH_RATE_LIMITED", message: "Too many authentication attempts. Try again later." },
      }, { "retry-after": "60" });
    }
    const detail: Record<string, JsonValue> = { result: "failure", reason };
    if (row && row.application_id !== applicationId) detail.boundApplicationId = row.application_id;
    await auditStatement(env, "sso_handoff_redeem_failed", {
      workspaceId: row?.workspace_id,
      targetEmployeeId: row?.employee_id,
      applicationId,
      detail,
    }).run();
    return json(env, requestId, 401, {
      error: { code: "SSO_HANDOFF_INVALID", message: "Single sign-on handoff is invalid." },
    });
  };

  const body = await readJsonObject(request);
  const code = body && Object.keys(body).length === 1 && typeof body.code === "string" && CODE_PATTERN.test(body.code)
    ? body.code : null;
  if (!applicationId || !code) return invalid("MALFORMED");

  const codeHash = await sha256Hex(code);
  const nowIso = new Date().toISOString();
  // Atomic single use: exactly one concurrent caller can change used_at. The
  // code is burned on first presentation even if the caller is the wrong App.
  const consumed = await env.DB.prepare(
    `UPDATE sso_handoffs
        SET used_at = ?2
      WHERE code_hash = ?1
        AND used_at IS NULL
        AND expires_at > ?2`
  ).bind(codeHash, nowIso).run();
  const changes = Number(consumed.meta?.changes ?? 0);

  const row = await env.DB.prepare(
    `SELECT h.workspace_id,
            h.employee_id,
            h.application_id,
            h.credential_version AS handoff_credential_version,
            h.expires_at,
            h.used_at,
            e.employee_no,
            e.name,
            e.email_verified_at,
            e.enabled,
            e.role_key,
            e.identity_admin,
            e.revision,
            e.activated_at,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            c.credential_version,
            a.status AS application_status,
            a.sso_return_url,
            wa.enabled AS workspace_application_enabled,
            ps.revoked_at AS portal_revoked_at,
            ps.expires_at AS portal_expires_at,
            ps.credential_version AS portal_credential_version
       FROM sso_handoffs h
       LEFT JOIN employees e ON e.employee_id = h.employee_id AND e.workspace_id = h.workspace_id
       LEFT JOIN employee_initial_credentials i ON i.employee_id = h.employee_id
       LEFT JOIN workspaces w ON w.workspace_id = h.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = h.employee_id
       LEFT JOIN applications a ON a.application_id = h.application_id
       LEFT JOIN workspace_applications wa
         ON wa.workspace_id = h.workspace_id AND wa.application_id = h.application_id
       LEFT JOIN identity_sessions ps ON ps.session_hash = h.issued_session_hash
      WHERE h.code_hash = ?1
      LIMIT 1`
  ).bind(codeHash).first<HandoffAuthorityRow>();

  if (changes !== 1) {
    if (!row) return invalid("UNKNOWN");
    return invalid(row.used_at ? "USED" : "EXPIRED", row);
  }
  if (!row) return invalid("UNKNOWN");
  if (row.application_id !== applicationId) return invalid("WRONG_APPLICATION", row);

  // Re-check current authority: issue-time checks are not trusted at redeem.
  if (row.workspace_status !== "active" || row.enabled !== 1 || !row.activated_at
      || row.initial_credential_present === 1 || row.employee_no === null || row.name === null
      || row.role_key === null || row.identity_admin === null || row.revision === null) {
    return invalid("EMPLOYEE_INVALID", row);
  }
  if (row.credential_version === null || row.credential_version !== row.handoff_credential_version) {
    return invalid("CREDENTIAL_CHANGED", row);
  }
  if (row.portal_revoked_at || !row.portal_expires_at || row.portal_expires_at <= nowIso
      || row.portal_credential_version !== row.credential_version) {
    return invalid("PORTAL_SESSION_INVALID", row);
  }
  if (row.application_status !== "active" || row.workspace_application_enabled !== 1 || !row.sso_return_url) {
    return invalid("APPLICATION_UNAVAILABLE", row);
  }
  const isWorkspaceSuperAdmin = row.super_admin_employee_id === row.employee_id;
  if (!await hasApplicationEntry(env, row.workspace_id, row.employee_id, applicationId, isWorkspaceSuperAdmin)) {
    return invalid("ACCESS_DENIED", row);
  }

  const subject: SessionSubject = {
    employee_id: row.employee_id,
    workspace_id: row.workspace_id,
    employee_no: row.employee_no,
    name: row.name,
    email_verified_at: row.email_verified_at,
    role_key: row.role_key,
    identity_admin: row.identity_admin,
    revision: row.revision,
    super_admin_employee_id: row.super_admin_employee_id,
    credential_version: row.credential_version,
  };
  const login = await createSession(env, subject, applicationId);
  await auditStatement(env, "sso_handoff_redeemed", {
    workspaceId: row.workspace_id,
    targetEmployeeId: row.employee_id,
    applicationId,
    detail: { result: "success" },
  }).run();
  return json(env, requestId, 200, {
    principal: login.principal as unknown as JsonValue,
    session: login.session as unknown as JsonValue,
  });
}
