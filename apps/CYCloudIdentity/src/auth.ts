import {
  createSessionToken,
  isSessionToken,
  normalizePassword,
  sha256Hex,
  verifyCredential,
} from "./crypto";
import { json, readJsonObject } from "./http";
import { exchangeInitialPassword } from "./initial-access";
import { resolveIdentitySession } from "./session-auth";
import { effectiveWorkspaceRole, hasApplicationEntry, type StoredEmployeeRole } from "./role-access";
import type { Env, IdentityPrincipal, JsonValue, LoginSuccess } from "./types";

type EmployeeAuthRow = {
  employee_id: string;
  workspace_id: string;
  employee_no: string;
  name: string;
  email_verified_at: string | null;
  enabled: number;
  role_key: StoredEmployeeRole;
  identity_admin: number;
  revision: number;
  workspace_status: string;
  super_admin_employee_id: string | null;
  credential_algorithm: string;
  credential_verifier: string;
  credential_version: number;
  activated_at: string | null;
  initial_credential_present: number;
};

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
            e.email_verified_at,
            e.enabled,
            e.role_key,
            e.identity_admin,
            e.revision,
            w.status AS workspace_status,
            w.super_admin_employee_id,
            c.algorithm AS credential_algorithm,
            c.verifier AS credential_verifier,
            c.credential_version,
            e.activated_at,
            CASE WHEN i.employee_id IS NULL THEN 0 ELSE 1 END AS initial_credential_present
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
       LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
       LEFT JOIN employee_initial_credentials i ON i.employee_id = e.employee_id
      WHERE e.workspace_id = ?1
        AND e.employee_no = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeNo).first<EmployeeAuthRow>();
}

async function workspaceApplicationAvailable(
  env: Env,
  workspaceId: string,
  applicationId: string,
): Promise<boolean> {
  return Boolean(await env.DB.prepare(
    `SELECT 1 AS available
       FROM workspace_applications wa
       JOIN applications a ON a.application_id = wa.application_id
      WHERE wa.workspace_id = ?1
        AND wa.application_id = ?2
        AND wa.enabled = 1
        AND a.status = 'active'
      LIMIT 1`
  ).bind(workspaceId, applicationId).first<{ available: number }>());
}

async function principalFromEmployee(env: Env, employee: EmployeeAuthRow): Promise<IdentityPrincipal> {
  const workspaceRole = effectiveWorkspaceRole(
    employee.employee_id,
    employee.super_admin_employee_id,
    employee.role_key,
  );
  return {
    workspaceId: employee.workspace_id,
    employeeId: employee.employee_id,
    employeeNo: employee.employee_no,
    displayName: employee.name,
    workspaceRole,
    isIdentityAdmin: workspaceRole === "ADMIN" && employee.identity_admin === 1,
    emailVerified: Boolean(employee.email_verified_at),
    isWorkspaceSuperAdmin: workspaceRole === "SUPER_ADMIN",
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
      error: { code: "INVALID_LOGIN_REQUEST", message: "Identity login request is invalid." },
    });
  }

  const employee = await employeeForLogin(env, workspaceId, employeeNo);
  if (employee && employee.initial_credential_present === 1 && !employee.activated_at && !employee.credential_algorithm) {
    return exchangeInitialPassword(env, requestId, employee, applicationId, password);
  }
  const credentialReady = Boolean(
    employee
    && employee.workspace_status === "active"
    && employee.enabled === 1
    && employee.credential_algorithm
    && employee.credential_verifier,
  );
  const authenticated = credentialReady
    ? await verifyCredential(password, employee!.credential_algorithm, employee!.credential_verifier)
    : false;

  if (!employee || !authenticated) {
    return json(env, requestId, 401, {
      error: { code: "AUTHENTICATION_FAILED", message: "Employee authentication failed." },
    });
  }

  if (!await workspaceApplicationAvailable(env, workspaceId, applicationId)) {
    return json(env, requestId, 403, {
      error: { code: "APPLICATION_ACCESS_DENIED", message: "Application access is not enabled for this Workspace." },
    });
  }

  const isWorkspaceSuperAdmin = employee.super_admin_employee_id === employee.employee_id;
  if (!await hasApplicationEntry(env, workspaceId, employee.employee_id, applicationId, isWorkspaceSuperAdmin)) {
    return json(env, requestId, 403, {
      error: { code: "APPLICATION_ACCESS_DENIED", message: "Employee does not have access to this application." },
    });
  }

  const login = await createSession(env, employee, applicationId);
  return json(env, requestId, 200, {
    principal: login.principal as unknown as JsonValue,
    session: login.session as unknown as JsonValue,
  });
}

export async function handleResolveSession(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const session = await resolveIdentitySession(request, env);
  if (!session) {
    return json(env, requestId, 401, {
      error: { code: "SESSION_INVALID", message: "Identity session is missing, invalid, or no longer authorized." },
    });
  }
  return json(env, requestId, 200, {
    principal: session.principal as unknown as JsonValue,
    session: { expiresAt: session.expiresAt },
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
