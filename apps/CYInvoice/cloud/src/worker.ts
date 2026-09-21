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
}

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

type DeviceRow = {
  device_id: string;
  workspace_id: string;
  display_name: string;
  pairing_id: string | null;
  paired_at: string | null;
  employee_onboarding_closed_at: string | null;
  recovery_email: string | null;
  recovery_email_verified_at: string | null;
};

type LocalSuperAdmin = {
  employeeNo: string;
  name: string;
  email: string;
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
const CLOUD_VERSION = "0.8.0";
const DEVICE_EMPLOYEE_IMPORT_WINDOW_MS = 30 * 60 * 1000;

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
    `SELECT d.device_id, d.workspace_id, d.display_name, d.pairing_id, d.paired_at,
            d.employee_onboarding_closed_at,
            w.recovery_email, w.recovery_email_verified_at
       FROM devices d
       JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.token_hash = ?1
        AND d.status = 'active'
        AND w.status = 'active'
      LIMIT 1`
  ).bind(tokenHash).first<DeviceRow>();
}

function normalizeEmail(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const email = value.trim().toLowerCase();
  if (email.length < 3 || email.length > 320 || /[\r\n\s]/.test(email)) return null;
  const at = email.lastIndexOf("@");
  if (at <= 0 || at >= email.length - 1 || email.indexOf("@") !== at) return null;
  const domain = email.slice(at + 1);
  if (!domain.includes(".") || domain.startsWith(".") || domain.endsWith(".")) return null;
  return email;
}

function normalizeLocalSuperAdmin(value: unknown): LocalSuperAdmin | null | "invalid" {
  if (value === null || value === undefined) return null;
  if (typeof value !== "object" || Array.isArray(value)) return "invalid";
  const raw = value as Record<string, unknown>;
  const employeeNo = typeof raw.employeeNo === "string" ? raw.employeeNo.trim() : "";
  const name = typeof raw.name === "string" ? raw.name.trim() : "";
  const email = normalizeEmail(raw.email);
  if (!/^\d{4}$/.test(employeeNo) || name.length < 1 || name.length > 120 || !email) return "invalid";
  return { employeeNo, name, email };
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

async function linkedEmployee(env: Env, deviceId: string, localEmployeeNo?: string): Promise<EmployeeRow | null> {
  const condition = localEmployeeNo
    ? "l.device_id = ?1 AND l.local_employee_no = ?2"
    : "l.device_id = ?1";
  const statement = env.DB.prepare(
    `SELECT e.employee_id, e.employee_no, e.name, e.email_normalized, e.role, e.enabled
       FROM device_employee_links l
       JOIN cloud_employees e ON e.employee_id = l.employee_id
      WHERE ${condition}
      ORDER BY CASE e.role WHEN 'SUPER_ADMIN' THEN 0 WHEN 'ADMIN' THEN 1 ELSE 2 END, e.employee_no
      LIMIT 1`
  );
  return localEmployeeNo
    ? statement.bind(deviceId, localEmployeeNo).first<EmployeeRow>()
    : statement.bind(deviceId).first<EmployeeRow>();
}

async function existingCandidateState(env: Env, deviceId: string, localEmployeeNo?: string): Promise<string | null> {
  const condition = localEmployeeNo
    ? "device_id = ?1 AND local_employee_no = ?2"
    : "device_id = ?1";
  const statement = env.DB.prepare(
    `SELECT state
       FROM employee_link_candidates
      WHERE ${condition}
      ORDER BY created_at DESC
      LIMIT 1`
  );
  const row = localEmployeeNo
    ? await statement.bind(deviceId, localEmployeeNo).first<{ state: string }>()
    : await statement.bind(deviceId).first<{ state: string }>();
  return row?.state ?? null;
}

function reconcileResponse(env: Env, requestId: string, state: string, employee?: EmployeeRow): Response {
  return json(env, requestId, 200, {
    reconciliation: {
      state,
      ...(employee ? { employee: employeeJson(employee) } : {}),
    },
  });
}

async function reconcileLocalEmployee(request: Request, env: Env, requestId: string): Promise<Response> {
  const device = await authenticateDevice(request, env);
  if (!device) {
    return json(env, requestId, 401, {
      error: { code: "UNAUTHORIZED", message: "Device authentication failed." },
    });
  }

  const body = await readJsonObject(request);
  if (!body) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_JSON", message: "A JSON object is required." },
    });
  }
  const localSuperAdmin = normalizeLocalSuperAdmin(body.localSuperAdmin);
  if (localSuperAdmin === "invalid") {
    return json(env, requestId, 400, {
      error: { code: "INVALID_LOCAL_SUPER_ADMIN", message: "Local SUPER_ADMIN information is invalid." },
    });
  }

  const localEmployeeNo = localSuperAdmin?.employeeNo;
  const alreadyLinked = await linkedEmployee(env, device.device_id, localEmployeeNo);
  if (alreadyLinked) return reconcileResponse(env, requestId, "linked", alreadyLinked);

  const priorCandidate = await existingCandidateState(env, device.device_id, localEmployeeNo);
  if (priorCandidate === "pending") return reconcileResponse(env, requestId, "requires_confirmation");

  if (device.employee_onboarding_closed_at) {
    return reconcileResponse(env, requestId, "device_only");
  }

  const employeeCount = await env.DB.prepare(
    "SELECT COUNT(*) AS count FROM cloud_employees WHERE workspace_id = ?1"
  ).bind(device.workspace_id).first<{ count: number }>();
  const count = Number(employeeCount?.count ?? 0);
  const now = new Date();
  const nowText = now.toISOString();

  if (count === 0) {
    if (device.pairing_id !== null) {
      return json(env, requestId, 409, {
        error: {
          code: "WORKSPACE_OWNER_MISSING",
          message: "Central Workspace owner has not been initialized; a paired Device cannot create it."
        }
      });
    }
    if (!localSuperAdmin) {
      return json(env, requestId, 409, {
        error: {
          code: "LOCAL_SUPER_ADMIN_REQUIRED",
          message: "The first Workspace Device must reconcile its existing Local SUPER_ADMIN."
        }
      });
    }

    const recoveryEmail = normalizeEmail(device.recovery_email);
    if (!recoveryEmail || !device.recovery_email_verified_at || recoveryEmail !== localSuperAdmin.email) {
      return json(env, requestId, 409, {
        error: {
          code: "OWNER_EMAIL_MISMATCH",
          message: "Local SUPER_ADMIN email does not match the verified Workspace recovery email."
        }
      });
    }

    const employeeId = `emp_${crypto.randomUUID()}`;
    try {
      await env.DB.batch([
        env.DB.prepare(
          `INSERT INTO cloud_employees (
              employee_id, workspace_id, employee_no, name, email_normalized,
              email_verified_at, role, enabled, source_device_id, created_at, updated_at
           ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'SUPER_ADMIN', 1, ?7, ?8, ?8)`
        ).bind(
          employeeId,
          device.workspace_id,
          localSuperAdmin.employeeNo,
          localSuperAdmin.name,
          localSuperAdmin.email,
          device.recovery_email_verified_at,
          device.device_id,
          nowText),
        env.DB.prepare(
          `INSERT INTO device_employee_links (device_id, employee_id, local_employee_no, linked_at)
           VALUES (?1, ?2, ?3, ?4)`
        ).bind(device.device_id, employeeId, localSuperAdmin.employeeNo, nowText),
        env.DB.prepare(
          "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
        ).bind(nowText, device.device_id),
      ]);
    } catch (error) {
      const recovered = await linkedEmployee(env, device.device_id, localSuperAdmin.employeeNo);
      if (recovered) return reconcileResponse(env, requestId, "linked", recovered);
      console.warn("workspace_owner_reconcile_failed", {
        requestId,
        deviceId: device.device_id,
        error: error instanceof Error ? error.message : "unknown_error",
      });
      return json(env, requestId, 409, {
        error: { code: "OWNER_RECONCILIATION_CONFLICT", message: "Workspace owner reconciliation conflicted with existing central identity." },
      });
    }

    const created = await linkedEmployee(env, device.device_id, localSuperAdmin.employeeNo);
    if (!created) throw new Error("central_owner_readback_failed");
    return reconcileResponse(env, requestId, "owner_created", created);
  }

  if (device.pairing_id === null) {
    return json(env, requestId, 409, {
      error: {
        code: "OWNER_RECONCILIATION_REQUIRED",
        message: "The first Device is not linked to the existing central Workspace owner."
      }
    });
  }

  if (device.paired_at) {
    const pairedAt = Date.parse(device.paired_at);
    if (Number.isFinite(pairedAt) && now.getTime() - pairedAt > DEVICE_EMPLOYEE_IMPORT_WINDOW_MS) {
      await env.DB.prepare(
        "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
      ).bind(nowText, device.device_id).run();
      return reconcileResponse(env, requestId, "import_window_expired");
    }
  }

  if (!localSuperAdmin) {
    await env.DB.prepare(
      "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
    ).bind(nowText, device.device_id).run();
    return reconcileResponse(env, requestId, "device_only");
  }

  const conflicts = await env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, role, enabled
       FROM cloud_employees
      WHERE workspace_id = ?1
        AND (employee_no = ?2 OR email_normalized = ?3)
      LIMIT 2`
  ).bind(device.workspace_id, localSuperAdmin.employeeNo, localSuperAdmin.email).all<EmployeeRow>();

  if ((conflicts.results?.length ?? 0) !== 0) {
    const candidateId = `cand_${crypto.randomUUID()}`;
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employee_link_candidates (
            candidate_id, workspace_id, device_id, local_employee_no,
            local_name, local_email_normalized, state, created_at
         ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'pending', ?7)
         ON CONFLICT(device_id, local_employee_no) DO NOTHING`
      ).bind(
        candidateId,
        device.workspace_id,
        device.device_id,
        localSuperAdmin.employeeNo,
        localSuperAdmin.name,
        localSuperAdmin.email,
        nowText),
      env.DB.prepare(
        "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
      ).bind(nowText, device.device_id),
    ]);
    return reconcileResponse(env, requestId, "requires_confirmation");
  }

  const employeeId = `emp_${crypto.randomUUID()}`;
  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO cloud_employees (
            employee_id, workspace_id, employee_no, name, email_normalized,
            email_verified_at, role, enabled, source_device_id, created_at, updated_at
         ) VALUES (?1, ?2, ?3, ?4, ?5, NULL, 'ADMIN', 1, ?6, ?7, ?7)`
      ).bind(
        employeeId,
        device.workspace_id,
        localSuperAdmin.employeeNo,
        localSuperAdmin.name,
        localSuperAdmin.email,
        device.device_id,
        nowText),
      env.DB.prepare(
        `INSERT INTO device_employee_links (device_id, employee_id, local_employee_no, linked_at)
         VALUES (?1, ?2, ?3, ?4)`
      ).bind(device.device_id, employeeId, localSuperAdmin.employeeNo, nowText),
      env.DB.prepare(
        "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
      ).bind(nowText, device.device_id),
    ]);
  } catch (error) {
    const recovered = await linkedEmployee(env, device.device_id, localSuperAdmin.employeeNo);
    if (recovered) return reconcileResponse(env, requestId, "linked", recovered);

    const candidateId = `cand_${crypto.randomUUID()}`;
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO employee_link_candidates (
            candidate_id, workspace_id, device_id, local_employee_no,
            local_name, local_email_normalized, state, created_at
         ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'pending', ?7)
         ON CONFLICT(device_id, local_employee_no) DO NOTHING`
      ).bind(
        candidateId,
        device.workspace_id,
        device.device_id,
        localSuperAdmin.employeeNo,
        localSuperAdmin.name,
        localSuperAdmin.email,
        nowText),
      env.DB.prepare(
        "UPDATE devices SET employee_onboarding_closed_at = ?1, updated_at = ?1 WHERE device_id = ?2 AND employee_onboarding_closed_at IS NULL"
      ).bind(nowText, device.device_id),
    ]);
    console.warn("local_super_admin_import_conflict", {
      requestId,
      deviceId: device.device_id,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return reconcileResponse(env, requestId, "requires_confirmation");
  }

  const created = await linkedEmployee(env, device.device_id, localSuperAdmin.employeeNo);
  if (!created) throw new Error("central_admin_readback_failed");
  return reconcileResponse(env, requestId, "admin_created", created);
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

async function verifyEmployeeSchemaHealth(env: Env, requestId: string): Promise<Response | null> {
  try {
    await env.DB.prepare("SELECT 1 AS ok FROM cloud_employees LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM device_employee_links LIMIT 1").first();
    await env.DB.prepare("SELECT 1 AS ok FROM employee_link_candidates LIMIT 1").first();
    return null;
  } catch (error) {
    console.error("employee_storage_health_failed", {
      requestId,
      error: error instanceof Error ? error.message : "unknown_error",
    });
    return json(env, requestId, 503, {
      storage: "unavailable",
      schemaVersion: env.SCHEMA_VERSION,
      error: { code: "STORAGE_UNAVAILABLE", message: "Backend employee storage health check failed." },
    });
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const requestId = requestIdFrom(request);
    const url = new URL(request.url);

    try {
      if (url.pathname === "/v1/employees/reconcile-local") {
        if (request.method !== "POST") {
          return json(env, requestId, 405, {
            error: { code: "METHOD_NOT_ALLOWED", message: "Method not allowed." },
          });
        }
        return reconcileLocalEmployee(request, env, requestId);
      }

      if (url.pathname === "/v1/employees") {
        if (request.method !== "GET") {
          return json(env, requestId, 405, {
            error: { code: "METHOD_NOT_ALLOWED", message: "Method not allowed." },
          });
        }
        return listEmployees(request, env, requestId);
      }

      if (url.pathname === "/v1/health" || url.pathname === "/v1/health/storage" || url.pathname === "/v1/health/db") {
        const problem = await verifyEmployeeSchemaHealth(env, requestId);
        if (problem) return problem;
      }

      return baseWorker.fetch(request, env);
    } catch (error) {
      console.error("employee_layer_request_failed", {
        requestId,
        path: url.pathname,
        error: error instanceof Error ? error.message : "unknown_error",
      });
      return json(env, requestId, 500, {
        error: { code: "INTERNAL_ERROR", message: "Request could not be completed." },
      });
    }
  },
} satisfies ExportedHandler<Env>;
