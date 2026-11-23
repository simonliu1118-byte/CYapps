import { canDeleteVerifiedEmployee } from "./account-lifecycle-testing";
import { requireIdentityAdministrator, requireWorkspaceAdmin, requireWorkspaceSuperAdmin, liveCoreSuperAdmin } from "./admin-auth";
import { sha256Hex } from "./crypto";
import { json, readJsonObject } from "./http";
import { effectiveWorkspaceRole, isCoreAccountApplication, type StoredEmployeeRole } from "./role-access";
import { canonicalSsoReturnUrl } from "./sso-return-url";
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


export async function handleRegisterWorkspaceApplication(
  request: Request, env: Env, requestId: string,
): Promise<Response> {
  const coreApplicationId = request.headers.get("x-identity-application")?.trim().toUpperCase() ?? "";
  const actor = isCoreAccountApplication(env, coreApplicationId)
    ? await requireWorkspaceSuperAdmin(request, env) : null;
  if (!actor) {
    return json(env, requestId, 403, {
      error: { code: "CORE_SUPER_ADMIN_REQUIRED", message: "Current Workspace Super Admin authority through the core account application is required." },
    });
  }
  const body = await readJsonObject(request);
  const applicationId = normalizeApplicationId(body?.applicationId);
  const displayName = typeof body?.displayName === "string" ? body.displayName.trim() : "";
  if (!body || Object.keys(body).some(key => !["applicationId", "displayName", "ssoReturnUrl", "displayNameZh"].includes(key))
      || !applicationId || !displayName || [...displayName].length > 120 || /[\u0000-\u001f\u007f]/.test(displayName)) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_APPLICATION_REGISTRATION", message: "Application ID and display name are required; Workspace and grants cannot be supplied." },
    });
  }
  // ssoReturnUrl: absent = unchanged, null = clear, string = set (canonical form).
  const ssoChange = Object.prototype.hasOwnProperty.call(body, "ssoReturnUrl");
  const ssoReturnUrl = ssoChange && body.ssoReturnUrl !== null ? canonicalSsoReturnUrl(body.ssoReturnUrl) : null;
  if (ssoChange && body.ssoReturnUrl !== null && !ssoReturnUrl) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_SSO_RETURN_URL", message: "Single sign-on return URL must be an https URL on a domain name, without credentials, port, query or fragment." },
    });
  }
  // displayNameZh: absent = unchanged, null = clear, string = set (trimmed, 1-60 chars).
  const zhChange = Object.prototype.hasOwnProperty.call(body, "displayNameZh");
  let displayNameZh: string | null = null;
  if (zhChange && body.displayNameZh !== null) {
    const trimmed = typeof body.displayNameZh === "string" ? body.displayNameZh.trim() : "";
    if (!trimmed || [...trimmed].length > 60 || /[\u0000-\u001f\u007f]/.test(trimmed)) {
      return json(env, requestId, 400, {
        error: { code: "INVALID_APPLICATION_ZH_NAME", message: "Chinese name must be 1 to 60 characters without control characters." },
      });
    }
    displayNameZh = trimmed;
  }
  if (isCoreAccountApplication(env, applicationId)) {
    return json(env, requestId, 409, {
      error: { code: "CORE_APPLICATION_ACCESS_LOCKED", message: "Core account application registration cannot be changed." },
    });
  }
  // The return URL is global catalog metadata. A Super Admin may change it only
  // while the application is not registered by any other Workspace, so one
  // Workspace can never redirect another Workspace's handoff codes.
  const sharedApplication = `SELECT 1 FROM workspace_applications other
    WHERE other.application_id = ?6 AND other.workspace_id <> ?2`;
  if (ssoChange && await env.DB.prepare(`${sharedApplication} LIMIT 1`).bind(
    null, actor.workspaceId, null, null, null, applicationId,
  ).first()) {
    return json(env, requestId, 409, {
      error: { code: "SSO_RETURN_URL_SHARED_APPLICATION", message: "Single sign-on return URL of an application registered by another Workspace cannot be changed here." },
    });
  }

  if (zhChange && await env.DB.prepare(`${sharedApplication} LIMIT 1`).bind(
    null, actor.workspaceId, null, null, null, applicationId,
  ).first()) {
    return json(env, requestId, 409, {
      error: { code: "APPLICATION_SHARED_ACROSS_WORKSPACES", message: "Chinese name of an application registered by another Workspace cannot be changed here." },
    });
  }

  const token = request.headers.get("authorization")!.trim().replace(/^Bearer\s+/i, "").trim();
  const now = new Date().toISOString();
  const values = [await sha256Hex(token), actor.workspaceId, actor.employeeId, coreApplicationId, now, applicationId, displayName];
  const registeredApplication = `SELECT 1 FROM applications a JOIN workspace_applications wa ON wa.application_id = a.application_id
    WHERE a.application_id = ?6 AND a.display_name = ?7 AND a.status = 'active' AND wa.workspace_id = ?2 AND wa.enabled = 1`;
  const results = await env.DB.batch([
    env.DB.prepare(`INSERT INTO applications(application_id, display_name, status, created_at, updated_at)
      SELECT ?6, ?7, 'active', ?5, ?5 WHERE EXISTS (${liveCoreSuperAdmin})
      ON CONFLICT(application_id) DO NOTHING`).bind(...values),
    env.DB.prepare(`INSERT INTO workspace_applications(workspace_id, application_id, enabled, created_at, updated_at)
      SELECT ?2, ?6, 1, ?5, ?5 FROM applications a
      WHERE a.application_id = ?6 AND a.display_name = ?7 AND a.status = 'active' AND EXISTS (${liveCoreSuperAdmin})
      ON CONFLICT(workspace_id, application_id) DO UPDATE SET enabled = 1, updated_at = excluded.updated_at
      WHERE workspace_applications.enabled <> 1`).bind(...values),
    env.DB.prepare(`INSERT INTO identity_audit_events(event_id, workspace_id, actor_employee_id, application_id, event_type, detail_json, created_at)
      SELECT ?8, ?2, ?3, ?6, 'workspace_application_registered', '{"enabled":true}', ?5
      WHERE EXISTS (${liveCoreSuperAdmin}) AND EXISTS (${registeredApplication})`).bind(...values, `audit_${crypto.randomUUID()}`),
    ...(ssoChange ? [
      // Audit first, under exactly the same guard as the update below, so an
      // event exists only when the stored value really changes.
      env.DB.prepare(`INSERT INTO identity_audit_events(event_id, workspace_id, actor_employee_id, application_id, event_type, detail_json, created_at)
        SELECT ?8, ?2, ?3, ?6, 'application_sso_return_url_updated', ?10, ?5
        WHERE EXISTS (${liveCoreSuperAdmin}) AND EXISTS (${registeredApplication})
          AND NOT EXISTS (${sharedApplication})
          AND EXISTS (SELECT 1 FROM applications WHERE application_id = ?6 AND sso_return_url IS NOT ?9)`)
        .bind(...values, `audit_${crypto.randomUUID()}`, ssoReturnUrl, JSON.stringify({ ssoReturnUrl })),
      env.DB.prepare(`UPDATE applications SET sso_return_url = ?9, updated_at = ?5
        WHERE application_id = ?6 AND display_name = ?7 AND status = 'active'
          AND sso_return_url IS NOT ?9
          AND EXISTS (${liveCoreSuperAdmin}) AND EXISTS (${registeredApplication})
          AND NOT EXISTS (${sharedApplication})`)
        .bind(...values, null, ssoReturnUrl),
    ] : []),
    ...(zhChange ? [
      env.DB.prepare(`INSERT INTO identity_audit_events(event_id, workspace_id, actor_employee_id, application_id, event_type, detail_json, created_at)
        SELECT ?8, ?2, ?3, ?6, 'application_zh_name_updated', ?10, ?5
        WHERE EXISTS (${liveCoreSuperAdmin}) AND EXISTS (${registeredApplication})
          AND NOT EXISTS (${sharedApplication})
          AND EXISTS (SELECT 1 FROM applications WHERE application_id = ?6 AND display_name_zh IS NOT ?9)`)
        .bind(...values, `audit_${crypto.randomUUID()}`, displayNameZh, JSON.stringify({ displayNameZh })),
      env.DB.prepare(`UPDATE applications SET display_name_zh = ?9, updated_at = ?5
        WHERE application_id = ?6 AND display_name = ?7 AND status = 'active'
          AND display_name_zh IS NOT ?9
          AND EXISTS (${liveCoreSuperAdmin}) AND EXISTS (${registeredApplication})
          AND NOT EXISTS (${sharedApplication})`)
        .bind(...values, null, displayNameZh),
    ] : []),
    env.DB.prepare(`SELECT EXISTS (${liveCoreSuperAdmin}) AS authorized,
      EXISTS (${registeredApplication}) AS registered,
      (SELECT sso_return_url FROM applications WHERE application_id = ?6) AS sso_return_url,
      (SELECT display_name_zh FROM applications WHERE application_id = ?6) AS display_name_zh`).bind(...values),
  ]);
  const outcome = results[results.length - 1].results?.[0] as
    { authorized?: number; registered?: number; sso_return_url?: string | null; display_name_zh?: string | null } | undefined;
  if (outcome?.authorized !== 1) {
    return json(env, requestId, 403, {
      error: { code: "CORE_SUPER_ADMIN_REQUIRED", message: "Workspace authority or Session changed before registration." },
    });
  }
  if (outcome.registered !== 1) {
    return json(env, requestId, 409, {
      error: { code: "APPLICATION_REGISTRATION_CONFLICT", message: "Application ID already has another display name or is globally disabled; existing registration was preserved." },
    });
  }
  const storedSsoReturnUrl = outcome.sso_return_url ?? null;
  if (ssoChange && storedSsoReturnUrl !== ssoReturnUrl) {
    // Another Workspace registered the application concurrently; the URL was not changed.
    return json(env, requestId, 409, {
      error: { code: "SSO_RETURN_URL_SHARED_APPLICATION", message: "Single sign-on return URL of an application registered by another Workspace cannot be changed here." },
    });
  }
  const storedZhName = outcome.display_name_zh ?? null;
  if (zhChange && storedZhName !== displayNameZh) {
    return json(env, requestId, 409, {
      error: { code: "APPLICATION_SHARED_ACROSS_WORKSPACES", message: "Chinese name of an application registered by another Workspace cannot be changed here." },
    });
  }
  return json(env, requestId, 200, {
    workspaceId: actor.workspaceId,
    application: { applicationId, displayName, displayNameZh: storedZhName, enabled: true, ssoReturnUrl: storedSsoReturnUrl },
  });
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

  const [employees, applications, directAccess] = await Promise.all([
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
              a.sso_return_url,
              a.display_name_zh,
              wa.enabled
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
      sso_return_url: string | null;
      display_name_zh: string | null;
      enabled: number;
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
    testingAccountDeletionAllowed: canDeleteVerifiedEmployee(request, env, actor),
    employees: employeeRows as unknown as JsonValue,
    applications: applicationRows as unknown as JsonValue,
    directAccess: (directAccess.results ?? []) as JsonValue,

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
