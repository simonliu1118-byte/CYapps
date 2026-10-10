// CYInvoice owns Device transport; CYID remains the only online Employee authority.
export interface CyIdEnv {
  DB: D1Database;
  CYID_ENABLED?: string;
  IDENTITY?: Fetcher;
  IDENTITY_APPLICATION_ID?: string;
  IDENTITY_WORKSPACE_ID?: string;
  IDENTITY_CYINVOICE_WORKSPACE_ID?: string;
  cyIdContext?: { revalidate: () => Promise<CyIdPrincipal>; verifiedEmail: () => Promise<string> };
}

export interface CyIdPrincipal {
  workspaceId: string;
  employeeId: string;
  employeeNo: string;
  displayName: string;
  workspaceRole: "SUPER_ADMIN" | "ADMIN" | "USER";
  isIdentityAdmin: boolean;
  emailVerified: boolean;
  isWorkspaceSuperAdmin: boolean;
  credentialVersion: number;
  employeeRevision: number;
}

export class CyIdError extends Error {
  constructor(public status: number, public code: string) { super(code); }
}

export function usesCyId(env: CyIdEnv): boolean {
  if (env.CYID_ENABLED !== undefined && !["true", "false"].includes(env.CYID_ENABLED))
    throw new CyIdError(503, "IDENTITY_CONFIGURATION_INVALID");
  return env.CYID_ENABLED === "true";
}

export function cyIdBinding(env: CyIdEnv) {
  if (!usesCyId(env) || !env.IDENTITY || !env.IDENTITY_APPLICATION_ID
      || !/^[A-Z0-9_-]{2,64}$/.test(env.IDENTITY_APPLICATION_ID)
      || !env.IDENTITY_WORKSPACE_ID || !env.IDENTITY_CYINVOICE_WORKSPACE_ID)
    throw new CyIdError(503, "IDENTITY_CONFIGURATION_INVALID");
  return { applicationId: env.IDENTITY_APPLICATION_ID, identityWorkspaceId: env.IDENTITY_WORKSPACE_ID,
    workspaceId: env.IDENTITY_CYINVOICE_WORKSPACE_ID, consumerVersion: "1.0.2" };
}

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
  return value as Record<string, unknown>;
}

function validatePrincipal(value: unknown, env: CyIdEnv): CyIdPrincipal {
  const p = object(value);
  const binding = cyIdBinding(env);
  if (p.workspaceId !== binding.identityWorkspaceId || typeof p.employeeId !== "string" || !p.employeeId
      || p.employeeId.length > 100 || typeof p.employeeNo !== "string" || !/^\d{4}$/.test(p.employeeNo)
      || typeof p.displayName !== "string" || !p.displayName.trim() || p.displayName.length > 120
      || !["SUPER_ADMIN", "ADMIN", "USER"].includes(String(p.workspaceRole))
      || typeof p.isIdentityAdmin !== "boolean" || typeof p.emailVerified !== "boolean"
      || p.isWorkspaceSuperAdmin !== (p.workspaceRole === "SUPER_ADMIN")
      || (p.isIdentityAdmin && p.workspaceRole !== "ADMIN")
      || !Number.isSafeInteger(p.credentialVersion) || Number(p.credentialVersion) < 1
      || !Number.isSafeInteger(p.employeeRevision) || Number(p.employeeRevision) < 1)
    throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
  // Only canonical fields cross the boundary. Never forward a provider token/verifier.
  return { workspaceId: String(p.workspaceId), employeeId: p.employeeId, employeeNo: p.employeeNo,
    displayName: p.displayName, workspaceRole: p.workspaceRole as CyIdPrincipal["workspaceRole"],
    isIdentityAdmin: p.isIdentityAdmin, emailVerified: p.emailVerified,
    isWorkspaceSuperAdmin: Boolean(p.isWorkspaceSuperAdmin), credentialVersion: Number(p.credentialVersion),
    employeeRevision: Number(p.employeeRevision) };
}

async function identityRequest(env: CyIdEnv, path: string, body?: unknown, token?: string, method = "POST") {
  const binding = cyIdBinding(env);
  let response: Response;
  try {
    response = await env.IDENTITY!.fetch(new Request(`https://identity.internal${path}`, {
      method, signal: AbortSignal.timeout(8000), headers: {
        "content-type": "application/json", "x-identity-application": binding.applicationId,
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      }, body: body === undefined ? undefined : JSON.stringify(body),
    }));
  } catch { throw new CyIdError(503, "IDENTITY_UNAVAILABLE"); }
  let parsed: Record<string, unknown>;
  try { parsed = object(await response.json()); }
  catch { throw new CyIdError(502, "IDENTITY_UNAVAILABLE"); }
  if (!response.ok || parsed.ok !== true) {
    const code = typeof parsed.error === "object" && parsed.error ? object(parsed.error).code : "";
    if (response.status === 429) throw new CyIdError(429, "LOGIN_RATE_LIMITED");
    if (response.status === 403 || code === "APPLICATION_ACCESS_DENIED") throw new CyIdError(403, "ACCESS_DENIED");
    if (response.status === 401) throw new CyIdError(401, path.endsWith("login") ? "LOGIN_FAILED" : "AUTH_INVALID");
    if (response.status === 400) throw new CyIdError(400, "INVALID_LOGIN_REQUEST");
    throw new CyIdError(503, "IDENTITY_UNAVAILABLE");
  }
  return parsed;
}

// One lifecycle owner. Every call has a fresh CYID Session; no consumer session store,
// no handler re-entry, no durable token, no retry that could replay a business mutation.
export async function withCyIdSession<T>(env: CyIdEnv, employeeNo: unknown, password: unknown,
  work: (principal: CyIdPrincipal, revalidate: () => Promise<CyIdPrincipal>, verifiedEmail: () => Promise<string>) => Promise<T>): Promise<T> {
  if (typeof employeeNo !== "string" || !/^\d{4}$/.test(employeeNo)
      || typeof password !== "string" || Array.from(password).length < 8 || Array.from(password).length > 16)
    throw new CyIdError(400, "INVALID_LOGIN_REQUEST");
  const binding = cyIdBinding(env);
  const login = await identityRequest(env, "/v1/identity/login", {
    workspaceId: binding.identityWorkspaceId, applicationId: binding.applicationId, employeeNo, password,
  });
  let token: string | undefined;
  try {
    const session = object(login.session);
    if (typeof session.token === "string" && /^cyid_[0-9a-f]{64}$/.test(session.token)) token = session.token;
    if (!token || login.passwordChangeRequired || login.firstLogin
        || typeof session.expiresAt !== "string" || Date.parse(session.expiresAt) <= Date.now()
        || !Number.isFinite(Date.parse(session.expiresAt))) throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
    const initial = validatePrincipal(login.principal, env);
    if (initial.employeeNo !== employeeNo) throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
    const revalidate = async () => {
      const resolved = await identityRequest(env, "/v1/identity/session/resolve", undefined, token);
      const principal = validatePrincipal(resolved.principal, env);
      if (principal.employeeId !== initial.employeeId || principal.employeeNo !== employeeNo)
        throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
      return principal;
    };
    const verifiedEmail = async () => {
      const principal = await revalidate();
      if (principal.workspaceRole !== "SUPER_ADMIN" || !principal.emailVerified)
        throw new CyIdError(403, "ACCESS_DENIED");
      const snapshot = await identityRequest(env, "/v1/admin/identity/snapshot", undefined, token, "GET");
      if (!Array.isArray(snapshot.employees)) throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
      const employee = snapshot.employees.map(object).find(e => e.employee_id === principal.employeeId);
      if (!employee || !employee.email_verified_at || typeof employee.email_normalized !== "string"
          || !employee.email_normalized.includes("@") || /[\r\n\s]/.test(employee.email_normalized))
        throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
      if (snapshot.workspaceId !== principal.workspaceId) throw new CyIdError(502, "IDENTITY_UNAVAILABLE");
      return employee.email_normalized;
    };
    return await work(await revalidate(), revalidate, verifiedEmail);
  } finally {
    // Logout failure never changes/retries the business result. A lost token remains
    // server-only and expires/revokes under CYID policy, not a local "logged out" claim.
    if (token) {
      try { await identityRequest(env, "/v1/identity/logout", undefined, token); }
      catch { console.warn("CYID_LOGOUT_UNCONFIRMED"); }
    }
  }
}

export async function cyIdDevice(request: Request, env: CyIdEnv) {
  const raw = /^Bearer (cydev_[0-9a-f]{64})$/i.exec(request.headers.get("authorization") ?? "")?.[1];
  if (!raw) throw new CyIdError(401, "DEVICE_INVALID");
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(raw)));
  const hash = Array.from(digest, x => x.toString(16).padStart(2, "0")).join("");
  const device = await env.DB.prepare(`SELECT d.device_id, d.workspace_id FROM devices d
    JOIN workspaces w ON w.workspace_id = d.workspace_id
    WHERE d.token_hash = ?1 AND d.status = 'active' AND w.status = 'active'`).bind(hash)
    .first<{ device_id: string; workspace_id: string }>();
  if (!device) throw new CyIdError(401, "DEVICE_INVALID");
  if (usesCyId(env) && device.workspace_id !== cyIdBinding(env).workspaceId)
    throw new CyIdError(409, "IDENTITY_WORKSPACE_MISMATCH");
  return device;
}

export function cyIdErrorResponse(error: unknown): Response {
  const normalized = error instanceof CyIdError ? error : new CyIdError(503, "IDENTITY_UNAVAILABLE");
  return Response.json({ ok: false, error: { code: normalized.code } }, {
    status: normalized.status, headers: { "cache-control": "no-store" },
  });
}

export async function handleCyId(request: Request, env: CyIdEnv): Promise<Response | null> {
  const path = new URL(request.url).pathname;
  if (path !== "/v1/identity-provider" && path !== "/v1/cyid/authenticate") return null;
  try {
    if (path === "/v1/identity-provider" && request.method === "GET") {
      const device = await cyIdDevice(request, env);
      if (usesCyId(env)) {
        cyIdBinding(env);
        try {
          const health = await env.IDENTITY!.fetch(new Request("https://cyid.private/v1/health", {
            signal: AbortSignal.timeout(4000),
          }));
          const body = object(await health.json());
          if (!health.ok || body.status !== "ok" || body.identity !== "ready")
            throw new CyIdError(503, "IDENTITY_UNAVAILABLE");
        } catch { throw new CyIdError(503, "IDENTITY_UNAVAILABLE"); }
      }
      return Response.json({ ok: true, provider: usesCyId(env) ? "CYID" : "BUILT_IN",
        workspaceId: device.workspace_id, deviceId: device.device_id,
        ...(usesCyId(env) ? cyIdBinding(env) : {}) }, { headers: { "cache-control": "no-store" } });
    }
    if (path !== "/v1/cyid/authenticate" || request.method !== "POST") throw new CyIdError(405, "METHOD_NOT_ALLOWED");
    if (!usesCyId(env)) throw new CyIdError(409, "IDENTITY_PROVIDER_MISMATCH");
    const device = await cyIdDevice(request, env);
    let body: Record<string, unknown>;
    try { body = object(await request.json()); } catch { throw new CyIdError(400, "INVALID_LOGIN_REQUEST"); }
    return await withCyIdSession(env, body.employeeNo, body.password, async (principal) => {
      // Device could have been revoked while CYID was being contacted.
      await cyIdDevice(request, env);
      return Response.json({ ok: true, provider: "CYID",
        deviceId: device.device_id, ...cyIdBinding(env), principal }, { headers: { "cache-control": "no-store" } });
    });
  } catch (error) { return cyIdErrorResponse(error); }
}
