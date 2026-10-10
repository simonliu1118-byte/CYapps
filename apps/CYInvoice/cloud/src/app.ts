import { handleCyId, usesCyId, withCyIdSession, cyIdDevice, cyIdBinding, CyIdError, cyIdErrorResponse, type CyIdEnv } from "./cyid";
import baseWorker from "./worker";
import { handleDeviceLifecycle } from "./device-lifecycle";
import { handleDeviceSelfStatus } from "./device-self-status";
import { handleEmployeeTransition } from "./employee-transition";
import { handleEmployeeTransitionActions } from "./employee-transition-actions";
import { handleEmployeeTransitionConflicts } from "./employee-transition-conflicts";
import { handleEmployeeAuthority } from "./employee-authority";
import { handleEmployeeManagement } from "./employee-management";
import { handleEmployeeAccountOperations } from "./employee-account-operations";
import { handleEmployeePasswordRecovery } from "./employee-password-recovery";
import { handleSuperAdminTransfer } from "./super-admin-transfer";
import { handleWebAuth } from "./web-auth";
import { handleWebPasswordRecovery } from "./web-password-recovery";
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
    const runtimeSync = await handleRuntimeSync(request, env);
    if (runtimeSync) return runtimeSync;
    const cyIdResponse = await handleCyId(request, env);
    if (cyIdResponse) return cyIdResponse;
    try {
      if (usesCyId(env)) {
        const path = new URL(request.url).pathname;
        // Legacy account authority must never remain an alternate entry in CYID mode.
        if (path.startsWith("/v1/employees") || path.startsWith("/v1/employee-")
            || path.startsWith("/v1/web-auth") || path.startsWith("/v1/web-password")
            || path.startsWith("/v1/super-admin") || path.startsWith("/v1/onboarding/bootstrap"))
          return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
      }
    } catch (error) { return cyIdErrorResponse(error); }
    const path = new URL(request.url).pathname;
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
    const passwordRecoveryResponse = await handleWebPasswordRecovery(request, env);
    if (passwordRecoveryResponse) return passwordRecoveryResponse;

    const webAuthResponse = await handleWebAuth(request, env);
    if (webAuthResponse) return webAuthResponse;

    const deviceSelfStatusResponse = await handleDeviceSelfStatus(request, env);
    if (deviceSelfStatusResponse) return deviceSelfStatusResponse;

    const deviceLifecycleResponse = await handleDeviceLifecycle(request, env);
    if (deviceLifecycleResponse) return deviceLifecycleResponse;

    const employeePasswordRecoveryResponse = await handleEmployeePasswordRecovery(request, env);
    if (employeePasswordRecoveryResponse) return employeePasswordRecoveryResponse;
    const employeeManagementResponse = await handleEmployeeManagement(request, env);
    if (employeeManagementResponse) return employeeManagementResponse;

    const employeeAccountResponse = await handleEmployeeAccountOperations(request, env);
    if (employeeAccountResponse) return employeeAccountResponse;

    const transferResponse = await handleSuperAdminTransfer(request, env);
    if (transferResponse) return transferResponse;

    const conflictResponse = await handleEmployeeTransitionConflicts(request, env);
    if (conflictResponse) return conflictResponse;

    const transitionResponse = await handleEmployeeTransition(request, env);
    if (transitionResponse) return transitionResponse;

    const transitionActionResponse = await handleEmployeeTransitionActions(request, env);
    if (transitionActionResponse) return transitionActionResponse;

    const authorityResponse = await handleEmployeeAuthority(request, env);
    if (authorityResponse) return authorityResponse;

    return baseWorker.fetch(request, env);
}
