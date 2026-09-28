import { createCredentialVerifier, CURRENT_CREDENTIAL_ALGORITHM } from "./crypto";
import { createEmailSender, normalizeAddress } from "./email";
import { json, readJsonObject } from "./http";
import { issueEmailOtp, verifyEmailOtp } from "./otp";
import type { Env, JsonValue } from "./types";

type BootstrapApplication = {
  applicationId: string;
  displayName: string;
};

type BootstrapRequestRow = {
  bootstrap_id: string;
  workspace_id: string;
  workspace_code: string;
  workspace_display_name: string;
  employee_id: string;
  employee_no: string;
  employee_name: string;
  email_normalized: string;
  credential_algorithm: string;
  credential_verifier: string;
  applications_json: string;
  expires_at: string;
  consumed_at: string | null;
};

const BOOTSTRAP_REQUEST_TTL_MS = 15 * 60 * 1000;
const MAX_BOOTSTRAP_APPLICATIONS = 20;

function normalizeWorkspaceCode(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 4 || normalized.length > 32 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

function normalizeDisplayName(value: unknown, max = 120): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length >= 1 && normalized.length <= max ? normalized : null;
}

function normalizeEmployeeNo(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return /^\d{4}$/.test(normalized) ? normalized : null;
}

function normalizeApplicationId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  if (normalized.length < 2 || normalized.length > 64 || /[^A-Z0-9_-]/.test(normalized)) return null;
  return normalized;
}

function normalizeApplications(value: unknown): BootstrapApplication[] | null {
  if (!Array.isArray(value) || value.length < 1 || value.length > MAX_BOOTSTRAP_APPLICATIONS) return null;
  const applications: BootstrapApplication[] = [];
  const seen = new Set<string>();
  for (const item of value) {
    if (!item || typeof item !== "object" || Array.isArray(item)) return null;
    const record = item as Record<string, unknown>;
    const applicationId = normalizeApplicationId(record.applicationId);
    const displayName = normalizeDisplayName(record.displayName);
    if (!applicationId || !displayName || seen.has(applicationId)) return null;
    seen.add(applicationId);
    applications.push({ applicationId, displayName });
  }
  return applications;
}

async function hashText(value: string): Promise<Uint8Array> {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
}

async function constantTimeTextEquals(left: string, right: string): Promise<boolean> {
  const [leftHash, rightHash] = await Promise.all([hashText(left), hashText(right)]);
  let difference = 0;
  for (let index = 0; index < leftHash.length; index += 1) difference |= leftHash[index] ^ rightHash[index];
  return difference === 0;
}

async function bootstrapAuthorized(request: Request, env: Env): Promise<boolean> {
  const configured = env.BOOTSTRAP_SECRET?.trim() ?? "";
  if (new TextEncoder().encode(configured).length < 32) throw new Error("BOOTSTRAP_SECRET_NOT_CONFIGURED");
  const header = request.headers.get("authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(header);
  const supplied = match?.[1]?.trim() ?? "";
  if (!supplied) return false;
  return constantTimeTextEquals(configured, supplied);
}

function emailSenderFrom(env: Env) {
  return createEmailSender({
    provider: env.EMAIL_PROVIDER,
    brevoApiKey: env.BREVO_API_KEY,
    resendApiKey: env.RESEND_API_KEY,
    from: env.EMAIL_FROM,
  });
}

async function workspaceExists(env: Env): Promise<boolean> {
  const row = await env.DB.prepare("SELECT 1 AS present FROM workspaces LIMIT 1").first<{ present: number }>();
  return Boolean(row);
}

async function activePendingBootstrap(env: Env, now: string): Promise<{ bootstrap_id: string; expires_at: string } | null> {
  return env.DB.prepare(
    `SELECT bootstrap_id, expires_at
       FROM bootstrap_requests
      WHERE consumed_at IS NULL
        AND expires_at > ?1
      ORDER BY created_at DESC
      LIMIT 1`
  ).bind(now).first<{ bootstrap_id: string; expires_at: string }>();
}

export async function handleBootstrapStart(request: Request, env: Env, requestId: string): Promise<Response> {
  if (!await bootstrapAuthorized(request, env)) {
    return json(env, requestId, 401, {
      error: { code: "BOOTSTRAP_UNAUTHORIZED", message: "Bootstrap authorization failed." },
    });
  }
  if (await workspaceExists(env)) {
    return json(env, requestId, 409, {
      error: { code: "BOOTSTRAP_ALREADY_COMPLETED", message: "Initial Workspace bootstrap is already complete." },
    });
  }

  const body = await readJsonObject(request);
  const workspace = body?.workspace;
  const employee = body?.employee;
  if (!workspace || typeof workspace !== "object" || Array.isArray(workspace)
      || !employee || typeof employee !== "object" || Array.isArray(employee)) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_BOOTSTRAP_REQUEST", message: "Bootstrap request is invalid." },
    });
  }

  const workspaceRecord = workspace as Record<string, unknown>;
  const employeeRecord = employee as Record<string, unknown>;
  const workspaceCode = normalizeWorkspaceCode(workspaceRecord.code);
  const workspaceDisplayName = normalizeDisplayName(workspaceRecord.displayName);
  const employeeNo = normalizeEmployeeNo(employeeRecord.employeeNo);
  const employeeName = normalizeDisplayName(employeeRecord.displayName);
  let email: string | null = null;
  try {
    if (typeof employeeRecord.email === "string") email = normalizeAddress(employeeRecord.email);
  } catch {
    email = null;
  }
  const password = typeof employeeRecord.password === "string" ? employeeRecord.password : "";
  const applications = normalizeApplications(body?.applications);
  if (!workspaceCode || !workspaceDisplayName || !employeeNo || !employeeName || !email || !applications) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_BOOTSTRAP_REQUEST", message: "Bootstrap request is invalid." },
    });
  }

  const now = new Date();
  const nowIso = now.toISOString();
  const pending = await activePendingBootstrap(env, nowIso);
  if (pending) {
    return json(env, requestId, 409, {
      error: {
        code: "BOOTSTRAP_ALREADY_PENDING",
        message: "A bootstrap verification is already pending.",
        bootstrapId: pending.bootstrap_id,
        expiresAt: pending.expires_at,
      },
    });
  }

  await env.DB.prepare(
    "DELETE FROM bootstrap_requests WHERE consumed_at IS NULL AND expires_at <= ?1"
  ).bind(nowIso).run();

  let credentialVerifier: string;
  try {
    credentialVerifier = await createCredentialVerifier(password);
  } catch {
    return json(env, requestId, 400, {
      error: { code: "INVALID_BOOTSTRAP_REQUEST", message: "Bootstrap request is invalid." },
    });
  }

  const bootstrapId = `boot_${crypto.randomUUID()}`;
  const workspaceId = `ws_${crypto.randomUUID()}`;
  const employeeId = `emp_${crypto.randomUUID()}`;
  const requestExpiresAt = new Date(now.getTime() + BOOTSTRAP_REQUEST_TTL_MS).toISOString();

  await env.DB.prepare(
    `INSERT INTO bootstrap_requests(
       bootstrap_id, workspace_id, workspace_code, workspace_display_name,
       employee_id, employee_no, employee_name, email_normalized,
       credential_algorithm, credential_verifier, applications_json,
       expires_at, created_at, updated_at
     ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?13)`
  ).bind(
    bootstrapId,
    workspaceId,
    workspaceCode,
    workspaceDisplayName,
    employeeId,
    employeeNo,
    employeeName,
    email,
    CURRENT_CREDENTIAL_ALGORITHM,
    credentialVerifier,
    JSON.stringify(applications),
    requestExpiresAt,
    nowIso,
  ).run();

  try {
    const otp = await issueEmailOtp(env, emailSenderFrom(env), {
      purpose: "workspace_bootstrap",
      scopeKey: bootstrapId,
      email,
      subject: "CY Identity 初始帳號驗證碼",
      textPrefix: "正在建立新的 Workspace 與第一位最高管理者。",
    });
    return json(env, requestId, 202, {
      bootstrap: {
        bootstrapId,
        challengeId: otp.challengeId,
        expiresAt: otp.expiresAt,
        resendAfter: otp.resendAfter,
      },
    });
  } catch (error) {
    await env.DB.prepare(
      "DELETE FROM bootstrap_requests WHERE bootstrap_id = ?1 AND consumed_at IS NULL"
    ).bind(bootstrapId).run();
    const code = error instanceof Error ? error.message : "BOOTSTRAP_EMAIL_FAILED";
    const status = code === "OTP_RESEND_COOLDOWN" || code === "OTP_RATE_LIMITED" || code === "EMAIL_DAILY_BUDGET_EXHAUSTED"
      ? 429
      : 503;
    return json(env, requestId, status, {
      error: { code, message: "Bootstrap verification email could not be issued." },
    });
  }
}

export async function handleBootstrapConfirm(request: Request, env: Env, requestId: string): Promise<Response> {
  if (!await bootstrapAuthorized(request, env)) {
    return json(env, requestId, 401, {
      error: { code: "BOOTSTRAP_UNAUTHORIZED", message: "Bootstrap authorization failed." },
    });
  }
  if (await workspaceExists(env)) {
    return json(env, requestId, 409, {
      error: { code: "BOOTSTRAP_ALREADY_COMPLETED", message: "Initial Workspace bootstrap is already complete." },
    });
  }

  const body = await readJsonObject(request);
  const bootstrapId = typeof body?.bootstrapId === "string" ? body.bootstrapId.trim() : "";
  const challengeId = typeof body?.challengeId === "string" ? body.challengeId.trim() : "";
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  if (!bootstrapId || !challengeId || !/^\d{6}$/.test(code)) {
    return json(env, requestId, 400, {
      error: { code: "INVALID_BOOTSTRAP_CONFIRMATION", message: "Bootstrap confirmation is invalid." },
    });
  }

  const now = new Date().toISOString();
  const staged = await env.DB.prepare(
    `SELECT bootstrap_id, workspace_id, workspace_code, workspace_display_name,
            employee_id, employee_no, employee_name, email_normalized,
            credential_algorithm, credential_verifier, applications_json,
            expires_at, consumed_at
       FROM bootstrap_requests
      WHERE bootstrap_id = ?1
      LIMIT 1`
  ).bind(bootstrapId).first<BootstrapRequestRow>();

  if (!staged || staged.consumed_at || staged.expires_at <= now) {
    return json(env, requestId, 400, {
      error: { code: "BOOTSTRAP_CONFIRMATION_FAILED", message: "Bootstrap verification failed." },
    });
  }

  const challenge = await verifyEmailOtp(env, {
    challengeId,
    purpose: "workspace_bootstrap",
    scopeKey: bootstrapId,
    code,
  });
  if (!challenge || challenge.email_normalized !== staged.email_normalized) {
    return json(env, requestId, 400, {
      error: { code: "BOOTSTRAP_CONFIRMATION_FAILED", message: "Bootstrap verification failed." },
    });
  }

  let applications: BootstrapApplication[];
  try {
    const parsed: unknown = JSON.parse(staged.applications_json);
    const normalized = normalizeApplications(parsed);
    if (!normalized) throw new Error("invalid staged application list");
    applications = normalized;
  } catch {
    return json(env, requestId, 500, {
      error: { code: "BOOTSTRAP_STAGING_INVALID", message: "Bootstrap staging data is invalid." },
    });
  }

  const statements: D1PreparedStatement[] = [
    env.DB.prepare(
      `UPDATE email_otp_challenges
          SET consumed_at = ?2, updated_at = ?2
        WHERE challenge_id = ?1
          AND consumed_at IS NULL
          AND delivery_state = 'sent'
          AND expires_at > ?2
          AND attempt_count < max_attempts`
    ).bind(challengeId, now),
    env.DB.prepare(
      `INSERT INTO workspaces(workspace_id, workspace_code, display_name, status, created_at, updated_at)
       VALUES(?1, ?2, ?3, 'bootstrap', ?4, ?4)`
    ).bind(staged.workspace_id, staged.workspace_code, staged.workspace_display_name, now),
    env.DB.prepare(
      `INSERT INTO employees(
         employee_id, workspace_id, employee_no, name, email_normalized,
         email_verified_at, enabled, created_at, updated_at
       ) VALUES(?1, ?2, ?3, ?4, ?5, ?6, 1, ?6, ?6)`
    ).bind(
      staged.employee_id,
      staged.workspace_id,
      staged.employee_no,
      staged.employee_name,
      staged.email_normalized,
      now,
    ),
    env.DB.prepare(
      `INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version, updated_at)
       VALUES(?1, ?2, ?3, 1, ?4)`
    ).bind(staged.employee_id, staged.credential_algorithm, staged.credential_verifier, now),
  ];

  for (const application of applications) {
    statements.push(
      env.DB.prepare(
        `INSERT INTO applications(application_id, display_name, status, created_at, updated_at)
         VALUES(?1, ?2, 'active', ?3, ?3)
         ON CONFLICT(application_id) DO UPDATE SET
           display_name = excluded.display_name,
           status = 'active',
           updated_at = excluded.updated_at`
      ).bind(application.applicationId, application.displayName, now),
    );
    statements.push(
      env.DB.prepare(
        `INSERT INTO workspace_applications(workspace_id, application_id, enabled, created_at, updated_at)
         VALUES(?1, ?2, 1, ?3, ?3)`
      ).bind(staged.workspace_id, application.applicationId, now),
    );
  }

  statements.push(
    env.DB.prepare(
      `UPDATE workspaces
          SET super_admin_employee_id = ?2,
              recovery_email_normalized = ?3,
              recovery_email_verified_at = ?4,
              status = 'active',
              revision = revision + 1,
              updated_at = ?4
        WHERE workspace_id = ?1
          AND status = 'bootstrap'`
    ).bind(staged.workspace_id, staged.employee_id, staged.email_normalized, now),
    env.DB.prepare(
      `UPDATE bootstrap_requests
          SET consumed_at = ?2, updated_at = ?2
        WHERE bootstrap_id = ?1
          AND consumed_at IS NULL`
    ).bind(bootstrapId, now),
    env.DB.prepare(
      `INSERT INTO identity_audit_events(
         event_id, workspace_id, actor_employee_id, target_employee_id,
         event_type, detail_json, created_at
       ) VALUES(?1, ?2, ?3, ?3, 'workspace_bootstrap_completed', ?4, ?5)`
    ).bind(
      `audit_${crypto.randomUUID()}`,
      staged.workspace_id,
      staged.employee_id,
      JSON.stringify({ applicationCount: applications.length }),
      now,
    ),
  );

  try {
    await env.DB.batch(statements);
  } catch {
    return json(env, requestId, 409, {
      error: { code: "BOOTSTRAP_CONFIRMATION_FAILED", message: "Bootstrap could not be committed." },
    });
  }

  return json(env, requestId, 201, {
    workspace: {
      workspaceId: staged.workspace_id,
      employeeId: staged.employee_id,
      employeeNo: staged.employee_no,
      isWorkspaceSuperAdmin: true,
      applicationCount: applications.length,
    } as unknown as JsonValue,
  });
}
