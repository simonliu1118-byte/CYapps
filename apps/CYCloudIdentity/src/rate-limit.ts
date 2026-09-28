import { sha256Hex } from "./crypto";
import { json } from "./http";
import type { Env } from "./types";

function normalizedPart(value: unknown, max: number): string {
  return typeof value === "string" ? value.trim().slice(0, max).toUpperCase() : "";
}

export async function enforceLoginRateLimit(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response | null> {
  if (!env.LOGIN_RATE_LIMITER) {
    return json(env, requestId, 503, {
      error: {
        code: "AUTH_RATE_LIMITER_UNAVAILABLE",
        message: "Authentication protection is not configured.",
      },
    });
  }

  let body: Record<string, unknown> = {};
  try {
    const parsed: unknown = await request.clone().json();
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      body = parsed as Record<string, unknown>;
    }
  } catch {
    // Malformed requests will be rejected by the login handler itself. They
    // share one bounded limiter key rather than forcing a D1/credential lookup.
  }

  const application = normalizedPart(body.applicationId, 64);
  const workspace = normalizedPart(body.workspaceId, 80);
  const employeeNo = normalizedPart(body.employeeNo, 16);
  const logicalKey = application && workspace && employeeNo
    ? `login:${application}:${workspace}:${employeeNo}`
    : "login:malformed";
  const key = await sha256Hex(logicalKey);
  const result = await env.LOGIN_RATE_LIMITER.limit({ key });
  if (result.success) return null;

  return json(env, requestId, 429, {
    error: {
      code: "AUTH_RATE_LIMITED",
      message: "Too many authentication attempts. Try again later.",
    },
  }, {
    "retry-after": "60",
  });
}
