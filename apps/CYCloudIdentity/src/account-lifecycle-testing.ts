import { isCoreAccountApplication } from "./role-access";
import type { Env, IdentityPrincipal } from "./types";

// Temporary user-approved testing exception, 2026-10-15. Restore both defaults
// before the first business release; the tracked restoration gate is in TODO.md.
export const ACCOUNT_LIFECYCLE_TESTING = {
  allowVerifiedDeletion: true,
  recordCreateDeleteAudit: false,
} as const;

export function canDeleteVerifiedEmployee(request: Request, env: Env, actor: IdentityPrincipal): boolean {
  return ACCOUNT_LIFECYCLE_TESTING.allowVerifiedDeletion && actor.workspaceRole === "SUPER_ADMIN"
    && isCoreAccountApplication(env, request.headers.get("x-identity-application")?.trim().toUpperCase() ?? "");
}
