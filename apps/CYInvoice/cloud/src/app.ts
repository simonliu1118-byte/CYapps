import baseWorker from "./worker";
import { handleEmployeeTransition } from "./employee-transition";
import { handleEmployeeTransitionActions } from "./employee-transition-actions";
import { handleEmployeeTransitionConflicts } from "./employee-transition-conflicts";
import { handleEmployeeAuthority } from "./employee-authority";
import { handleEmployeeManagement } from "./employee-management";
import { handleEmployeeAccountOperations } from "./employee-account-operations";
import { handleSuperAdminTransfer } from "./super-admin-transfer";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  BOOTSTRAP_KEY?: string;
  OTP_PEPPER?: string;
  EMAIL_PROVIDER?: string;
  BREVO_API_KEY?: string;
  RESEND_API_KEY?: string;
  EMAIL_FROM?: string;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
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
  },
};
