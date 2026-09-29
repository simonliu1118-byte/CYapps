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
