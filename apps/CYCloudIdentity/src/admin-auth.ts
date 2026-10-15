import { isIdentityAdminPrincipal, isWorkspaceAdminPrincipal } from "./role-access";
import { requireIdentitySession } from "./session-auth";
import type { Env, IdentityPrincipal } from "./types";

export async function requireWorkspaceAdmin(
  request: Request,
  env: Env,
): Promise<IdentityPrincipal | null> {
  const principal = await requireIdentitySession(request, env);
  return principal && isWorkspaceAdminPrincipal(principal) ? principal : null;
}

export async function requireIdentityAdministrator(
  request: Request,
  env: Env,
): Promise<IdentityPrincipal | null> {
  const principal = await requireIdentitySession(request, env);
  return principal && isIdentityAdminPrincipal(principal) ? principal : null;
}

export async function requireWorkspaceSuperAdmin(
  request: Request,
  env: Env,
): Promise<IdentityPrincipal | null> {
  const principal = await requireIdentitySession(request, env);
  return principal?.workspaceRole === "SUPER_ADMIN" ? principal : null;
}

// Recheck the same core-account Session inside the mutation transaction. This
// prevents a revoked Session or concurrent authority transfer from registering
// an application after the initial management guard succeeded.
export const liveCoreSuperAdmin = `
  SELECT 1 FROM identity_sessions s
  JOIN employees e ON e.employee_id = s.employee_id AND e.workspace_id = s.workspace_id
  JOIN employee_credentials c ON c.employee_id = e.employee_id
  JOIN workspaces w ON w.workspace_id = s.workspace_id
  JOIN applications a ON a.application_id = s.application_id
  JOIN workspace_applications wa ON wa.workspace_id = s.workspace_id AND wa.application_id = s.application_id
  WHERE s.session_hash = ?1 AND s.workspace_id = ?2 AND s.employee_id = ?3
    AND s.application_id = ?4 AND s.revoked_at IS NULL
    AND s.expires_at > strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
    AND s.credential_version = c.credential_version AND e.enabled = 1
    AND w.status = 'active' AND w.super_admin_employee_id = e.employee_id
    AND a.status = 'active' AND wa.enabled = 1`;
