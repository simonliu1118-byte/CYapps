import type { Env, JsonValue } from "./types";

const SERVICE_NAME = "cycloud-identity";
const SERVICE_VERSION = "0.1.7";

export function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(supplied)) return supplied;
  return crypto.randomUUID();
}

export function json(
  env: Env,
  requestId: string,
  status: number,
  body: Record<string, JsonValue>,
  headers?: HeadersInit,
): Response {
  return new Response(JSON.stringify({
    ok: status >= 200 && status < 300,
    service: SERVICE_NAME,
    serviceVersion: SERVICE_VERSION,
    apiVersion: env.API_VERSION ?? "v1",
    environment: env.APP_ENV ?? "unknown",
    requestId,
    timestamp: new Date().toISOString(),
    ...body,
  }), {
    status,
    headers: {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8",
      "x-content-type-options": "nosniff",
      "x-frame-options": "DENY",
      "referrer-policy": "no-referrer",
      "x-request-id": requestId,
      ...headers,
    },
  });
}

export async function readJsonObject(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    return value as Record<string, unknown>;
  } catch {
    return null;
  }
}
