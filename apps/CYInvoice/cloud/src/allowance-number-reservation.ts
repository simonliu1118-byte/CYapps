// V3.2-C3c1: Cloud ledger first-number reservation ONLY (no g0401).
// Reuses historical #440 daily number encoding, not its obsolete
// work_items.ALLOWANCE dependency. Targets schema16 + review drafts 17/18.
// This internal service MUST receive a Worker-owned CYID authenticated actor,
// never a browser role, caller-supplied approval, or official-success flag.
import {loadEncryptedCloudAllowance} from "./allowance-frozen-store";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryFrozenAllowanceByOriginalNumber} from "./amego-allowance-exact-readback";
import {reconcileOfficialAllowanceForOriginal} from "./allowance-official-proof";

export const MAX_ALLOWANCE_DAY_SEQUENCE=999+26*36*36;
const DIGITS="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const VALID_ID=/^[A-Za-z0-9_-]{8,100}$/;
const VALID_WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const VALID_DEVICE=/^dev_[0-9a-f-]{36}$/;
const VALID_HASH=/^[0-9a-f]{64}$/;
export function allowanceNumberFromSequence(day:string,sequence:number):string {
 if(typeof day!=="string"||!/^\d{8}$/.test(day)||
   !Number.isSafeInteger(sequence)||sequence<1||sequence>MAX_ALLOWANCE_DAY_SEQUENCE)
  throw new Error("ALLOWANCE_SEQUENCE_INVALID");
 const y=+day.slice(0,4),m=+day.slice(4,6),d=+day.slice(6,8);
 const calendar=new Date(Date.UTC(y,m-1,d));
 if(calendar.getUTCFullYear()!==y||calendar.getUTCMonth()+1!==m||
    calendar.getUTCDate()!==d)
  throw new Error("ALLOWANCE_DATE_INVALID");
 if(sequence<=999) return `A${day}${String(sequence).padStart(3,"0")}`;
 const n=sequence-1000;
 return `A${day}${String.fromCharCode(65+Math.floor(n/1296))}`+
   `${DIGITS[Math.floor(n%1296/36)]}${DIGITS[n%36]}`;
}
export interface CloudAllowanceReserveTarget {
 workspaceId:string;applicationId:string;deviceId:string;
 expectedRevision:number;
}
export interface CloudAllowanceReservedNumber {
 applicationId:string;ledgerId:string;allowanceNumber:string;
 sequence:number;attempt:number;status:"reserved";requestHash:string;
 // A reserved number is NOT approval to send or an AMEGO success.
 maySendWithoutFreshOfficialCheck:false;
 sentToAmego:false;
}
interface ApprovalRow{
 approval_id:string;application_id:string;workspace_id:string;
 reviewed_revision:number;manager_actor_id:string;manager_device_id:string;
 request_hash:string;valid_until_utc:string;
}
interface LedgerRow{
 ledger_id:string;application_id:string;workspace_id:string;
 allowance_number:string;sequence:number;attempt:number;
 status:"reserved"|"sending"|"confirmed"|"occupied_external";
 request_hash:string;seller_invoice:string;environment:"test"|"production";
 allowance_day:string;
 collision_code:number|null;
}
function sameManager(a:AllowanceActor|null,b:AllowanceActor|null,workspace:string){
 return !!a&&!!b&&a.workspaceId===workspace&&b.workspaceId===workspace&&
   (a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN")&&
   a.employeeId===b.employeeId&&a.workspaceRole===b.workspaceRole&&
   typeof a.employeeId==="string"&&a.employeeId.length>0&&a.employeeId.length<=100;
}
function requireManager(actor:AllowanceActor|null,workspace:string): asserts actor is AllowanceActor {
 if(!actor||actor.workspaceId!==workspace||
   (actor.workspaceRole!=="ADMIN"&&actor.workspaceRole!=="SUPER_ADMIN")||
   typeof actor.employeeId!=="string"||!actor.employeeId||
   actor.employeeId.length>100)
  throw new Error("ALLOWANCE_RESERVE_MANAGER_REQUIRED");
}
async function deviceReady(db:D1Database,target:CloudAllowanceReserveTarget){
 const d=await db.prepare(`SELECT d.device_id FROM devices d
 JOIN workspaces w ON w.workspace_id=d.workspace_id
 WHERE d.device_id=?1 AND d.workspace_id=?2
 AND d.status='active' AND d.employee_authority_state='cloud'
 AND w.status='active' LIMIT 1`)
 .bind(target.deviceId,target.workspaceId).first<{device_id:string}>();
 return !!d;
}
async function latestLedger(db:D1Database,target:CloudAllowanceReserveTarget){
 return db.prepare(`SELECT ledger_id,application_id,workspace_id,
    allowance_number,sequence,attempt,status,request_hash,
    seller_invoice,environment,allowance_day,collision_code
 FROM allowance_number_ledger
 WHERE workspace_id=?1 AND application_id=?2
 ORDER BY attempt DESC LIMIT 1`)
 .bind(target.workspaceId,target.applicationId).first<LedgerRow>();
}
function readbackOf(row:LedgerRow,hash:string,day:string,seller:string,env:string)
 :CloudAllowanceReservedNumber{
 if(row.request_hash!==hash||row.seller_invoice!==seller||
    row.environment!==env||row.allowance_day!==day||
    !Number.isSafeInteger(row.attempt)||row.attempt<1||row.status!=="reserved"||
    row.allowance_number!==allowanceNumberFromSequence(day,row.sequence))
  throw new Error("ALLOWANCE_RESERVE_PRIOR_UNSAFE_OR_NOT_RESERVABLE");
 return {applicationId:row.application_id,ledgerId:row.ledger_id,
  allowanceNumber:row.allowance_number,sequence:row.sequence,
  attempt:row.attempt,status:"reserved",requestHash:hash,
  maySendWithoutFreshOfficialCheck:false,sentToAmego:false};
}
export async function reserveApprovedCloudAllowanceNumber(
 db:D1Database,target:CloudAllowanceReserveTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,clock:()=>Date=()=>new Date()
):Promise<CloudAllowanceReservedNumber>{
 if(typeof target.workspaceId!=="string"||!VALID_WS.test(target.workspaceId)||
   typeof target.applicationId!=="string"||!VALID_ID.test(target.applicationId)||
   typeof target.deviceId!=="string"||!VALID_DEVICE.test(target.deviceId)||
   !Number.isSafeInteger(target.expectedRevision)||target.expectedRevision<3)
  throw new Error("ALLOWANCE_RESERVE_TARGET_INVALID");
 const first=await authority.revalidate();
 requireManager(first,target.workspaceId);
 if(!await deviceReady(db,target))throw new Error("ALLOWANCE_RESERVE_DEVICE_REVOKED");
 const frozen=await loadEncryptedCloudAllowance(db,target,ctx,authority);
 if(frozen.status!=="approved"||frozen.revision!==target.expectedRevision||
    !VALID_HASH.test(frozen.requestHash))
  throw new Error("ALLOWANCE_RESERVE_NOT_APPROVED");
 const grant=await db.prepare(`SELECT approval_id,application_id,workspace_id,
 reviewed_revision,manager_actor_id,manager_device_id,request_hash,
 valid_until_utc FROM allowance_execution_approvals
 WHERE application_id=?1 AND workspace_id=?2
 AND reviewed_revision=?3 AND request_hash=?4 LIMIT 1`)
 .bind(target.applicationId,target.workspaceId,target.expectedRevision-1,
  frozen.requestHash).first<ApprovalRow>();
 if(!grant||grant.manager_actor_id!==first.employeeId||
    grant.manager_device_id!==target.deviceId)
  throw new Error("ALLOWANCE_RESERVE_GRANT_MISMATCH");
 const now=clock();
 if(!(now instanceof Date)||!Number.isFinite(now.valueOf()))
  throw new Error("ALLOWANCE_RESERVE_CLOCK_INVALID");
 const at=now.toISOString();
 const company=await db.prepare(`SELECT seller_invoice,revision,amego_app_key_ciphertext
 FROM company_settings WHERE workspace_id=?1`)
 .bind(target.workspaceId).first<{
   seller_invoice:string;revision:number;amego_app_key_ciphertext:string|null
 }>();
 if(!company||company.seller_invoice!==frozen.sellerInvoice||
    company.revision!==frozen.companyRevision||!company.amego_app_key_ciphertext)
  throw new Error("ALLOWANCE_RESERVE_COMPANY_CHANGED");
 const second=await authority.revalidate();
 if(!sameManager(first,second,target.workspaceId)||
    !await deviceReady(db,target))
  throw new Error("ALLOWANCE_RESERVE_AUTH_CHANGED");
 const existing=await latestLedger(db,target);
 // Returning an existing RESERVED number is read-only. The later one-shot
 // sender still MUST check a fresh official balance and win sending CAS.
 // Unknown/sending/confirmed/external-collision must NEVER reserve again here.
 if(existing){
  if(existing.attempt>1&&!await hasCollisionReservationAudit(db,target,existing.ledger_id))
   throw new Error("ALLOWANCE_RESERVE_COLLISION_AUDIT_REQUIRED");
  const reserved=readbackOf(existing,frozen.requestHash,
    frozen.allowanceDay,frozen.sellerInvoice,frozen.environment);
  const after=await authority.revalidate();
  if(!sameManager(first,after,target.workspaceId)||!await deviceReady(db,target))
   throw new Error("ALLOWANCE_RESERVE_READBACK_REQUIRED");
  return reserved;
 }
 if(!(Date.parse(grant.valid_until_utc)>now.valueOf()))
  throw new Error("ALLOWANCE_RESERVE_GRANT_EXPIRED");
 // SQL-time MAX+1 is the only allocator. The conditional INSERT plus
 // UNIQUE(day,sequence) serializes two independent devices; if the winner
 // cannot be established, return an ambiguous READBACK_REQUIRED outcome.
 const candidate=await db.prepare(`SELECT COALESCE(MAX(sequence),0)+1 AS n
 FROM allowance_number_ledger
 WHERE seller_invoice=?1 AND environment=?2 AND allowance_day=?3`)
 .bind(frozen.sellerInvoice,frozen.environment,frozen.allowanceDay)
 .first<{n:number}>();
 if(!candidate||!Number.isSafeInteger(candidate.n))
  throw new Error("ALLOWANCE_RESERVE_SEQUENCE_UNAVAILABLE");
 const sequence=candidate.n;
 const number=allowanceNumberFromSequence(frozen.allowanceDay,sequence);
 const ledgerId="al_"+crypto.randomUUID();
 let result:D1Result;
 try{
  result=await db.prepare(`INSERT INTO allowance_number_ledger
   (ledger_id,application_id,workspace_id,seller_invoice,environment,
    allowance_day,sequence,allowance_number,attempt,request_hash,status,
    created_by_device_id,created_at,updated_at)
   SELECT ?1,a.application_id,a.workspace_id,?4,?5,?6,?7,?8,
     1,?9,'reserved',?10,?11,?11
   FROM allowance_applications a
   JOIN allowance_frozen_requests f ON f.application_id=a.application_id
      AND f.workspace_id=a.workspace_id
   JOIN allowance_execution_approvals p ON p.application_id=a.application_id
   JOIN devices d ON d.device_id=?10 AND d.workspace_id=a.workspace_id
   JOIN workspaces w ON w.workspace_id=a.workspace_id
   JOIN company_settings c ON c.workspace_id=a.workspace_id
   WHERE a.application_id=?2 AND a.workspace_id=?3
    AND a.status='approved' AND a.revision=?12
    AND a.request_hash=?9 AND f.request_hash=?9
    AND f.seller_invoice=?4 AND f.environment=?5 AND f.allowance_day=?6
    AND f.company_settings_revision=?13 AND c.revision=?13
    AND c.seller_invoice=?4 AND c.amego_app_key_ciphertext IS NOT NULL
    AND p.reviewed_revision=?12-1
    AND p.request_hash=?9 AND p.manager_actor_id=?14
    AND p.manager_device_id=?10 AND p.valid_until_utc>?11
    AND d.status='active' AND d.employee_authority_state='cloud'
    AND w.status='active'
    AND NOT EXISTS (SELECT 1 FROM allowance_number_ledger old
      WHERE old.application_id=?2)
    AND ?7=(SELECT COALESCE(MAX(other.sequence),0)+1
      FROM allowance_number_ledger other
      WHERE other.seller_invoice=?4 AND other.environment=?5
        AND other.allowance_day=?6)`)
   .bind(ledgerId,target.applicationId,target.workspaceId,
     frozen.sellerInvoice,frozen.environment,frozen.allowanceDay,
     sequence,number,frozen.requestHash,target.deviceId,at,
     target.expectedRevision,frozen.companyRevision,first.employeeId).run();
 }catch{throw new Error("ALLOWANCE_RESERVE_READBACK_REQUIRED");}
 if(result.meta.changes!==1)
  throw new Error("ALLOWANCE_RESERVE_READBACK_REQUIRED");
 // After any uncertain last-state readback, do not retry the INSERT or
 // assume a lost response means the number was not reserved.
 const last=await authority.revalidate();
 if(!sameManager(first,last,target.workspaceId)||!await deviceReady(db,target))
  throw new Error("ALLOWANCE_RESERVE_READBACK_REQUIRED");
 return {applicationId:target.applicationId,ledgerId,
   allowanceNumber:number,sequence,attempt:1,status:"reserved",
   requestHash:frozen.requestHash,maySendWithoutFreshOfficialCheck:false,
   sentToAmego:false};
}

export async function hasCollisionReservationAudit(db:D1Database,
 t:CloudAllowanceReserveTarget,ledgerId:string):Promise<boolean>{
 return !!await db.prepare(`SELECT r.review_id FROM allowance_manual_review_audit r
 JOIN allowance_number_ledger old ON old.ledger_id=r.ledger_id
 JOIN allowance_number_ledger n ON n.ledger_id=r.next_ledger_id
 WHERE r.workspace_id=?1 AND r.application_id=?2 AND r.next_ledger_id=?3
 AND r.disposition='external_collision_reserved' AND old.status='occupied_external'
 AND n.application_id=old.application_id AND n.workspace_id=old.workspace_id
 AND n.attempt=old.attempt+1 AND n.request_hash=old.request_hash LIMIT 1`)
 .bind(t.workspaceId,t.applicationId,ledgerId).first();
}

// The same allocator owns first and manager-reviewed subsequent reservations.
// This never posts to g0401, changes the old attempt, or unlocks an unknown.
export async function reserveCloudAllowanceAfterExternalCollision(
 db:D1Database,t:CloudAllowanceReserveTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,reviewId:string,previousNumber:string,confirmation:string,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<CloudAllowanceReservedNumber>{
 if(!VALID_WS.test(t.workspaceId)||!VALID_ID.test(t.applicationId)||
 !VALID_DEVICE.test(t.deviceId)||!Number.isSafeInteger(t.expectedRevision)||
 t.expectedRevision<3||!/^[A-Za-z0-9_-]{12,100}$/.test(reviewId)||
 !/^A\d{8}[0-9A-Z]{3}$/.test(previousNumber)||
 confirmation!==`確認外部占號 ${previousNumber} 並預留下一號`)
  throw new Error("ALLOWANCE_COLLISION_CONFIRMATION_REQUIRED");
 const first=await authority.revalidate();requireManager(first,t.workspaceId);
 if(!await deviceReady(db,t))throw new Error("ALLOWANCE_COLLISION_DEVICE_REVOKED");
 const frozen=await loadEncryptedCloudAllowance(db,t,ctx,authority);
 const old=await latestLedger(db,t);
 if(!old||old.status!=="occupied_external"||![4040161,4040162].includes(old.collision_code??0)||
 old.allowance_number!==previousNumber||old.request_hash!==frozen.requestHash||
 old.seller_invoice!==frozen.sellerInvoice||old.environment!==frozen.environment||
 old.allowance_day!==frozen.allowanceDay||!Number.isSafeInteger(old.attempt)||old.attempt<1||
 frozen.status!=="approved"||frozen.revision!==t.expectedRevision)
  throw new Error("ALLOWANCE_COLLISION_NOT_ELIGIBLE");
 const grant=await db.prepare(`SELECT manager_actor_id,manager_device_id,
  official_snapshot_hash,valid_until_utc FROM allowance_execution_approvals
  WHERE workspace_id=?1 AND application_id=?2 AND reviewed_revision=?3
  AND request_hash=?4 LIMIT 1`)
 .bind(t.workspaceId,t.applicationId,t.expectedRevision-1,frozen.requestHash)
 .first<{manager_actor_id:string;manager_device_id:string;
  official_snapshot_hash:string;valid_until_utc:string}>();
 if(!grant||grant.manager_actor_id!==first.employeeId||grant.manager_device_id!==t.deviceId)
  throw new Error("ALLOWANCE_COLLISION_GRANT_MISMATCH");
 const recorded=await db.prepare(`SELECT check_id FROM allowance_reconciliation_checks
  WHERE ledger_id=?1 AND workspace_id=?2 AND application_id=?3
  AND evidence_kind='external_collision' LIMIT 1`)
 .bind(old.ledger_id,t.workspaceId,t.applicationId).first();
 if(!recorded)throw new Error("ALLOWANCE_COLLISION_EVIDENCE_REQUIRED");
 const company=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==frozen.companyRevision||company.sellerInvoice!==frozen.sellerInvoice)
  throw new Error("ALLOWANCE_COLLISION_COMPANY_CHANGED");
 const external=await queryFrozenAllowanceByOriginalNumber({sellerInvoice:company.sellerInvoice,
  appKey:company.amegoAppKey,allowanceNumber:previousNumber,allowanceDay:frozen.allowanceDay,
  unnumberedTemplateJson:frozen.unnumberedTemplateJson},transport,clock,true);
 if(external.kind!=="verified_external_collision")
  throw new Error("ALLOWANCE_COLLISION_NOT_DISTINCT");
 const proof=await reconcileOfficialAllowanceForOriginal(frozen,{
  sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey},transport,clock);
 const now=clock(),at=now.toISOString();
 if(!(Date.parse(grant.valid_until_utc)>now.valueOf())||
  proof.officialEvidenceHash!==grant.official_snapshot_hash||
  proof.applicationId!==t.applicationId||proof.originalInvoiceNumber!==frozen.originalInvoiceNumber||
  proof.requestedGross!==(BigInt(frozen.requestedGross)*10000000n).toString()||
  [external.officialCheckedAtUtc,proof.officialCheckedAtUtc].some(value=>
   !Number.isFinite(Date.parse(value))||now.valueOf()-Date.parse(value)>60_000||
   Date.parse(value)>now.valueOf()+1000))
  throw new Error("ALLOWANCE_COLLISION_OFFICIAL_CHANGED_OR_GRANT_EXPIRED");
 const current=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,ctx.encryptionKeyHex);
 const second=await authority.revalidate();
 if(!sameManager(first,second,t.workspaceId)||!await deviceReady(db,t)||
 current.revision!==company.revision||current.sellerInvoice!==company.sellerInvoice||
 current.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_COLLISION_AUTH_OR_COMPANY_CHANGED");
 const candidate=await db.prepare(`SELECT COALESCE(MAX(sequence),0)+1 AS n
  FROM allowance_number_ledger WHERE seller_invoice=?1 AND environment=?2 AND allowance_day=?3`)
 .bind(frozen.sellerInvoice,frozen.environment,frozen.allowanceDay).first<{n:number}>();
 const sequence=candidate?.n??0,number=allowanceNumberFromSequence(frozen.allowanceDay,sequence);
 const nextId="al_"+crypto.randomUUID(),checkId="chk_"+crypto.randomUUID();
 try{
  const result=await db.batch([
   db.prepare(`INSERT INTO allowance_reconciliation_checks
    (check_id,ledger_id,application_id,workspace_id,allowance_number,official_snapshot_hash,
     checked_by_device_id,checked_by_actor_id,checked_at,evidence_kind)
    VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,'external_collision')`)
    .bind(checkId,old.ledger_id,t.applicationId,t.workspaceId,previousNumber,
     external.officialSnapshotHash,t.deviceId,first.employeeId,at),
   db.prepare(`INSERT INTO allowance_number_ledger
    (ledger_id,application_id,workspace_id,seller_invoice,environment,allowance_day,
     sequence,allowance_number,attempt,request_hash,status,created_by_device_id,created_at,updated_at)
    SELECT ?1,l.application_id,l.workspace_id,l.seller_invoice,l.environment,l.allowance_day,
     ?2,?3,l.attempt+1,l.request_hash,'reserved',?4,?5,?5
    FROM allowance_number_ledger l JOIN allowance_applications a ON a.application_id=l.application_id
    JOIN allowance_frozen_requests f ON f.application_id=a.application_id
    JOIN allowance_execution_approvals p ON p.application_id=a.application_id
    JOIN company_settings c ON c.workspace_id=a.workspace_id
    JOIN devices d ON d.device_id=?4 AND d.workspace_id=a.workspace_id
    JOIN workspaces w ON w.workspace_id=a.workspace_id
    WHERE l.ledger_id=?6 AND l.workspace_id=?7 AND l.application_id=?8
    AND l.status='occupied_external' AND l.collision_code IN (4040161,4040162)
    AND l.allowance_number=?9 AND l.request_hash=?10 AND l.attempt=?11
    AND a.status='approved' AND a.revision=?12 AND a.manager_actor_id=?13 AND a.request_hash=?10
    AND f.workspace_id=?7 AND f.request_hash=?10 AND f.company_settings_revision=?14
    AND c.revision=?14 AND c.seller_invoice=l.seller_invoice AND c.amego_app_key_ciphertext IS NOT NULL
    AND p.workspace_id=?7 AND p.reviewed_revision=?12-1 AND p.request_hash=?10
    AND p.manager_actor_id=?13 AND p.manager_device_id=?4 AND p.valid_until_utc>?5
    AND p.official_snapshot_hash=?15 AND d.status='active'
    AND d.employee_authority_state='cloud' AND w.status='active'
    AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger n WHERE n.application_id=?8 AND n.attempt>?11)
    AND ?2=(SELECT COALESCE(MAX(n.sequence),0)+1 FROM allowance_number_ledger n
     WHERE n.seller_invoice=l.seller_invoice AND n.environment=l.environment AND n.allowance_day=l.allowance_day)`)
    .bind(nextId,sequence,number,t.deviceId,at,old.ledger_id,t.workspaceId,t.applicationId,
     previousNumber,frozen.requestHash,old.attempt,t.expectedRevision,first.employeeId,
     frozen.companyRevision,proof.officialEvidenceHash),
   // VALUES is deliberate: a lost allocation CAS must make this source guard
   // fail, rolling back the entire batch rather than committing a partial audit.
   db.prepare(`INSERT INTO allowance_manual_review_audit
    (review_id,application_id,workspace_id,ledger_id,allowance_number,ledger_status_at_review,
     disposition,evidence_check_id,next_ledger_id,reviewed_by_actor_id,reviewed_by_device_id,reviewed_at)
    VALUES(?1,?2,?3,?4,?5,'occupied_external','external_collision_reserved',?6,?7,?8,?9,?10)`)
    .bind(reviewId,t.applicationId,t.workspaceId,old.ledger_id,previousNumber,
     checkId,nextId,first.employeeId,t.deviceId,at)
  ]);
  if(result.some(r=>r.meta.changes!==1))throw new Error("conflict");
 }catch{throw new Error("ALLOWANCE_COLLISION_READBACK_REQUIRED");}
 const last=await authority.revalidate();
 if(!sameManager(first,last,t.workspaceId)||!await deviceReady(db,t))
  throw new Error("ALLOWANCE_COLLISION_READBACK_REQUIRED");
 return {applicationId:t.applicationId,ledgerId:nextId,allowanceNumber:number,
  sequence,attempt:old.attempt+1,status:"reserved",requestHash:frozen.requestHash,
  maySendWithoutFreshOfficialCheck:false,sentToAmego:false};
}
