// Internal V3 Work Item storage, not a public route or AMEGO operation.
// Calling route must revalidate employee authority. Every write independently
// fences the Device and Workspace at SQL execution time.
export type WorkItemKind = "VOID" | "ALLOWANCE" | "ALLOWANCE_VOID";
export type WorkItemStatus = "open" | "review" | "closed";

export interface NewWorkItem {
  itemId: string; workspaceId: string; deviceId: string;
  environment: "production" | "test"; kind: WorkItemKind;
  referenceHash: string; actorId: string; builtInVerifier?: string;
}
export interface WorkItemTransition {
  itemId: string; workspaceId: string; deviceId: string; actorId: string;
  expectedRevision: number; from: "open" | "review"; to: "review" | "closed";
  builtInVerifier?: string;
}

function validId(value: string): boolean {
  return typeof value === "string" && /^[a-zA-Z0-9_-]{10,100}$/.test(value);
}
function validActor(value: string): boolean {
  return typeof value === "string" && value.length >= 1 && value.length <= 100 &&
    !/[\u0000-\u001f\u007f]/u.test(value);
}

// Duplicate source references are conflicts. A lost DB response is reconciled
// by read, not by creating another item; no call here can issue an invoice.
export async function createWorkItem(db: D1Database, input: NewWorkItem): Promise<boolean> {
  if (!validId(input.itemId) || !validId(input.deviceId) || !validActor(input.actorId) ||
      !/^[a-f0-9]{64}$/.test(input.referenceHash) ||
      !["production", "test"].includes(input.environment) ||
      !["VOID", "ALLOWANCE", "ALLOWANCE_VOID"].includes(input.kind))
    throw new Error("INVALID_WORK_ITEM");
  const now = new Date().toISOString();
  const result = await db.prepare(`
    INSERT INTO work_items (
      work_item_id, workspace_id, opened_by_device_id, updated_by_device_id,
      environment, kind, reference_hash, actor_id, created_at, updated_at)
    SELECT ?1, ?2, ?3, ?3, ?4, ?5, ?6, ?7, ?8, ?8
    WHERE EXISTS (
      SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id = d.workspace_id
      WHERE d.device_id = ?3 AND d.workspace_id = ?2
        AND d.status = 'active' AND w.status = 'active')
    ${input.builtInVerifier === undefined ? "" : `
    AND EXISTS (SELECT 1 FROM cloud_employees e
      WHERE e.employee_id = ?7 AND e.workspace_id = ?2
        AND e.enabled = 1 AND e.role IN ('ADMIN','SUPER_ADMIN')
        AND e.credential_verifier = ?9)`}`
  ).bind(input.itemId, input.workspaceId, input.deviceId, input.environment,
         input.kind, input.referenceHash, input.actorId, now,
         ...(input.builtInVerifier === undefined ? [] : [input.builtInVerifier])).run();
  return result.meta.changes === 1;
}

// A stale revision, revoked Device, wrong Workspace, or invalid transition
// changes zero rows; no blind retry. "closed" closes the manual workflow only.
export async function transitionWorkItem(db: D1Database, input: WorkItemTransition): Promise<boolean> {
  if (!validId(input.itemId) || !validId(input.deviceId) || !validActor(input.actorId) ||
      !Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 1 ||
      !((input.from === "open" && input.to === "review") ||
        (input.from === "review" && input.to === "closed")))
    throw new Error("INVALID_WORK_ITEM_TRANSITION");
  const now = new Date().toISOString();
  const result = await db.prepare(`
    UPDATE work_items SET status = ?1, revision = revision + 1,
      updated_by_device_id = ?2, actor_id = ?3, updated_at = ?4
    WHERE work_item_id = ?5 AND workspace_id = ?6 AND status = ?7 AND revision = ?8
      AND EXISTS (
        SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id = d.workspace_id
        WHERE d.device_id = ?2 AND d.workspace_id = ?6
          AND d.status = 'active' AND w.status = 'active')
      ${input.builtInVerifier === undefined ? "" : `
      AND EXISTS (SELECT 1 FROM cloud_employees e
        WHERE e.employee_id = ?3 AND e.workspace_id = ?6
          AND e.enabled = 1 AND e.role IN ('ADMIN','SUPER_ADMIN')
          AND e.credential_verifier = ?9)`}`
  ).bind(input.to, input.deviceId, input.actorId, now, input.itemId,
         input.workspaceId, input.from, input.expectedRevision,
         ...(input.builtInVerifier === undefined ? [] : [input.builtInVerifier])).run();
  return result.meta.changes === 1;
}

export async function getWorkItem(db: D1Database, workspaceId: string, itemId: string) {
  if (!validId(itemId)) throw new Error("INVALID_WORK_ITEM");
  return db.prepare(`
    SELECT work_item_id, workspace_id, environment, kind, reference_hash,
      status, revision, updated_by_device_id, actor_id, created_at, updated_at
    FROM work_items WHERE workspace_id = ?1 AND work_item_id = ?2
    LIMIT 1`).bind(workspaceId, itemId).first();
}
