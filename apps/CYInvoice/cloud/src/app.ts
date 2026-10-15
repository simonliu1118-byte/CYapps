import { handleCyId, usesCyId, withCyIdSession, cyIdDevice, cyIdBinding, CyIdError, cyIdErrorResponse, type CyIdEnv } from "./cyid";
import baseWorker from "./worker";
import { handleDeviceLifecycle } from "./device-lifecycle";
import { handleDeviceSelfStatus } from "./device-self-status";
import { handleRuntimeSync } from "./runtime-sync";
import { handleWorkItems } from "./work-items-route";
import { handleAllowanceApplications } from "./allowance-applications-route";
import { handleCompanySettings } from "./company-settings-route";
import { handleRecoveryRoutes } from "./recovery-routes";
import { WorkerEntrypoint } from "cloudflare:workers";
import {
  issueCyWebRecoveryInvitation, recoveryInvitationStatus, revokeRecoveryInvitation,
  type RecoveryIssueResult,
} from "./recovery-issuer";
import type { CyWebRecoveryRequest } from "./recovery-authority";

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

/**
 * Named private Service Binding entrypoint used ONLY by trusted CYWEB Worker.
 * CYWEB must bind to entrypoint CyInvoiceRecoveryEntrypoint explicitly.
 * No public HTTP route provides this capability. Every invocation re-checks
 * the CYInvoice Application scoped CYID SUPER_ADMIN authority and the D1 profile.
 */
export class CyInvoiceRecoveryEntrypoint extends WorkerEntrypoint<Env> {
  async issueRecoveryInvitation(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    return issueCyWebRecoveryInvitation(this.env, input);
  }

  async getRecoveryInvitationStatus(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    return recoveryInvitationStatus(this.env, input);
  }

  async revokeRecoveryInvitation(input: CyWebRecoveryRequest): Promise<RecoveryIssueResult> {
    return revokeRecoveryInvitation(this.env, input);
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    // V3 ships only Local (no Worker) and CYID Cloud. An old/incorrect
    // Built-in deployment must fail closed BEFORE handling Device or account
    // mutations; preserving its old D1 tables does not grant product support.
    try {
      if (!usesCyId(env))
        return cyIdErrorResponse(new CyIdError(409, "CYID_REQUIRED"));
      cyIdBinding(env);
    } catch (error) { return cyIdErrorResponse(error); }

    const runtimeSync = await handleRuntimeSync(request, env);
    if (runtimeSync) return runtimeSync;
    const cyIdResponse = await handleCyId(request, env);
    if (cyIdResponse) return cyIdResponse;

    const path = new URL(request.url).pathname;
    // Explicitly reject retired API contracts rather than silently routing
    // them into the generic Worker (which would return an ambiguous 404).
    if (path.startsWith("/v1/employees") || path.startsWith("/v1/employee-")
        || path.startsWith("/v1/web-auth") || path.startsWith("/v1/web-password")
        || path.startsWith("/v1/super-admin") || path.startsWith("/v1/onboarding/bootstrap")
        || path === "/v1/bootstrap")
      return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
    const recoveryRoute = request.method === "POST"
      && (path === "/v1/device-recovery/preview" || path === "/v1/device-recovery/claim");
    const needsEmployee = request.method === "POST" && [
      "/v1/device-recovery/preview", "/v1/device-recovery/claim",
      "/v1/devices/revoke", "/v1/devices/rename", "/v1/device-pairings/authorization-email",
      "/v1/device-pairings", "/v1/device-invitations",
      "/v1/device-invitations/claim", "/v1/device-invitations/revoke", "/v1/device-invitations/preview",
      "/v1/work-items/create", "/v1/work-items/get", "/v1/work-items/transition",
      "/v1/allowance-applications/create", "/v1/allowance-applications/get",
      "/v1/allowance-applications/list", "/v1/allowance-applications/review-preview",
      "/v1/company-settings/get", "/v1/company-settings/update",
    ].includes(path);
    try {
      if (usesCyId(env)) {
        cyIdBinding(env);
        if (path === "/v1/bootstrap") return cyIdErrorResponse(new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH"));
        if (needsEmployee) {
          if (!recoveryRoute
              && !["/v1/device-pairings/claim", "/v1/device-invitations/claim", "/v1/device-invitations/preview"].includes(path))
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
    const disasterRecoveryResponse = await handleRecoveryRoutes(request, env);
    if (disasterRecoveryResponse) return disasterRecoveryResponse;

    const deviceSelfStatusResponse = await handleDeviceSelfStatus(request, env);
    if (deviceSelfStatusResponse) return deviceSelfStatusResponse;

    const companySettingsResponse = await handleCompanySettings(request, env);
    if (companySettingsResponse) return companySettingsResponse;

    const allowanceApplicationResponse = await handleAllowanceApplications(request, env);
    if (allowanceApplicationResponse) return allowanceApplicationResponse;

    const workItemResponse = await handleWorkItems(request, env);
    if (workItemResponse) return workItemResponse;

    const deviceLifecycleResponse = await handleDeviceLifecycle(request, env);
    if (deviceLifecycleResponse) return deviceLifecycleResponse;

    return baseWorker.fetch(request, env);
}
