import { cyIdBinding, CyIdError, usesCyId, withCyIdSession, type CyIdEnv, type CyIdPrincipal } from "./cyid";

/**
 * Pure internal trust-boundary helper for the future named CYInvoice private RPC.
 * Not reachable via a public Worker fetch route; no invitation is written here.
 * The CYWEB core session is deliberately absent from the input contract.
 */
export interface CyWebRecoveryRequest {
  operationId: string;
  employeeNo: string;
  password: string;
  expectedEmployeeId: string;
  expectedIdentityWorkspaceId: string;
  expectedCredentialVersion: number;
  expectedEmployeeRevision: number;
}

export interface CyWebRecoveryAuthority {
  operationId: string;
  workspaceId: string;
  identityWorkspaceId: string;
  employeeId: string;
  employeeNo: string;
  email: string;
  credentialVersion: number;
  employeeRevision: number;
}

const OPERATION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function recoveryRequest(raw: unknown): CyWebRecoveryRequest {
  if (!raw || typeof raw !== "object" || Array.isArray(raw))
    throw new CyIdError(400, "RECOVERY_REQUEST_INVALID");
  const obj = raw as Record<string, unknown>;
  const allowed = [
    "operationId", "employeeNo", "password", "expectedEmployeeId",
    "expectedIdentityWorkspaceId", "expectedCredentialVersion", "expectedEmployeeRevision",
  ];
  if (Object.keys(obj).some(k => !allowed.includes(k))
      || typeof obj.operationId !== "string" || !OPERATION_ID.test(obj.operationId)
      || typeof obj.employeeNo !== "string" || !/^[0-9]{4}$/.test(obj.employeeNo)
      || typeof obj.password !== "string" || Array.from(obj.password).length < 8
      || Array.from(obj.password).length > 16
      || typeof obj.expectedEmployeeId !== "string"
      || obj.expectedEmployeeId.length < 1 || obj.expectedEmployeeId.length > 100
      || typeof obj.expectedIdentityWorkspaceId !== "string"
      || obj.expectedIdentityWorkspaceId.length < 1 || obj.expectedIdentityWorkspaceId.length > 100
      || !Number.isSafeInteger(obj.expectedCredentialVersion) || Number(obj.expectedCredentialVersion) < 1
      || !Number.isSafeInteger(obj.expectedEmployeeRevision) || Number(obj.expectedEmployeeRevision) < 1)
    throw new CyIdError(400, "RECOVERY_REQUEST_INVALID");
  return obj as unknown as CyWebRecoveryRequest;
}

function checkAuthority(principal: CyIdPrincipal, input: CyWebRecoveryRequest): void {
  if (!principal.emailVerified || !principal.isWorkspaceSuperAdmin
      || principal.workspaceRole !== "SUPER_ADMIN" || principal.employeeNo !== input.employeeNo
      || principal.employeeId !== input.expectedEmployeeId
      || principal.workspaceId !== input.expectedIdentityWorkspaceId
      || principal.credentialVersion !== input.expectedCredentialVersion
      || principal.employeeRevision !== input.expectedEmployeeRevision)
    throw new CyIdError(403, "RECOVERY_AUTHORITY_CHANGED");
}

/**
 * The action only receives a "verifyNow" function that MUST be called again
 * at each mutation/delivery boundary. Do not cache its result as permission to
 * issue a second invitation after a lost network response.
 */
export async function withAuthorizedCyWebRecovery<T>(
  env: CyIdEnv,
  raw: unknown,
  action: (verifyNow: () => Promise<CyWebRecoveryAuthority>) => Promise<T>,
): Promise<T> {
  const input = recoveryRequest(raw);
  if (!usesCyId(env)) throw new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH");
  const binding = cyIdBinding(env);
  if (binding.identityWorkspaceId !== input.expectedIdentityWorkspaceId)
    throw new CyIdError(403, "RECOVERY_SCOPE_MISMATCH");

  return withCyIdSession(env, input.employeeNo, input.password,
    async (_initial, revalidate, verifiedEmail) => {
      const verifyNow = async (): Promise<CyWebRecoveryAuthority> => {
        checkAuthority(await revalidate(), input);
        const email = await verifiedEmail();
        checkAuthority(await revalidate(), input);

        // Both profiles must exist on the *original business D1*. Never
        // dynamically choose an Identity D1 by browser-provided workspace.
        const ready = await env.DB.prepare(
          `SELECT w.workspace_id FROM workspaces w
            JOIN cyid_storage_profile p
              ON p.workspace_id = w.workspace_id AND p.profile = 'CYID_ONLY'
            JOIN cyweb_recovery_storage_profile r
              ON r.workspace_id = w.workspace_id AND r.profile = 'CYWEB_RECOVERY'
            WHERE w.workspace_id = ?1 AND w.status = 'active' LIMIT 1`,
        ).bind(binding.workspaceId).first<{ workspace_id: string }>();
        if (ready?.workspace_id !== binding.workspaceId)
          throw new CyIdError(409, "RECOVERY_STORAGE_NOT_READY");

        return {
          operationId: input.operationId,
          workspaceId: binding.workspaceId,
          identityWorkspaceId: binding.identityWorkspaceId,
          employeeId: input.expectedEmployeeId,
          employeeNo: input.employeeNo,
          email,
          credentialVersion: input.expectedCredentialVersion,
          employeeRevision: input.expectedEmployeeRevision,
        };
      };
      await verifyNow();
      return action(verifyNow);
    });
}
