import { requireWorkspaceSuperAdmin } from "./admin-auth";
import { json, readJsonObject } from "./http";
import type { Env, JsonValue } from "./types";

type GroupRow = {
  group_id: string;
  group_key: string;
  display_name: string;
  description: string | null;
  status: "active" | "disabled";
  revision: number;
};

type WorkspaceApplicationRow = {
  application_id: string;
  display_name: string;
  application_status: string;
  enabled: number;
  compatibility_role_mode: "USER_ADMIN" | null;
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

function normalizeGroupKey(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 1 || normalized.length > 64 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

function normalizeDisplayName(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 1 && normalized.length <= 120 ? normalized : null;
}

function normalizeDescription(value: unknown): string | null | undefined {
  if (value === undefined) return undefined;
  if (value === null) return null;
  if (typeof value !== "string") return undefined;
  const normalized = value.trim();
  return normalized.length <= 500 ? (normalized || null) : undefined;
}

function normalizeRoleKey(value: unknown): "USER" | "ADMIN" | null | undefined {
  if (value === undefined) return undefined;
  if (value === null || value === "") return null;
  if (typeof value !== "string") return undefined;
  const normalized = value.trim().toUpperCase();
  return normalized === "USER" || normalized === "ADMIN" ? normalized : undefined;
}

function normalizeCompatibilityMode(value: unknown): "USER_ADMIN" | null | undefined {
  if (value === null || value === "") return null;
  if (typeof value !== "string") return undefined;
  return value.trim().toUpperCase() === "USER_ADMIN" ? "USER_ADMIN" : undefined;
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

async function workspaceApplication(
  env: Env,
  workspaceId: string,
  applicationId: string,
): Promise<WorkspaceApplicationRow | null> {
  return env.DB.prepare(
    `SELECT wa.application_id,
            a.display_name,
            a.status AS application_status,
            wa.enabled,
            wa.compatibility_role_mode
       FROM workspace_applications wa
       JOIN applications a ON a.application_id = wa.application_id
      WHERE wa.workspace_id = ?1
        AND wa.application_id = ?2
      LIMIT 1`
  ).bind(workspaceId, applicationId).first<WorkspaceApplicationRow>();
}

async function groupInWorkspace(env: Env, workspaceId: string, groupId: string): Promise<boolean> {
  return Boolean(await env.DB.prepare(
    `SELECT 1 AS present
       FROM identity_groups
      WHERE workspace_id = ?1
        AND group_id = ?2
      LIMIT 1`
  ).bind(workspaceId, groupId).first<{ present: number }>());
}

async function employeeInWorkspace(env: Env, workspaceId: string, employeeId: string): Promise<boolean> {
  return Boolean(await env.DB.prepare(
    `SELECT 1 AS present
       FROM employees
      WHERE workspace_id = ?1
        AND employee_id = ?2
      LIMIT 1`
  ).bind(workspaceId, employeeId).first<{ present: number }>());
}

export async function handleIdentityAdminSnapshot(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }

  const [employees, groups, memberships, applications, directAccess, groupAccess] = await Promise.all([
    env.DB.prepare(
      `SELECT employee_id, employee_no, name, email_normalized, email_verified_at,
              enabled, revision
         FROM employees
        WHERE workspace_id = ?1
        ORDER BY employee_no`
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
      `SELECT wa.application_id, a.display_name, a.status AS application_status,
              wa.enabled, wa.compatibility_role_mode
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
      `SELECT group_id, application_id, enabled, application_role_key
         FROM identity_group_application_access
        WHERE workspace_id = ?1
        ORDER BY group_id, application_id`
    ).bind(actor.workspaceId).all(),
  ]);

  return json(env, requestId, 200, {
    workspaceId: actor.workspaceId,
    employees: (employees.results ?? []) as JsonValue,
    groups: (groups.results ?? []) as JsonValue,
    memberships: (memberships.results ?? []) as JsonValue,
    applications: (applications.results ?? []) as JsonValue,
    directAccess: (directAccess.results ?? []) as JsonValue,
    groupAccess: (groupAccess.results ?? []) as JsonValue,
  });
}

export async function handleCreateIdentityGroup(
  request: Request,
  env: Env,
  requestId: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const body = await readJsonObject(request);
  const groupKey = normalizeGroupKey(body?.groupKey);
  const displayName = normalizeDisplayName(body?.displayName);
  const description = normalizeDescription(body?.description);
  if (!groupKey || !displayName || description === undefined) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_IDENTITY_GROUP", message: "Identity Group input is invalid." },
    });
  }

  const groupId = `grp_${crypto.randomUUID()}`;
  const now = new Date().toISOString();
  try {
    await env.DB.prepare(
      `INSERT INTO identity_groups(
         group_id, workspace_id, group_key, display_name, description,
         status, revision, created_at, updated_at
       ) VALUES(?1, ?2, ?3, ?4, ?5, 'active', 1, ?6, ?6)`
    ).bind(groupId, actor.workspaceId, groupKey, displayName, description, now).run();
  } catch {
    return json(env, requestId, 409, {
      error: { code: "IDENTITY_GROUP_CONFLICT", message: "Identity Group could not be created." },
    });
  }
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    eventType: "identity_group_created",
    detail: { groupId, groupKey },
  });
  return json(env, requestId, 201, { group: { groupId, groupKey, displayName, description, status: "active", revision: 1 } });
}

export async function handleUpdateIdentityGroup(
  request: Request,
  env: Env,
  requestId: string,
  groupIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const groupId = normalizeId(groupIdRaw);
  const body = await readJsonObject(request);
  const groupKey = normalizeGroupKey(body?.groupKey);
  const displayName = normalizeDisplayName(body?.displayName);
  const description = normalizeDescription(body?.description);
  const status = body?.status === "active" || body?.status === "disabled" ? body.status : null;
  const revision = typeof body?.revision === "number" && Number.isInteger(body.revision) && body.revision >= 1
    ? body.revision
    : null;
  if (!groupId || !groupKey || !displayName || description === undefined || !status || !revision) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_IDENTITY_GROUP", message: "Identity Group update is invalid." },
    });
  }
  const now = new Date().toISOString();
  try {
    const row = await env.DB.prepare(
      `UPDATE identity_groups
          SET group_key = ?3,
              display_name = ?4,
              description = ?5,
              status = ?6,
              revision = revision + 1,
              updated_at = ?7
        WHERE workspace_id = ?1
          AND group_id = ?2
          AND revision = ?8
      RETURNING group_id, group_key, display_name, description, status, revision`
    ).bind(
      actor.workspaceId,
      groupId,
      groupKey,
      displayName,
      description,
      status,
      now,
      revision,
    ).first<GroupRow>();
    if (!row) {
      return json(env, requestId, 409, {
        error: { code: "IDENTITY_GROUP_REVISION_CONFLICT", message: "Identity Group revision no longer matches." },
      });
    }
    await audit(env, {
      workspaceId: actor.workspaceId,
      actorEmployeeId: actor.employeeId,
      eventType: "identity_group_updated",
      detail: { groupId, groupKey: row.group_key, status: row.status, revision: row.revision },
    });
    return json(env, requestId, 200, {
      group: {
        groupId: row.group_id,
        groupKey: row.group_key,
        displayName: row.display_name,
        description: row.description,
        status: row.status,
        revision: row.revision,
      },
    });
  } catch {
    return json(env, requestId, 409, {
      error: { code: "IDENTITY_GROUP_CONFLICT", message: "Identity Group could not be updated." },
    });
  }
}

export async function handlePutIdentityGroupMember(
  request: Request,
  env: Env,
  requestId: string,
  groupIdRaw: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const groupId = normalizeId(groupIdRaw);
  const employeeId = normalizeId(employeeIdRaw);
  if (!groupId || !employeeId
      || !await groupInWorkspace(env, actor.workspaceId, groupId)
      || !await employeeInWorkspace(env, actor.workspaceId, employeeId)) {
    return json(env, requestId, 404, {
      error: { code: "IDENTITY_MEMBER_NOT_FOUND", message: "Identity Group or Employee was not found." },
    });
  }
  await env.DB.prepare(
    `INSERT INTO employee_identity_groups(workspace_id, employee_id, group_id, created_at)
     VALUES(?1, ?2, ?3, ?4)
     ON CONFLICT(workspace_id, employee_id, group_id) DO NOTHING`
  ).bind(actor.workspaceId, employeeId, groupId, new Date().toISOString()).run();
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    targetEmployeeId: employeeId,
    eventType: "identity_group_member_added",
    detail: { groupId },
  });
  return json(env, requestId, 200, { membership: { groupId, employeeId, enabled: true } });
}

export async function handleDeleteIdentityGroupMember(
  request: Request,
  env: Env,
  requestId: string,
  groupIdRaw: string,
  employeeIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const groupId = normalizeId(groupIdRaw);
  const employeeId = normalizeId(employeeIdRaw);
  if (!groupId || !employeeId) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_IDENTITY_MEMBERSHIP", message: "Identity Group membership input is invalid." },
    });
  }
  await env.DB.prepare(
    `DELETE FROM employee_identity_groups
      WHERE workspace_id = ?1
        AND employee_id = ?2
        AND group_id = ?3`
  ).bind(actor.workspaceId, employeeId, groupId).run();
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    targetEmployeeId: employeeId,
    eventType: "identity_group_member_removed",
    detail: { groupId },
  });
  return json(env, requestId, 200, { membership: { groupId, employeeId, enabled: false } });
}

export async function handlePutGroupApplicationAccess(
  request: Request,
  env: Env,
  requestId: string,
  groupIdRaw: string,
  applicationIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const groupId = normalizeId(groupIdRaw);
  const applicationId = normalizeApplicationId(applicationIdRaw);
  const body = await readJsonObject(request);
  const enabled = typeof body?.enabled === "boolean" ? body.enabled : null;
  const roleKey = normalizeRoleKey(body?.applicationRoleKey);
  if (!groupId || !applicationId || enabled === null || roleKey === undefined) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_APPLICATION_ACCESS", message: "Application access input is invalid." },
    });
  }
  if (!await groupInWorkspace(env, actor.workspaceId, groupId)) {
    return json(env, requestId, 404, {
      error: { code: "IDENTITY_GROUP_NOT_FOUND", message: "Identity Group was not found." },
    });
  }
  const application = await workspaceApplication(env, actor.workspaceId, applicationId);
  if (!application || application.application_status !== "active" || application.enabled !== 1) {
    return json(env, requestId, 404, {
      error: { code: "APPLICATION_NOT_AVAILABLE", message: "Application is not enabled for this Workspace." },
    });
  }
  if (enabled && application.compatibility_role_mode === "USER_ADMIN" && !roleKey) {
    return json(env, requestId, 400, {
      error: { code: "APPLICATION_ROLE_REQUIRED", message: "This Application requires User or Admin role mapping." },
    });
  }
  const now = new Date().toISOString();
  await env.DB.prepare(
    `INSERT INTO identity_group_application_access(
       workspace_id, group_id, application_id, enabled, application_role_key, created_at, updated_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?6)
     ON CONFLICT(workspace_id, group_id, application_id) DO UPDATE SET
       enabled = excluded.enabled,
       application_role_key = excluded.application_role_key,
       updated_at = excluded.updated_at`
  ).bind(
    actor.workspaceId,
    groupId,
    applicationId,
    enabled ? 1 : 0,
    enabled ? roleKey : null,
    now,
  ).run();
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    applicationId,
    eventType: "identity_group_application_access_updated",
    detail: { groupId, enabled, applicationRoleKey: enabled ? roleKey ?? null : null },
  });
  return json(env, requestId, 200, {
    access: { groupId, applicationId, enabled, applicationRoleKey: enabled ? roleKey ?? null : null },
  });
}

export async function handlePutEmployeeApplicationAccess(
  request: Request,
  env: Env,
  requestId: string,
  employeeIdRaw: string,
  applicationIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
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
  if (!await employeeInWorkspace(env, actor.workspaceId, employeeId)) {
    return json(env, requestId, 404, {
      error: { code: "EMPLOYEE_NOT_FOUND", message: "Employee was not found." },
    });
  }
  const application = await workspaceApplication(env, actor.workspaceId, applicationId);
  if (!application || application.application_status !== "active" || application.enabled !== 1) {
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
  ).bind(actor.workspaceId, employeeId, applicationId, enabled ? 1 : 0, now).run();
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    targetEmployeeId: employeeId,
    applicationId,
    eventType: "employee_application_access_updated",
    detail: { enabled },
  });
  return json(env, requestId, 200, { access: { employeeId, applicationId, enabled } });
}

export async function handlePutApplicationCompatibilityRoleMode(
  request: Request,
  env: Env,
  requestId: string,
  applicationIdRaw: string,
): Promise<Response> {
  const actor = await requireWorkspaceSuperAdmin(request, env);
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "WORKSPACE_SUPER_ADMIN_REQUIRED", message: "Workspace highest authority is required." },
    });
  }
  const applicationId = normalizeApplicationId(applicationIdRaw);
  const body = await readJsonObject(request);
  const mode = normalizeCompatibilityMode(body?.mode);
  if (!applicationId || mode === undefined) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_COMPATIBILITY_ROLE_MODE", message: "Compatibility role mode is invalid." },
    });
  }
  const application = await workspaceApplication(env, actor.workspaceId, applicationId);
  if (!application || application.application_status !== "active" || application.enabled !== 1) {
    return json(env, requestId, 404, {
      error: { code: "APPLICATION_NOT_AVAILABLE", message: "Application is not enabled for this Workspace." },
    });
  }
  const now = new Date().toISOString();
  const statements: D1PreparedStatement[] = [];
  if (mode === "USER_ADMIN") {
    statements.push(
      env.DB.prepare(
        `UPDATE identity_group_application_access
            SET application_role_key = COALESCE(application_role_key, 'USER'),
                updated_at = ?3
          WHERE workspace_id = ?1
            AND application_id = ?2
            AND enabled = 1`
      ).bind(actor.workspaceId, applicationId, now),
    );
  }
  statements.push(
    env.DB.prepare(
      `UPDATE workspace_applications
          SET compatibility_role_mode = ?3,
              updated_at = ?4
        WHERE workspace_id = ?1
          AND application_id = ?2`
    ).bind(actor.workspaceId, applicationId, mode, now),
  );
  await env.DB.batch(statements);
  await audit(env, {
    workspaceId: actor.workspaceId,
    actorEmployeeId: actor.employeeId,
    applicationId,
    eventType: "application_compatibility_role_mode_updated",
    detail: { mode },
  });
  return json(env, requestId, 200, { application: { applicationId, compatibilityRoleMode: mode } });
}
