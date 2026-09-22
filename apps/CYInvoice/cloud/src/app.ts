import baseWorker from "./worker";
import { handleEmployeeTransition } from "./employee-transition";
import { handleEmployeeTransitionActions } from "./employee-transition-actions";
import { handleEmployeeAuthority } from "./employee-authority";

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
    const transitionResponse = await handleEmployeeTransition(request, env);
    if (transitionResponse) return transitionResponse;

    const transitionActionResponse = await handleEmployeeTransitionActions(request, env);
    if (transitionActionResponse) return transitionActionResponse;

    const authorityResponse = await handleEmployeeAuthority(request, env);
    if (authorityResponse) return authorityResponse;

    return baseWorker.fetch(request, env);
  },
};
