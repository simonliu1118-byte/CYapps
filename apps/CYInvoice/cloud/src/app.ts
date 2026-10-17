import { handleCyId, usesCyId, withCyIdSession, cyIdDevice, cyIdBinding, CyIdError, cyIdErrorResponse, type CyIdEnv } from "./cyid";
import baseWorker from "./worker";
import { handleDeviceLifecycle } from "./device-lifecycle";
import { handleDeviceSelfStatus } from "./device-self-status";
import { handleRuntimeSync } from "./runtime-sync";
import { handleCompanySettings } from "./company-settings-route";
import { handleRecoveryRoutes } from "./recovery-routes";
import { WorkerEntrypoint } from "cloudflare:workers";
import { issueCyWebRecoveryInvitation, recoveryInvitationStatus, revokeRecoveryInvitation, type RecoveryIssueResult } from "./recovery-issuer";
import type { CyWebRecoveryRequest } from "./recovery-authority";

interface Env extends CyIdEnv {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  // Feature remains disabled until the original D1 was safely upgraded.
  COMPANY_SETTINGS_ENABLED?: string;
  RECOVERY_ENABLED?: string;
  COMPANY_SETTINGS_KEY_HEX?: string;
  WEB_LOGIN_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_LOGIN_IP_RATE_LIMIT: RateLimit;
  WEB_PASSWORD_RESET_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_PASSWORD_RESET_IP_RATE_LIMIT: RateLimit;
  BOOTSTRAP_KEY?: string;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

 // Limit untrusted V3.0 credential-bearing request bodies BEFORE the generic
 // CYID Login -> Resolve -> Logout flow parses JSON or touches a Device.
 // Do not rely on Content-Length: streamed/chunked requests may omit it.
async function boundedEmployeeBody(request: Request, maxBytes: number): Promise<Record<string, unknown> | null> {
  const header = request.headers.get("content-length");
  if (header !== null && (!/^[0-9]+$/.test(header) || Number(header) > maxBytes))
    throw new CyIdError(413, "REQUEST_TOO_LARGE");
  const reader = request.clone().body?.getReader();
  if (!reader) return null;
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > maxBytes) {
        await reader.cancel().catch(() => undefined);
        throw new CyIdError(413, "REQUEST_TOO_LARGE");
      }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try {
    const body: unknown = JSON.parse(new TextDecoder("utf-8", {fatal:true}).decode(bytes));
    return body && typeof body === "object" && !Array.isArray(body)
      ? body as Record<string, unknown> : null;
  } catch { return null; }
}

// No public issuance URL: named CYWEB private Service Binding only.
// Remains disabled until 3.0 D1 migration and recovery maintenance are approved.
function recoveryEnabled(env: Env): boolean {
  return env.RECOVERY_ENABLED === "true" && /^[0-9]+$/.test(env.SCHEMA_VERSION)
    && Number(env.SCHEMA_VERSION) >= 15;
}
export class CyInvoiceRecoveryEntrypoint extends WorkerEntrypoint<Env> {
  private checkReady(): void {
    if (!recoveryEnabled(this.env) || !usesCyId(this.env))
      throw new CyIdError(503, "RECOVERY_NOT_READY");
    cyIdBinding(this.env);
  }
  async issueRecoveryInvitation(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    this.checkReady();
    return issueCyWebRecoveryInvitation(this.env, input);
  }
  async getRecoveryInvitationStatus(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    this.checkReady();
    return recoveryInvitationStatus(this.env, input);
  }
  async revokeRecoveryInvitation(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    this.checkReady();
    return revokeRecoveryInvitation(this.env, input);
  }
}
export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    // CYInvoice V3.0 has exactly one online Employee authority: CYID.
    // Reject a missing/disabled binding BEFORE runtime sync, identity,
    // Device or any legacy account mutator can run. Local has no Worker.
    try {
      if (!usesCyId(env))
        return cyIdErrorResponse(new CyIdError(409, "CYID_REQUIRED"));
      cyIdBinding(env);
    } catch (error) { return cyIdErrorResponse(error); }

    const runtimeSync = await handleRuntimeSync(request, env);
    if (runtimeSync) return runtimeSync;
    const cyIdResponse = await handleCyId(request, env);
    if (cyIdResponse) return cyIdResponse;
    // Old Built-in routes must fail explicitly even when old schema tables
    // are still present for historical preservation.
    const path = new URL(request.url).pathname;
    if (path.startsWith("/v1/employees") || path.startsWith("/v1/employee-")
        || path.startsWith("/v1/web-auth") || path.startsWith("/v1/web-password")
        || path.startsWith("/v1/super-admin") || path.startsWith("/v1/onboarding/bootstrap")
        || path === "/v1/bootstrap")
      return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
    const recoveryPath = path === "/v1/device-recovery/preview" ||
      path === "/v1/device-recovery/claim";
    if (recoveryPath && !recoveryEnabled(env))
      return Response.json({ok:false,error:{code:"RECOVERY_NOT_READY"}},{
        status:503,headers:{"cache-control":"no-store"}
      });
    const companySettingsPath = path === "/v1/company-settings/get" ||
      path === "/v1/company-settings/update" ||
      path === "/v1/company-settings/provision";
    // Exposing the route requires BOTH an explicit opt-in and the reviewed
    // recovery+company migration marker. Schema 13 is the existing D1.
    // Do not probe missing tables or silently fall back to local credentials.
    if (companySettingsPath && (env.COMPANY_SETTINGS_ENABLED !== "true" ||
        !/^[0-9]+$/.test(env.SCHEMA_VERSION) || Number(env.SCHEMA_VERSION) < 15))
      return Response.json({ok:false,error:{code:"COMPANY_SETTINGS_NOT_READY"}}, {
        status:503,headers:{"cache-control":"no-store"}
      });
    const needsEmployee = request.method === "POST" && [
      "/v1/device-recovery/preview", "/v1/device-recovery/claim",
      "/v1/devices/revoke", "/v1/devices/rename", "/v1/device-pairings/authorization-email",
      "/v1/device-pairings", "/v1/device-invitations",
      "/v1/device-invitations/claim", "/v1/device-invitations/revoke", "/v1/device-invitations/preview",
      "/v1/company-settings/get", "/v1/company-settings/update",
      "/v1/company-settings/provision",
    ].includes(path);
    try {
      if (usesCyId(env)) {
        cyIdBinding(env);
        if (path === "/v1/bootstrap") return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
        if (needsEmployee) {
          // Company card: 4096 bytes; recovery: 2048 bytes. This bounded read
          // runs before Device/CYID authorization, including missing-length bodies.
          const bounded = companySettingsPath || recoveryPath;
          const boundedBody = bounded
            ? await boundedEmployeeBody(request, recoveryPath ? 2048 : 4096) : null;
          if (!recoveryPath && !["/v1/device-pairings/claim", "/v1/device-invitations/claim", "/v1/device-invitations/preview"].includes(path))
            await cyIdDevice(request, env);
          const body = bounded ? boundedBody
            : await request.clone().json<Record<string, unknown>>().catch(() => null);
          return await withCyIdSession(env, body?.employeeNo, body?.password, async (_, revalidate, verifiedEmail) =>
            dispatchRequest(request, { ...env, cyIdContext: { revalidate, verifiedEmail } }));
        }
      }
      return await dispatchRequest(request, env);
    } catch (error) { return cyIdErrorResponse(error); }
  },
};

async function dispatchRequest(request: Request, env: Env): Promise<Response> {
    const recoveryResponse = await handleRecoveryRoutes(request, env);
    if (recoveryResponse) return recoveryResponse;

    const deviceSelfStatusResponse = await handleDeviceSelfStatus(request, env);
    if (deviceSelfStatusResponse) return deviceSelfStatusResponse;

    const deviceLifecycleResponse = await handleDeviceLifecycle(request, env);
    if (deviceLifecycleResponse) return deviceLifecycleResponse;

    const companySettingsResponse = await handleCompanySettings(request, env);
    if (companySettingsResponse) return companySettingsResponse;

    return baseWorker.fetch(request, env);
}
