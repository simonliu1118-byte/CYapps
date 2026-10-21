// V3.2 D/E: Cloud g0501 reconciliation after network recovery.
// READ ONLY: never starts a g0501, changes a D1 claim, marks completion,
// retries a financial mutation or infers THIS POST from D0501/99.
import {loadCloudAllowanceVoidForManager} from "./allowance-void-applications";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {readOfficialAllowanceVoidStatus} from "./amego-allowance-status";
import type {AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";

export type CloudVoidReadbackState =
 "not_claimed_unverified"|"official_void_observed_unattributed"|
 "official_void_unconfirmed"|"official_unavailable_readback_only";
export interface CloudVoidReconnectionObservation {
 applicationId:string;
 claimRecorded:boolean;
 state:CloudVoidReadbackState;
 officialSnapshotHash:string|null;
 checkedAtUtc:string|null;
 officiallyVoided:boolean|null;
 requiresManualReview:true;
 safeToAutoResend:false;
 safeToAutoClose:false;
 thisPostAttributed:false;
}
export async function readCloudVoidAfterReconnect(
 db:D1Database,t:{workspaceId:string;applicationId:string;deviceId:string},
 ctx:CloudAllowanceContext,authority:AllowanceActorSource,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<CloudVoidReconnectionObservation>{
 // Manager-only: the internal Frozen fields are never returned, even when
 // an unknown provider result is being investigated after a lost POST reply.
 const first=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 const claim=await db.prepare("SELECT application_id,request_hash,"+
  "official_snapshot_hash FROM allowance_void_send_claims "+
  "WHERE application_id=?1 AND workspace_id=?2 LIMIT 1")
  .bind(t.applicationId,t.workspaceId)
  .first<{application_id:string;request_hash:string;official_snapshot_hash:string}>();
 if(claim && (claim.application_id!==first.applicationId||
  claim.request_hash!==first.requestHash||
  !/^[0-9a-f]{64}$/.test(claim.official_snapshot_hash)))
  throw new Error("VOID_READBACK_CLAIM_INVALID");
 const base={applicationId:t.applicationId,claimRecorded:!!claim,
  requiresManualReview:true,safeToAutoResend:false,safeToAutoClose:false,
  thisPostAttributed:false} as const;
 if(!claim){
  // No Cloud claim does NOT prove Local never transmitted; offline devices
  // cannot be globally coordinated without a trusted ownership handoff.
  const last=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
  if(last.requestHash!==first.requestHash)
   throw new Error("VOID_READBACK_APPLICATION_CHANGED");
  return {...base,state:"not_claimed_unverified",officialSnapshotHash:null,
   checkedAtUtc:null,officiallyVoided:null};
 }
 const company=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==first.companyRevision||
  company.sellerInvoice!==first.sellerInvoice)
  throw new Error("VOID_READBACK_COMPANY_CHANGED");
 let status:Awaited<ReturnType<typeof readOfficialAllowanceVoidStatus>>|null=null;
 try{
  status=await readOfficialAllowanceVoidStatus({
   sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
   allowanceNumber:first.allowanceNumber
  },transport,clock);
 }catch{
  // Offline or malformed official response: original Claim remains permanent.
  // No additional POST or automatic close can follow this observation.
 }
 const last=await loadCloudAllowanceVoidForManager(db,t,ctx,authority);
 if(last.requestHash!==first.requestHash||
  last.allowanceNumber!==first.allowanceNumber)
  throw new Error("VOID_READBACK_APPLICATION_CHANGED");
 const current=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(current.revision!==company.revision||
  current.sellerInvoice!==company.sellerInvoice||
  current.amegoAppKey!==company.amegoAppKey)
  throw new Error("VOID_READBACK_COMPANY_CHANGED");
 if(!status)
  return {...base,state:"official_unavailable_readback_only",
   officialSnapshotHash:null,checkedAtUtc:null,officiallyVoided:null};
 return {...base,state:status.officialCancelled
   ?"official_void_observed_unattributed":"official_void_unconfirmed",
  officialSnapshotHash:status.statusEvidenceSha256,
  checkedAtUtc:status.checkedAtUtc,
  officiallyVoided:status.officialCancelled};
}
