import { handleCyId, usesCyId, withCyIdSession, cyIdDevice, cyIdBinding, CyIdError, cyIdErrorResponse, type CyIdEnv } from "./cyid";
import baseWorker from "./worker";
import { handleDeviceLifecycle } from "./device-lifecycle";
import { handleDeviceSelfStatus } from "./device-self-status";
import { handleRuntimeSync } from "./runtime-sync";

interface Env extends CyIdEnv {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
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
    const needsEmployee = request.method === "POST" && [
      "/v1/devices/revoke", "/v1/devices/rename", "/v1/device-pairings/authorization-email",
      "/v1/device-pairings", "/v1/device-invitations",
      "/v1/device-invitations/claim", "/v1/device-invitations/revoke", "/v1/device-invitations/preview",
    ].includes(path);
    try {
      if (usesCyId(env)) {
        cyIdBinding(env);
        if (path === "/v1/bootstrap") return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
        if (needsEmployee) {
          if (!["/v1/device-pairings/claim", "/v1/device-invitations/claim", "/v1/device-invitations/preview"].includes(path))
            await cyIdDevice(request, env);
          const body = await request.clone().json<Record<string, unknown>>().catch(() => null);
          return await withCyIdSession(env, body?.employeeNo, body?.password, async (_, revalidate, verifiedEmail) =>
            dispatchRequest(request, { ...env, cyIdContext: { revalidate, verifiedEmail } }));
        }
      }
      return await dispatchRequest(request, env);
    } catch (error) { return cyIdErrorResponse(error); }
  },
};

async function dispatchRequest(request: Request, env: Env): Promise<Response> {
    const deviceSelfStatusResponse = await handleDeviceSelfStatus(request, env);
    if (deviceSelfStatusResponse) return deviceSelfStatusResponse;

    const deviceLifecycleResponse = await handleDeviceLifecycle(request, env);
    if (deviceLifecycleResponse) return deviceLifecycleResponse;

    return baseWorker.fetch(request, env);
}
