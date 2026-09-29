import { createEmailSender } from "./email";
import { json } from "./http";
import { findActiveEmailOtp, issueEmailOtp } from "./otp";
import { requireIdentitySession } from "./session-auth";
import type { Env, JsonValue } from "./types";

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
}

function verificationScope(workspaceId: string, employeeId: string): string {
  return `${workspaceId}:${employeeId}`;
}

function portalUrl(env: Env): string | null {
  const configured = env.ACCOUNT_PORTAL_URL?.trim() ?? "";
  if (!configured) return null;
  try {
    const url = new URL(configured);
    if (url.protocol !== "https:" && url.protocol !== "http:") return null;
    url.searchParams.set("verifyEmail", "1");
    return url.toString();
  } catch {
    return null;
  }
}

export async function handleStartCurrentEmailVerification(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const principal = await requireIdentitySession(request, env);
  if (!principal) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }

  const employee = await env.DB.prepare(
    `SELECT email_normalized, email_verified_at, activated_at
       FROM employees
      WHERE workspace_id = ?1
        AND employee_id = ?2
      LIMIT 1`
  ).bind(principal.workspaceId, principal.employeeId).first<{
    email_normalized: string;
    email_verified_at: string | null;
    activated_at: string | null;
  }>();
  if (!employee || !employee.activated_at) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_VERIFICATION_NOT_AVAILABLE", message: "Current Email verification is not available for this account." },
    });
  }
  if (employee.email_verified_at) {
    return json(env, requestId, 409, {
      error: { code: "EMAIL_ALREADY_VERIFIED", message: "Current Email is already verified." },
    });
  }

  const scopeKey = verificationScope(principal.workspaceId, principal.employeeId);
  try {
    const existing = await findActiveEmailOtp(env, {
      purpose: "employee_email_verification",
      scopeKey,
      email: employee.email_normalized,
    });
    if (existing) {
      return json(env, requestId, 202, {
        verification: existing as unknown as JsonValue,
        reused: true,
      });
    }

    const link = portalUrl(env);
    const issued = await issueEmailOtp(env, emailSenderFrom(env), {
      purpose: "employee_email_verification",
      scopeKey,
      workspaceId: principal.workspaceId,
      email: employee.email_normalized,
      subject: "CY Identity Email 驗證碼",
      textPrefix: "請完成目前 CY Identity Email 的重新驗證。",
      ...(link ? { textSuffix: `開啟 CY Web 完成 Email 驗證：${link}` } : {}),
    });
    return json(env, requestId, 202, {
      verification: issued as unknown as JsonValue,
      reused: false,
    });
  } catch (error) {
    const code = error instanceof Error ? error.message : "EMAIL_VERIFICATION_FAILED";
    const status = code === "OTP_RESEND_COOLDOWN"
      || code === "OTP_RATE_LIMITED"
      || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
      || code === "WORKSPACE_EMAIL_DAILY_LIMIT_EXHAUSTED"
      ? 429
      : 503;
    return json(env, requestId, status, {
      error: { code, message: "Email verification code could not be issued." },
    });
  }
}
