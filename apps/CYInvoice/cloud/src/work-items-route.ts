import { cyIdDevice, usesCyId, type CyIdEnv, CyIdError } from "./cyid";
import { createWorkItem, getWorkItem, transitionWorkItem } from "./work-items";

interface Env extends CyIdEnv { DB: D1Database }
type Manager = { id: string };
const paths = new Set([
  "/v1/work-items/create", "/v1/work-items/get", "/v1/work-items/transition",
]);
function response(status: number, value: Record<string, unknown>): Response {
  return Response.json({ ok: status < 300, ...value }, {
    status, headers: { "cache-control": "no-store", "x-content-type-options": "nosniff" },
  });
}
async function device(request: Request, env: Env) {
  const token = /^Bearer (cydev_[0-9a-f]{64})$/i.exec(request.headers.get("authorization") ?? "")?.[1];
  if (!token) return null;
  const digest = Array.from(new Uint8Array(await crypto.subtle.digest(
    "SHA-256", new TextEncoder().encode(token.toLowerCase()))),
  x => x.toString(16).padStart(2,"0")).join("");
  const row = await env.DB.prepare(`
    SELECT d.device_id, d.workspace_id, d.employee_authority_state
    FROM devices d JOIN workspaces w ON d.workspace_id = w.workspace_id
    WHERE d.token_hash = ?1 AND d.status='active' AND w.status='active'
    LIMIT 1`).bind(digest).first<{
      device_id: string; workspace_id: string; employee_authority_state: string;
    }>();
  if (row) {
    const verified = await cyIdDevice(request, env);
    if (verified.device_id !== row.device_id || verified.workspace_id !== row.workspace_id)
      throw new CyIdError(409,"IDENTITY_WORKSPACE_MISMATCH");
  }
  return row;
}
async function manager(env: Env, authorityState: string): Promise<Manager | null> {
  if (authorityState !== "cloud" || !usesCyId(env)) return null;
  // Only the current CYID authority grants manager permissions. There is no
  // fallback to a local password or a retired Built-in employee table.
  const principal = await env.cyIdContext?.revalidate();
  return principal && (principal.workspaceRole === "SUPER_ADMIN" || principal.workspaceRole === "ADMIN")
    ? { id: principal.employeeId } : null;
}
async function jsonBody(request: Request): Promise<Record<string, unknown> | null> {
  if ((Number(request.headers.get("content-length") || "0")) > 8192) return null;
  const raw = await request.text();
  if (raw.length > 8192) return null;
  try {
    const obj: unknown = JSON.parse(raw);
    return obj && typeof obj === "object" && !Array.isArray(obj)
      ? obj as Record<string, unknown> : null;
  } catch { return null; }
}

// Exactly three POST actions; employee password never travels in query params.
// A missing draft schema is a closed service, not a reason to create tables.
export async function handleWorkItems(request: Request, env: Env): Promise<Response | null> {
  const path = new URL(request.url).pathname;
  if (!paths.has(path)) return null;
  if (!usesCyId(env)) return response(409,{error:{code:"CYID_REQUIRED"}});
  if (request.method !== "POST") return response(405,{error:{code:"METHOD_NOT_ALLOWED"}});
  const d = await device(request,env);
  if (!d) return response(401,{error:{code:"DEVICE_INVALID"}});
  const body = await jsonBody(request);
  if (!body) return response(400,{error:{code:"INVALID_REQUEST"}});
  const m = await manager(env,d.employee_authority_state);
  if (!m) return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
  const itemId = body.itemId;
  if (typeof itemId !== "string" || !/^[a-zA-Z0-9_-]{10,100}$/.test(itemId))
    return response(400,{error:{code:"INVALID_WORK_ITEM"}});
  try {
    if (path === "/v1/work-items/get") {
      const item = await getWorkItem(env.DB,d.workspace_id,itemId);
      return item ? response(200,{item}) : response(404,{error:{code:"NOT_FOUND"}});
    }
    // Revalidation is as late as practical; the SQL guards additionally
    // check Device/Workspace; CYID identity was revalidated immediately above.
    const latest = await manager(env,d.employee_authority_state);
    if (!latest || latest.id !== m.id)
      return response(409,{error:{code:"AUTHORITY_CHANGED"}});
    let changed: boolean;
    if (path === "/v1/work-items/create") {
      // Allowances require a server-frozen exceptional application. The generic
      // manager endpoint must never accept a client-chosen allowance hash.
      if (body.kind === "ALLOWANCE")
        return response(409,{error:{code:"ALLOWANCE_APPLICATION_REQUIRED"}});
      if (typeof body.referenceHash !== "string" || !/^[a-f0-9]{64}$/.test(body.referenceHash) ||
          (body.kind !== "VOID" && body.kind !== "ALLOWANCE" && body.kind !== "ALLOWANCE_VOID") ||
          (body.environment !== "production" && body.environment !== "test"))
        return response(400,{error:{code:"INVALID_WORK_ITEM"}});
      changed = await createWorkItem(env.DB,{
        itemId,workspaceId:d.workspace_id,deviceId:d.device_id,
        environment:body.environment,kind:body.kind,referenceHash:body.referenceHash,
        actorId:latest.id,
      });
    } else {
      if (!Number.isSafeInteger(body.expectedRevision) ||
          body.from !== "open" && body.from !== "review" ||
          body.to !== "review" && body.to !== "closed")
        return response(400,{error:{code:"INVALID_TRANSITION"}});
      changed = await transitionWorkItem(env.DB,{
        itemId,workspaceId:d.workspace_id,deviceId:d.device_id,actorId:latest.id,
        expectedRevision:body.expectedRevision as number,
        from:body.from as "open"|"review",to:body.to as "review"|"closed",
      });
    }
    // A failed CAS or missing auth fence returns conflict, never an automatic retry.
    if (!changed) return response(409,{error:{code:"WORK_ITEM_CONFLICT"}});
    return response(200,{item:await getWorkItem(env.DB,d.workspace_id,itemId)});
  } catch (error) {
    if (error instanceof Error && (error.message === "INVALID_WORK_ITEM" ||
        error.message === "INVALID_WORK_ITEM_TRANSITION"))
      return response(400,{error:{code:"INVALID_REQUEST"}});
    if (error instanceof Error && /UNIQUE constraint failed/i.test(error.message))
      return response(409,{error:{code:"WORK_ITEM_CONFLICT"}});
    // The optional SQL draft is deliberately not installed on current D1.
    if (error instanceof Error && /no such table: work_items/i.test(error.message))
      return response(503,{error:{code:"WORK_ITEMS_NOT_READY"}});
    // A write might have committed despite a lost D1 response: caller must
    // reconcile by GET, never reissue an AMEGO action.
    return response(503,{error:{code:"WORK_ITEM_RESULT_UNKNOWN"}});
  }
}
