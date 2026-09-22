interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceRow = {
  device_id: string;
  workspace_id: string;
  employee_authority_state: string;
  employee_transition_snapshot_hash: string | null;
  employee_transition_completed_at: string | null;
};

type EmployeeRow = {
  employee_id: string;
  employee_no: string;
  name: string;
  email_normalized: string;
  email_verified_at: string | null;
  role: string;
  enabled: number;
  credential_verifier: string | null;
  credential_algorithm: string | null;
  credential_version: number;
  revision: number;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.1";

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
    `SELECT d.device_id, d.workspace_id, d.employee_authority_state,
            d.employee_transition_snapshot_hash, d.employee_transition_completed_at
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1 AND d.status = 'active' AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<DeviceRow>();
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

function normalizedSnapshotHash(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^[0-9a-f]{64}$/.test(normalized) ? normalized : null;
}

async function workspaceRevision(env: Env, workspaceId: string): Promise<number> {
  const row = await env.DB.prepare(
    "SELECT employee_revision FROM workspaces WHERE workspace_id = ?1 LIMIT 1"
  ).bind(workspaceId).first<{ employee_revision: number }>();
  return Number(row?.employee_revision ?? 0);
}

async function readiness(env: Env, device: DeviceRow): Promise<{ total: number; unresolved: number; invalidCentral: number }> {
  const transition = await env.DB.prepare(
    `SELECT COUNT(*) AS total,
            SUM(CASE WHEN state = 'ready' AND matched_employee_id IS NOT NULL THEN 0 ELSE 1 END) AS unresolved
       FROM employee_transition_items
      WHERE device_id = ?1`
  ).bind(device.device_id).first<{ total: number; unresolved: number | null }>();

  const invalid = await env.DB.prepare(
    `SELECT COUNT(*) AS count
       FROM cloud_employees
      WHERE workspace_id = ?1
        AND (email_verified_at IS NULL
             OR credential_verifier IS NULL
             OR credential_algorithm <> 'pbkdf2-sha256'
             OR credential_version < 1)`
  ).bind(device.workspace_id).first<{ count: number }>();

  return {
    total: Number(transition?.total ?? 0),
    unresolved: Number(transition?.unresolved ?? 0),
    invalidCentral: Number(invalid?.count ?? 0),
  };
}

async function authorityStatus(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  const state = await readiness(env, device);
  return json(env, requestId, 200, {
    authority: {
      deviceId: device.device_id,
      workspaceId: device.workspace_id,
      state: device.employee_authority_state,
      transitionSnapshotHash: device.employee_transition_snapshot_hash,
      transitionItemCount: state.total,
      unresolvedCount: state.unresolved,
      invalidCentralEmployeeCount: state.invalidCentral,
      readyForCutover: device.employee_authority_state === "transitioning"
        && state.total > 0 && state.unresolved === 0 && state.invalidCentral === 0,
      completedAt: device.employee_transition_completed_at,
      workspaceRevision: await workspaceRevision(env, device.workspace_id),
    },
  });
}

async function employeeSnapshot(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });

  const state = await readiness(env, device);
  if (device.employee_authority_state === "transitioning"
      && (state.total === 0 || state.unresolved !== 0 || state.invalidCentral !== 0)) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_AUTHORITY_NOT_READY", message: "Employee transition must be fully resolved before a credential cache snapshot is issued." },
    });
  }

  const rows = await env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_algorithm,
            credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1
      ORDER BY CASE role WHEN 'SUPER_ADMIN' THEN 0 WHEN 'ADMIN' THEN 1 ELSE 2 END, employee_no`
  ).bind(device.workspace_id).all<EmployeeRow>();
  const employees = rows.results ?? [];
  if (employees.length === 0)
    return json(env, requestId, 409, { error: { code: "EMPLOYEE_AUTHORITY_NOT_READY", message: "Workspace has no central Employees." } });

  if (employees.some(employee => !employee.email_verified_at
      || !employee.credential_verifier
      || employee.credential_algorithm !== "pbkdf2-sha256"
      || employee.credential_version < 1)) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_CREDENTIAL_CACHE_NOT_READY", message: "One or more central Employee credentials are not ready for offline authorization." },
    });
  }

  return json(env, requestId, 200, {
    employeeSnapshot: {
      workspaceId: device.workspace_id,
      workspaceRevision: await workspaceRevision(env, device.workspace_id),
      employees: employees.map(employee => ({
        employeeId: employee.employee_id,
        employeeNo: employee.employee_no,
        name: employee.name,
        email: employee.email_normalized,
        role: employee.role,
        enabled: employee.enabled === 1,
        emailVerified: true,
        credentialVerifier: employee.credential_verifier!,
        credentialAlgorithm: employee.credential_algorithm!,
        credentialVersion: employee.credential_version,
        revision: employee.revision,
      })),
    },
  });
}

async function cutover(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });

  if (device.employee_authority_state === "cloud") return authorityStatus(request, env, requestId);

  const body = await readJsonObject(request);
  if (!body) return json(env, requestId, 400, { error: { code: "INVALID_JSON", message: "A JSON object is required." } });
  const suppliedSnapshot = normalizedSnapshotHash(body.snapshotHash);
  if (!suppliedSnapshot || suppliedSnapshot !== device.employee_transition_snapshot_hash) {
    return json(env, requestId, 409, {
      error: { code: "EMPLOYEE_TRANSITION_SNAPSHOT_CHANGED", message: "Run transition inspection again before cutover." },
    });
  }

  const state = await readiness(env, device);
  if (state.total === 0 || state.unresolved !== 0 || state.invalidCentral !== 0) {
    return json(env, requestId, 409, {
      error: {
        code: "EMPLOYEE_AUTHORITY_NOT_READY",
        message: "All transition items and central credentials must be ready before Cloud Employee authority cutover."
      },
    });
  }

  const superAdmin = await env.DB.prepare(
    `SELECT COUNT(*) AS count FROM cloud_employees
      WHERE workspace_id = ?1 AND role = 'SUPER_ADMIN' AND enabled = 1`
  ).bind(device.workspace_id).first<{ count: number }>();
  if (Number(superAdmin?.count ?? 0) !== 1) {
    return json(env, requestId, 409, {
      error: { code: "SUPER_ADMIN_INVARIANT_FAILED", message: "Workspace must have exactly one enabled SUPER_ADMIN before cutover." },
    });
  }

  const now = new Date().toISOString();
  const update = await env.DB.prepare(
    `UPDATE devices
        SET employee_authority_state = 'cloud', employee_transition_completed_at = ?1, updated_at = ?1
      WHERE device_id = ?2 AND employee_authority_state = 'transitioning'
        AND employee_transition_snapshot_hash = ?3`
  ).bind(now, device.device_id, suppliedSnapshot).run();

  if (!update.success)
    return json(env, requestId, 503, { error: { code: "EMPLOYEE_AUTHORITY_CUTOVER_FAILED", message: "Employee authority cutover could not be committed." } });

  return json(env, requestId, 200, {
    authority: {
      deviceId: device.device_id,
      workspaceId: device.workspace_id,
      state: "cloud",
      transitionSnapshotHash: suppliedSnapshot,
      transitionItemCount: state.total,
      unresolvedCount: 0,
      invalidCentralEmployeeCount: 0,
      readyForCutover: false,
      completedAt: now,
      workspaceRevision: await workspaceRevision(env, device.workspace_id),
    },
  });
}

export async function handleEmployeeAuthority(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "GET" && url.pathname === "/v1/employee-authority/status")
      return await authorityStatus(request, env, requestId);
    if (request.method === "GET" && url.pathname === "/v1/employee-authority/snapshot")
      return await employeeSnapshot(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-authority/cutover")
      return await cutover(request, env, requestId);
    return null;
  } catch (error) {
    console.error("employee_authority_request_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, { error: { code: "EMPLOYEE_AUTHORITY_REQUEST_FAILED", message: "Employee authority request could not be completed." } });
  }
}
