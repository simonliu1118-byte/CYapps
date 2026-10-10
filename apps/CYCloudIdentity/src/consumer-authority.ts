import { WorkerEntrypoint } from "cloudflare:workers";
import { effectiveWorkspaceRole, isCoreAccountApplication, type StoredEmployeeRole } from "./role-access";
import type { Env, WorkspaceRole } from "./types";

export interface CachedAuthority {
  employeeId: string;
  credentialVersion: number;
  employeeRevision: number;
  workspaceRole: WorkspaceRole;
  isIdentityAdmin: boolean;
  emailVerified: boolean;
}

// Named private RPC capability. It has no public HTTP route and cannot issue a
// principal, Session, password verifier, positive grant or business authorization.
export class ConsumerAuthoritySync extends WorkerEntrypoint<Env> {
  async invalidateCache(input: { workspaceId: string; applicationId: string; authorities: CachedAuthority[] }) {
    if (!input || typeof input.workspaceId !== "string" || input.workspaceId.length < 5 || input.workspaceId.length > 80
        || typeof input.applicationId !== "string" || !/^[A-Z0-9_-]{2,64}$/.test(input.applicationId)
        || !Array.isArray(input.authorities) || input.authorities.length > 10_000)
      throw new Error("INVALID_AUTHORITY_SYNC_REQUEST");
    const scope = await this.env.DB.prepare(`SELECT w.status AS workspace_status, w.super_admin_employee_id,
      a.status AS application_status, wa.enabled FROM workspaces w
      JOIN workspace_applications wa ON wa.workspace_id = w.workspace_id
      JOIN applications a ON a.application_id = wa.application_id
      WHERE w.workspace_id = ?1 AND a.application_id = ?2 LIMIT 1`)
      .bind(input.workspaceId, input.applicationId)
      .first<{ workspace_status: string; super_admin_employee_id: string | null; application_status: string; enabled: number }>();
    const available = !!scope && scope.workspace_status === "active" && scope.application_status === "active" && scope.enabled === 1;
    type Row = { employee_id: string; enabled: number; role_key: StoredEmployeeRole; identity_admin: number;
      email_verified_at: string | null; revision: number; credential_version: number | null; access_enabled: number };
    const rows = available ? await this.env.DB.prepare(`SELECT e.employee_id, e.enabled, e.role_key, e.identity_admin,
      e.email_verified_at, e.revision, c.credential_version, COALESCE(aa.enabled, 0) AS access_enabled
      FROM json_each(?3) ids JOIN employees e ON e.employee_id = ids.value AND e.workspace_id = ?1
      LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
      LEFT JOIN employee_application_access aa ON aa.employee_id = e.employee_id
        AND aa.workspace_id = e.workspace_id AND aa.application_id = ?2`)
      .bind(input.workspaceId, input.applicationId, JSON.stringify(input.authorities.map(c => c?.employeeId))).all<Row>() : null;
    const current = new Map((rows?.results ?? []).map(row => [row.employee_id, row]));
    const invalidated: string[] = [];
    const seen = new Set<string>();
    for (const cached of input.authorities) {
      if (!cached || typeof cached.employeeId !== "string" || cached.employeeId.length < 1 || cached.employeeId.length > 100
          || seen.has(cached.employeeId) || !Number.isSafeInteger(cached.credentialVersion) || cached.credentialVersion < 1
          || !Number.isSafeInteger(cached.employeeRevision) || cached.employeeRevision < 1
          || !["SUPER_ADMIN", "ADMIN", "USER"].includes(cached.workspaceRole)
          || typeof cached.isIdentityAdmin !== "boolean" || typeof cached.emailVerified !== "boolean")
        throw new Error("INVALID_AUTHORITY_SYNC_REQUEST");
      seen.add(cached.employeeId);
      if (!available) { invalidated.push(cached.employeeId); continue; }
      const row = current.get(cached.employeeId);
      const role = row ? effectiveWorkspaceRole(cached.employeeId, scope!.super_admin_employee_id, row.role_key) : null;
      if (!row || row.enabled !== 1 || (role !== "SUPER_ADMIN" && !isCoreAccountApplication(this.env, input.applicationId) && row.access_enabled !== 1)
          || row.credential_version !== cached.credentialVersion || row.revision !== cached.employeeRevision
          || role !== cached.workspaceRole || (role === "ADMIN" && row.identity_admin === 1) !== cached.isIdentityAdmin
          || Boolean(row.email_verified_at) !== cached.emailVerified)
        invalidated.push(cached.employeeId);
    }
    return { workspaceId: input.workspaceId, applicationId: input.applicationId, available, invalidated };
  }
}
