// V3.2-C3b1: INTERNAL manager official preflight preview ONLY.
// No public route, approval grant, ledger allocation, g0401 or g0501.
// Adapted from PR #440 reviewer flow without legacy work_items.ALLOWANCE.
// Only a trusted Worker-owned caller may inject an authenticated CYID
// authority; browser-supplied roles, seller credentials or official claims
// are never accepted.
import {loadEncryptedCloudAllowance} from "./allowance-frozen-store";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialInvoiceReadOnly} from "./amego-invoice-query";
import type {OfficialInvoiceSnapshot} from "./amego-invoice-query";

export interface AllowanceManagerPreviewTarget {
  workspaceId:string;
  applicationId:string;
  deviceId:string;
  expectedRevision:number;
}
export interface AllowanceOfficialSummary {
  allowanceNumber:string|null;
  allowanceDate:string|null;
  uploadStatus:number|null;
  cancellationDate:string|null;
  metadataComplete:boolean;
}
export interface AllowanceManagerPreview {
  applicationId:string;
  revision:number;
  environment:"test"|"production";
  requestedGross:number;
  originalInvoiceNumber:string;
  officialUploadStatus:number;
  officialInvoiceCancelled:boolean;
  officialMutationPending:boolean;
  officialSummaryComplete:boolean;
  officialAllowanceCount:number;
  officialAllowances:AllowanceOfficialSummary[];
  checkedAtUtc:string;
  officialSnapshotHash:string;
  // invoice_query summary is not a per-original-invoice allowance balance.
  // This preview never authorizes any g0401 mutation or creates a grant.
  canApprove:false;
  canAllocateNumber:false;
  safeToInferRemainingBalance:false;
  requiresIndependentOfficialReconciliation:true;
}
function manager(a:AllowanceActor|null,workspace:string):a is AllowanceActor {
  return !!a&&a.workspaceId===workspace&&
    (a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN")&&
    typeof a.employeeId==="string"&&a.employeeId.length>0&&a.employeeId.length<=100;
}
function sameManager(a:AllowanceActor|null,b:AllowanceActor|null,ws:string):boolean {
  return manager(a,ws)&&manager(b,ws)&&a.employeeId===b.employeeId&&
    a.workspaceRole===b.workspaceRole;
}
async function activeManagerDevice(db:D1Database,t:AllowanceManagerPreviewTarget):Promise<boolean>{
 const row=await db.prepare(`SELECT d.device_id FROM devices d
   JOIN workspaces w ON w.workspace_id=d.workspace_id
   WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active'
     AND d.employee_authority_state='cloud' AND w.status='active' LIMIT 1`)
   .bind(t.deviceId,t.workspaceId).first<{device_id:string}>();
 return !!row;
}
async function unchangedReview(db:D1Database,t:AllowanceManagerPreviewTarget,
  actorId:string,requestHash:string):Promise<boolean>{
 const row=await db.prepare(`SELECT application_id FROM allowance_applications
   WHERE workspace_id=?1 AND application_id=?2 AND (status='open' OR (status='review' AND manager_actor_id=?4))
     AND revision=?3 AND request_hash=?5 LIMIT 1`)
  .bind(t.workspaceId,t.applicationId,t.expectedRevision,actorId,requestHash)
  .first<{application_id:string}>();
 return !!row;
}
function previewOf(applicationId:string,revision:number,requestedGross:number,
  env:"test"|"production",invoice:string,snapshot:OfficialInvoiceSnapshot):AllowanceManagerPreview {
 const blocked=snapshot.uploadStatus!==99||snapshot.cancelDate!==0||
  snapshot.hasUnsettledMutation||snapshot.voidPending||
  !snapshot.allowanceSummaryPresent||
  snapshot.allowanceHistory.some(x=>!x.metadataComplete);
 return {
  applicationId,revision,requestedGross,environment:env,
  originalInvoiceNumber:invoice,
  officialUploadStatus:snapshot.uploadStatus,
  officialInvoiceCancelled:snapshot.cancelDate!==0,
  officialMutationPending:snapshot.hasUnsettledMutation||snapshot.voidPending,
  officialSummaryComplete:!blocked,
  officialAllowanceCount:snapshot.existingAllowanceCount,
  officialAllowances:snapshot.allowanceHistory.map(x=>({
   allowanceNumber:x.allowanceNumber,allowanceDate:x.allowanceDate,
   uploadStatus:x.uploadStatus,cancellationDate:x.cancelDate,
   metadataComplete:x.metadataComplete
  })),
  checkedAtUtc:snapshot.checkedAtUtc,
  officialSnapshotHash:snapshot.snapshotSha256,
  canApprove:false,canAllocateNumber:false,
  safeToInferRemainingBalance:false,
  requiresIndependentOfficialReconciliation:true
 };
}
// The official gateway uses signed POST /json/invoice_query as a READ operation,
// never AMEGO mutations. Production caller must use trusted Worker env secrets;
// test transport/clock injection below is only for isolated source tests.
export async function previewCloudAllowanceForManager(
 db:D1Database,target:AllowanceManagerPreviewTarget,
 ctx:CloudAllowanceContext,authority:AllowanceActorSource,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<AllowanceManagerPreview> {
 if(!Number.isSafeInteger(target.expectedRevision)||target.expectedRevision<1)
  throw new Error("ALLOWANCE_REVIEW_REVISION_INVALID");
 const before=await authority.revalidate();
 if(!manager(before,target.workspaceId)||
   !await activeManagerDevice(db,target))
  throw new Error("ALLOWANCE_REVIEW_MANAGER_REQUIRED");
 const frozen=await loadEncryptedCloudAllowance(db,target,ctx,authority);
 if(!["open","review"].includes(frozen.status)||frozen.revision!==target.expectedRevision||
    !await unchangedReview(db,target,before.employeeId,frozen.requestHash))
  throw new Error("ALLOWANCE_REVIEW_STALE");
 const company=await loadAmegoCompanySettingsForGateway(db,target.workspaceId,
   ctx.encryptionKeyHex);
 if(company.revision!==frozen.companyRevision||
    company.sellerInvoice!==frozen.sellerInvoice)
  throw new Error("ALLOWANCE_REVIEW_COMPANY_CHANGED");
 // Only this Worker-side gateway supplies credentials, invoice number and
 // official evidence. No client-supplied "canApprove" or gross balance.
 const official=await queryOfficialInvoiceReadOnly({
   sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
   originalInvoiceNumber:frozen.originalInvoiceNumber
 },transport,clock);
 if(official.invoiceNumber!==frozen.originalInvoiceNumber)
  throw new Error("ALLOWANCE_REVIEW_OFFICIAL_INVOICE_CHANGED");
 const checked=Date.parse(official.checkedAtUtc),now=clock().valueOf();
 if(!Number.isFinite(checked)||!Number.isFinite(now)||
   checked>now+1000||now-checked>60_000)
  throw new Error("ALLOWANCE_REVIEW_OFFICIAL_STALE");
 const after=await authority.revalidate();
 if(!sameManager(before,after,target.workspaceId)||
   !await activeManagerDevice(db,target)||
   !await unchangedReview(db,target,before.employeeId,frozen.requestHash))
  throw new Error("ALLOWANCE_REVIEW_AUTH_OR_REVISION_CHANGED");
 const current=await loadAmegoCompanySettingsForGateway(db,target.workspaceId,
  ctx.encryptionKeyHex);
 if(current.revision!==company.revision||
    current.sellerInvoice!==company.sellerInvoice||
    current.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_REVIEW_COMPANY_CHANGED");
 return previewOf(frozen.applicationId,frozen.revision,frozen.requestedGross,
  frozen.environment,frozen.originalInvoiceNumber,official);
}
