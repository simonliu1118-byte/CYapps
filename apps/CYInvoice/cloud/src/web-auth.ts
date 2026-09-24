interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
  WEB_LOGIN_EMPLOYEE_RATE_LIMIT: RateLimit;
  WEB_LOGIN_IP_RATE_LIMIT: RateLimit;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type EmployeeRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  role: string;
  enabled: number;
  credential_verifier: string | null;
  credential_algorithm: string | null;
  credential_version: number;
  revision: number;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.3";
const APPLICATION_POLICIES: Record<string, ReadonlySet<string>> = {
  CYAccountingWeb: new Set(["SUPER_ADMIN", "ADMIN"]),
};

function requestIdFrom(request: Request): string {
  const supplied = request.headers.get("x-request-id")?.trim();
  if (supplied && supplied.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(supplied)) return supplied;
  return crypto.randomUUID();
}

function json(env: Env, requestId: string, status: number, body: Record<string, JsonValue>, headers?: HeadersInit): Response {
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
      ...headers,
    },
  });
}

async function readJsonObject(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    return value as Record<string, unknown>;
  } catch {
    return null;
  }
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizePassword(value: unknown): string | null {
  if (typeof value !== "string" || value.length < 1 || value.length > 200) return null;
  return value;
}

function normalizeApplication(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return Object.prototype.hasOwnProperty.call(APPLICATION_POLICIES, normalized) ? normalized : null;
}

function fromHex(value: string): Uint8Array | null {
  if (value.length === 0 || value.length % 2 !== 0 || !/^[0-9a-f]+$/i.test(value)) return null;
  const bytes = new Uint8Array(value.length / 2);
  for (let index = 0; index < bytes.length; index += 1) {
    const parsed = Number.parseInt(value.slice(index * 2, index * 2 + 2), 16);
    if (!Number.isFinite(parsed)) return null;
    bytes[index] = parsed;
  }
  return bytes;
}

export async function verifyPassword(password: string, verifier: string): Promise<boolean> {
  const parts = verifier.split("$");
  if (parts.length !== 4 || parts[0] !== "pbkdf2-sha256") return false;
  const iterations = Number.parseInt(parts[1], 10);
  const salt = fromHex(parts[2]);
  const expected = fromHex(parts[3]);
  if (!Number.isInteger(iterations) || iterations < 100_000 || iterations > 2_000_000
      || !salt || salt.length < 16 || !expected || expected.length !== 32) return false;

  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  const actual = new Uint8Array(await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: Uint8Array.from(salt).buffer, iterations },
    key,
    expected.length * 8,
  ));
  let difference = 0;
  for (let index = 0; index < expected.length; index += 1) difference |= actual[index] ^ expected[index];
  return difference === 0;
}

async function activeWorkspace(env: Env): Promise<string | null> {
  const result = await env.DB.prepare(
    `SELECT workspace_id
       FROM workspaces
      WHERE status = 'active'
      ORDER BY created_at, workspace_id
      LIMIT 2`
  ).all<{ workspace_id: string }>();
  const rows = result.results || [];
  return rows.length === 1 ? rows[0].workspace_id : null;
}

async function employeeAuthorityReady(env: Env, workspaceId: string): Promise<boolean> {
  const row = await env.DB.prepare(
    `SELECT 1 AS ready
       FROM devices
      WHERE workspace_id = ?1
        AND status = 'active'
        AND employee_authority_state = 'cloud'
      LIMIT 1`
  ).bind(workspaceId).first<{ ready: number }>();
  return Boolean(row);
}

async function employeeByNo(env: Env, workspaceId: string, employeeNo: string): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT employee_id, workspace_id, employee_no, name, role, enabled,
            credential_verifier, credential_algorithm, credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRow>();
}

async function rateLimitLogin(request: Request, env: Env, application: string, employeeNo: string): Promise<boolean> {
  const employeeResult = await env.WEB_LOGIN_EMPLOYEE_RATE_LIMIT.limit({
    key: `${application}:employee:${employeeNo}`,
  });
  if (!employeeResult.success) return false;

  const ip = request.headers.get("cf-connecting-ip")?.trim() || "unknown";
  const ipResult = await env.WEB_LOGIN_IP_RATE_LIMIT.limit({
    key: `${application}:ip:${ip}`,
  });
  return ipResult.success;
}

async function login(request: Request, env: Env, requestId: string): Promise<Response> {
  const body = await readJsonObject(request);
  const application = normalizeApplication(body?.application);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const password = normalizePassword(body?.password);
  if (!application || !employeeNo || !password) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_WEB_LOGIN_REQUEST", message: "Web login request is invalid." },
    });
  }

  if (!await rateLimitLogin(request, env, application, employeeNo)) {
    return json(env, requestId, 429, {
      error: { code: "WEB_LOGIN_RATE_LIMITED", message: "Too many login attempts. Please try again later." },
    }, { "retry-after": "60" });
  }

  const workspaceId = await activeWorkspace(env);
  if (!workspaceId) {
    return json(env, requestId, 503, {
      error: { code: "WEB_AUTH_NOT_READY", message: "Central Employee authority is not ready." },
    });
  }
  if (!await employeeAuthorityReady(env, workspaceId)) {
    return json(env, requestId, 503, {
      error: { code: "WEB_AUTH_NOT_READY", message: "Central Employee authority is not ready." },
    });
  }

  const employee = await employeeByNo(env, workspaceId, employeeNo);
  const credentialReady = Boolean(
    employee
    && employee.enabled === 1
    && employee.credential_algorithm === "pbkdf2-sha256"
    && employee.credential_verifier,
  );
  const authenticated = credentialReady
    ? await verifyPassword(password, employee!.credential_verifier!)
    : false;

  if (!employee || !authenticated) {
    return json(env, requestId, 401, {
      error: { code: "WEB_AUTHENTICATION_FAILED", message: "Employee authentication failed." },
    });
  }

  const allowedRoles = APPLICATION_POLICIES[application];
  if (!allowedRoles.has(employee.role)) {
    return json(env, requestId, 403, {
      error: { code: "APPLICATION_ACCESS_DENIED", message: "Employee does not have access to this application." },
    });
  }

  return json(env, requestId, 200, {
    application,
    employee: {
      employeeId: employee.employee_id,
      employeeNo: employee.employee_no,
      name: employee.name,
      role: employee.role,
      credentialVersion: employee.credential_version,
      revision: employee.revision,
    },
  });
}

export async function handleWebAuth(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  if (request.method !== "POST" || url.pathname !== "/v1/web-auth/login") return null;

  const requestId = requestIdFrom(request);
  try {
    return await login(request, env, requestId);
  } catch (error) {
    console.error("web_auth_failed", {
      requestId,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, {
      error: { code: "WEB_AUTH_FAILED", message: "Web authentication could not be completed." },
    });
  }
}
