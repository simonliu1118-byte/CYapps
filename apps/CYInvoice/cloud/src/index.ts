interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.1.0";
const MAX_REQUEST_ID_LENGTH = 128;

function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= MAX_REQUEST_ID_LENGTH && /^[A-Za-z0-9._:-]+$/.test(supplied)) {
    return supplied;
  }
  return crypto.randomUUID();
}

function json(env: Env, requestId: string, status: number, body: Record<string, JsonValue>, extraHeaders?: HeadersInit): Response {
  const headers = new Headers(extraHeaders);
  headers.set("cache-control", "no-store");
  headers.set("content-type", "application/json; charset=utf-8");
  headers.set("x-content-type-options", "nosniff");
  headers.set("x-request-id", requestId);

  return new Response(
    JSON.stringify({
      ok: status >= 200 && status < 300,
      service: SERVICE_NAME,
      cloudVersion: CLOUD_VERSION,
      apiVersion: env.API_VERSION,
      environment: env.APP_ENV,
      requestId,
      timestamp: new Date().toISOString(),
      ...body,
    }),
    { status, headers }
  );
}

function methodNotAllowed(env: Env, requestId: string): Response {
  return json(
    env,
    requestId,
    405,
    {
      error: {
        code: "METHOD_NOT_ALLOWED",
        message: "Method not allowed."
      }
    },
    { allow: "GET" }
  );
}

async function databaseHealth(env: Env, requestId: string): Promise<Response> {
  try {
    // Verify both foundation tables without exposing row counts or business data.
    await env.DB.prepare("SELECT 1 AS ok FROM workspaces LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM devices LIMIT 1").first();

    return json(env, requestId, 200, {
      database: "ok",
      schemaVersion: env.SCHEMA_VERSION
    });
  } catch (error) {
    console.error("database_health_failed", {
      requestId,
      error: error instanceof Error ? error.message : "unknown_error"
    });

    return json(env, requestId, 503, {
      database: "unavailable",
      schemaVersion: env.SCHEMA_VERSION,
      error: {
        code: "DATABASE_UNAVAILABLE",
        message: "Database health check failed."
      }
    });
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const requestId = requestIdFrom(request);
    const url = new URL(request.url);

    if (request.method !== "GET") {
      return methodNotAllowed(env, requestId);
    }

    switch (url.pathname) {
      case "/":
      case "/health":
      case "/v1/health":
        return json(env, requestId, 200, {
          status: "ok",
          schemaVersion: env.SCHEMA_VERSION
        });

      case "/v1/health/db":
        return databaseHealth(env, requestId);

      case "/v1/version":
        return json(env, requestId, 200, {
          schemaVersion: env.SCHEMA_VERSION
        });

      default:
        return json(env, requestId, 404, {
          error: {
            code: "NOT_FOUND",
            message: "Route not found."
          }
        });
    }
  }
} satisfies ExportedHandler<Env>;
