// CYInvoice V3.2-C3c2: INTERNAL one-shot sending CAS only.
// Derived from PR #440's sending gate; uses current schema16 + C1/C2 drafts,
// C3a Frozen request, C3b2a official per-original proof and C3b2b manager grant.
// This does NOT send g0401. No public Worker route; only the future single-shot
// service may invoke this immediately before its one and only provider POST.
// An ambiguous database result is ALWAYS readback-only, never retry-send.
import {loadEncryptedCloudAllowance} from "./allowance-frozen-store";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext,StoredCloudAllowance} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway,amegoCredentialsForEnvironment} from "./company-settings";
import {reconcileOfficialAllowanceForOriginal} from "./allowance-official-proof";
import {allowanceNumberFromSequence,hasCollisionReservationAudit} from "./allowance-number-reservation";

export interface CloudAllowanceSendingTarget {
 workspaceId:string;
 applicationId:string;
 deviceId:string;
 expectedRevision:number;
 ledgerId:string;
 allowanceNumber:string;
}
interface GrantRow {
 reviewed_revision:number;request_hash:string;official_snapshot_hash:string;
 manager_actor_id:string;manager_device_id:string;valid_until_utc:string;
}
interface LedgerRow {
 ledger_id:string;application_id:string;workspace_id:string;
 seller_invoice:string;environment:"production"|"test";
 allowance_day:string;allowance_number:string;sequence:number;
 attempt:number;status:"reserved"|"sending"|"confirmed"|"occupied_external";
 request_hash:string;created_by_device_id:string;
}
export interface CloudAllowanceSendingClaim {
 approvedRequest:StoredCloudAllowance;
 managerActorId:string;
 managerWorkspaceRole:AllowanceActor["workspaceRole"];
 applicationId:string;
 ledgerId:string;
 allowanceNumber:string;
 requestHash:string;
 officialEvidenceHash:string;
 status:"sending";
 claimedExactlyOnce:true;
 providerPostPerformed:false;
 // The future owner must couple the CAS win to a ONE-SHOT transport in the
 // same logical operation; this module alone cannot approve a future POST.
 mayPostG0401FromThisResponse:false;
}
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const NUMBER=/^A\d{8}[0-9A-Z]{3}$/;
const HASH=/^[a-f0-9]{64}$/;
function isManager(actor:AllowanceActor|null,ws:string):actor is AllowanceActor{
 return !!actor&&actor.workspaceId===ws&&
 (actor.workspaceRole==="ADMIN"||actor.workspaceRole==="SUPER_ADMIN")&&
 typeof actor.employeeId==="string"&&actor.employeeId.length>0&&
 actor.employeeId.length<=100;
}
function sameManager(a:AllowanceActor|null,b:AllowanceActor|null,ws:string):boolean{
 return isManager(a,ws)&&isManager(b,ws)&&
 a.employeeId===b.employeeId&&a.workspaceRole===b.workspaceRole;
}
async function activeDevice(db:D1Database,t:CloudAllowanceSendingTarget){
 const row=await db.prepare(`SELECT d.device_id FROM devices d
 JOIN workspaces w ON w.workspace_id=d.workspace_id
 WHERE d.device_id=?1 AND d.workspace_id=?2
 AND d.status='active' AND d.employee_authority_state='cloud'
 AND w.status='active' LIMIT 1`)
  .bind(t.deviceId,t.workspaceId).first<{device_id:string}>();
 return !!row;
}
function nowUtc(clock:()=>Date):Date{
 const value=clock();
 if(!(value instanceof Date)||!Number.isFinite(value.getTime()))
  throw new Error("ALLOWANCE_SENDING_CLOCK_INVALID");
 return value;
}
function validate(t:CloudAllowanceSendingTarget){
 if(typeof t.workspaceId!=="string"||!WS.test(t.workspaceId)||
 typeof t.applicationId!=="string"||!ID.test(t.applicationId)||
 typeof t.deviceId!=="string"||!DEVICE.test(t.deviceId)||
 typeof t.ledgerId!=="string"||!/^al_[0-9a-f-]{36}$/.test(t.ledgerId)||
 typeof t.allowanceNumber!=="string"||!NUMBER.test(t.allowanceNumber)||
 !Number.isSafeInteger(t.expectedRevision)||t.expectedRevision<3)
  throw new Error("ALLOWANCE_SENDING_TARGET_INVALID");
}
export async function claimCloudAllowanceOneShotSending(
 db:D1Database,t:CloudAllowanceSendingTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date()
):Promise<CloudAllowanceSendingClaim>{
 validate(t);
 const first=await authority.revalidate();
 if(!isManager(first,t.workspaceId)||!await activeDevice(db,t))
  throw new Error("ALLOWANCE_SENDING_MANAGER_REQUIRED");
 const frozen=await loadEncryptedCloudAllowance(db,t,ctx,authority);
 if(frozen.status!=="approved"||frozen.revision!==t.expectedRevision||
 !HASH.test(frozen.requestHash))
  throw new Error("ALLOWANCE_SENDING_NOT_APPROVED");
 const ledger=await db.prepare(`SELECT ledger_id,application_id,workspace_id,
 seller_invoice,environment,allowance_day,allowance_number,sequence,attempt,
 status,request_hash,created_by_device_id
 FROM allowance_number_ledger WHERE ledger_id=?1 AND application_id=?2
 AND workspace_id=?3 LIMIT 1`)
 .bind(t.ledgerId,t.applicationId,t.workspaceId).first<LedgerRow>();
 if(!ledger||ledger.status!=="reserved"||!Number.isSafeInteger(ledger.attempt)||ledger.attempt<1||
 ledger.allowance_number!==t.allowanceNumber||
 ledger.allowance_number!==allowanceNumberFromSequence(
   frozen.allowanceDay,ledger.sequence)||
 ledger.request_hash!==frozen.requestHash||
 ledger.created_by_device_id!==t.deviceId||
 ledger.seller_invoice!==frozen.sellerInvoice||
 ledger.environment!==frozen.environment||ledger.allowance_day!==frozen.allowanceDay)
  throw new Error("ALLOWANCE_SENDING_LEDGER_NOT_RESERVED");
 if(ledger.attempt>1&&!await hasCollisionReservationAudit(db,t,ledger.ledger_id))
  throw new Error("ALLOWANCE_SENDING_COLLISION_AUDIT_REQUIRED");
 const grant=await db.prepare(`SELECT reviewed_revision,request_hash,
 official_snapshot_hash,manager_actor_id,manager_device_id,valid_until_utc
 FROM allowance_execution_approvals WHERE application_id=?1
 AND workspace_id=?2 AND reviewed_revision=?3 AND request_hash=?4 LIMIT 1`)
 .bind(t.applicationId,t.workspaceId,t.expectedRevision-1,
 frozen.requestHash).first<GrantRow>();
 if(!grant||grant.manager_actor_id!==first.employeeId||
 grant.manager_device_id!==t.deviceId||!HASH.test(grant.official_snapshot_hash))
  throw new Error("ALLOWANCE_SENDING_GRANT_MISMATCH");
 const company=await loadAmegoCompanySettingsForGateway(
 db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.sellerInvoice!==frozen.sellerInvoice||
 company.revision!==frozen.companyRevision)
  throw new Error("ALLOWANCE_SENDING_COMPANY_CHANGED");
 // Do NOT accept browser-supplied official balances. Make new signed
 // invoice_query + allowance_query read-only calls for ALL historical numbers.
 const proof=await reconcileOfficialAllowanceForOriginal(frozen,{
  ...amegoCredentialsForEnvironment(company,ctx.environment)
 },transport,clock);
 const date=nowUtc(clock),at=date.toISOString();
 const checked=Date.parse(proof.officialCheckedAtUtc);
 if(proof.applicationId!==t.applicationId||
 proof.originalInvoiceNumber!==frozen.originalInvoiceNumber||
 proof.officialEvidenceHash!==grant.official_snapshot_hash||
 proof.requestedGross!==(BigInt(frozen.requestedGross)*10000000n).toString()||
 proof.approvalRecorded!==false||proof.numberReserved!==false||
 !Number.isFinite(checked)||checked>date.valueOf()+1000||
 date.valueOf()-checked>60_000)
  throw new Error("ALLOWANCE_SENDING_OFFICIAL_CHANGED_OR_GRANT_EXPIRED");
 const second=await authority.revalidate();
 if(!sameManager(first,second,t.workspaceId)||
 !await activeDevice(db,t))
  throw new Error("ALLOWANCE_SENDING_AUTH_CHANGED");
 const currentCompany=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(currentCompany.revision!==company.revision||
 currentCompany.sellerInvoice!==company.sellerInvoice||
 currentCompany.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_SENDING_COMPANY_CHANGED");
 // Single SQL-time CAS is the only transition owner.
 // Existing C2 schema trigger also forbids simultaneous 'sending' attempts
 // on the same original invoice (across distinct approved applications).
 // The trusted official hash and exact same grant are part of SQL CAS.
 let result:D1Result;
 try{
 result=await db.prepare(`UPDATE allowance_number_ledger
 SET status='sending',updated_at=?1
 WHERE ledger_id=?2 AND workspace_id=?3 AND application_id=?4
 AND allowance_number=?5 AND status='reserved'
 AND request_hash=?6 AND created_by_device_id=?7
 AND seller_invoice=?8 AND environment=?9 AND allowance_day=?10
 AND sequence=?11 AND attempt=?16 AND collision_code IS NULL
 AND EXISTS (
  SELECT 1 FROM allowance_applications a
  JOIN allowance_frozen_requests f ON f.application_id=a.application_id
    AND f.workspace_id=a.workspace_id
  JOIN allowance_execution_approvals p ON p.application_id=a.application_id
  JOIN devices d ON d.device_id=?7 AND d.workspace_id=a.workspace_id
  JOIN workspaces w ON w.workspace_id=a.workspace_id
  JOIN company_settings c ON c.workspace_id=a.workspace_id
  WHERE a.application_id=?4 AND a.workspace_id=?3
   AND a.status='approved' AND a.revision=?12
   AND a.request_hash=?6 AND a.manager_actor_id=?13
   AND f.request_hash=?6 AND f.environment=?9 AND f.allowance_day=?10
   AND f.seller_invoice=?8 AND f.company_settings_revision=?14
   AND c.seller_invoice=?8 AND c.revision=?14
   AND c.amego_app_key_ciphertext IS NOT NULL
   AND p.reviewed_revision=?12-1
   AND p.manager_actor_id=?13 AND p.manager_device_id=?7
   AND p.request_hash=?6 AND p.official_snapshot_hash=?15
   AND d.status='active' AND d.employee_authority_state='cloud'
   AND w.status='active'
  )
 AND NOT EXISTS (
  SELECT 1 FROM allowance_frozen_requests original
  JOIN allowance_frozen_requests competing
   ON competing.seller_invoice=original.seller_invoice
    AND competing.environment=original.environment
    AND competing.original_invoice_fingerprint=original.original_invoice_fingerprint
    AND competing.application_id<>original.application_id
  JOIN allowance_number_ledger other ON other.application_id=competing.application_id
    AND other.status='sending'
  WHERE original.application_id=?4
 )`)
  .bind(at,t.ledgerId,t.workspaceId,t.applicationId,t.allowanceNumber,
   frozen.requestHash,t.deviceId,frozen.sellerInvoice,frozen.environment,
   frozen.allowanceDay,ledger.sequence,t.expectedRevision,first.employeeId,
   frozen.companyRevision,proof.officialEvidenceHash,ledger.attempt).run();
 }catch{
  // A D1 timeout may happen AFTER status moved to sending. In that case
  // every new invocation fails read-only at the ledger guard above.
  throw new Error("ALLOWANCE_SENDING_READBACK_ONLY");
 }
 if(result.meta.changes!==1)
  throw new Error("ALLOWANCE_SENDING_READBACK_ONLY");
 const after=await authority.revalidate();
 if(!sameManager(first,after,t.workspaceId)||!await activeDevice(db,t))
  throw new Error("ALLOWANCE_SENDING_READBACK_ONLY");
 return {approvedRequest:frozen,managerActorId:first.employeeId,managerWorkspaceRole:first.workspaceRole,
  applicationId:t.applicationId,ledgerId:t.ledgerId,
  allowanceNumber:t.allowanceNumber,requestHash:frozen.requestHash,
  officialEvidenceHash:proof.officialEvidenceHash,status:"sending",
  claimedExactlyOnce:true,providerPostPerformed:false,
  mayPostG0401FromThisResponse:false};
}
