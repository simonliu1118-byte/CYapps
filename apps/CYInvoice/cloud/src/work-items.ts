// V3.1 non-allowance Work Item storage. Never an invoice ledger, official status or AMEGO action.
// Calling route must revalidate employee authority. Every write independently
// fences the Device and Workspace at SQL execution time.
export type WorkItemKind = "VOID" | "UPLOAD_ISSUE";
export const uploadIssueCodes = [
  "invoice_list_failed","query_failed","remote_not_found",
  "unknown_remote_not_found","reconciliation_failed","ambiguous_match",
  "local_write_failed","upload_failed","duplicate_order_id",
] as const;
export type UploadIssueCode = typeof uploadIssueCodes[number];
export type WorkItemStatus = "open" | "review" | "closed";
export const uploadResolutionCodes = [
  "resolved_locally","checked_official","duplicate_report",
  "not_actionable","needs_followup"
] as const;
export type UploadResolutionCode=typeof uploadResolutionCodes[number];

export interface NewWorkItem {
  itemId: string; workspaceId: string; deviceId: string;
  environment: "production" | "test"; kind: WorkItemKind;
  referenceHash: string; actorId: string;
}
export interface ReportUploadIssue {
  itemId:string; workspaceId:string; deviceId:string; actorId:string;
  environment:"production"|"test"; referenceHash:string; issueCode:UploadIssueCode;
}
export async function reportUploadIssue(db:D1Database,input:ReportUploadIssue):Promise<boolean>{
  if(!validId(input.itemId)||!validId(input.deviceId)||!validActor(input.actorId)||
     !/^[a-f0-9]{64}$/.test(input.referenceHash)||
     !["test","production"].includes(input.environment)||
     !uploadIssueCodes.includes(input.issueCode))
     throw new Error("INVALID_UPLOAD_ISSUE");
  const now=new Date().toISOString();
  // Data from Windows is only a local problem REPORT, never proof of any
  // provider action. No recipient/order/details/secrets or AMEGO call.
  const result=await db.prepare(`
    INSERT INTO work_items(
      work_item_id,workspace_id,opened_by_device_id,updated_by_device_id,
      environment,kind,issue_code,reference_hash,actor_id,reported_by_actor_id,
      created_at,updated_at)
    SELECT ?1,?2,?3,?3,?4,'UPLOAD_ISSUE',?5,?6,?7,?7,?8,?8
    WHERE EXISTS (
      SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id
      WHERE d.device_id=?3 AND d.workspace_id=?2
        AND d.status='active' AND w.status='active'
        AND d.employee_authority_state='cloud')`
  ).bind(input.itemId,input.workspaceId,input.deviceId,input.environment,
    input.issueCode,input.referenceHash,input.actorId,now).run();
  return result.meta.changes===1;
}

export interface WorkItemTransition {
  itemId: string; workspaceId: string; deviceId: string; actorId: string;
  expectedRevision: number; from: "open" | "review"; to: "review" | "closed";
  resolutionCode?:UploadResolutionCode;
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
      input.kind !== "VOID")
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
    `
  ).bind(input.itemId, input.workspaceId, input.deviceId, input.environment,
         input.kind, input.referenceHash, input.actorId, now).run();
  return result.meta.changes === 1;
}

// A stale revision, revoked Device, wrong Workspace, or invalid transition
// changes zero rows; no blind retry. "closed" closes the manual workflow only.
export async function transitionWorkItem(db: D1Database, input: WorkItemTransition): Promise<boolean> {
  if (!validId(input.itemId) || !validId(input.deviceId) || !validActor(input.actorId) ||
      (input.resolutionCode!==undefined &&
       !uploadResolutionCodes.includes(input.resolutionCode)) ||
      !Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 1 ||
      !((input.from === "open" && input.to === "review") ||
        (input.from === "review" && input.to === "closed")))
    throw new Error("INVALID_WORK_ITEM_TRANSITION");
  const now = new Date().toISOString();
  const result = await db.prepare(`
    UPDATE work_items SET status = ?1, revision = revision + 1,
      updated_by_device_id = ?2, actor_id = ?3, updated_at = ?4,
      reviewed_by_actor_id = CASE WHEN kind='UPLOAD_ISSUE' AND ?1='review'
        THEN ?3 ELSE reviewed_by_actor_id END,
      closed_by_actor_id = CASE WHEN kind='UPLOAD_ISSUE' AND ?1='closed'
        THEN ?3 ELSE closed_by_actor_id END,
      resolution_code = CASE WHEN kind='UPLOAD_ISSUE' AND ?1='closed'
        THEN ?9 ELSE resolution_code END
    WHERE work_item_id = ?5 AND workspace_id = ?6 AND status = ?7 AND revision = ?8
      AND (kind<>'UPLOAD_ISSUE' OR ?1<>'closed' OR
        ?9 IN ('resolved_locally','checked_official','duplicate_report',
          'not_actionable','needs_followup'))
      AND EXISTS (
        SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id = d.workspace_id
        WHERE d.device_id = ?2 AND d.workspace_id = ?6
          AND d.status = 'active' AND w.status = 'active')
      `
  ).bind(input.to, input.deviceId, input.actorId, now, input.itemId,
         input.workspaceId, input.from, input.expectedRevision,
         input.resolutionCode??null).run();
  return result.meta.changes === 1;
}

export async function getWorkItem(db: D1Database, workspaceId: string, itemId: string) {
  if (!validId(itemId)) throw new Error("INVALID_WORK_ITEM");
  return db.prepare(`
    SELECT work_item_id, workspace_id, environment, kind, reference_hash,
      status, revision, updated_by_device_id, actor_id,
      issue_code,reported_by_actor_id,reviewed_by_actor_id,closed_by_actor_id,
      resolution_code,created_at,updated_at
    FROM work_items WHERE workspace_id = ?1 AND work_item_id = ?2
    LIMIT 1`).bind(workspaceId, itemId).first();
}


// Manager-only, bounded exceptional Work Item index. The original invoices,
// hashes, actor/device identifiers, templates and secrets are intentionally
// NOT in the returned projection. This is not a source-of-truth invoice list.
export async function listWorkItems(db:D1Database,workspaceId:string,options:{
  kind?:WorkItemKind;
  status?:"open"|"review"|"closed";
  limit:number; beforeCreatedAt?:string; beforeItemId?:string;
}){
  const items=await db.prepare(
    "SELECT work_item_id,environment,kind,issue_code,status,revision,created_at,updated_at "+
    "FROM work_items WHERE workspace_id=?1 "+
    "AND (?2 IS NULL OR kind=?2) "+
    "AND ((?3 IS NULL AND status IN ('open','review')) OR status=?3) "+
    "AND (?4 IS NULL OR created_at<?4 OR (created_at=?4 AND work_item_id<?5)) "+
    "ORDER BY created_at DESC,work_item_id DESC LIMIT ?6"
  ).bind(workspaceId,options.kind??null,options.status??null,
    options.beforeCreatedAt??null,options.beforeItemId??null,
    options.limit+1).all<{
      work_item_id:string;environment:string;kind:string;issue_code:string|null;status:string;
      revision:number;created_at:string;updated_at:string;
    }>();
  const page=items.results.slice(0,options.limit);
  const last=page.at(-1);
  return {items:page,nextCursor:items.results.length>options.limit&&last?
    {createdAt:last.created_at,itemId:last.work_item_id}:null};
}
