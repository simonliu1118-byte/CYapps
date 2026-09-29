import { requireIdentityAdministrator, requireWorkspaceAdmin } from "./admin-auth";
import { json, readJsonObject } from "./http";
import { effectiveWorkspaceRole, isCoreAccountApplication, type StoredEmployeeRole } from "./role-access";
import type { Env, JsonValue } from "./types";

type AccessTargetRow = {
  employee_id: string;
  role_key: StoredEmployeeRole;
  identity_admin: number;
  super_admin_employee_id: string | null;
};

function normalizeId(value: unknown, min = 5, max = 80): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= min && normalized.length <= max ? normalized : null;
}

function normalizeApplicationId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 2 || normalized.length > 64 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

async function audit(
  env: Env,
  input: {
    workspaceId: string;
    actorEmployeeId: string;
    targetEmployeeId?: string | null;
    applicationId?: string | null;
    eventType: string;
    detail?: Record<string, JsonValue>;
  },
): Promise<void> {
  await env.DB.prepare(
    `INSERT INTO identity_audit_events(
       event_id, workspace_id, actor_employee_id, target_employee_id,
       application_id, event_type, detail_json, created_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)`
  ).bind(
    `audit_${crypto.randomUUID()}`,
    input.workspaceId,
    input.actorEmployeeId,
    input.targetEmployeeId ?? null,
    input.applicationId ?? null,
    input.eventType,
    input.detail ? JSON.stringify(input.detail) : null,
    new Date().toISOString(),
  ).run();
}

export async function handleRoleAccessSnapshot(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const actor = await requireWorkspaceAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_ADMIN_REQUIRED", message: "Workspace administrator authority is required." },
    });
  }

  const [employees, applications, directAccess, groups, memberships, groupAccess] = await Promise.all([
    env.DB.prepare(
      `SELECT e.employee_id,
              e.employee_no,
              e.name,
              e.email_normalized,
              e.email_verified_at,
              e.enabled,
              e.role_key,
              e.identity_admin,
              e.activated_at,
              e.revision,
              CASE WHEN c.employee_id IS NULL THEN 0 ELSE 1 END AS credential_present,
              CASE WHEN w.super_admin_employee_id = e.employee_id THEN 1 ELSE 0 END AS is_super_admin
         FROM employees e
         JOIN workspaces w ON w.workspace_id = e.workspace_id
         LEFT JOIN employee_credentials c ON c.employee_id = e.employee_id
        WHERE e.workspace_id = ?1
        ORDER BY e.employee_no`
    ).bind(actor.workspaceId).all(),
    env.DB.prepare(
      `SELECT wa.application_id,
              a.display_name,
              a.status AS application_status,
              wa.enabled,
              wa.compatibility_role_mode
         FROM workspace_applications wa
         JOIN applications a ON a.application_id = wa.application_id
        WHERE wa.workspace_id = ?1
        ORDER BY a.display_name, wa.application_id`
    ).bind(actor.workspaceId).all(),
    env.DB.prepare(
      `SELECT employee_id, application_id, enabled
         FROM employee_application_access
        WHERE workspace_id = ?1
        ORDER BY employee_id, application_id`
    ).bind(actor.workspaceId).all(),
    env.DB.prepare(
      `SELECT group_id, group_key, display_name, description, status, revision
         FROM identity_groups
        WHERE workspace_id = ?1
        ORDER BY display_name, group_key`
    ).bind(actor.workspaceId).all(),
    env.DB.prepare(
      `SELECT employee_id, group_id
         FROM employee_identity_groups
        WHERE workspace_id = ?1
        ORDER BY employee_id, group_id`
    ).bind(actor.workspaceId).all(),
    env.DB.prepare(
      `SELECT group_id, application_id, enabled, application_role_key
         FROM identity_group_application_access
        WHERE workspace_id = ?1
        ORDER BY group_id, application_id`
    ).bind(actor.workspaceId).all(),
  ]);

  const employeeRows = (employees.results ?? []).map(raw => {
    const row = raw as unknown as {
      employee_id: string;
      employee_no: string;
      name: string;
      email_normalized: string;
      email_verified_at: string | null;
      enabled: number;
      role_key: StoredEmployeeRole;
      identity_admin: number;
      activated_at: string | null;
      revision: number;
      credential_present: number;
      is_super_admin: number;
    };
    return {
      ...row,
      workspace_role: row.is_super_admin === 1 ? "SUPER_ADMIN" : row.role_key,
    };
  });

  const applicationRows = (applications.results ?? []).map(raw => {
    const row = raw as unknown as {
      application_id: string;
      display_name: string;
      application_status: string;
      enabled: number;
      compatibility_role_mode: "USER_ADMIN" | null;
    };
    return {
      ...row,
      core_access_locked: isCoreAccountApplication(env, row.application_id) ? 1 : 0,
    };
  });

  return json(env, requestId, 200, {
    workspaceId: actor.workspaceId,
    actor: {
      employeeId: actor.employeeId,
      workspaceRole: actor.workspaceRole,
      isIdentityAdmin: actor.isIdentityAdmin,
    },
    employees: employeeRows as unknown as JsonValue,
    applications: applicationRows as unknown as JsonValue,
    directAccess: (directAccess.results ?? []) as JsonValue,
    // Legacy read-only projection kept temporarily so a not-yet-updated consumer
    // does not fail while the new CY Web UI is deployed. These rows no longer
    // participate in login/session authorization.
    groups: (groups.results ?? []) as JsonValue,
    memberships: (memberships.results ?? []) as JsonValue,
    groupAccess: (groupAccess.results ?? []) as JsonValue,
  });
}

export async function handlePutDirectApplicationAccess(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
  applicationIdRaw: string,
): Promise<Response> {
  const actor = await requireIdentityAdministrator(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "IDENTITY_ADMIN_REQUIRED", message: "Identity administration authority is required." },
    });
  }

  const employeeId = normalizeId(employeeIdRaw);
  const applicationId = normalizeApplicationId(applicationIdRaw);
  const body = await readJsonObject(request);
  const enabled = typeof body?.enabled === "boolean" ? body.enabled : null;
  if (!employeeId || !applicationId || enabled === null) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_APPLICATION_ACCESS", message: "Application access input is invalid." },
    });
  }
  if (employeeId === actor.employeeId) {
    return json(env, requestId, 409, {
      error: { code: "SELF_ACCESS_CHANGE_NOT_ALLOWED", message: "Identity administrators cannot change their own Application Access." },
    });
  }
  if (isCoreAccountApplication(env, applicationId)) {
    return json(env, requestId, 409, {
      error: { code: "CORE_APPLICATION_ACCESS_LOCKED", message: "Core CY Web account access is required and cannot be changed." },
    });
  }

  const target = await env.DB.prepare(
    `SELECT e.employee_id, e.role_key, e.identity_admin, w.super_admin_employee_id
       FROM employees e
       JOIN workspaces w ON w.workspace_id = e.workspace_id
      WHERE e.workspace_id = ?1
        AND e.employee_id = ?2
      LIMIT 1`
  ).bind(actor.workspaceId, employeeId).first<AccessTargetRow>();
  if (!target) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  if (effectiveWorkspaceRole(target.employee_id, target.super_admin_employee_id, target.role_key) === "SUPER_ADMIN") {
    return json(env, requestId, 409, {
      error: { code: "SUPER_ADMIN_ACCESS_PROTECTED", message: "Super Admin Application Access is automatic and cannot be changed." },
    });
  }

  const application = await env.DB.prepare(
    `SELECT 1 AS available
       FROM workspace_applications wa
       JOIN applications a ON a.application_id = wa.application_id
      WHERE wa.workspace_id = ?1
        AND wa.application_id = ?2
        AND wa.enabled = 1
        AND a.status = 'active'
      LIMIT 1`
  ).bind(actor.workspaceId, applicationId).first<{ available: number }>();
  if (!application) {
    return json(env, requestId, 404, {
      error: { code: "APPLICATION_NOT_AVAILABLE", message: "Application is not enabled for this Workspace." },
    });
  }

  const now = new Date().toISOString();
  await env.DB.prepare(
    `INSERT INTO employee_application_access(
       workspace_id, employee_id, application_id, enabled, created_at, updated_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?5)
     ON CONFLICT(workspace_id, employee_id, application_id) DO UPDATE SET
       enabled = excluded.enabled,
       updated_at = excluded.updated_at`
  ).bind(actor.workspaceId, target.employee_id, applicationId, enabled ? 1 : 0, now).run();

  if (!enabled) {
    await env.DB.prepare(
      `UPDATE identity_sessions
          SET revoked_at = COALESCE(revoked_at, ?3)
        WHERE employee_id = ?1
          AND application_id = ?2`
    ).bind(target.employee_id, applicationId, now).run();
  }

  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    targetEmployeeId: target.employee_id,
    applicationId,
    eventType: "employee_application_access_updated",
    detail: { enabled },
  });

  return json(env, requestId, 200, {
    access: { employeeId: target.employee_id, applicationId, enabled },
    affectedApplicationSessionsRevoked: !enabled,
  });
}
