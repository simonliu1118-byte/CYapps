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
  pairing_id: string | null;
  employee_authority_state: string;
  employee_transition_snapshot_hash: string | null;
  recovery_email: string | null;
  recovery_email_verified_at: string | null;
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
  credential_version: number;
  revision: number;
};

type LocalEmployee = {
  employeeNo: string;
  name: string;
  email: string;
  role: "SUPER_ADMIN" | "ADMIN" | "EMPLOYEE";
  enabled: boolean;
};

type MatchKind =
  | "none"
  | "same_employee"
  | "employee_no_only"
  | "email_only"
  | "split"
  | "bootstrap_owner";

type TransitionState =
  | "bootstrap_owner_pending"
  | "new_email_pending"
  | "matched_existing"
  | "credential_pending"
  | "conflict"
  | "ready";

type InspectionItem = {
  local: LocalEmployee;
  suggestedCloudRole: "SUPER_ADMIN" | "ADMIN" | "EMPLOYEE";
  state: TransitionState;
  matchKind: MatchKind;
  matchedEmployee: EmployeeRow | null;
  employeeNoMatch: EmployeeRow | null;
  emailMatch: EmployeeRow | null;
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
    `SELECT d.device_id, d.workspace_id, d.pairing_id, d.employee_authority_state,
            d.employee_transition_snapshot_hash,
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

function normalizeLocalEmployee(value: unknown): LocalEmployee | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const raw = value as Record<string, unknown>;
  const employeeNo = typeof raw.employeeNo === "string" ? raw.employeeNo.trim() : "";
  const name = typeof raw.name === "string" ? raw.name.trim() : "";
  const email = normalizeEmail(raw.email);
  const role = typeof raw.role === "string" ? raw.role.trim().toUpperCase() : "";
  const enabled = raw.enabled;
  if (!/^\d{4}$/.test(employeeNo) || name.length < 1 || name.length > 120 || !email) return null;
  if (role !== "SUPER_ADMIN" && role !== "ADMIN" && role !== "EMPLOYEE") return null;
  if (typeof enabled !== "boolean") return null;
  return { employeeNo, name, email, role, enabled };
}

async function readLocalEmployees(request: Request): Promise<LocalEmployee[] | null> {
  try {
    const value: unknown = await request.json();
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    const raw = value as Record<string, unknown>;
    if (!Array.isArray(raw.localEmployees) || raw.localEmployees.length < 1 || raw.localEmployees.length > 500) return null;
    const employees: LocalEmployee[] = [];
    for (const item of raw.localEmployees) {
      const normalized = normalizeLocalEmployee(item);
      if (!normalized) return null;
      employees.push(normalized);
    }
    return employees;
  } catch {
    return null;
  }
}

async function localSnapshotHash(localEmployees: LocalEmployee[]): Promise<string> {
  const canonical = [...localEmployees]
    .sort((left, right) => left.employeeNo.localeCompare(right.employeeNo, "en"))
    .map(employee => ({
      employeeNo: employee.employeeNo,
      name: employee.name,
      email: employee.email,
      role: employee.role,
      enabled: employee.enabled,
    }));
  return sha256Hex(JSON.stringify(canonical));
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
    credentialVersion: Number(row.credential_version ?? 0),
    revision: Number(row.revision ?? 1),
  };
}

function suggestedRole(localRole: LocalEmployee["role"], bootstrapOwner: boolean): LocalEmployee["role"] {
  if (bootstrapOwner) return "SUPER_ADMIN";
  return localRole === "SUPER_ADMIN" ? "ADMIN" : localRole;
}

async function employeeByNo(env: Env, workspaceId: string, employeeNo: string): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeRow>();
}

async function employeeByEmail(env: Env, workspaceId: string, email: string): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
            role, enabled, credential_verifier, credential_version, revision
       FROM cloud_employees
      WHERE workspace_id = ?1 AND email_normalized = ?2
      LIMIT 1`
  ).bind(workspaceId, email).first<EmployeeRow>();
}

async function linkedEmployeeByLocal(
  env: Env,
  workspaceId: string,
  deviceId: string,
  localEmployeeNo: string,
): Promise<EmployeeRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id, e.employee_no, e.name, e.email_normalized, e.email_verified_at,
            e.role, e.enabled, e.credential_verifier, e.credential_version, e.revision
       FROM device_employee_links l
       JOIN cloud_employees e ON e.employee_id = l.employee_id
      WHERE l.device_id = ?1
        AND l.local_employee_no = ?2
        AND e.workspace_id = ?3
      LIMIT 1`
  ).bind(deviceId, localEmployeeNo, workspaceId).first<EmployeeRow>();
}

async function upsertTransitionItem(
  env: Env,
  device: DeviceRow,
  item: InspectionItem,
  now: string,
): Promise<void> {
  const existing = await env.DB.prepare(
    `SELECT transition_item_id
       FROM employee_transition_items
      WHERE device_id = ?1 AND local_employee_no = ?2
      LIMIT 1`
  ).bind(device.device_id, item.local.employeeNo).first<{ transition_item_id: string }>();
  const transitionItemId = existing?.transition_item_id ?? `eti_${crypto.randomUUID()}`;

  await env.DB.prepare(
    `INSERT INTO employee_transition_items (
         transition_item_id, workspace_id, device_id,
         local_employee_no, local_name, local_email_normalized, local_role, local_enabled,
         suggested_cloud_role, state, match_kind,
         matched_employee_id, employee_no_match_id, email_match_id,
         created_at, updated_at, resolved_at
     ) VALUES (
         ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11,
         ?12, ?13, ?14, ?15, ?15, ?16
     )
     ON CONFLICT(device_id, local_employee_no) DO UPDATE SET
         local_name = excluded.local_name,
         local_email_normalized = excluded.local_email_normalized,
         local_role = excluded.local_role,
         local_enabled = excluded.local_enabled,
         suggested_cloud_role = excluded.suggested_cloud_role,
         state = excluded.state,
         match_kind = excluded.match_kind,
         matched_employee_id = excluded.matched_employee_id,
         employee_no_match_id = excluded.employee_no_match_id,
         email_match_id = excluded.email_match_id,
         updated_at = excluded.updated_at,
         resolved_at = excluded.resolved_at`
  ).bind(
    transitionItemId,
    device.workspace_id,
    device.device_id,
    item.local.employeeNo,
    item.local.name,
    item.local.email,
    item.local.role,
    item.local.enabled ? 1 : 0,
    item.suggestedCloudRole,
    item.state,
    item.matchKind,
    item.matchedEmployee?.employee_id ?? null,
    item.employeeNoMatch?.employee_id ?? null,
    item.emailMatch?.employee_id ?? null,
    now,
    item.state === "ready" || item.state === "matched_existing" ? now : null,
  ).run();
}

async function ensureExactLink(env: Env, deviceId: string, localEmployeeNo: string, employeeId: string): Promise<void> {
  const existingByLocal = await env.DB.prepare(
    `SELECT employee_id FROM device_employee_links
      WHERE device_id = ?1 AND local_employee_no = ?2 LIMIT 1`
  ).bind(deviceId, localEmployeeNo).first<{ employee_id: string }>();
  if (existingByLocal) {
    if (existingByLocal.employee_id !== employeeId) throw new Error("LOCAL_EMPLOYEE_LINK_CONFLICT");
    return;
  }

  const existingByEmployee = await env.DB.prepare(
    `SELECT local_employee_no FROM device_employee_links
      WHERE device_id = ?1 AND employee_id = ?2 LIMIT 1`
  ).bind(deviceId, employeeId).first<{ local_employee_no: string }>();
  if (existingByEmployee && existingByEmployee.local_employee_no !== localEmployeeNo)
    throw new Error("CLOUD_EMPLOYEE_LINK_CONFLICT");

  await env.DB.prepare(
    `INSERT INTO device_employee_links (device_id, employee_id, local_employee_no)
     VALUES (?1, ?2, ?3)`
  ).bind(deviceId, employeeId, localEmployeeNo).run();
}

async function inspectEmployeeTransition(request: Request, env: Env): Promise<Response> {
  const requestId = requestIdFrom(request);
  const device = await authenticateDevice(request, env);
  if (!device) {
    return json(env, requestId, 401, {
      error: { code: "UNAUTHORIZED", message: "Device authentication failed." },
    });
  }

  if (device.employee_authority_state === "cloud") {
    return json(env, requestId, 409, {
      error: {
        code: "EMPLOYEE_AUTHORITY_ALREADY_CLOUD",
        message: "This Device has already completed Cloud Employee authority cutover."
      },
    });
  }

  const localEmployees = await readLocalEmployees(request);
  if (!localEmployees) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_LOCAL_EMPLOYEES", message: "A non-empty valid localEmployees array is required." },
    });
  }

  const employeeNos = new Set<string>();
  const emails = new Set<string>();
  for (const employee of localEmployees) {
    if (employeeNos.has(employee.employeeNo)) {
      return json(env, requestId, 409, {
        error: { code: "LOCAL_EMPLOYEE_NO_DUPLICATE", message: "Local Employee No values must be unique." },
      });
    }
    if (emails.has(employee.email)) {
      return json(env, requestId, 409, {
        error: { code: "LOCAL_EMPLOYEE_EMAIL_DUPLICATE", message: "Local Employee Email values must be unique before Cloud conversion." },
      });
    }
    employeeNos.add(employee.employeeNo);
    emails.add(employee.email);
  }

  const countRow = await env.DB.prepare(
    "SELECT COUNT(*) AS count FROM cloud_employees WHERE workspace_id = ?1"
  ).bind(device.workspace_id).first<{ count: number }>();
  const centralCount = Number(countRow?.count ?? 0);

  let bootstrapOwnerNo: string | null = null;
  if (centralCount === 0 && device.pairing_id === null) {
    const localOwners = localEmployees.filter(employee => employee.role === "SUPER_ADMIN" && employee.enabled);
    if (localOwners.length !== 1) {
      return json(env, requestId, 409, {
        error: {
          code: "BOOTSTRAP_OWNER_REQUIRED",
          message: "The first Workspace Device must contain exactly one enabled Local SUPER_ADMIN during conversion."
        },
      });
    }
    const recoveryEmail = normalizeEmail(device.recovery_email);
    if (!recoveryEmail || !device.recovery_email_verified_at || recoveryEmail !== localOwners[0].email) {
      return json(env, requestId, 409, {
        error: {
          code: "BOOTSTRAP_OWNER_EMAIL_MISMATCH",
          message: "The Local SUPER_ADMIN Email must match the verified Workspace recovery Email."
        },
      });
    }
    bootstrapOwnerNo = localOwners[0].employeeNo;
  } else if (centralCount === 0 && device.pairing_id !== null) {
    return json(env, requestId, 409, {
      error: {
        code: "WORKSPACE_OWNER_MISSING",
        message: "A paired Device cannot initialize the Workspace SUPER_ADMIN."
      },
    });
  }

  const snapshotHash = await localSnapshotHash(localEmployees);
  const results: InspectionItem[] = [];
  const now = new Date().toISOString();

  await env.DB.batch([
    env.DB.prepare("DELETE FROM employee_transition_items WHERE device_id = ?1").bind(device.device_id),
    env.DB.prepare(
      `UPDATE devices
          SET employee_transition_snapshot_hash = ?1,
              employee_transition_snapshot_at = ?2,
              employee_transition_started_at = COALESCE(employee_transition_started_at, ?2),
              updated_at = ?2
        WHERE device_id = ?3 AND employee_authority_state = 'transitioning'`
    ).bind(snapshotHash, now, device.device_id),
  ]);

  for (const local of localEmployees) {
    if (bootstrapOwnerNo === local.employeeNo) {
      const item: InspectionItem = {
        local,
        suggestedCloudRole: "SUPER_ADMIN",
        state: "bootstrap_owner_pending",
        matchKind: "bootstrap_owner",
        matchedEmployee: null,
        employeeNoMatch: null,
        emailMatch: null,
      };
      await upsertTransitionItem(env, device, item, now);
      results.push(item);
      continue;
    }

    const [employeeNoMatch, emailMatch, linkedEmployee] = await Promise.all([
      employeeByNo(env, device.workspace_id, local.employeeNo),
      employeeByEmail(env, device.workspace_id, local.email),
      linkedEmployeeByLocal(env, device.workspace_id, device.device_id, local.employeeNo),
    ]);

    let matchKind: MatchKind;
    if (!employeeNoMatch && !emailMatch) matchKind = "none";
    else if (employeeNoMatch && emailMatch && employeeNoMatch.employee_id === emailMatch.employee_id) matchKind = "same_employee";
    else if (employeeNoMatch && !emailMatch) matchKind = "employee_no_only";
    else if (!employeeNoMatch && emailMatch) matchKind = "email_only";
    else matchKind = "split";

    let state: TransitionState;
    let matchedEmployee: EmployeeRow | null = null;
    let targetRole = suggestedRole(local.role, false);

    // A persisted Device/local -> Cloud Employee link is an explicit identity
    // decision. It may come from an exact automatic match or a prior SUPER_ADMIN
    // conflict resolution. Reinspection must never discard that decision merely
    // because the legacy Local fields still conflict with the Cloud record.
    if (linkedEmployee) {
      matchedEmployee = linkedEmployee;
      targetRole = linkedEmployee.role as LocalEmployee["role"];
      state = linkedEmployee.credential_verifier && linkedEmployee.email_verified_at ? "ready" : "credential_pending";
    } else if (!employeeNoMatch && !emailMatch) {
      state = "new_email_pending";
    } else if (employeeNoMatch && emailMatch && employeeNoMatch.employee_id === emailMatch.employee_id) {
      matchedEmployee = employeeNoMatch;
      targetRole = matchedEmployee.role as LocalEmployee["role"];
      state = matchedEmployee.credential_verifier && matchedEmployee.email_verified_at ? "ready" : "credential_pending";
      await ensureExactLink(env, device.device_id, local.employeeNo, matchedEmployee.employee_id);
    } else {
      state = "conflict";
    }

    const item: InspectionItem = {
      local,
      suggestedCloudRole: targetRole,
      state,
      matchKind,
      matchedEmployee,
      employeeNoMatch,
      emailMatch,
    };
    await upsertTransitionItem(env, device, item, now);
    results.push(item);
  }

  const unresolvedCount = results.filter(item => item.state !== "ready").length;
  const conflictCount = results.filter(item => item.state === "conflict").length;

  return json(env, requestId, 200, {
    transition: {
      deviceId: device.device_id,
      authorityState: device.employee_authority_state,
      snapshotHash,
      localEmployeeCount: localEmployees.length,
      unresolvedCount,
      conflictCount,
      readyForCutover: unresolvedCount === 0,
      items: results.map(item => ({
        local: {
          employeeNo: item.local.employeeNo,
          name: item.local.name,
          email: item.local.email,
          role: item.local.role,
          enabled: item.local.enabled,
        },
        suggestedCloudRole: item.suggestedCloudRole,
        state: item.state,
        matchKind: item.matchKind,
        matchedEmployee: employeeJson(item.matchedEmployee),
        employeeNoMatch: employeeJson(item.employeeNoMatch),
        emailMatch: employeeJson(item.emailMatch),
      })),
    },
  });
}

export async function handleEmployeeTransition(request: Request, env: Env): Promise<Response | null> {
  const url = new URL(request.url);
  if (request.method === "POST" && url.pathname === "/v1/employee-transition/inspect") {
    try {
      return await inspectEmployeeTransition(request, env);
    } catch (error) {
      const requestId = requestIdFrom(request);
      const message = error instanceof Error ? error.message : "unknown error";
      if (message === "LOCAL_EMPLOYEE_LINK_CONFLICT" || message === "CLOUD_EMPLOYEE_LINK_CONFLICT") {
        return json(env, requestId, 409, {
          error: { code: message, message: "An existing Device-to-Employee link conflicts with this identity inspection." },
        });
      }
      console.error("employee_transition_inspection_failed", { requestId, error: message });
      return json(env, requestId, 503, {
        error: { code: "EMPLOYEE_TRANSITION_UNAVAILABLE", message: "Employee transition inspection is temporarily unavailable." },
      });
    }
  }
  return null;
}
