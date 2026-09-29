interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type SelfStatusRow = {
  device_id: string;
  workspace_id: string;
  status: string;
  revoked_at: string | null;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.8";

function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(supplied)) return supplied;
  return crypto.randomUUID();
}

function json(env: Env, requestId: string, status: number, body: Record<string, JsonValue>): Response {
  return new Response(JSON.stringify({
    ok: status >= 200 && status < 300,
    service: SERVICE_NAME,
    cloudVersion: CLOUD_VERSION,
    apiVersion: env.API_VERSION,
    environment: env.APP_ENV,
    requestId,
    timestamp: new Date().toISOString(),
    ...body,
  }), {
    status,
    headers: {
      "cache-control": "no-store",
      "content-type": "application/json; charset=utf-8",
      "x-content-type-options": "nosniff",
      "x-request-id": requestId,
    },
  });
}

function errorResponse(env: Env, requestId: string, status: number, code: string, message: string): Response {
  return json(env, requestId, status, { error: { code, message } });
}

function bearerToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  const token = match?.[1]?.trim().toLowerCase() ?? "";
  return /^cydev_[0-9a-f]{64}$/.test(token) ? token : null;
}

async function sha256Hex(value: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

async function selfStatus(request: Request, env: Env, requestId: string): Promise<Response> {
  const token = bearerToken(request);
  if (!token)
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device token is invalid.");

  const tokenHash = await sha256Hex(token);
  const row = await env.DB.prepare(
    `SELECT device_id, workspace_id, status, revoked_at
       FROM devices
      WHERE token_hash = ?1
      LIMIT 1`
  ).bind(tokenHash).first<SelfStatusRow>();

  if (!row)
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device token is unknown.");
  if (row.status !== "active" && row.status !== "revoked")
    return errorResponse(env, requestId, 409, "DEVICE_STATE_INVALID", "Device lifecycle state is invalid.");

  return json(env, requestId, 200, {
    deviceId: row.device_id,
    workspaceId: row.workspace_id,
    status: row.status,
    revokedAt: row.revoked_at ?? "",
  });
}

export async function handleDeviceSelfStatus(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  if (request.method !== "GET" || url.pathname !== "/v1/devices/self-status") return null;

  const requestId = requestIdFrom(request);
  try {
    return await selfStatus(request, env, requestId);
  } catch (error) {
    console.error("device_self_status_failed", {
      requestId,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return errorResponse(
      env,
      requestId,
      500,
      "DEVICE_SELF_STATUS_FAILED",
      "Device self status could not be determined.",
    );
  }
}
