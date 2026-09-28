import {
  createSessionToken,
  isSessionToken,
  normalizePassword,
  sha256Hex,
  verifyCredential,
} from "./crypto";
import { json, readJsonObject } from "./http";
import type { Env, IdentityPrincipal, JsonValue, LoginSuccess } from "./types";

type EmployeeAuthRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  enabled: number;
  revision: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  credential_algorithm: string;
  credential_verifier: string;
  credential_version: number;
};

type SessionAuthorityRow = {
  workspace_id: string;
  employee_id: string;
  application_id: string;
  session_credential_version: number;
  created_at: string;
  expires_at: string;
  revoked_at: string | null;
  employee_no: string;
  name: string;
  employee_enabled: number;
  employee_revision: number;
  current_credential_version: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  application_status: string;
  workspace_application_enabled: number;
};

const PBKDF2_ALGORITHM = "pbkdf2-sha256";
const DEFAULT_SESSION_TTL_SECONDS = 8 * 60 * 60;
const MIN_SESSION_TTL_SECONDS = 15 * 60;
const MAX_SESSION_TTL_SECONDS = 24 * 60 * 60;

function normalizeWorkspaceId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 5 && normalized.length <= 80 ? normalized : null;
}

function normalizeApplicationId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 2 || normalized.length > 64 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function sessionTtlSeconds(env: Env): number {
  const configured = Number(env.SESSION_TTL_SECONDS ?? DEFAULT_SESSION_TTL_SECONDS);
  if (!Number.isFinite(configured)) return DEFAULT_SESSION_TTL_SECONDS;
  return Math.max(MIN_SESSION_TTL_SECONDS, Math.min(Math.trunc(configured), MAX_SESSION_TTL_SECONDS));
}

function bearerSessionToken(request: Request): string | null {
  const raw = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(raw);
  const token = match?.[1]?.trim() ?? "";
  return isSessionToken(token) ? token : null;
}

function applicationHeader(request: Request): string | null {
  return normalizeApplicationId(request.headers.get("x-identity-application"));
}

async function employeeForLogin(
  env: Env,
  workspaceId: string,
  employeeNo: string,
): Promise<EmployeeAuthRow | null> {
  return env.DB.prepare(
    `SELECT e.employee_id,
            e.workspace_id,
            e.employee_no,
            e.name,
            e.enabled,
            e.revision,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            c.algorithm AS credential_algorithm,
            c.verifier AS credential_verifier,
            c.credential_version
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       JOIN employee_credentials c ON c.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeAuthRow>();
}

async function applicationEnabledForWorkspace(
  env: Env,
  workspaceId: string,
  applicationId: string,
): Promise<boolean> {
  const row = await env.DB.prepare(
    `SELECT 1 AS enabled
       FROM workspace_applications wa
       JOIN applications a ON a.application_id = wa.application_id
      WHERE wa.workspace_id = ?1
        AND wa.application_id = ?2
        AND wa.enabled = 1
        AND a.status = 'active'
      LIMIT 1`
  ).bind(workspaceId, applicationId).first<{ enabled: number }>();
  return Boolean(row);
}

async function employeeHasApplicationAccess(
  env: Env,
  workspaceId: string,
  employeeId: string,
  applicationId: string,
  isWorkspaceSuperAdmin: boolean,
): Promise<boolean> {
  if (isWorkspaceSuperAdmin) return true;

  const row = await env.DB.prepare(
    `SELECT 1 AS allowed
       FROM employee_application_access ea
      WHERE ea.workspace_id = ?1
        AND ea.employee_id = ?2
        AND ea.application_id = ?3
        AND ea.enabled = 1
      UNION ALL
     SELECT 1 AS allowed
       FROM employee_identity_groups eg
       JOIN identity_groups g
         ON g.group_id = eg.group_id
        AND g.workspace_id = eg.workspace_id
       JOIN identity_group_application_access ga
         ON ga.group_id = eg.group_id
        AND ga.workspace_id = eg.workspace_id
      WHERE eg.workspace_id = ?1
        AND eg.employee_id = ?2
        AND ga.application_id = ?3
        AND g.status = 'active'
        AND ga.enabled = 1
      LIMIT 1`
  ).bind(workspaceId, employeeId, applicationId).first<{ allowed: number }>();
  return Boolean(row);
}

async function groupKeys(
  env: Env,
  workspaceId: string,
  employeeId: string,
): Promise<string[]> {
  const result = await env.DB.prepare(
    `SELECT g.group_key
       FROM employee_identity_groups eg
       JOIN identity_groups g
         ON g.group_id = eg.group_id
        AND g.workspace_id = eg.workspace_id
      WHERE eg.workspace_id = ?1
        AND eg.employee_id = ?2
        AND g.status = 'active'
      ORDER BY g.group_key`
  ).bind(workspaceId, employeeId).all<{ group_key: string }>();
  return (result.results ?? []).map(row => row.group_key);
}

async function principalFromEmployee(
  env: Env,
  employee: EmployeeAuthRow,
): Promise<IdentityPrincipal> {
  const isWorkspaceSuperAdmin = employee.super_admin_employee_id === employee.employee_id;
  return {
    workspaceId: employee.workspace_id,
    employeeId: employee.employee_id,
    employeeNo: employee.employee_no,
    displayName: employee.name,
    isWorkspaceSuperAdmin,
    groupKeys: await groupKeys(env, employee.workspace_id, employee.employee_id),
    credentialVersion: employee.credential_version,
    employeeRevision: employee.revision,
  };
}

async function createSession(
  env: Env,
  employee: EmployeeAuthRow,
  applicationId: string,
): Promise<LoginSuccess> {
  const token = createSessionToken();
  const tokenHash = await sha256Hex(token);
  const createdAt = new Date();
  const expiresAt = new Date(createdAt.getTime() + sessionTtlSeconds(env) * 1000);

  const result = await env.DB.prepare(
    `INSERT INTO identity_sessions(
        session_hash,
        workspace_id,
        employee_id,
        application_id,
        credential_version,
        employee_revision,
        created_at,
        expires_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)`
  ).bind(
    tokenHash,
    employee.workspace_id,
    employee.employee_id,
    applicationId,
    employee.credential_version,
    employee.revision,
    createdAt.toISOString(),
    expiresAt.toISOString(),
  ).run();

  if (!result.success) throw new Error("identity session insert failed");

  return {
    principal: await principalFromEmployee(env, employee),
    session: {
      token,
      expiresAt: expiresAt.toISOString(),
    },
  };
}

export async function handleLogin(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const body = await readJsonObject(request);
  const workspaceId = normalizeWorkspaceId(body?.workspaceId);
  const applicationId = normalizeApplicationId(body?.applicationId);
  const employeeNo = normalizeEmployeeNo(body?.employeeNo);
  const password = normalizePassword(body?.password);

  if (!workspaceId || !applicationId || !employeeNo || !password) {
    return json(env, requestId, 400, {
      error: {
        code: "INVALID_LOGIN_REQUEST",
        message: "Identity login request is invalid.",
      },
    });
  }

  const employee = await employeeForLogin(env, workspaceId, employeeNo);
  const credentialReady = Boolean(
    employee
    && employee.workspace_status === "active"
    && employee.enabled === 1
    && employee.credential_algorithm === PBKDF2_ALGORITHM
    && employee.credential_verifier,
  );
  const authenticated = credentialReady
    ? await verifyCredential(password, employee!.credential_verifier)
    : false;

  if (!employee || !authenticated) {
    return json(env, requestId, 401, {
      error: {
        code: "AUTHENTICATION_FAILED",
        message: "Employee authentication failed.",
      },
    });
  }

  if (!await applicationEnabledForWorkspace(env, workspaceId, applicationId)) {
    return json(env, requestId, 403, {
      error: {
        code: "APPLICATION_ACCESS_DENIED",
        message: "Application access is not enabled for this Workspace.",
      },
    });
  }

  const isWorkspaceSuperAdmin = employee.super_admin_employee_id === employee.employee_id;
  if (!await employeeHasApplicationAccess(
    env,
    workspaceId,
    employee.employee_id,
    applicationId,
    isWorkspaceSuperAdmin,
  )) {
    return json(env, requestId, 403, {
      error: {
        code: "APPLICATION_ACCESS_DENIED",
        message: "Employee does not have access to this application.",
      },
    });
  }

  const login = await createSession(env, employee, applicationId);
  return json(env, requestId, 200, {
    principal: login.principal as unknown as JsonValue,
    session: login.session as unknown as JsonValue,
  });
}

async function sessionAuthority(
  env: Env,
  sessionHash: string,
  applicationId: string,
): Promise<SessionAuthorityRow | null> {
  return env.DB.prepare(
    `SELECT s.workspace_id,
            s.employee_id,
            s.application_id,
            s.credential_version AS session_credential_version,
            s.created_at,
            s.expires_at,
            s.revoked_at,
            e.employee_no,
            e.name,
            e.enabled AS employee_enabled,
            e.revision AS employee_revision,
            c.credential_version AS current_credential_version,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            a.status AS application_status,
            wa.enabled AS workspace_application_enabled
       FROM identity_sessions s
       JOIN employees e
         ON e.employee_id = s.employee_id
        AND e.workspace_id = s.workspace_id
       JOIN employee_credentials c ON c.employee_id = e.employee_id
       JOIN workspaces w ON w.workspace_id = s.workspace_id
       JOIN applications a ON a.application_id = s.application_id
       JOIN workspace_applications wa
         ON wa.workspace_id = s.workspace_id
        AND wa.application_id = s.application_id
      WHERE s.session_hash = ?1
        AND s.application_id = ?2
      LIMIT 1`
  ).bind(sessionHash, applicationId).first<SessionAuthorityRow>();
}

async function resolvedPrincipal(
  env: Env,
  row: SessionAuthorityRow,
): Promise<IdentityPrincipal | null> {
  const now = new Date().toISOString();
  if (row.revoked_at || row.expires_at <= now) return null;
  if (row.workspace_status !== "active" || row.employee_enabled !== 1) return null;
  if (row.application_status !== "active" || row.workspace_application_enabled !== 1) return null;
  if (row.session_credential_version !== row.current_credential_version) return null;

  const isWorkspaceSuperAdmin = row.super_admin_employee_id === row.employee_id;
  if (!await employeeHasApplicationAccess(
    env,
    row.workspace_id,
    row.employee_id,
    row.application_id,
    isWorkspaceSuperAdmin,
  )) return null;

  return {
    workspaceId: row.workspace_id,
    employeeId: row.employee_id,
    employeeNo: row.employee_no,
    displayName: row.name,
    isWorkspaceSuperAdmin,
    groupKeys: await groupKeys(env, row.workspace_id, row.employee_id),
    credentialVersion: row.current_credential_version,
    employeeRevision: row.employee_revision,
  };
}

export async function handleResolveSession(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const token = bearerSessionToken(request);
  const applicationId = applicationHeader(request);
  if (!token || !applicationId) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }

  const row = await sessionAuthority(env, await sha256Hex(token), applicationId);
  if (!row) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing or invalid." },
    });
  }

  const principal = await resolvedPrincipal(env, row);
  if (!principal) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is no longer authorized." },
    });
  }

  return json(env, requestId, 200, {
    principal: principal as unknown as JsonValue,
    session: {
      expiresAt: row.expires_at,
    },
  });
}

export async function handleLogout(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const token = bearerSessionToken(request);
  const applicationId = applicationHeader(request);
  if (!token || !applicationId) {
    return json(env, requestId, 200, { loggedOut: true });
  }

  const now = new Date().toISOString();
  await env.DB.prepare(
    `UPDATE identity_sessions
        SET revoked_at = COALESCE(revoked_at, ?1)
      WHERE session_hash = ?2
        AND application_id = ?3`
  ).bind(now, await sha256Hex(token), applicationId).run();

  return json(env, requestId, 200, { loggedOut: true });
}
