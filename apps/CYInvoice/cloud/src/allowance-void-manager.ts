// V3 DRAFT: server-owned ALLOWANCE_VOID manager actions.
// Never accepts a browser-supplied seller, AppKey, official result,
// cancellation number, reviewer, or D1 state. Operations require the CYID
// app-scoped revalidation performed again by the caller after live I/O.
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialAllowanceReadOnly} from "./amego-allowance-query";
import {readOfficialAllowanceVoidStatus} from "./amego-allowance-status";
import {postAllowanceVoidOnce} from "./amego-allowance-void";
import {scanAllowanceExceptionPages} from "./amego-allowance-list";
export type VoidManagerAction="approve"|"send-once"|"status-readback"|"close"|"reconcile";
export interface VoidManagerTarget{
  workspaceId:string;workItemId:string;deviceId:string;expectedRevision:number;
  environment:"test"|"production";encryptionKeyHex:string;
  employeeId:string;
}
export interface VoidManagerAuthority{
  revalidate():Promise<{workspaceId:string;employeeId:string;
    role:"ADMIN"|"SUPER_ADMIN"|"USER"}|null>;
}
interface VoidSource{
  work_item_id:string;workspace_id:string;allowance_number:string;
  original_invoice_number:string;seller_invoice:string;
  company_settings_revision:number;environment:string;
  reference_hash:string;work_reference_hash:string;
  work_revision:number;work_status:string;
}
interface PriorApproval{
  approval_id:string;official_snapshot_sha256:string;
  valid_until_utc:string;
}
const ok=(x:Awaited<ReturnType<VoidManagerAuthority["revalidate"]>>,
 t:VoidManagerTarget)=>x?.employeeId===t.employeeId &&
 x.workspaceId===t.workspaceId && (x.role==="ADMIN"||x.role==="SUPER_ADMIN");
function precondition(t:VoidManagerTarget){
  if(!/^[A-Za-z0-9_-]{10,100}$/.test(t.workItemId)||
     !t.workspaceId||!t.deviceId||
     !Number.isSafeInteger(t.expectedRevision)||t.expectedRevision<1||
     !/^[0-9a-fA-F]{64}$/.test(t.encryptionKeyHex))
    throw new Error("ALLOWANCE_VOID_MANAGER_INVALID");
}
async function liveSource(db:D1Database,t:VoidManagerTarget):Promise<VoidSource>{
  const row=await db.prepare(
    "SELECT r.work_item_id,r.workspace_id,r.allowance_number,"+
    "r.original_invoice_number,r.seller_invoice,r.company_settings_revision,"+
    "r.environment,r.reference_hash,wi.reference_hash AS work_reference_hash,"+
    "wi.revision AS work_revision,wi.status AS work_status "+
    "FROM allowance_void_requests r JOIN work_items wi "+
    "ON wi.work_item_id=r.work_item_id AND wi.workspace_id=r.workspace_id "+
    "WHERE r.workspace_id=?1 AND r.work_item_id=?2 AND wi.kind='ALLOWANCE_VOID'"
  ).bind(t.workspaceId,t.workItemId).first<VoidSource>();
  if(!row||row.environment!==t.environment||
    row.reference_hash!==row.work_reference_hash||
    row.work_revision!==t.expectedRevision||row.work_status!=="review")
    throw new Error("ALLOWANCE_VOID_SOURCE_CHANGED");
  return row;
}
async function officialIssuer(db:D1Database,t:VoidManagerTarget,
 source:VoidSource){
  const company=await loadAmegoCompanySettingsForGateway(db,
    t.workspaceId,t.encryptionKeyHex);
  if(company.sellerInvoice!==source.seller_invoice ||
    company.revision!==source.company_settings_revision)
    throw new Error("ALLOWANCE_VOID_COMPANY_CHANGED");
  return company;
}
async function inspect(db:D1Database,t:VoidManagerTarget,source:VoidSource){
  const company=await officialIssuer(db,t,source);
  const p=await queryOfficialAllowanceReadOnly({
    sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
    allowanceNumber:source.allowance_number,
    originalInvoiceNumber:source.original_invoice_number
  });
  if(!p.possibleMatch||p.invoiceType!=="D0401"||
     p.uploadStatus!==99||p.cancelDate!==0)
    throw new Error("ALLOWANCE_VOID_OFFICIAL_NOT_ELIGIBLE");
  return{company,original:p};
}
async function authorized(t:VoidManagerTarget,authority:VoidManagerAuthority){
  const p=await authority.revalidate();
  if(!ok(p,t))throw new Error("ALLOWANCE_VOID_MANAGER_AUTH_CHANGED");
}
const readback=(id:string,t:VoidManagerTarget,official:{
  allowanceNumber:string;type:string;uploadStatus:number;
  officialCancelled:boolean;definitiveOpen:boolean;
  checkedAtUtc:string;statusEvidenceSha256:string;
})=>({
  itemId:id,revision:t.expectedRevision,
  allowanceNumber:official.allowanceNumber,officialType:official.type,
  officialUploadStatus:official.uploadStatus,
  officialCancelled:official.officialCancelled,
  definitiveOpen:official.definitiveOpen,
  checkedAtUtc:official.checkedAtUtc,
  evidenceSha256:official.statusEvidenceSha256,
  canAutoRetry:false,canAutoClose:false
});
export async function managerAllowanceVoidAction(
 action:VoidManagerAction,db:D1Database,t:VoidManagerTarget,
 authority:VoidManagerAuthority
):Promise<Record<string,unknown>>{
  precondition(t);
  await authorized(t,authority);
  const source=await liveSource(db,t);
  const now=new Date().toISOString();
  if(action==="reconcile"){
    // Only an existing g0501 one-shot claim is eligible. Not-seen-in-list
    // is never proof of failure and must not reopen an already sent attempt.
    const claim=await db.prepare(
      "SELECT work_item_id FROM allowance_void_send_claims "+
      "WHERE workspace_id=?1 AND work_item_id=?2 AND allowance_number=?3 LIMIT 1"
    ).bind(t.workspaceId,t.workItemId,source.allowance_number)
      .first<{work_item_id:string}>();
    if(!claim)throw new Error("ALLOWANCE_VOID_RECONCILE_NOT_SENT");
    const company=await officialIssuer(db,t,source);
    // Explicit on-demand 14-day UTC window. No Cron, no full invoice mirror,
    // no caller-provided seller, date range, credentials or allowance number.
    const end=new Date();
    const start=new Date(end.getTime()-13*86400000);
    const compact=(d:Date)=>d.toISOString().slice(0,10).replace(/-/g,"");
    const scanned=await scanAllowanceExceptionPages({
      sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
      dateStart:compact(start),dateEnd:compact(end)
    },[{workItemId:t.workItemId,allowanceNumber:source.allowance_number}]);
    await authorized(t,authority);
    await liveSource(db,t); // Keep the same original Work Item revision.
    const hint=scanned.hints[0];
    if(!hint.needsSelectiveQuery)
      return {itemId:t.workItemId,revision:t.expectedRevision,
        allowanceNumber:source.allowance_number,listReason:hint.reason,
        requiresManualReconciliation:true,canAutoRetry:false,canAutoClose:false};
    // Reuse the existing official allowance_status readback AND its immutable
    // D1 official_checks audit; no parallel status writer or business state.
    const observed=await managerAllowanceVoidAction("status-readback",db,t,authority);
    return {itemId:t.workItemId,revision:t.expectedRevision,
      allowanceNumber:source.allowance_number,listReason:hint.reason,
      observedTypes:hint.observedTypes,readback:observed.readback,
      requiresManualReconciliation:true,canAutoRetry:false,canAutoClose:false};
  }
  if(action==="approve"){
    const sent=await db.prepare(
      "SELECT work_item_id FROM allowance_void_send_claims "+
      "WHERE work_item_id=?1 LIMIT 1").bind(t.workItemId)
      .first<{work_item_id:string}>();
    if(sent)throw new Error("ALLOWANCE_VOID_ALREADY_SENT");
    const checked=await inspect(db,t,source);
    await authorized(t,authority);
    // Approved source is tied to the official snapshot AND D1 revision.
    // No direct g0501 request, no reusable grant after expiry.
    const approvalId="ava_"+crypto.randomUUID();
    const when=new Date().toISOString();
    const expires=new Date(Date.parse(when)+15*60_000).toISOString();
    const result=await db.prepare(
      "INSERT INTO allowance_void_approvals(approval_id,work_item_id,workspace_id,"+
      "expected_revision,official_snapshot_sha256,official_checked_at,"+
      "approved_by_employee_id,approved_by_device_id,approved_at,valid_until_utc) "+
      "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10)"
    ).bind(approvalId,t.workItemId,t.workspaceId,t.expectedRevision,
      checked.original.officialSnapshotSha256,checked.original.checkedAtUtc,
      t.employeeId,t.deviceId,when,expires).run();
    if(result.meta.changes!==1)throw new Error("ALLOWANCE_VOID_APPROVAL_UNKNOWN");
    return{approvalId,itemId:t.workItemId,revision:t.expectedRevision,
      validUntilUtc:expires,canIssueNow:false};
  }
  if(action==="send-once"){
    const approval=await db.prepare(
      "SELECT approval_id,official_snapshot_sha256,valid_until_utc "+
      "FROM allowance_void_approvals WHERE workspace_id=?1 AND work_item_id=?2 "+
      "AND expected_revision=?3 AND valid_until_utc>?4 "+
      "ORDER BY approved_at DESC LIMIT 1"
    ).bind(t.workspaceId,t.workItemId,t.expectedRevision,now).first<PriorApproval>();
    if(!approval)throw new Error("ALLOWANCE_VOID_APPROVAL_EXPIRED");
    const checked=await inspect(db,t,source);
    if(approval.official_snapshot_sha256!==checked.original.officialSnapshotSha256)
      throw new Error("ALLOWANCE_VOID_FINAL_PREFLIGHT_CHANGED");
    await authorized(t,authority);
    const current=await officialIssuer(db,t,source);
    if(current.amegoAppKey!==checked.company.amegoAppKey)
      throw new Error("ALLOWANCE_VOID_COMPANY_CHANGED");
    // Claim before network I/O. Unique work_item_id prevents every replay,
    // even if HTTP failed, the Worker crashed or the original Device died.
    const claimedAt=new Date().toISOString();
    let result:D1Result<unknown>;
    try{
      result=await db.prepare(
        "INSERT INTO allowance_void_send_claims(work_item_id,workspace_id,"+
        "approval_id,allowance_number,expected_revision,claimed_by_employee_id,"+
        "claimed_by_device_id,claimed_at) VALUES(?1,?2,?3,?4,?5,?6,?7,?8)"
      ).bind(t.workItemId,t.workspaceId,approval.approval_id,
        source.allowance_number,t.expectedRevision,t.employeeId,t.deviceId,
        claimedAt).run();
    }catch{
      throw new Error("ALLOWANCE_VOID_ALREADY_CLAIMED_READBACK_REQUIRED");
    }
    if(result.meta.changes!==1)
      throw new Error("ALLOWANCE_VOID_CLAIM_UNCONFIRMED");
    let outcome:Awaited<ReturnType<typeof postAllowanceVoidOnce>>;
    try{
      outcome=await postAllowanceVoidOnce({
        sellerInvoice:current.sellerInvoice,appKey:current.amegoAppKey,
        allowanceNumber:source.allowance_number
      });
    }catch{outcome={kind:"unknown_result"};}
    return{itemId:t.workItemId,revision:t.expectedRevision,
      allowanceNumber:source.allowance_number,outcome:outcome.kind,
      ...(outcome.kind==="unknown_result"?{}:{officialCode:outcome.code}),
      canAutoRetry:false,canAutoClose:false,mustReadback:true};
  }
  // Both status-readback and close re-check AMEGO; an HTTP success on g0501
  // cannot be equated with official D0501/99 completion.
  const company=await officialIssuer(db,t,source);
  const official=await readOfficialAllowanceVoidStatus({
    sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
    allowanceNumber:source.allowance_number
  });
  await authorized(t,authority);
  if(action==="close"&&!official.officialCancelled)
    throw new Error("ALLOWANCE_VOID_NOT_OFFICIALLY_CANCELLED");
  const checkId="avc_"+crypto.randomUUID();
  const stmt=db.prepare(
    "INSERT INTO allowance_void_official_checks(check_id,work_item_id,workspace_id,"+
    "allowance_number,official_type,official_upload_status,"+
    "official_snapshot_sha256,checked_at,checked_by_employee_id,"+
    "checked_by_device_id) VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10)"
  ).bind(checkId,t.workItemId,t.workspaceId,source.allowance_number,
    official.type,official.uploadStatus,official.statusEvidenceSha256,
    official.checkedAtUtc,t.employeeId,t.deviceId);
  if(action==="status-readback"){
    const written=await stmt.run();
    if(written.meta.changes!==1)throw new Error("ALLOWANCE_VOID_CHECK_UNCONFIRMED");
    return{readback:readback(t.workItemId,t,official)};
  }
  // Atomic audit INSERT + status/revision CAS; SQL requires D0501/99 source.
  const updated=db.prepare(
    "UPDATE work_items SET status='closed',revision=revision+1,"+
    "updated_by_device_id=?1,actor_id=?2,updated_at=?3 "+
    "WHERE work_item_id=?4 AND workspace_id=?5 AND kind='ALLOWANCE_VOID' "+
    "AND status='review' AND revision=?6 AND EXISTS ("+
    "SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id "+
    "WHERE d.device_id=?1 AND d.workspace_id=?5 AND d.status='active' "+
    "AND d.employee_authority_state='cloud' AND w.status='active')"
  ).bind(t.deviceId,t.employeeId,new Date().toISOString(),
    t.workItemId,t.workspaceId,t.expectedRevision);
  const results=await db.batch([stmt,updated]);
  if(results.length!==2||results.some(x=>x.meta.changes!==1))
    throw new Error("ALLOWANCE_VOID_CLOSE_CONFLICT");
  return{itemId:t.workItemId,previousRevision:t.expectedRevision,
    revision:t.expectedRevision+1,status:"closed",
    allowanceNumber:source.allowance_number,officialConfirmed:true};
}
