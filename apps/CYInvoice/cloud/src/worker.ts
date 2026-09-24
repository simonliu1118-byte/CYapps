import baseWorker from "./index";

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
  WEB_LOGIN_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_LOGIN_IP_RATE_LIMIT: RateLimit;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceRow = {
  device_id: string;
  workspace_id: string;
};

type EmployeeRow = {
  employee_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  role: string;
  enabled: number;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.3";

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

async function sha256Hex(value: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, "0")).join("");
}

function bearerToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  const token = match?.[1]?.trim().toLowerCase() ?? "";
  return /^cydev_[0-9a-f]{64}$/.test(token) ? token : null;
}

async function authenticateDevice(request: Request, env: Env): Promise<DeviceRow | null> {
  const token = bearerToken(request);
  if (!token) return null;
  const tokenHash = await sha256Hex(token);
  return env.DB.prepare(
    `SELECT d.device_id, d.workspace_id
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<DeviceRow>();
}

function employeeJson(row: EmployeeRow): Record<string, JsonValue> {
  return {
    employeeId: row.employee_id,
    employeeNo: row.employee_no,
    name: row.name,
    email: row.email_normalized,
    role: row.role,
    enabled: row.enabled === 1,
  };
}

async function listEmployees(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) {
    return json(env, requestId, 401, {
      error: { code: "UNAUTHORIZED", message: "Device authentication failed." },
    });
  }

  const result = await env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, role, enabled
       FROM cloud_employees
      WHERE workspace_id = ?1
      ORDER BY CASE role WHEN 'SUPER_ADMIN' THEN 0 WHEN 'ADMIN' THEN 1 ELSE 2 END,
               employee_no`
  ).bind(device.workspace_id).all<EmployeeRow>();

  return json(env, requestId, 200, {
    employees: (result.results ?? []).map(employeeJson),
  });
}

function retiredReconciliation(env: Env, requestId: string): Response {
  return json(env, requestId, 410, {
    error: {
      code: "LEGACY_EMPLOYEE_RECONCILIATION_RETIRED",
      message: "Single-account reconciliation has been retired. Use the whole-device Employee Transition flow.",
    },
  });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const requestId = requestIdFrom(request);

    if (request.method === "POST" && url.pathname === "/v1/employees/reconcile-local") {
      const device = await authenticateDevice(request, env);
      if (!device) {
        return json(env, requestId, 401, {
          error: { code: "UNAUTHORIZED", message: "Device authentication failed." },
        });
      }
      return retiredReconciliation(env, requestId);
    }

    if (request.method === "GET" && url.pathname === "/v1/employees")
      return listEmployees(request, env, requestId);

    return baseWorker.fetch(request, env);
  },
} satisfies ExportedHandler<Env>;
