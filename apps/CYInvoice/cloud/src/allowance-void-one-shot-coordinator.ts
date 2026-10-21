// V3.2-E DRAFT ONLY. Sole internal Cloud g0501 send owner.
// NOT ROUTED; schema20 not deployed; explicitly opt-in in tests only.
// Before any future public send route: shut down legacy Local senders,
// obtain operator authorization and enforce a default-off Worker send flag.
// No D1 inventory hash can establish absent offline devices.
// Claim first -> at most ONE provider POST. Lost claim reply = ZERO posts.
// A provider code=0 or D0501/99 can never attribute this mutation.
import {loadCloudAllowanceVoidForManager,activeVoidDevice,
 isVoidManager,sameVoidManager} from "./allowance-void-applications";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialVoidTarget,sameVoidCompanyCard,freshVoidOfficialEvidence}
 from "./amego-allowance-void-proof";
import {postAllowanceVoidOnce} from "./amego-allowance-void";
import type {OneShotAllowanceVoidOutcome} from "./amego-allowance-void";
import type {AllowanceActorSource,CloudAllowanceContext}
 from "./allowance-frozen-store";

export interface VoidOnceTarget {
 workspaceId:string;applicationId:string;deviceId:string;
 expectedRevision:2;confirmation:string;
}
interface StoredGrant {
 request_hash:string;official_snapshot_hash:string;
 manager_actor_id:string;manager_device_id:string;
 approved_at:string;valid_until_utc:string;
}
export interface VoidOnceOutcome {
 applicationId:string;
 outcome:OneShotAllowanceVoidOutcome["kind"];
 providerPostAttempted:true;
 claimedExactlyOnce:true;
 auditRecorded:boolean;
 manualReviewRequired:true;
 safeToAutoResend:false;safeToAutoClose:false;
 proofOfThisPost:false;
}
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const HASH=/^[a-f0-9]{64}$/;
function now(clock:()=>Date):Date{
 const value=clock();
 if(!(value instanceof Date)||!Number.isFinite(value.valueOf()))
  throw new Error("VOID_EXEC_CLOCK_INVALID");
 return value;
}
export async function executeCloudVoidOnce(
 db:D1Database,t:VoidOnceTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date(),authorizedCutover=false
):Promise<VoidOnceOutcome>{
 // No public route sets this. Source/fake tests may opt in explicitly;
 // production requires separate approval and Local sender shutdown BEFORE
 // a future route can be designed. Default: ZERO financial POSTs or claims.
 if(authorizedCutover!==true)throw new Error("VOID_EXEC_SEND_DISABLED");
 if(!t||!WS.test(t.workspaceId)||!ID.test(t.applicationId)||
  !DEVICE.test(t.deviceId)||t.expectedRevision!==2||
  typeof t.confirmation!=="string")
  throw new Error("VOID_EXEC_TARGET_INVALID");
 const first=await authority.revalidate();
 if(!isVoidManager(first,t.workspaceId)||!await activeVoidDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_EXEC_MANAGER_REQUIRED");
 const frozen=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 if(frozen.status!=="approved"||frozen.revision!==2||
  t.confirmation!=="作廢 "+frozen.allowanceNumber)
  throw new Error("VOID_EXEC_NOT_APPROVED_OR_CONFIRMATION_INVALID");
 const grant=await db.prepare("SELECT request_hash,official_snapshot_hash,"+
  "manager_actor_id,manager_device_id,approved_at,valid_until_utc "+
  "FROM allowance_void_approvals "+
  "WHERE application_id=?1 AND workspace_id=?2 LIMIT 1")
  .bind(t.applicationId,t.workspaceId).first<StoredGrant>();
 const at=now(clock);
 if(!grant||grant.request_hash!==frozen.requestHash||
  !HASH.test(grant.official_snapshot_hash)||
  grant.manager_actor_id!==first.employeeId||
  grant.manager_device_id!==t.deviceId||
  !Number.isFinite(Date.parse(grant.approved_at))||
  !Number.isFinite(Date.parse(grant.valid_until_utc))||
  at.valueOf()<Date.parse(grant.approved_at)||
  at.valueOf()>=Date.parse(grant.valid_until_utc)||
  Date.parse(grant.valid_until_utc)-Date.parse(grant.approved_at)>900_000)
  throw new Error("VOID_EXEC_GRANT_MISSING_EXPIRED_OR_CHANGED");
 const company=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==frozen.companyRevision||
  company.sellerInvoice!==frozen.sellerInvoice)
  throw new Error("VOID_EXEC_COMPANY_CHANGED");
 // Fresh signed read-only official proof just before claiming the one POST.
 const proof=await queryOfficialVoidTarget({
  sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
  allowanceNumber:frozen.allowanceNumber,
  originalInvoiceNumber:frozen.originalInvoiceNumber
 },transport,clock);
 const timestamp=now(clock);
 if(proof.officialSnapshotHash!==grant.official_snapshot_hash||
  !freshVoidOfficialEvidence(proof.checkedAtUtc,timestamp)||
  timestamp.valueOf()>=Date.parse(grant.valid_until_utc))
  throw new Error("VOID_EXEC_OFFICIAL_CHANGED_OR_STALE");
 const second=await authority.revalidate();
 if(!sameVoidManager(first,second,t.workspaceId)||
  !await activeVoidDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_EXEC_AUTH_REVOKED");
 const current=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 const companyAfter=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(current.status!=="approved"||current.revision!==2||
  current.requestHash!==frozen.requestHash||
  current.allowanceNumber!==frozen.allowanceNumber||
  !sameVoidCompanyCard(company,companyAfter))
  throw new Error("VOID_EXEC_STALE");
 // Permanent D1 claim written BEFORE the provider request. This is NOT a
 // separable method or an authorization token; one coordinator owns both.
 // SQL trigger also checks active Device, company revision, original grant.
 let claim:D1Result;
 try{
  claim=await db.prepare("INSERT INTO allowance_void_send_claims "+
   "(application_id,workspace_id,environment,seller_invoice,"+
   "allowance_fingerprint,request_hash,official_snapshot_hash,"+
   "manager_actor_id,manager_device_id,claimed_at) "+
   "SELECT r.application_id,r.workspace_id,r.environment,r.seller_invoice,"+
   "r.allowance_fingerprint,r.request_hash,?4,?5,?6,?7 "+
   "FROM allowance_void_requests r "+
   "JOIN allowance_void_approvals a ON a.application_id=r.application_id "+
   "AND a.workspace_id=r.workspace_id "+
   "JOIN devices d ON d.device_id=?6 AND d.workspace_id=r.workspace_id "+
   "JOIN workspaces w ON w.workspace_id=r.workspace_id "+
   "JOIN company_settings c ON c.workspace_id=r.workspace_id "+
   "WHERE r.application_id=?1 AND r.workspace_id=?2 "+
   "AND r.request_hash=?3 AND r.status='approved' AND r.revision=2 "+
   "AND a.request_hash=?3 AND a.official_snapshot_hash=?4 "+
   "AND a.manager_actor_id=?5 AND a.manager_device_id=?6 "+
   "AND c.revision=r.company_revision AND c.seller_invoice=r.seller_invoice "+
   "AND d.status='active' AND d.employee_authority_state='cloud' "+
   "AND w.status='active' AND "+
   "NOT EXISTS(SELECT 1 FROM allowance_void_send_claims x "+
   "WHERE x.application_id=r.application_id)")
   .bind(t.applicationId,t.workspaceId,frozen.requestHash,
     proof.officialSnapshotHash,first.employeeId,t.deviceId,
     timestamp.toISOString()).run();
 }catch{
  // May be committed despite missing D1 acknowledgement. READBACK ONLY.
  throw new Error("VOID_EXEC_CLAIM_UNCONFIRMED_NO_POST");
 }
 if(claim.meta.changes!==1)
  throw new Error("VOID_EXEC_CLAIM_UNCONFIRMED_NO_POST");
 // Emergency stop or revocation after claim must suppress even the FIRST POST.
 // The durable claim is never released or sent again after any failure.
 let stillAuthorized=false;
 try{
  const actor=await authority.revalidate();
  stillAuthorized=sameVoidManager(first,actor,t.workspaceId)&&
   await activeVoidDevice(db,t.workspaceId,t.deviceId);
  if(stillAuthorized){
   const latestCompany=await loadAmegoCompanySettingsForGateway(
    db,t.workspaceId,ctx.encryptionKeyHex);
   stillAuthorized=sameVoidCompanyCard(company,latestCompany);
  }
 }catch{/* Any uncertain authority or D1 state is an irreversible no-POST hold. */}
 if(!stillAuthorized){
  // Distinguish a claimed-but-suppressed POST from an unknown provider result.
  // This audit is best effort: failure never releases a permanent claim.
  try{
   await db.prepare("INSERT INTO allowance_void_audit "+
    "(application_id,workspace_id,kind,actor_id,device_id,recorded_at) "+
    "VALUES(?1,?2,'post_claim_interlock_no_post',?3,?4,?5)")
    .bind(t.applicationId,t.workspaceId,first.employeeId,t.deviceId,
     now(clock).toISOString()).run();
  }catch{/* Keep claim forever even if audit write fails. */}
  throw new Error("VOID_EXEC_POST_CLAIM_INTERLOCK_NO_POST");
 }
 // From here the permanent claim is irrevocable, even on exception.
 let result:OneShotAllowanceVoidOutcome;
 try{
  result=await postAllowanceVoidOnce({
   sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
   allowanceNumber:frozen.allowanceNumber
  },transport,clock);
 }catch{result={kind:"unknown_result"};}
 const kind=result.kind==="accepted_unconfirmed"?"accepted_unconfirmed":
  result.kind==="definite_rejection_manual"?"rejected_unconfirmed":"unknown_result";
 let auditRecorded=false;
 try{
  const audit=await db.prepare("INSERT INTO allowance_void_audit "+
   "(application_id,workspace_id,kind,actor_id,device_id,recorded_at) "+
   "VALUES(?1,?2,?3,?4,?5,?6)")
   .bind(t.applicationId,t.workspaceId,kind,first.employeeId,t.deviceId,
    now(clock).toISOString()).run();
  auditRecorded=audit.meta.changes===1;
 }catch{
  // Missing outcome audit cannot release the claim or trigger a replay.
  auditRecorded=false;
 }
 return {applicationId:t.applicationId,outcome:result.kind,
  providerPostAttempted:true,claimedExactlyOnce:true,auditRecorded,
  manualReviewRequired:true,safeToAutoResend:false,safeToAutoClose:false,
  proofOfThisPost:false};
}
