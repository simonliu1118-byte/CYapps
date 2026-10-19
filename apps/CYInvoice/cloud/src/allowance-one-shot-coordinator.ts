// V3.2-C3c3b - sole INTERNAL Worker-side orchestration of one g0401 attempt.
// No HTTP route, live secret, deployment or genuine provider requests in CI.
// Any crash after the CAS leaves the ledger 'sending': retry means READBACK,
// never another g0401 POST. An exact readback alone is UNATTRIBUTED and
// may not set 'confirmed' without stronger origin evidence.
import {claimCloudAllowanceOneShotSending} from "./allowance-sending-claim";
import type {CloudAllowanceSendingTarget} from "./allowance-sending-claim";
import {loadEncryptedCloudAllowance} from "./allowance-frozen-store";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {postNumberedAllowanceOnce} from "./amego-allowance-issue";
import type {OneShotAllowanceOutcome} from "./amego-allowance-issue";
import {queryExactFrozenAllowanceByOriginalNumber} from "./amego-allowance-exact-readback";

export interface OneShotCloudResult {
 applicationId:string;ledgerId:string;allowanceNumber:string;
 issueKind:OneShotAllowanceOutcome["kind"];
 originalNumberReadback:"full_match_unattributed"|"unconfirmed";
 recordedReadback:boolean;
 providerPostAttempted:true;
 ledgerStatus:"sending";
 safeToAutoRetry:false;
 safeToAutoConfirm:false;
 canAllocateAnotherNumber:false;
}
function isSameManager(a:AllowanceActor|null,b:AllowanceActor|null,ws:string):boolean{
 return !!a&&!!b&&a.workspaceId===ws&&b.workspaceId===ws&&
 a.employeeId===b.employeeId&&
 (a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN")&&
 a.workspaceRole===b.workspaceRole;
}
async function activeDevice(db:D1Database,ws:string,device:string):Promise<boolean>{
 return !!await db.prepare(`SELECT d.device_id FROM devices d
 JOIN workspaces w ON w.workspace_id=d.workspace_id
 WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active'
 AND d.employee_authority_state='cloud' AND w.status='active' LIMIT 1`)
 .bind(device,ws).first<{device_id:string}>();
}
async function recordReadback(
 db:D1Database,t:CloudAllowanceSendingTarget,actor:string,
 hash:string,clock:()=>Date
):Promise<boolean>{
 const date=clock();
 if(!(date instanceof Date)||!Number.isFinite(date.getTime()))
  return false;
 try {
  const r=await db.prepare(`INSERT INTO allowance_reconciliation_checks
   (check_id,ledger_id,application_id,workspace_id,allowance_number,
    official_snapshot_hash,checked_by_device_id,checked_by_actor_id,checked_at)
   SELECT ?1,l.ledger_id,l.application_id,l.workspace_id,l.allowance_number,
    ?6,?7,?8,?9 FROM allowance_number_ledger l
   JOIN devices d ON d.device_id=?7 AND d.workspace_id=l.workspace_id
   JOIN workspaces w ON w.workspace_id=l.workspace_id
   WHERE l.ledger_id=?2 AND l.application_id=?3 AND l.workspace_id=?4
    AND l.allowance_number=?5 AND l.status='sending'
    AND d.status='active' AND d.employee_authority_state='cloud'
    AND w.status='active'`)
  .bind("chk_"+crypto.randomUUID(),t.ledgerId,t.applicationId,t.workspaceId,
   t.allowanceNumber,hash,t.deviceId,actor,date.toISOString()).run();
  return r.meta.changes===1;
 }catch{return false;}
}
// Trusted Cloud handler may call this once per explicit manager confirmation.
// Do not export a stand-alone network POST endpoint or accept buyer/key from
// the request. This function owns claim -> provider POST -> exact readback.
export async function executeCloudAllowanceExactlyOnce(
 db:D1Database,t:CloudAllowanceSendingTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date()
):Promise<OneShotCloudResult>{
 const first=await authority.revalidate();
 if(!first||!await activeDevice(db,t.workspaceId,t.deviceId))
  throw new Error("ALLOWANCE_EXEC_AUTH_REQUIRED");
 // Fresh official balance check and atomic 'reserved -> sending' is owned by
 // C3c2. Once it returns successfully, no code path may claim again.
 await claimCloudAllowanceOneShotSending(db,t,ctx,authority,transport,clock);
 // Any failure here is intentionally an UNKNOWN after CAS. Keep 'sending'.
 // Never restart the coordinator from its first step.
 const frozen=await loadEncryptedCloudAllowance(db,t,ctx,authority);
 const company=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 const second=await authority.revalidate();
 if(!isSameManager(first,second,t.workspaceId)||
    !await activeDevice(db,t.workspaceId,t.deviceId)||
    frozen.status!=="approved"||frozen.revision!==t.expectedRevision||
    frozen.sellerInvoice!==company.sellerInvoice||
    frozen.companyRevision!==company.revision)
  throw new Error("ALLOWANCE_EXEC_POST_CAS_READBACK_ONLY");
 const input={sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
  allowanceNumber:t.allowanceNumber,allowanceDay:frozen.allowanceDay,
  unnumberedTemplateJson:frozen.unnumberedTemplateJson};
 let issued:OneShotAllowanceOutcome={kind:"unknown_result"};
 try{issued=await postNumberedAllowanceOnce(input,transport,clock);}
 catch{issued={kind:"unknown_result"};}
 // After the single POST, a provider/network failure or a process crash is
 // ambiguous. Read original number at most once in this invocation.
 // No conditional transport retries, even for code=15 or code=0.
 let readback:"full_match_unattributed"|"unconfirmed"="unconfirmed";
 let recorded=false;
 try {
  const matching=await queryExactFrozenAllowanceByOriginalNumber(
    input,transport,clock);
  if(matching.kind==="full_frozen_match_unattributed"){
   readback="full_match_unattributed";
   const latest=await authority.revalidate();
   if(isSameManager(first,latest,t.workspaceId)&&
      await activeDevice(db,t.workspaceId,t.deviceId))
    recorded=await recordReadback(db,t,first.employeeId,
      matching.officialSnapshotHash,clock);
  }
 }catch{/* unreadable/mismatched official response remains UNKNOWN */}
 return {applicationId:t.applicationId,ledgerId:t.ledgerId,
  allowanceNumber:t.allowanceNumber,issueKind:issued.kind,
  originalNumberReadback:readback,recordedReadback:recorded,
  providerPostAttempted:true,ledgerStatus:"sending",
  safeToAutoRetry:false,safeToAutoConfirm:false,
  canAllocateAnotherNumber:false};
}
