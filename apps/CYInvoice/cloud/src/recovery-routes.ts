import { cyIdBinding, CyIdError, usesCyId, type CyIdEnv, type CyIdPrincipal } from "./cyid";
import { claimRecoveryAndRevokeOldDevices } from "./recovery-claim";
import type { CyWebRecoveryAuthority } from "./recovery-authority";

/**
 * The only public no-old-Device entrypoint is a narrowly scoped recovery
 * preview/claim, each requiring fresh CYInvoice app-scoped SUPER_ADMIN login.
 * Not a generic "ignore device auth" route.
 */
export interface RecoveryRouteEnv extends CyIdEnv {
  DB: D1Database;
  cyIdContext?: {
    revalidate: () => Promise<CyIdPrincipal>;
    verifiedEmail: () => Promise<string>;
  };
}

type RecoveryRequest = {
  code: string;
  employeeNo: string;
  password: string;
  deviceToken?: string;
  deviceDisplayName?: string;
  clientVersion?: string;
};

function error(status: number, code: string): Response {
  return new Response(JSON.stringify({
    ok: false, error: {code, message: "Recovery request denied."},
  }), {status, headers:{"content-type":"application/json","cache-control":"no-store"}});
}
function ok(status: number, value: Record<string, unknown>): Response {
  return new Response(JSON.stringify({ok:true,...value}),{
    status, headers:{"content-type":"application/json","cache-control":"no-store"},
  });
}
function hex40(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{40}$/i.test(value);
}
function valid(value: unknown): value is RecoveryRequest {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const v = value as Record<string, unknown>;
  const keys = Object.keys(v);
  const allowed = ["code","employeeNo","password","deviceToken","deviceDisplayName","clientVersion"];
  return keys.every(k => allowed.includes(k))
    && hex40(v.code)
    && typeof v.employeeNo === "string" && /^[0-9]{4}$/.test(v.employeeNo)
    && typeof v.password === "string" && Array.from(v.password).length >= 8
    && Array.from(v.password).length <= 16;
}
async function codeHash(code: string): Promise<string> {
  const bytes = new Uint8Array(await crypto.subtle.digest("SHA-256",
    new TextEncoder().encode(code.toLowerCase())));
  return [...bytes].map(byte=>byte.toString(16).padStart(2,"0")).join("");
}

async function authorize(
  env: RecoveryRouteEnv,
  expectedEmployeeNo: string,
): Promise<CyWebRecoveryAuthority> {
  const context = env.cyIdContext;
  if (!context) throw new CyIdError(401,"AUTH_REQUIRED");
  const binding = cyIdBinding(env);
  const first = await context.revalidate();
  if (first.workspaceId !== binding.identityWorkspaceId
      || first.employeeNo !== expectedEmployeeNo
      || first.workspaceRole !== "SUPER_ADMIN" || !first.isWorkspaceSuperAdmin
      || !first.emailVerified)
    throw new CyIdError(403,"SUPER_ADMIN_REQUIRED");
  const email = await context.verifiedEmail();
  const second = await context.revalidate();
  if (first.employeeId !== second.employeeId
      || first.credentialVersion !== second.credentialVersion
      || first.employeeRevision !== second.employeeRevision
      || second.workspaceRole !== "SUPER_ADMIN"
      || !second.emailVerified || !second.isWorkspaceSuperAdmin)
    throw new CyIdError(403,"RECOVERY_AUTHORITY_CHANGED");
  return {
    operationId:"",
    workspaceId:binding.workspaceId,
    identityWorkspaceId:binding.identityWorkspaceId,
    employeeId:second.employeeId,
    employeeNo:second.employeeNo,
    email,
    credentialVersion:second.credentialVersion,
    employeeRevision:second.employeeRevision,
  };
}

type PreviewRow = {
  invitation_id: string;
  workspace_id: string;
  display_name: string;
  issued_by_cyid_employee_id: string | null;
  issuer_credential_version: number | null;
  issuer_employee_revision: number | null;
};
export async function handleRecoveryRoutes(
  request: Request,
  env: RecoveryRouteEnv,
): Promise<Response | null> {
  const path = new URL(request.url).pathname;
  const preview = path === "/v1/device-recovery/preview";
  const claim = path === "/v1/device-recovery/claim";
  if (!preview && !claim) return null;
  if (request.method !== "POST") return error(405,"METHOD_NOT_ALLOWED");
  if (!usesCyId(env)) return error(409,"IDENTITY_PROVIDER_MISMATCH");
  const length = Number(request.headers.get("content-length"));
  if (!Number.isFinite(length) || length > 2048)
    return error(413,"INVALID_RECOVERY_REQUEST");
  if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json"))
    return error(415,"UNSUPPORTED_CONTENT_TYPE");
  // Bound JSON body (Content-Length absent is also covered by the size cap
  // after reading). Never log request data, which contains a CYID password.
  const rawText = await request.text();
  if (new TextEncoder().encode(rawText).byteLength > 2048)
    return error(413,"INVALID_RECOVERY_REQUEST");
  let input: unknown;
  try { input = JSON.parse(rawText); }
  catch { return error(400,"INVALID_RECOVERY_REQUEST"); }
  if (!valid(input)) return error(400,"INVALID_RECOVERY_REQUEST");
  const data = input as RecoveryRequest;
  if (preview && ["deviceToken","deviceDisplayName","clientVersion"]
      .some(k => Object.hasOwn(data,k)))
    return error(400,"INVALID_RECOVERY_REQUEST");
  if (claim && (typeof data.deviceToken !== "string" || !/^cydev_[0-9a-f]{64}$/.test(data.deviceToken)
      || typeof data.deviceDisplayName !== "string"
      || data.deviceDisplayName.trim().length < 1 || data.deviceDisplayName.trim().length > 120
      || typeof data.clientVersion !== "string" || data.clientVersion.trim().length < 1
      || data.clientVersion.trim().length > 64))
    return error(400,"INVALID_RECOVERY_REQUEST");

  const actor = await authorize(env,data.employeeNo);
  if (preview) {
    const now = new Date().toISOString();
    const row = await env.DB.prepare(`SELECT i.invitation_id, i.workspace_id,
          w.display_name, i.issued_by_cyid_employee_id,
          i.issuer_credential_version, i.issuer_employee_revision
        FROM device_invitations i
        JOIN workspaces w ON w.workspace_id=i.workspace_id AND w.status='active'
        JOIN cyid_storage_profile p ON p.workspace_id=w.workspace_id AND p.profile='CYID_ONLY'
        JOIN cyweb_recovery_storage_profile r ON r.workspace_id=w.workspace_id
          AND r.profile='CYWEB_RECOVERY'
        WHERE i.code_hash=?1 AND i.workspace_id=?2 AND i.issuer_channel='CYWEB'
          AND i.purpose='RECOVERY' AND i.delivery_state='sent'
          AND i.revoked_at IS NULL AND i.consumed_at IS NULL AND i.expires_at>?3
        LIMIT 1`)
      .bind(await codeHash(data.code),actor.workspaceId,now).first<PreviewRow>();
    if (!row || row.issued_by_cyid_employee_id !== actor.employeeId
      || row.issuer_credential_version !== actor.credentialVersion
      || row.issuer_employee_revision !== actor.employeeRevision)
      return error(409,"RECOVERY_INVITATION_UNAVAILABLE");
    const expiry = await env.DB.prepare(`SELECT expires_at FROM device_invitations
      WHERE invitation_id=?1 AND workspace_id=?2`)
      .bind(row.invitation_id,actor.workspaceId).first<{expires_at:string}>();
    await authorize(env,data.employeeNo);
    return ok(200,{
      workspace:{workspaceId:actor.workspaceId,displayName:row.display_name},
      identity:{identityWorkspaceId:actor.identityWorkspaceId,applicationId:cyIdBinding(env).applicationId},
      invitation:{invitationId:row.invitation_id,expiresAt:expiry?.expires_at},
    });
  }

  // Every claim validates the same current CYID principal before the
  // transaction, and must revalidate through the callback at the mutation.
  const claimResult = await claimRecoveryAndRevokeOldDevices(env,{
    code:data.code,
    deviceToken:data.deviceToken!,
    deviceDisplayName:data.deviceDisplayName!,
    clientVersion:data.clientVersion!,
    requestId:(request.headers.get("x-request-id")||"").match(/^[A-Za-z0-9._:-]{1,128}$/)
      ? request.headers.get("x-request-id")! : crypto.randomUUID(),
  },async () => {
    const fresh = await authorize(env,data.employeeNo);
    if (fresh.employeeId !== actor.employeeId || fresh.workspaceId !== actor.workspaceId
        || fresh.credentialVersion !== actor.credentialVersion
        || fresh.employeeRevision !== actor.employeeRevision)
      throw new CyIdError(403,"RECOVERY_AUTHORITY_CHANGED");
    return fresh;
  });
  return ok(claimResult.recovered?200:201,{
    recovered:claimResult.recovered,
    workspace:{workspaceId:claimResult.workspaceId},
    device:{deviceId:claimResult.deviceId,displayName:claimResult.deviceDisplayName},
  });
}
