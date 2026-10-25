// Existing Cloud void owner. One original-number claim, bounded evidenced
// attempts, and official readback. A claim is an identity fence, not a lifetime
// ban on safe same-number retry. Legacy unproven claims stay readback-only.
import {loadCloudAllowanceVoidForManager,activeVoidDevice} from "./allowance-void-applications";
import {loadAmegoCompanySettingsForGateway,amegoCredentialsForEnvironment} from "./company-settings";
import {queryOfficialVoidTarget,sameVoidCompanyCard} from "./amego-allowance-void-proof";
import {postAllowanceVoidOnce} from "./amego-allowance-void";
import {readOfficialAllowanceVoidStatus} from "./amego-allowance-status";
import {recordExecutionEvent,saveExecutionResult,executionError,type CloudExecutionResult} from "./allowance-one-shot-coordinator";
import type {AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
export interface VoidOnceTarget {
 workspaceId:string;applicationId:string;deviceId:string;expectedRevision:2;
}
export async function executeCloudVoidOnce(db:D1Database,t:VoidOnceTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,clock:()=>Date=()=>new Date(),
 enabled=false,requestId="internal"):Promise<CloudExecutionResult>{
 const scope=authority.approvedOperation;
 if(scope&&(scope.workspaceId!==t.workspaceId||scope.applicationId!==t.applicationId||scope.operation!=="void"))
  throw new Error("APPROVED_OPERATION_SCOPE_MISMATCH");
 if(!enabled)throw new Error("VOID_EXEC_SEND_DISABLED");
 const finish=(result:CloudExecutionResult)=>saveExecutionResult(db,t,"void",result,requestId,clock);
 try{
  const actor=await authority.revalidate();
  if(!actor||!["ADMIN","SUPER_ADMIN"].includes(actor.workspaceRole)||
    !await activeVoidDevice(db,t.workspaceId,t.deviceId))throw new Error("VOID_EXEC_AUTH_REQUIRED");
  const frozen=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
  const grant=await db.prepare(`SELECT request_hash,official_snapshot_hash,manager_actor_id,manager_device_id
   FROM allowance_void_approvals WHERE application_id=?1 AND workspace_id=?2`)
   .bind(t.applicationId,t.workspaceId).first<{request_hash:string;official_snapshot_hash:string;manager_actor_id:string;manager_device_id:string}>();
  if(frozen.status!=="approved"||frozen.revision!==2||!grant||grant.request_hash!==frozen.requestHash||
    grant.manager_actor_id!==actor.employeeId||grant.manager_device_id!==t.deviceId)
   throw new Error("VOID_EXEC_APPROVAL_CHANGED");
  const company=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
  if(company.revision!==frozen.companyRevision||company.sellerInvoice!==frozen.sellerInvoice)
   throw new Error("VOID_EXEC_COMPANY_CHANGED");
  const input={...amegoCredentialsForEnvironment(company,ctx.environment),allowanceNumber:frozen.allowanceNumber};
  const status=await readOfficialAllowanceVoidStatus(input,transport,clock);
  const prior=await db.prepare(`SELECT application_id FROM allowance_void_send_claims WHERE application_id=?1 AND workspace_id=?2`)
   .bind(t.applicationId,t.workspaceId).first();
  let claimed=!!prior;
  async function claim(){
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor!.employeeId||fresh.workspaceRole!==actor!.workspaceRole||
     !await activeVoidDevice(db,t.workspaceId,t.deviceId))throw new Error("VOID_EXEC_AUTH_REVOKED");
   if(!sameVoidCompanyCard(company,await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex)))
    throw new Error("VOID_EXEC_COMPANY_CHANGED");
   if(claimed)return;
   const r=await db.prepare(`INSERT INTO allowance_void_send_claims
    (application_id,workspace_id,environment,seller_invoice,allowance_fingerprint,request_hash,
     official_snapshot_hash,manager_actor_id,manager_device_id,claimed_at)
    SELECT r.application_id,r.workspace_id,r.environment,r.seller_invoice,r.allowance_fingerprint,
     r.request_hash,?3,?4,?5,?6 FROM allowance_void_requests r
    WHERE r.application_id=?1 AND r.workspace_id=?2 AND r.status='approved' AND r.request_hash=?7
     AND NOT EXISTS(SELECT 1 FROM allowance_void_send_claims c WHERE c.application_id=r.application_id)`)
    .bind(t.applicationId,t.workspaceId,grant!.official_snapshot_hash,actor!.employeeId,t.deviceId,
     clock().toISOString(),frozen.requestHash).run();
   if(r.meta.changes!==1)throw new Error("VOID_EXEC_CLAIM_UNCONFIRMED_NO_POST");
   claimed=true;
  }
  async function complete(hash:string){
   await claim();
   await db.prepare(`INSERT INTO allowance_void_audit
    (application_id,workspace_id,kind,evidence_hash,actor_id,device_id,recorded_at)
    VALUES(?1,?2,'official_void_observed_unattributed',?3,?4,?5,?6)`)
    .bind(t.applicationId,t.workspaceId,hash,actor!.employeeId,t.deviceId,clock().toISOString()).run();
   return finish({applicationId:t.applicationId,executionState:"completed",allowanceNumber:frozen.allowanceNumber});
  }
  if(status.officialCancelled)return await complete(status.statusEvidenceSha256);
  if(!status.definitiveOpen){
   if([1,2,3,31,32].includes(status.uploadStatus))return await finish({applicationId:t.applicationId,executionState:"processing",allowanceNumber:frozen.allowanceNumber});
   throw new Error("VOID_EXEC_OFFICIAL_STATE_INVALID");
  }
  // Validate original invoice and original allowance items before every POST.
  const proof=await queryOfficialVoidTarget({...input,originalInvoiceNumber:frozen.originalInvoiceNumber},transport,clock);
  if(proof.officialSnapshotHash!==grant.official_snapshot_hash)throw new Error("VOID_EXEC_OFFICIAL_CHANGED");
  await claim();
  const counts=await db.prepare(`SELECT COUNT(*) AS n,MAX(attempt) AS last,
   MAX(started_at) AS started,
   SUM(CASE WHEN provider_code=4050141 THEN 1 ELSE 0 END) AS waiting,
   SUM(CASE WHEN provider_code IS NOT NULL AND provider_code NOT IN (0,4050132,4050141) THEN 1 ELSE 0 END) AS rejected
   FROM allowance_void_send_attempts WHERE application_id=?1`).bind(t.applicationId)
   .first<{n:number;last:number|null;started:string|null;waiting:number|null;rejected:number|null}>();
  if(!counts)throw new Error("VOID_ATTEMPTS_UNAVAILABLE");
  if(prior&&counts.n===0)throw new Error("VOID_LEGACY_CLAIM_READBACK_ONLY");
  if(counts.rejected)throw new Error("VOID_PROVIDER_REJECTED");
  if(counts.n>=3)throw new Error("VOID_RETRY_LIMIT_REACHED");
  if(counts.waiting||counts.started&&clock().valueOf()-Date.parse(counts.started)<60_000)
   return await finish({applicationId:t.applicationId,executionState:"processing",allowanceNumber:frozen.allowanceNumber});
  const attempt=counts.n+1,at=clock().toISOString();
  const saved=await db.prepare(`INSERT INTO allowance_void_send_attempts(application_id,attempt,started_at)
   SELECT ?1,?2,?3 WHERE COALESCE((SELECT MAX(attempt) FROM allowance_void_send_attempts WHERE application_id=?1),0)=?2-1`)
   .bind(t.applicationId,attempt,at).run();
  if(saved.meta.changes!==1)throw new Error("VOID_ATTEMPT_CHANGED_NO_POST");
  const finalActor=await authority.revalidate();
  if(!finalActor||finalActor.employeeId!==actor.employeeId||finalActor.workspaceRole!==actor.workspaceRole||
    !await activeVoidDevice(db,t.workspaceId,t.deviceId)||
    !sameVoidCompanyCard(company,await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex)))
   throw new Error("VOID_EXEC_POST_CLAIM_INTERLOCK_NO_POST");
  await recordExecutionEvent(db,t,"void","send_started",requestId,clock,{attempt,responseReceived:false});
  const sent=await postAllowanceVoidOnce(input,transport,clock, httpStatus=>recordExecutionEvent(db,t,"void","http_response",requestId,clock,{attempt,httpStatus,responseReceived:true}));
  const code="code" in sent?sent.code:undefined;
  if(code!==undefined)await db.prepare(`UPDATE allowance_void_send_attempts SET response_received=1,provider_code=?3
   WHERE application_id=?1 AND attempt=?2 AND response_received=0`).bind(t.applicationId,attempt,code).run();
  await recordExecutionEvent(db,t,"void","send_response",requestId,clock,{attempt,code,responseReceived:code!==undefined});
  await db.prepare(`INSERT INTO allowance_void_audit(application_id,workspace_id,kind,actor_id,device_id,recorded_at)
   VALUES(?1,?2,?3,?4,?5,?6)`).bind(t.applicationId,t.workspaceId,
   code===0?"accepted_unconfirmed":code===undefined?"unknown_result":"rejected_unconfirmed",
   actor.employeeId,t.deviceId,clock().toISOString()).run();
  const after=await readOfficialAllowanceVoidStatus(input,transport,clock);
  if(after.officialCancelled)return await complete(after.statusEvidenceSha256);
  const waiting=code===0||code===4050141||[1,2,3,31,32].includes(after.uploadStatus);
  return await finish({applicationId:t.applicationId,executionState:waiting?"processing":"problem",
   allowanceNumber:frozen.allowanceNumber,...(waiting?{}:{errorCode:code===undefined?"VOID_RESULT_UNKNOWN":"AMEGO_"+code})});
 }catch(error){
  const errorCode=executionError(error);
  await recordExecutionEvent(db,t,"void","execution_failed",requestId,clock,{errorCode});
  return await finish({applicationId:t.applicationId,executionState:"problem",errorCode});
 }
}
