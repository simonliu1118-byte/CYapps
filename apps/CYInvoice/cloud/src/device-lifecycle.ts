import { recordSecurityEvent } from "./security-audit";
import { verifyPassword } from "./web-auth";

interface Env {
  DB: D1Database;
  APP_ENV: string;
  API_VERSION: string;
  SCHEMA_VERSION: string;
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceIdentity = {
  deviceId: string;
  workspaceId: string;
  employeeAuthorityState: string;
};

type DeviceRow = {
  device_id: string;
  display_name: string;
  status: string;
  client_version: string | null;
  paired_at: string | null;
  last_seen_at: string | null;
  created_at: string;
  revoked_at: string | null;
};

type SuperAdminRow = {
  employee_id: string;
  credential_verifier: string | null;
  credential_algorithm: string | null;
};

const SERVICE_NAME = "cyinvoice-cloud";
const CLOUD_VERSION = "0.8.7";

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

function errorResponse(
  env: Env,
  requestId: string,
  status: number,
  code: string,
  message: string,
): Response {
  return json(env, requestId, status, { error: { code, message } });
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

async function authenticateDevice(request: Request, env: Env): Promise<DeviceIdentity | null> {
  const token = bearerToken(request);
  if (!token) return null;
  const tokenHash = await sha256Hex(token);
  const row = await env.DB.prepare(
    `SELECT d.device_id, d.workspace_id, d.employee_authority_state
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<{
    device_id: string;
    workspace_id: string;
    employee_authority_state: string;
  }>();
  return row ? {
    deviceId: row.device_id,
    workspaceId: row.workspace_id,
    employeeAuthorityState: row.employee_authority_state,
  } : null;
}

async function readJsonObject(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const value: unknown = await request.json();
    return value && typeof value === "object" && !Array.isArray(value)
      ? value as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

function normalizedEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizedPassword(value: unknown): string | null {
  return typeof value === "string" && value.length >= 1 && value.length <= 200 ? value : null;
}

function normalizedDeviceId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toLowerCase();
  return /^dev_[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(normalized)
    ? normalized
    : null;
}

async function authenticateSuperAdmin(
  env: Env,
  device: DeviceIdentity,
  body: Record<string, unknown> | null,
): Promise<SuperAdminRow | null> {
  if (device.employeeAuthorityState !== "cloud") return null;
  const employeeNo = normalizedEmployeeNo(body?.employeeNo);
  const password = normalizedPassword(body?.password);
  if (!employeeNo || !password) return null;

  const employee = await env.DB.prepare(
    `SELECT employee_id, credential_verifier, credential_algorithm
       FROM cloud_employees
      WHERE workspace_id = ?1
        AND employee_no = ?2
        AND role = 'SUPER_ADMIN'
        AND enabled = 1
      LIMIT 1`
  ).bind(device.workspaceId, employeeNo).first<SuperAdminRow>();
  if (!employee || employee.credential_algorithm !== "pbkdf2-sha256" || !employee.credential_verifier)
    return null;
  return await verifyPassword(password, employee.credential_verifier) ? employee : null;
}

function deviceJson(row: DeviceRow, currentDeviceId: string): Record<string, JsonValue> {
  return {
    deviceId: row.device_id,
    displayName: row.display_name,
    status: row.status,
    clientVersion: row.client_version ?? "",
    pairedAt: row.paired_at ?? "",
    lastSeenAt: row.last_seen_at ?? "",
    createdAt: row.created_at,
    revokedAt: row.revoked_at ?? "",
    current: row.device_id === currentDeviceId,
  };
}

async function listDevices(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device)
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");

  const result = await env.DB.prepare(
    `SELECT device_id, display_name, status, client_version, paired_at,
            last_seen_at, created_at, revoked_at
       FROM devices
      WHERE workspace_id = ?1
      ORDER BY CASE status WHEN 'active' THEN 0 ELSE 1 END,
               created_at DESC, device_id`
  ).bind(device.workspaceId).all<DeviceRow>();
  const devices = result.results ?? [];
  return json(env, requestId, 200, {
    currentDeviceId: device.deviceId,
    activeDeviceCount: devices.filter(row => row.status === "active").length,
    devices: devices.map(row => deviceJson(row, device.deviceId)),
  });
}

async function revokeDevice(request: Request, env: Env, requestId: string): Promise<Response> {
  const actorDevice = await authenticateDevice(request, env);
  if (!actorDevice)
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");

  const body = await readJsonObject(request);
  const targetDeviceId = normalizedDeviceId(body?.targetDeviceId);
  if (!targetDeviceId)
    return errorResponse(env, requestId, 400, "INVALID_DEVICE", "Target Device ID is invalid.");

  const superAdmin = await authenticateSuperAdmin(env, actorDevice, body);
  if (!superAdmin) {
    await recordSecurityEvent(env.DB, {
      workspaceId: actorDevice.workspaceId,
      type: "device_revoked",
      outcome: "denied",
      actorDeviceId: actorDevice.deviceId,
      targetDeviceId,
      reasonCode: "SUPER_ADMIN_AUTH_FAILED",
      requestId,
    });
    return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  }

  const target = await env.DB.prepare(
    `SELECT device_id, display_name, status, client_version, paired_at,
            last_seen_at, created_at, revoked_at
       FROM devices
      WHERE device_id = ?1 AND workspace_id = ?2
      LIMIT 1`
  ).bind(targetDeviceId, actorDevice.workspaceId).first<DeviceRow>();
  if (!target)
    return errorResponse(env, requestId, 404, "DEVICE_NOT_FOUND", "Target Device was not found in this Workspace.");

  if (target.status === "revoked") {
    return json(env, requestId, 200, {
      device: deviceJson(target, actorDevice.deviceId),
      alreadyRevoked: true,
    });
  }

  const now = new Date().toISOString();
  const reasonCode = targetDeviceId === actorDevice.deviceId ? "SELF_RETIRE" : "SUPER_ADMIN_REVOKE";
  const result = await env.DB.batch([
    env.DB.prepare(
      `UPDATE devices
          SET status = 'revoked', revoked_at = ?1, updated_at = ?1
        WHERE device_id = ?2
          AND workspace_id = ?3
          AND status = 'active'
          AND (SELECT COUNT(*) FROM devices
                 WHERE workspace_id = ?3 AND status = 'active') > 1`
    ).bind(now, targetDeviceId, actorDevice.workspaceId),
    env.DB.prepare(
      `INSERT INTO security_audit_events (
         event_id, workspace_id, event_type, outcome, actor_device_id,
         actor_employee_id, target_device_id, reason_code, request_id, occurred_at
       )
       SELECT ?1, workspace_id, 'device_revoked', 'success', ?2, ?3,
              device_id, ?4, ?5, ?6
         FROM devices
        WHERE device_id = ?7
          AND workspace_id = ?8
          AND status = 'revoked'
          AND revoked_at = ?6`
    ).bind(
      `evt_${crypto.randomUUID()}`,
      actorDevice.deviceId,
      superAdmin.employee_id,
      reasonCode,
      requestId,
      now,
      targetDeviceId,
      actorDevice.workspaceId),
  ]);

  if (result[0].meta.changes !== 1) {
    const latest = await env.DB.prepare(
      `SELECT device_id, display_name, status, client_version, paired_at,
              last_seen_at, created_at, revoked_at
         FROM devices
        WHERE device_id = ?1 AND workspace_id = ?2
        LIMIT 1`
    ).bind(targetDeviceId, actorDevice.workspaceId).first<DeviceRow>();
    if (latest?.status === "revoked") {
      return json(env, requestId, 200, {
        device: deviceJson(latest, actorDevice.deviceId),
        alreadyRevoked: true,
      });
    }

    await recordSecurityEvent(env.DB, {
      workspaceId: actorDevice.workspaceId,
      type: "device_revoked",
      outcome: "denied",
      actorDeviceId: actorDevice.deviceId,
      actorEmployeeId: superAdmin.employee_id,
      targetDeviceId,
      reasonCode: "LAST_ACTIVE_DEVICE",
      requestId,
    });
    return errorResponse(
      env,
      requestId,
      409,
      "LAST_ACTIVE_DEVICE",
      "The last active Device cannot be revoked. Join another trusted Device first.",
    );
  }

  if (result[1].meta.changes !== 1)
    throw new Error("Device revoke audit event was not persisted atomically.");

  const revoked = await env.DB.prepare(
    `SELECT device_id, display_name, status, client_version, paired_at,
            last_seen_at, created_at, revoked_at
       FROM devices
      WHERE device_id = ?1 AND workspace_id = ?2
      LIMIT 1`
  ).bind(targetDeviceId, actorDevice.workspaceId).first<DeviceRow>();
  if (!revoked || revoked.status !== "revoked")
    throw new Error("Device revoke result could not be verified.");

  return json(env, requestId, 200, {
    device: deviceJson(revoked, actorDevice.deviceId),
    alreadyRevoked: false,
  });
}

export async function handleDeviceLifecycle(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  if (request.method === "GET" && url.pathname === "/v1/devices")
    return listDevices(request, env, requestId);
  if (request.method === "POST" && url.pathname === "/v1/devices/revoke")
    return revokeDevice(request, env, requestId);
  return null;
}
