import type { Env, IdentityPrincipal, WorkspaceRole } from "./types";

export type StoredEmployeeRole = "USER" | "ADMIN";

export function normalizeStoredEmployeeRole(value: unknown): StoredEmployeeRole | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  return normalized === "USER" || normalized === "ADMIN" ? normalized : null;
}

export function effectiveWorkspaceRole(
  employeeId: string,
  superAdminEmployeeId: string | null,
  storedRole: StoredEmployeeRole,
): WorkspaceRole {
  return employeeId === superAdminEmployeeId ? "SUPER_ADMIN" : storedRole;
}

function configuredCoreApplicationId(env: Env): string | null {
  const value = env.CORE_ACCOUNT_APPLICATION_ID?.trim().toUpperCase() ?? "";
  if (value.length < 2 || value.length > 64 || /[^A-Z0-9_-]/.test(value)) return null;
  return value;
}

export function isCoreAccountApplication(env: Env, applicationId: string): boolean {
  const configured = configuredCoreApplicationId(env);
  return configured !== null && configured === applicationId.trim().toUpperCase();
}

export async function hasApplicationEntry(
  env: Env,
  workspaceId: string,
  employeeId: string,
  applicationId: string,
  isWorkspaceSuperAdmin: boolean,
): Promise<boolean> {
  if (isWorkspaceSuperAdmin || isCoreAccountApplication(env, applicationId)) return true;

  const row = await env.DB.prepare(
    `SELECT 1 AS allowed
       FROM employee_application_access
      WHERE workspace_id = ?1
        AND employee_id = ?2
        AND application_id = ?3
        AND enabled = 1
      LIMIT 1`
  ).bind(workspaceId, employeeId, applicationId).first<{ allowed: number }>();
  return Boolean(row);
}

export function isWorkspaceAdminPrincipal(principal: IdentityPrincipal): boolean {
  return principal.workspaceRole === "ADMIN" || principal.workspaceRole === "SUPER_ADMIN";
}

export function isIdentityAdminPrincipal(principal: IdentityPrincipal): boolean {
  return principal.workspaceRole === "SUPER_ADMIN"
    || (principal.workspaceRole === "ADMIN" && principal.isIdentityAdmin);
}

export function canManageEmployeeLifecycle(
  actor: IdentityPrincipal,
  targetRole: WorkspaceRole,
  targetIdentityAdmin: boolean,
): boolean {
  if (actor.workspaceRole === "SUPER_ADMIN") return targetRole !== "SUPER_ADMIN";
  if (actor.workspaceRole !== "ADMIN") return false;

  if (actor.isIdentityAdmin) {
    if (targetRole === "SUPER_ADMIN" || targetIdentityAdmin) return false;
    return true;
  }

  return targetRole === "USER" && !targetIdentityAdmin;
}

export function canCreateRole(actor: IdentityPrincipal, role: StoredEmployeeRole): boolean {
  if (actor.workspaceRole === "SUPER_ADMIN") return true;
  if (actor.workspaceRole !== "ADMIN") return false;
  if (actor.isIdentityAdmin) return true;
  return role === "USER";
}
