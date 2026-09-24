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

type ConflictRow = {
  transition_item_id: string;
  device_id: string;
  device_display_name: string;
  local_employee_no: string;
  local_name: string;
  local_email_normalized: string;
  local_role: string;
  local_enabled: number;
  match_kind: string;
  employee_no_match_id: string | null;
  email_match_id: string | null;
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
    `SELECT d.device_id, d.workspace_id, d.employee_authority_state
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

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizePassword(value: unknown): string | null {
  if (typeof value !== "string" || value.length < 1 || value.length > 200) return null;
  return value;
}

function normalizeIdentifier(value: unknown, prefix: string): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (normalized.length < prefix.length + 1 || normalized.length > 120 || !normalized.startsWith(prefix)) return null;
  return normalized;
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

async function verifyPassword(password: string, verifier: string): Promise<boolean> {
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
    ["deriveBits"]);
  const saltBuffer = Uint8Array.from(salt).buffer;
  const actual = new Uint8Array(await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: saltBuffer, iterations },
    key,
    expected.length * 8));
  let difference = 0;
  for (let index = 0; index < expected.length; index += 1)
    difference |= actual[index] ^ expected[index];
  return difference === 0;
}

async function requireSuperAdmin(
  env: Env,
  device: DeviceRow,
  employeeNo: string,
  password: string,
): Promise<EmployeeRow | null> {
  if (device.employee_authority_state !== "cloud") return null;
  const employee = await env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_algorithm,
            credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2
      LIMIT 1`
  ).bind(device.workspace_id, employeeNo).first<EmployeeRow>();
  if (!employee || employee.role !== "SUPER_ADMIN" || employee.enabled !== 1
      || employee.credential_algorithm !== "pbkdf2-sha256" || !employee.credential_verifier)
    return null;
  return await verifyPassword(password, employee.credential_verifier) ? employee : null;
}

function employeeJson(row: EmployeeRow | null): Record<string, JsonValue> | null {
  if (!row) return null;
  return {
    employeeId: row.employee_id,
    employeeNo: row.employee_no,
    name: row.name,
    email: row.email_normalized,
    emailVerified: Boolean(row.email_verified_at),
    role: row.role,
    enabled: row.enabled === 1,
    credentialReady: Boolean(row.credential_verifier),
    credentialVersion: Number(row.credential_version),
    revision: Number(row.revision),
  };
}

async function employeeById(env: Env, workspaceId: string, employeeId: string | null): Promise<EmployeeRow | null> {
  if (!employeeId) return null;
  return env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_algorithm,
            credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_id = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeId).first<EmployeeRow>();
}

async function conflictRows(env: Env, workspaceId: string): Promise<ConflictRow[]> {
  const rows = await env.DB.prepare(
    `SELECT i.transition_item_id, i.device_id, d.display_name AS device_display_name,
            i.local_employee_no, i.local_name, i.local_email_normalized,
            i.local_role, i.local_enabled, i.match_kind,
            i.employee_no_match_id, i.email_match_id
       FROM employee_transition_items i
       JOIN devices d ON d.device_id = i.device_id
      WHERE i.workspace_id = ?1 AND i.state = 'conflict'
      ORDER BY i.created_at, i.device_id, i.local_employee_no`
  ).bind(workspaceId).all<ConflictRow>();
  return rows.results ?? [];
}

async function conflictJson(env: Env, workspaceId: string, row: ConflictRow): Promise<Record<string, JsonValue>> {
  const [employeeNoMatch, emailMatch] = await Promise.all([
    employeeById(env, workspaceId, row.employee_no_match_id),
    employeeById(env, workspaceId, row.email_match_id),
  ]);
  return {
    transitionItemId: row.transition_item_id,
    sourceDeviceId: row.device_id,
    sourceDeviceName: row.device_display_name,
    local: {
      employeeNo: row.local_employee_no,
      name: row.local_name,
      email: row.local_email_normalized,
      role: row.local_role,
      enabled: row.local_enabled === 1,
    },
    matchKind: row.match_kind,
    employeeNoMatch: employeeJson(employeeNoMatch),
    emailMatch: employeeJson(emailMatch),
  };
}

async function countConflicts(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  const row = await env.DB.prepare(
    "SELECT COUNT(*) AS count FROM employee_transition_items WHERE workspace_id = ?1 AND state = 'conflict'"
  ).bind(device.workspace_id).first<{ count: number }>();
  return json(env, requestId, 200, { conflictCount: Number(row?.count ?? 0) });
}

async function listConflicts(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  const body = await readJsonObject(request);
  const employeeNo = normalizeEmployeeNo(body?.actorEmployeeNo);
  const password = normalizePassword(body?.actorPassword);
  if (!employeeNo || !password)
    return json(env, requestId, 400, { error: { code: "INVALID_ADMIN_CREDENTIALS", message: "SUPER_ADMIN credentials are required." } });
  const actor = await requireSuperAdmin(env, device, employeeNo, password);
  if (!actor)
    return json(env, requestId, 403, { error: { code: "SUPER_ADMIN_REQUIRED", message: "Current Workspace SUPER_ADMIN authentication failed." } });

  const rows = await conflictRows(env, device.workspace_id);
  const items: JsonValue[] = [];
  for (const row of rows) items.push(await conflictJson(env, device.workspace_id, row));
  return json(env, requestId, 200, { conflictCount: rows.length, conflicts: items });
}

async function resolveConflict(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) return json(env, requestId, 401, { error: { code: "UNAUTHORIZED", message: "Device authentication failed." } });
  const body = await readJsonObject(request);
  const employeeNo = normalizeEmployeeNo(body?.actorEmployeeNo);
  const password = normalizePassword(body?.actorPassword);
  const transitionItemId = normalizeIdentifier(body?.transitionItemId, "eti_");
  const targetEmployeeId = normalizeIdentifier(body?.targetEmployeeId, "emp_");
  if (!employeeNo || !password || !transitionItemId || !targetEmployeeId)
    return json(env, requestId, 400, { error: { code: "INVALID_CONFLICT_RESOLUTION", message: "Conflict resolution request is invalid." } });

  const actor = await requireSuperAdmin(env, device, employeeNo, password);
  if (!actor)
    return json(env, requestId, 403, { error: { code: "SUPER_ADMIN_REQUIRED", message: "Current Workspace SUPER_ADMIN authentication failed." } });

  const conflict = await env.DB.prepare(
    `SELECT i.transition_item_id, i.device_id, d.display_name AS device_display_name,
            i.local_employee_no, i.local_name, i.local_email_normalized,
            i.local_role, i.local_enabled, i.match_kind,
            i.employee_no_match_id, i.email_match_id
       FROM employee_transition_items i
       JOIN devices d ON d.device_id = i.device_id
      WHERE i.workspace_id = ?1 AND i.transition_item_id = ?2 AND i.state = 'conflict'
      LIMIT 1`
  ).bind(device.workspace_id, transitionItemId).first<ConflictRow>();
  if (!conflict)
    return json(env, requestId, 404, { error: { code: "TRANSITION_CONFLICT_NOT_FOUND", message: "Transition conflict was not found or is already resolved." } });

  const allowedTargets = new Set([conflict.employee_no_match_id, conflict.email_match_id].filter((value): value is string => Boolean(value)));
  if (!allowedTargets.has(targetEmployeeId))
    return json(env, requestId, 409, { error: { code: "TRANSITION_TARGET_NOT_CANDIDATE", message: "Selected Cloud Employee is not one of the conflicting identity candidates." } });

  const target = await employeeById(env, device.workspace_id, targetEmployeeId);
  if (!target || !target.email_verified_at || !target.credential_verifier || target.credential_algorithm !== "pbkdf2-sha256" || target.credential_version < 1)
    return json(env, requestId, 409, { error: { code: "TRANSITION_TARGET_NOT_READY", message: "Selected Cloud Employee is not ready for offline authority." } });

  const localLink = await env.DB.prepare(
    "SELECT employee_id FROM device_employee_links WHERE device_id = ?1 AND local_employee_no = ?2 LIMIT 1"
  ).bind(conflict.device_id, conflict.local_employee_no).first<{ employee_id: string }>();
  if (localLink && localLink.employee_id !== targetEmployeeId)
    return json(env, requestId, 409, { error: { code: "TRANSITION_LOCAL_LINK_CONFLICT", message: "This Local Employee is already linked to another Cloud Employee." } });

  const employeeLink = await env.DB.prepare(
    "SELECT local_employee_no FROM device_employee_links WHERE device_id = ?1 AND employee_id = ?2 LIMIT 1"
  ).bind(conflict.device_id, targetEmployeeId).first<{ local_employee_no: string }>();
  if (employeeLink && employeeLink.local_employee_no !== conflict.local_employee_no)
    return json(env, requestId, 409, { error: { code: "TRANSITION_CLOUD_LINK_CONFLICT", message: "This Cloud Employee is already linked to another Local Employee on the Device." } });

  const now = new Date().toISOString();
  if (!localLink) {
    await env.DB.prepare(
      "INSERT INTO device_employee_links (device_id, employee_id, local_employee_no, linked_at) VALUES (?1, ?2, ?3, ?4)"
    ).bind(conflict.device_id, targetEmployeeId, conflict.local_employee_no, now).run();
  }
  const updated = await env.DB.prepare(
    `UPDATE employee_transition_items
        SET matched_employee_id = ?1, state = 'ready', resolved_at = ?2, updated_at = ?2
      WHERE transition_item_id = ?3 AND workspace_id = ?4 AND state = 'conflict'`
  ).bind(targetEmployeeId, now, transitionItemId, device.workspace_id).run();
  if (!updated.success)
    return json(env, requestId, 503, { error: { code: "TRANSITION_CONFLICT_RESOLUTION_FAILED", message: "Conflict resolution could not be committed." } });

  const refreshed = await env.DB.prepare(
    `SELECT i.transition_item_id, i.device_id, d.display_name AS device_display_name,
            i.local_employee_no, i.local_name, i.local_email_normalized,
            i.local_role, i.local_enabled, i.match_kind,
            i.employee_no_match_id, i.email_match_id
       FROM employee_transition_items i
       JOIN devices d ON d.device_id = i.device_id
      WHERE i.transition_item_id = ?1 LIMIT 1`
  ).bind(transitionItemId).first<ConflictRow>();

  return json(env, requestId, 200, {
    resolved: true,
    transitionItemId,
    resolvedByEmployeeId: actor.employee_id,
    targetEmployee: employeeJson(target),
    conflict: refreshed ? await conflictJson(env, device.workspace_id, refreshed) : null,
  });
}

export async function handleEmployeeTransitionConflicts(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "GET" && url.pathname === "/v1/employee-transition/conflicts/count")
      return await countConflicts(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/conflicts/list")
      return await listConflicts(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/employee-transition/conflicts/resolve")
      return await resolveConflict(request, env, requestId);
    return null;
  } catch (error) {
    console.error("employee_transition_conflict_request_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 500, {
      error: { code: "EMPLOYEE_TRANSITION_CONFLICT_REQUEST_FAILED", message: "Employee transition conflict request could not be completed." },
    });
  }
}
