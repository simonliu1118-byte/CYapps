import { isSessionToken, sha256Hex } from "./crypto";
import type { Env, IdentityPrincipal } from "./types";

type AdminSessionRow = {
  workspace_id: string;
  employee_id: string;
  employee_no: string;
  name: string;
  employee_enabled: number;
  employee_revision: number;
  current_credential_version: number;
  session_credential_version: number;
  application_id: string;
  application_status: string;
  workspace_application_enabled: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  expires_at: string;
  revoked_at: string | null;
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

async function groupKeys(env: Env, workspaceId: string, employeeId: string): Promise<string[]> {
  const result = await env.DB.prepare(
    `SELECT g.group_key
       FROM employee_identity_groups eg
       JOIN identity_groups g
         ON g.group_id = eg.group_id
        AND g.workspace_id = eg.workspace_id
      WHERE eg.workspace_id = ?1
        AND eg.employee_id = ?2
        AND g.status = 'active'
      ORDER BY g.group_key`
  ).bind(workspaceId, employeeId).all<{ group_key: string }>();
  return (result.results ?? []).map(row => row.group_key);
}

export async function requireWorkspaceSuperAdmin(
  request: Request,
  env: Env,
): Promise<IdentityPrincipal | null> {
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
            e.enabled AS employee_enabled,
            e.revision AS employee_revision,
            c.credential_version AS current_credential_version,
            a.status AS application_status,
            wa.enabled AS workspace_application_enabled,
            w.status AS workspace_status,
            w.super_admin_employee_id
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
  ).bind(await sha256Hex(token), applicationId).first<AdminSessionRow>();

  const now = new Date().toISOString();
  if (!row || row.revoked_at || row.expires_at <= now) return null;
  if (row.workspace_status !== "active" || row.employee_enabled !== 1) return null;
  if (row.application_status !== "active" || row.workspace_application_enabled !== 1) return null;
  if (row.session_credential_version !== row.current_credential_version) return null;
  if (row.super_admin_employee_id !== row.employee_id) return null;

  return {
    workspaceId: row.workspace_id,
    employeeId: row.employee_id,
    employeeNo: row.employee_no,
    displayName: row.name,
    isWorkspaceSuperAdmin: true,
    groupKeys: await groupKeys(env, row.workspace_id, row.employee_id),
    credentialVersion: row.current_credential_version,
    employeeRevision: row.employee_revision,
  };
}
