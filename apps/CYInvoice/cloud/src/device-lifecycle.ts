import { usesCyId, cyIdDevice, CyIdError, cyIdErrorResponse, type CyIdEnv } from "./cyid";
import { recordSecurityEvent } from "./security-audit";
import { verifyPassword } from "./web-auth";

interface Env extends CyIdEnv {
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
const CLOUD_VERSION = "0.8.9";

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
  if (usesCyId(env)) await cyIdDevice(request, env);
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
  if (usesCyId(env)) {
    if (!env.cyIdContext) throw new CyIdError(401, "AUTH_REQUIRED");
    const principal = await env.cyIdContext.revalidate();
    if (principal.workspaceRole !== "SUPER_ADMIN") return null;
    return { employee_id: principal.employeeId, credential_algorithm: null, credential_verifier: null };
  }
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
  if (!target) {
    await recordSecurityEvent(env.DB, {
      workspaceId: actorDevice.workspaceId,
      type: "device_revoked",
      outcome: "denied",
      actorDeviceId: actorDevice.deviceId,
      actorEmployeeId: superAdmin.employee_id,
      targetDeviceId,
      reasonCode: "DEVICE_NOT_FOUND",
      requestId,
    });
    return errorResponse(env, requestId, 404, "DEVICE_NOT_FOUND", "Target Device was not found in this Workspace.");
  }

  if (target.status === "revoked") {
    await recordSecurityEvent(env.DB, {
      workspaceId: actorDevice.workspaceId,
      type: "device_revoked",
      outcome: "success",
      actorDeviceId: actorDevice.deviceId,
      actorEmployeeId: superAdmin.employee_id,
      targetDeviceId,
      reasonCode: "ALREADY_REVOKED",
      requestId,
    });
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
      await recordSecurityEvent(env.DB, {
        workspaceId: actorDevice.workspaceId,
        type: "device_revoked",
        outcome: "success",
        actorDeviceId: actorDevice.deviceId,
        actorEmployeeId: superAdmin.employee_id,
        targetDeviceId,
        reasonCode: "ALREADY_REVOKED",
        requestId,
      });
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

async function readManagedDevice(env: Env, deviceId: string, workspaceId: string): Promise<DeviceRow | null> {
  return env.DB.prepare(
    `SELECT device_id, display_name, status, client_version, paired_at,
            last_seen_at, created_at, revoked_at
       FROM devices WHERE device_id = ?1 AND workspace_id = ?2 LIMIT 1`
  ).bind(deviceId, workspaceId).first<DeviceRow>();
}

async function reportUsage(request: Request, env: Env, requestId: string): Promise<Response> {
  const actor = await authenticateDevice(request, env);
  if (!actor) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const body = await readJsonObject(request);
  const version = typeof body?.clientVersion === "string" ? body.clientVersion.trim() : "";
  if (!version || version.length > 64 || /[\u0000-\u001f\u007f-\u009f]/u.test(version))
    return errorResponse(env, requestId, 400, "INVALID_CLIENT_VERSION", "Client version is invalid.");
  const now = new Date().toISOString();
  const result = await env.DB.prepare(
    `UPDATE devices SET client_version = ?1, last_seen_at = ?2, updated_at = ?2
      WHERE device_id = ?3 AND workspace_id = ?4 AND status = 'active'
        AND EXISTS (SELECT 1 FROM workspaces WHERE workspace_id = ?4 AND status = 'active')`
  ).bind(version, now, actor.deviceId, actor.workspaceId).run();
  if (result.meta.changes !== 1)
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device is no longer active.");
  const device = await readManagedDevice(env, actor.deviceId, actor.workspaceId);
  if (!device || device.status !== "active")
    return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device is no longer active.");
  return json(env, requestId, 200, { device: deviceJson(device, actor.deviceId) });
}

async function renameDevice(request: Request, env: Env, requestId: string): Promise<Response> {
  const actor = await authenticateDevice(request, env);
  if (!actor) return errorResponse(env, requestId, 401, "UNAUTHORIZED", "Device authentication failed.");
  const body = await readJsonObject(request);
  const targetDeviceId = normalizedDeviceId(body?.targetDeviceId);
  const displayName = typeof body?.displayName === "string" ? body.displayName.trim() : "";
  if (!targetDeviceId)
    return errorResponse(env, requestId, 400, "INVALID_DEVICE", "Target Device ID is invalid.");
  if (!displayName || displayName.length > 120 || /[\u0000-\u001f\u007f-\u009f]/u.test(displayName))
    return errorResponse(env, requestId, 400, "INVALID_DEVICE_NAME", "Device display name is invalid.");
  const owner = await authenticateSuperAdmin(env, actor, body);
  if (!owner) {
    await recordSecurityEvent(env.DB, {
      workspaceId: actor.workspaceId, type: "device_renamed", outcome: "denied",
      actorDeviceId: actor.deviceId, targetDeviceId, reasonCode: "SUPER_ADMIN_AUTH_FAILED", requestId,
    });
    return errorResponse(env, requestId, 401, "SUPER_ADMIN_AUTH_FAILED", "Super administrator authentication failed.");
  }
  const now = new Date().toISOString();
  const results = await env.DB.batch([
    env.DB.prepare(
      `UPDATE devices SET display_name = ?1, updated_at = ?2
        WHERE device_id = ?3 AND workspace_id = ?4 AND status = 'active'
          AND EXISTS (SELECT 1 FROM devices a JOIN workspaces w ON w.workspace_id = a.workspace_id
                       WHERE a.device_id = ?5 AND a.workspace_id = ?4 AND a.status = 'active' AND w.status = 'active')
          AND (?8 = 1 OR EXISTS (SELECT 1 FROM cloud_employees WHERE employee_id = ?6 AND workspace_id = ?4
                       AND role = 'SUPER_ADMIN' AND enabled = 1 AND credential_verifier = ?7))`
    ).bind(displayName, now, targetDeviceId, actor.workspaceId, actor.deviceId,
      owner.employee_id, owner.credential_verifier, usesCyId(env) ? 1 : 0),
    env.DB.prepare(
      `INSERT INTO security_audit_events (
         event_id, workspace_id, event_type, outcome, actor_device_id,
         actor_employee_id, target_device_id, reason_code, request_id, occurred_at
       ) SELECT ?1, ?2, 'device_renamed', 'success', ?3, ?4, ?5, 'SUPER_ADMIN_RENAME', ?6, ?7
          WHERE changes() = 1`
    ).bind(`evt_${crypto.randomUUID()}`, actor.workspaceId, actor.deviceId,
      owner.employee_id, targetDeviceId, requestId, now),
  ]);
  if (results[0].meta.changes !== 1) {
    await recordSecurityEvent(env.DB, {
      workspaceId: actor.workspaceId, type: "device_renamed", outcome: "denied",
      actorDeviceId: actor.deviceId, actorEmployeeId: owner.employee_id,
      targetDeviceId, reasonCode: "DEVICE_UNAVAILABLE", requestId,
    });
    return errorResponse(env, requestId, 409, "DEVICE_UNAVAILABLE", "Device or authority changed. Refresh and try again.");
  }
  if (results[1].meta.changes !== 1) throw new Error("Device rename audit was not persisted atomically.");
  const device = await readManagedDevice(env, targetDeviceId, actor.workspaceId);
  if (!device || device.status !== "active")
    return errorResponse(env, requestId, 409, "DEVICE_UNAVAILABLE", "Device is no longer active.");
  return json(env, requestId, 200, { device: deviceJson(device, actor.deviceId) });
}

export async function handleDeviceLifecycle(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  const requestId = requestIdFrom(request);
  try {
    if (request.method === "GET" && url.pathname === "/v1/devices")
      return await listDevices(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/devices/revoke")
      return await revokeDevice(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/devices/usage")
      return await reportUsage(request, env, requestId);
    if (request.method === "POST" && url.pathname === "/v1/devices/rename")
      return await renameDevice(request, env, requestId);
    return null;
  } catch (error) {
    if (error instanceof CyIdError) return cyIdErrorResponse(error);
    console.error("device_lifecycle_request_failed", {
      requestId,
      path: url.pathname,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return errorResponse(
      env,
      requestId,
      500,
      "DEVICE_LIFECYCLE_REQUEST_FAILED",
      "Device lifecycle request could not be completed.",
    );
  }
}
