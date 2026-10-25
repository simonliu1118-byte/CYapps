// The existing Cloud issue owner: approved request -> allocation -> one POST
// per number -> original-number reconciliation. Unknown outcomes never resend.
import {claimCloudAllowanceOneShotSending,type CloudAllowanceSendingTarget} from "./allowance-sending-claim";
import {loadEncryptedCloudAllowance,type AllowanceActorSource,type CloudAllowanceContext,type StoredCloudAllowance} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway,amegoCredentialsForEnvironment} from "./company-settings";
import {postNumberedAllowanceOnce} from "./amego-allowance-issue";
import {queryFrozenAllowanceByOriginalNumber} from "./amego-allowance-exact-readback";
import {reserveApprovedCloudAllowanceNumber} from "./allowance-number-reservation";
import {cyIdBinding,type CyIdEnv} from "./cyid";
import {executeCloudVoidOnce} from "./allowance-void-one-shot-coordinator";

export interface CloudExecutionResult {
 applicationId:string;executionState:"processing"|"completed"|"problem";
 allowanceNumber?:string;ledgerStatus?:string;errorCode?:string;
}
export interface AllowanceExecutionEnv extends CyIdEnv {
 APP_ENV:string;SCHEMA_VERSION:string;COMPANY_SETTINGS_KEY_HEX?:string;
 ALLOWANCE_API_ENABLED?:string;ALLOWANCE_G0401_SEND_ENABLED?:string;
 ALLOWANCE_G0501_SEND_ENABLED?:string;
}
export async function recordExecutionEvent(db:D1Database,t:{workspaceId:string;applicationId:string},
 operation:"issue"|"void",stage:string,requestId:string,clock:()=>Date,
 evidence:{attempt?:number;code?:number;httpStatus?:number;responseReceived?:boolean;errorCode?:string}={}){
 await db.prepare(`INSERT INTO allowance_execution_events
  (workspace_id,application_id,operation,stage,attempt,provider_code,http_status,
   response_received,request_id,error_code,recorded_at) VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11)`)
 .bind(t.workspaceId,t.applicationId,operation,stage,evidence.attempt??null,evidence.code??null,
  evidence.httpStatus??null,evidence.responseReceived===undefined?null:evidence.responseReceived?1:0,
  requestId,evidence.errorCode??"",clock().toISOString()).run();
}
export async function saveExecutionResult(db:D1Database,t:{workspaceId:string;applicationId:string},
 operation:"issue"|"void",result:CloudExecutionResult,requestId:string,clock:()=>Date){
 const table=operation==="issue"?"allowance_applications":"allowance_void_requests";
 const error=result.errorCode??"";
 await db.prepare(`UPDATE ${table} SET execution_state=?3,
  first_error=CASE WHEN first_error='' AND ?4<>'' THEN ?4 ELSE first_error END,
  last_error=?4,next_check_at=?5,request_id=?6 WHERE workspace_id=?1 AND application_id=?2 AND (execution_state<>'completed' OR ?3='completed')`)
 .bind(t.workspaceId,t.applicationId,result.executionState,error,
  new Date(clock().valueOf()+(result.executionState==="problem"?300_000:60_000)).toISOString(),requestId).run();
 return result;
}
export function executionError(error:unknown):string{
 const code=error instanceof Error?error.message:"ALLOWANCE_EXECUTION_FAILED";
 // Transport/SQL exception text may carry private payloads. Stable codes only.
 return /^[A-Z][A-Z0-9_]{2,100}$/.test(code)?code:"ALLOWANCE_EXECUTION_OR_STORAGE_FAILED";
}
// This is a revocation interlock for ONE persisted business approval, not a
// login, principal cache, or permission to create/approve another operation.
// No password/session/token is stored or manufactured during recovery.
export function approvedExecutionAuthority(env:AllowanceExecutionEnv,workspaceId:string,
 snapshot:string|null,managerActorId:string,
 operation:{applicationId:string;operation:"issue"|"void"}):AllowanceActorSource{
 return {approvedOperation:{workspaceId,...operation},revalidate:async()=>{
  if(!snapshot||!env.IDENTITY_AUTHORITY)throw new Error("APPROVAL_AUTHORITY_UNVERIFIABLE");
  const cached=JSON.parse(snapshot) as Record<string,unknown>;
  const b=cyIdBinding(env);
  if(workspaceId!==b.workspaceId||cached.identityWorkspaceId!==b.identityWorkspaceId||cached.applicationId!==b.applicationId||cached.employeeId!==managerActorId||
    !["ADMIN","SUPER_ADMIN"].includes(String(cached.workspaceRole))||cached.emailVerified!==true)
   throw new Error("APPROVAL_AUTHORITY_INVALID");
  const checked=await env.IDENTITY_AUTHORITY.invalidateCache({workspaceId:b.identityWorkspaceId,
   applicationId:b.applicationId,authorities:[cached]}) as Record<string,unknown>;
  if(checked.workspaceId!==b.identityWorkspaceId||checked.applicationId!==b.applicationId||
     checked.available!==true||!Array.isArray(checked.invalidated)||checked.invalidated.length!==0)
   throw new Error("APPROVAL_AUTHORITY_REVOKED");
  return {workspaceId,employeeId:managerActorId,workspaceRole:cached.workspaceRole as "ADMIN"|"SUPER_ADMIN"};
 }};
}
async function readback(db:D1Database,t:CloudAllowanceSendingTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch,clock:()=>Date,requestId:string,approvedRequest?:StoredCloudAllowance):Promise<CloudExecutionResult>{
 const frozen=approvedRequest??await loadEncryptedCloudAllowance(db,t,ctx,authority);
 const company=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==frozen.companyRevision||company.sellerInvoice!==frozen.sellerInvoice)
  throw new Error("ALLOWANCE_COMPANY_CHANGED");
 const matching=await queryFrozenAllowanceByOriginalNumber({...amegoCredentialsForEnvironment(company,ctx.environment),allowanceNumber:t.allowanceNumber,
  allowanceDay:frozen.allowanceDay,unnumberedTemplateJson:frozen.unnumberedTemplateJson},transport,clock);
 if(matching.kind==="pending_official"){
  if(!await authority.revalidate())throw new Error("APPROVAL_AUTHORITY_REVOKED");
  return {applicationId:t.applicationId,executionState:"processing",allowanceNumber:t.allowanceNumber,ledgerStatus:"sending"};
 }
 if(matching.kind!=="full_frozen_match_unattributed")throw new Error("ALLOWANCE_READBACK_NOT_MATCHED");
 const current=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 if(current.revision!==company.revision||current.sellerInvoice!==company.sellerInvoice||current.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_COMPANY_CHANGED");
 const actor=await authority.revalidate();if(!actor)throw new Error("APPROVAL_AUTHORITY_REVOKED");
 const at=clock().toISOString();
 // Evidence commits with confirmation. A failed transaction stays sending.
 const rows=await db.batch([
  db.prepare(`INSERT INTO allowance_reconciliation_checks
   (check_id,ledger_id,application_id,workspace_id,allowance_number,official_snapshot_hash,
    checked_by_device_id,checked_by_actor_id,checked_at)
   VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9)`)
   .bind("chk_"+crypto.randomUUID(),t.ledgerId,t.applicationId,t.workspaceId,t.allowanceNumber,
    matching.officialSnapshotHash,t.deviceId,actor.employeeId,at),
  db.prepare(`UPDATE allowance_number_ledger SET status='confirmed',updated_at=?2
    WHERE ledger_id=?1 AND status='sending'`).bind(t.ledgerId,at)
 ]);
 if(rows.some(r=>r.meta.changes!==1))throw new Error("ALLOWANCE_CONFIRMATION_SAVE_FAILED");
 await recordExecutionEvent(db,t,"issue","official_confirmed",requestId,clock,{responseReceived:true});
 return {applicationId:t.applicationId,executionState:"completed",allowanceNumber:t.allowanceNumber,ledgerStatus:"confirmed"};
}
export async function executeCloudAllowanceExactlyOnce(db:D1Database,t:CloudAllowanceSendingTarget,
 ctx:CloudAllowanceContext,authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date(),requestId="internal"):Promise<CloudExecutionResult>{
 // The claim already verified and decrypted this exact immutable request.
 // Carry it through the same operation instead of reloading it twice.
 const claim=await claimCloudAllowanceOneShotSending(db,t,ctx,authority,transport,clock);
 const frozen=claim.approvedRequest;
 const company=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 await recordExecutionEvent(db,t,"issue","send_prepared",requestId,clock,{responseReceived:false});
 const final=await authority.revalidate();
 const active=await db.prepare(`SELECT d.device_id FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id
  WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'`)
  .bind(t.deviceId,t.workspaceId).first();
 const card=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 if(!final||final.employeeId!==claim.managerActorId||final.workspaceRole!==claim.managerWorkspaceRole||
  final.workspaceId!==t.workspaceId||!active||company.revision!==frozen.companyRevision||
  company.sellerInvoice!==frozen.sellerInvoice||card.revision!==company.revision||
  card.sellerInvoice!==company.sellerInvoice||card.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_POST_CAS_AUTHORITY_CHANGED_NO_POST");
 const sent=await postNumberedAllowanceOnce({...amegoCredentialsForEnvironment(company,ctx.environment),
  allowanceNumber:t.allowanceNumber,allowanceDay:frozen.allowanceDay,
  unnumberedTemplateJson:frozen.unnumberedTemplateJson},transport,clock,
  httpStatus=>recordExecutionEvent(db,t,"issue","http_response",requestId,clock,{httpStatus,responseReceived:true}));
 await recordExecutionEvent(db,t,"issue","send_response",requestId,clock,
  {responseReceived:sent.kind!=="unknown_result",code:"code" in sent?sent.code:undefined});
 if(sent.kind==="confirmed_number_collision"){
  const at=clock().toISOString();
  const rows=await db.batch([
   db.prepare(`UPDATE allowance_number_ledger SET status='occupied_external',collision_code=?2,updated_at=?3
    WHERE ledger_id=?1 AND status='sending'`).bind(t.ledgerId,sent.code,at),
   db.prepare(`INSERT INTO allowance_number_rejections(ledger_id,provider_code,recorded_at) VALUES(?1,?2,?3)`)
    .bind(t.ledgerId,sent.code,at)
  ]);
  if(rows.some(r=>r.meta.changes!==1))throw new Error("ALLOWANCE_REJECTION_SAVE_FAILED");
  return {applicationId:t.applicationId,executionState:"processing",allowanceNumber:t.allowanceNumber,ledgerStatus:"occupied_external"};
 }
 // Even a timeout is queried once before classification. Never repeat POST.
 try{return await readback(db,t,ctx,authority,transport,clock,requestId,frozen);}
 catch(error){return {applicationId:t.applicationId,executionState:"problem",allowanceNumber:t.allowanceNumber,
  ledgerStatus:"sending",errorCode:sent.kind==="definite_rejection"?"AMEGO_"+sent.code:executionError(error)};}
}
export async function executeApprovedCloudAllowance(db:D1Database,
 t:{workspaceId:string;applicationId:string;deviceId:string;expectedRevision:number},ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,clock:()=>Date=()=>new Date(),requestId="internal"){
 const scope=authority.approvedOperation;
 if(scope&&(scope.workspaceId!==t.workspaceId||scope.applicationId!==t.applicationId||scope.operation!=="issue"))
  throw new Error("APPROVED_OPERATION_SCOPE_MISMATCH");
 try{
  for(;;){
   const latest=await db.prepare(`SELECT ledger_id,allowance_number,status FROM allowance_number_ledger
    WHERE workspace_id=?1 AND application_id=?2 ORDER BY attempt DESC LIMIT 1`)
    .bind(t.workspaceId,t.applicationId).first<{ledger_id:string;allowance_number:string;status:string}>();
   let result:CloudExecutionResult;
   if(latest?.status==="confirmed")result={applicationId:t.applicationId,executionState:"completed",allowanceNumber:latest.allowance_number,ledgerStatus:"confirmed"};
   else if(latest?.status==="sending")result=await readback(db,{...t,ledgerId:latest.ledger_id,allowanceNumber:latest.allowance_number},ctx,authority,transport,clock,requestId);
   else{
    const reserved=await reserveApprovedCloudAllowanceNumber(db,t,ctx,authority,clock);
    result=await executeCloudAllowanceExactlyOnce(db,{...t,ledgerId:reserved.ledgerId,allowanceNumber:reserved.allowanceNumber},ctx,authority,transport,clock,requestId);
   }
   if(result.ledgerStatus==="occupied_external")continue; // explicit rejection, no count cap
   return await saveExecutionResult(db,t,"issue",result,requestId,clock);
  }
 }catch(error){
  const result:CloudExecutionResult={applicationId:t.applicationId,executionState:"problem",errorCode:executionError(error)};
  await recordExecutionEvent(db,t,"issue","execution_failed",requestId,clock,{errorCode:result.errorCode});
  return await saveExecutionResult(db,t,"issue",result,requestId,clock);
 }
}
// Driven by the existing Device/runtime sync; no second scheduler or queue.
export async function resumeApprovedCloudAllowances(env:AllowanceExecutionEnv,workspaceId:string,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()){
 if(env.ALLOWANCE_API_ENABLED!=="true"||Number(env.SCHEMA_VERSION)<21||!env.COMPANY_SETTINGS_KEY_HEX)return;
 const at=clock().toISOString();
 for(const operation of ["issue","void"] as const){
  if((operation==="issue"?env.ALLOWANCE_G0401_SEND_ENABLED:env.ALLOWANCE_G0501_SEND_ENABLED)!=="true")continue;
  const table=operation==="issue"?"allowance_applications":"allowance_void_requests";
  const grants=operation==="issue"?"allowance_execution_approvals":"allowance_void_approvals";
  const rows=await env.DB.prepare(`SELECT a.application_id,a.environment,a.revision,g.manager_device_id,g.manager_actor_id,g.authority_snapshot
   FROM ${table} a JOIN ${grants} g ON g.application_id=a.application_id
   WHERE a.workspace_id=?1 AND a.status='approved' AND a.execution_state<>'completed' AND a.next_check_at<=?2
   ORDER BY a.next_check_at,a.application_id LIMIT 10`).bind(workspaceId,at)
   .all<{application_id:string;environment:"test"|"production";revision:number;manager_device_id:string;manager_actor_id:string;authority_snapshot:string|null}>();
  for(const row of rows.results){
   if(row.environment!=="test"&&row.environment!=="production")throw new Error("ALLOWANCE_ENVIRONMENT_INVALID");
   const ctx:CloudAllowanceContext={environment:row.environment,encryptionKeyHex:env.COMPANY_SETTINGS_KEY_HEX};
   const authority=approvedExecutionAuthority(env,workspaceId,row.authority_snapshot,row.manager_actor_id,{applicationId:row.application_id,operation});
   const target={workspaceId,applicationId:row.application_id,deviceId:row.manager_device_id,expectedRevision:row.revision};
   const requestId="resume-"+crypto.randomUUID();
   if(operation==="issue")await executeApprovedCloudAllowance(env.DB,target,ctx,authority,transport,clock,requestId);
   else await executeCloudVoidOnce(env.DB,{...target,expectedRevision:2},ctx,authority,transport,clock,true,requestId);
  }
 }
}
