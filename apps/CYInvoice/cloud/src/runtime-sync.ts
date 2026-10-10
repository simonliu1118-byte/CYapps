import { readDeviceSelfStatus } from "./device-self-status";
import { employeeSnapshot } from "./employee-authority";
import { usesCyId, cyIdBinding, cyIdDevice, CyIdError, type CyIdEnv } from "./cyid";

interface Env extends CyIdEnv { APP_ENV: string; API_VERSION: string; SCHEMA_VERSION: string }

export async function handleRuntimeSync(request: Request, env: Env): Promise<Response | null> {
  if (request.method !== "POST" || new URL(request.url).pathname !== "/v1/runtime/sync") return null;
  const requestId = crypto.randomUUID();
  const deviceResponse = await readDeviceSelfStatus(request, env, requestId);
  if (!deviceResponse.ok) return deviceResponse;
  const self = await deviceResponse.json<Record<string, unknown>>();
  const result: Record<string, unknown> = { ok: true, device: { deviceId: self.deviceId, workspaceId: self.workspaceId,
    status: self.status, workspaceStatus: self.workspaceStatus, revokedAt: self.revokedAt } };
  const respond = () => Response.json(result, { headers: { "cache-control": "no-store" } });
  // Return only terminal self-state. Provider state must never hide an explicit revoke.
  if (self.status === "revoked" || self.workspaceStatus !== "active") return respond();
  let body: { synchronize?: boolean; authorities?: unknown[] };
  try { body = await request.json(); } catch { return Response.json({ ok: false }, { status: 400 }); }
  if (!body || typeof body.synchronize !== "boolean" || !Array.isArray(body.authorities) || body.authorities.length > 10_000)
    return Response.json({ ok: false }, { status: 400 });
  try {
    if (usesCyId(env)) {
      const binding = cyIdBinding(env);
      await cyIdDevice(request, env);
      result.binding = { provider: "CYID", ...binding };
      if (body.synchronize) {
        if (!env.IDENTITY_AUTHORITY) throw new CyIdError(503, "IDENTITY_CONFIGURATION_INVALID");
        const sync = await env.IDENTITY_AUTHORITY.invalidateCache({ workspaceId: binding.identityWorkspaceId,
          applicationId: binding.applicationId, authorities: body.authorities }) as Record<string, unknown>;
        if (!sync || sync.workspaceId !== binding.identityWorkspaceId || sync.applicationId !== binding.applicationId
            || typeof sync.available !== "boolean" || !Array.isArray(sync.invalidated)
            || sync.invalidated.some(id => typeof id !== "string")) throw new CyIdError(502, "IDENTITY_RESPONSE_INVALID");
        result.permissions = { ok: sync.available, invalidated: sync.invalidated,
          ...(sync.available ? {} : { code: "ACCESS_DENIED" }) };
      } else {
        const health = await env.IDENTITY!.fetch(new Request("https://cyid.private/v1/health", { signal: AbortSignal.timeout(4000) }));
        const status = await health.json<Record<string, unknown>>();
        if (!health.ok || status.identity !== "ready") throw new Error("IDENTITY_UNAVAILABLE");
      }
    } else {
      result.binding = { provider: "BUILT_IN", workspaceId: self.workspaceId };
      if (body.synchronize) {
        const response = await employeeSnapshot(request, env, requestId);
        const snapshot = await response.json<Record<string, unknown>>();
        result.permissions = response.ok ? { ok: true, employeeSnapshot: snapshot.employeeSnapshot }
          : { ok: false, code: (snapshot.error as { code?: string })?.code ?? "IDENTITY_UNAVAILABLE" };
      }
    }
  } catch (error) {
    result.permissions = { ok: false, code: error instanceof CyIdError ? error.code : "IDENTITY_UNAVAILABLE" };
  }
  // Check even when the provider failed while revocation was occurring.
  const final = await readDeviceSelfStatus(request, env, requestId);
  if (!final.ok) return final;
  const latest = await final.json<Record<string, unknown>>();
  if (latest.status === "revoked" || latest.workspaceStatus !== "active") {
    result.device = { deviceId: latest.deviceId, workspaceId: latest.workspaceId, status: latest.status,
      workspaceStatus: latest.workspaceStatus, revokedAt: latest.revokedAt };
    delete result.binding;
    delete result.permissions;
  }
  return respond();
}
