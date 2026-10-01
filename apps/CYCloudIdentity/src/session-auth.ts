import { isSessionToken, sha256Hex } from "./crypto";
import { effectiveWorkspaceRole, hasApplicationEntry, type StoredEmployeeRole } from "./role-access";
import type { Env, IdentityPrincipal } from "./types";

type SessionAuthorityRow = {
  workspace_id: string;
  employee_id: string;
  application_id: string;
  session_credential_version: number;
  expires_at: string;
  revoked_at: string | null;
  employee_no: string;
  name: string;
  email_verified_at: string | null;
  employee_enabled: number;
  employee_role_key: StoredEmployeeRole;
  employee_identity_admin: number;
  employee_revision: number;
  current_credential_version: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  application_status: string;
  workspace_application_enabled: number;
};

function bearerSessionToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  const token = match?.[1]?.trim() ?? "";
  return isSessionToken(token) ? token : null;
}

function applicationHeader(request: Request): string | null {
  const raw = request.headers.get("x-identity-application")?.trim().toUpperCase() ?? "";
  if (raw.length < 2 || raw.length > 64 || /[^A-Z0-9_-]/.test(raw)) return null;
  return raw;
}

export async function resolveIdentitySession(
  request: Request,
  env: Env,
): Promise<{ principal: IdentityPrincipal; expiresAt: string } | null> {
  const token = bearerSessionToken(request);
  const applicationId = applicationHeader(request);
  if (!token || !applicationId) return null;

  const row = await env.DB.prepare(
    `SELECT s.workspace_id,
            s.employee_id,
            s.application_id,
            s.credential_version AS session_credential_version,
            s.expires_at,
            s.revoked_at,
            e.employee_no,
            e.name,
            e.email_verified_at,
            e.enabled AS employee_enabled,
            e.role_key AS employee_role_key,
            e.identity_admin AS employee_identity_admin,
            e.revision AS employee_revision,
            c.credential_version AS current_credential_version,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            a.status AS application_status,
            wa.enabled AS workspace_application_enabled
       FROM identity_sessions s
       JOIN employees e
         ON e.employee_id = s.employee_id
        AND e.workspace_id = s.workspace_id
       JOIN employee_credentials c ON c.employee_id = e.employee_id
       JOIN workspaces w ON w.workspace_id = s.workspace_id
       JOIN applications a ON a.application_id = s.application_id
       JOIN workspace_applications wa
         ON wa.workspace_id = s.workspace_id
        AND wa.application_id = s.application_id
      WHERE s.session_hash = ?1
        AND s.application_id = ?2
      LIMIT 1`
  ).bind(await sha256Hex(token), applicationId).first<SessionAuthorityRow>();

  const now = new Date().toISOString();
  if (!row || row.revoked_at || row.expires_at <= now) return null;
  if (row.workspace_status !== "active" || row.employee_enabled !== 1) return null;
  if (row.application_status !== "active" || row.workspace_application_enabled !== 1) return null;
  if (row.session_credential_version !== row.current_credential_version) return null;

  const workspaceRole = effectiveWorkspaceRole(
    row.employee_id,
    row.super_admin_employee_id,
    row.employee_role_key,
  );
  if (!await hasApplicationEntry(
    env,
    row.workspace_id,
    row.employee_id,
    row.application_id,
    workspaceRole === "SUPER_ADMIN",
  )) return null;

  const principal: IdentityPrincipal = {
    workspaceId: row.workspace_id,
    employeeId: row.employee_id,
    employeeNo: row.employee_no,
    displayName: row.name,
    workspaceRole,
    isIdentityAdmin: workspaceRole === "ADMIN" && row.employee_identity_admin === 1,
    emailVerified: Boolean(row.email_verified_at),
    isWorkspaceSuperAdmin: workspaceRole === "SUPER_ADMIN",
    credentialVersion: row.current_credential_version,
    employeeRevision: row.employee_revision,
  };
  return { principal, expiresAt: row.expires_at };
}

export async function requireIdentitySession(
  request: Request,
  env: Env,
): Promise<IdentityPrincipal | null> {
  return (await resolveIdentitySession(request, env))?.principal ?? null;
}
