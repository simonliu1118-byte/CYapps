// CYInvoice V3.2-C3b2b: trusted INTERNAL manager approval writer.
// Based on the single immutable decision pattern in historical PR #440;
// targets the standalone schema16 + draft17/18 allowance owner, NEVER
// old work_items.ALLOWANCE. No HTTP route, g0401, g0501 or number reserve.
// The caller MUST create authority from the current CYID consumer session
// and bind a verified active Device: roles or evidence supplied by clients
// are never accepted as proof.
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
import {loadEncryptedCloudAllowance} from "./allowance-frozen-store";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {reconcileOfficialAllowanceForOriginal} from "./allowance-official-proof";
export interface AllowanceApprovalTarget {
 workspaceId:string;applicationId:string;deviceId:string;expectedRevision:number;
}

export interface CloudAllowanceApproval {
 approvalId:string;
 applicationId:string;
 approvedRevision:number;
 managerActorId:string;
 approvedAtUtc:string;
 validUntilUtc:string;
 requestHash:string;
 officialEvidenceHash:string;
 numberReserved:false;
 sentToAmego:false;
}
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const HASH=/^[a-f0-9]{64}$/;
function manager(a:AllowanceActor|null,workspace:string):a is AllowanceActor{
 return !!a&&a.workspaceId===workspace&&
  (a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN")&&
  typeof a.employeeId==="string"&&a.employeeId.length>0&&a.employeeId.length<=100;
}
function sameManager(a:AllowanceActor|null,b:AllowanceActor|null,ws:string):boolean{
 return manager(a,ws)&&manager(b,ws)&&
   a.employeeId===b.employeeId&&a.workspaceRole===b.workspaceRole;
}
async function validDevice(db:D1Database,t:AllowanceApprovalTarget):Promise<boolean>{
 const row=await db.prepare(`SELECT d.device_id FROM devices d
  JOIN workspaces w ON w.workspace_id=d.workspace_id
  WHERE d.device_id=?1 AND d.workspace_id=?2 AND
    d.status='active' AND d.employee_authority_state='cloud'
    AND w.status='active' LIMIT 1`)
  .bind(t.deviceId,t.workspaceId).first<{device_id:string}>();
 return !!row;
}
async function inReview(db:D1Database,t:AllowanceApprovalTarget,
 actor:string,requestHash:string):Promise<boolean>{
 const row=await db.prepare(`SELECT application_id FROM allowance_applications
  WHERE application_id=?1 AND workspace_id=?2 AND (status='open' OR (status='review' AND manager_actor_id=?4))
   AND revision=?3 AND request_hash=?5 LIMIT 1`)
   .bind(t.applicationId,t.workspaceId,t.expectedRevision,actor,requestHash)
   .first<{application_id:string}>();
 return !!row;
}
function nowUtc(clock:()=>Date):Date{
 const time=clock();
 if(!(time instanceof Date)||!Number.isFinite(time.valueOf()))
  throw new Error("ALLOWANCE_APPROVAL_CLOCK_INVALID");
 return time;
}

// The grant is written BEFORE the approved transition in one D1.batch.
// The existing C2 SQL trigger requires the immutable grant at the update.
// No separate .run() calls may split the two statements: a crash between
// operations would otherwise orphan the approval or misstate business status.
export async function approveCloudAllowanceAfterOfficialProof(
 db:D1Database,t:AllowanceApprovalTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date()
):Promise<CloudAllowanceApproval>{
 if(authority.approvedOperation)throw new Error("LIVE_SESSION_REQUIRED_FOR_NEW_APPROVAL");
 if(typeof t.workspaceId!=="string"||!/^ws_[A-Za-z0-9_-]{3,100}$/.test(t.workspaceId)||
    typeof t.applicationId!=="string"||!ID.test(t.applicationId)||
    typeof t.deviceId!=="string"||!DEVICE.test(t.deviceId)||
    !Number.isSafeInteger(t.expectedRevision)||t.expectedRevision<1)
  throw new Error("ALLOWANCE_APPROVAL_TARGET_INVALID");
 const first=await authority.revalidate();
 if(!manager(first,t.workspaceId)||!await validDevice(db,t))
  throw new Error("ALLOWANCE_APPROVAL_MANAGER_REQUIRED");
 const frozen=await loadEncryptedCloudAllowance(db,t,ctx,authority);
 if(!["open","review"].includes(frozen.status)||frozen.revision!==t.expectedRevision||
    !await inReview(db,t,first.employeeId,frozen.requestHash))
  throw new Error("ALLOWANCE_APPROVAL_STALE");
 const company=await loadAmegoCompanySettingsForGateway(
  db,t.workspaceId,ctx.encryptionKeyHex);
 if(company.revision!==frozen.companyRevision||
    company.sellerInvoice!==frozen.sellerInvoice)
  throw new Error("ALLOWANCE_APPROVAL_COMPANY_CHANGED");
 // This is the ONLY provider evidence source: the signed invoice_query
 // plus per-number signed allowance_query detail for this original invoice.
 const proof=await reconcileOfficialAllowanceForOriginal(frozen,{
  sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey
 },transport,clock);
 const date=nowUtc(clock);
 const checked=Date.parse(proof.officialCheckedAtUtc);
 if(proof.applicationId!==t.applicationId||
    proof.originalInvoiceNumber!==frozen.originalInvoiceNumber||
    !HASH.test(proof.officialEvidenceHash)||
    proof.approvalRecorded!==false||proof.numberReserved!==false||
    proof.requestedGross!==(BigInt(frozen.requestedGross)*10000000n).toString()||
    !Number.isFinite(checked)||checked>date.valueOf()+1000||
    date.valueOf()-checked>60_000)
  throw new Error("ALLOWANCE_APPROVAL_PROOF_INVALID_OR_STALE");
 const second=await authority.revalidate();
 if(!sameManager(first,second,t.workspaceId)||
    !await validDevice(db,t)||
    !await inReview(db,t,first.employeeId,frozen.requestHash))
  throw new Error("ALLOWANCE_APPROVAL_AUTH_OR_REVISION_CHANGED");
 const changed=await loadAmegoCompanySettingsForGateway(
   db,t.workspaceId,ctx.encryptionKeyHex);
 if(changed.revision!==company.revision||
    changed.sellerInvoice!==company.sellerInvoice||
    changed.amegoAppKey!==company.amegoAppKey)
  throw new Error("ALLOWANCE_APPROVAL_COMPANY_CHANGED");
 // A short-lived grant is only a permission for the NEXT one-shot flow to
 // re-read AMEGO. The date is UTC and its expiration is not g0401 evidence.
 const reviewedRevision=t.expectedRevision+(frozen.status==="open"?1:0);
 const approvalId="apr_"+crypto.randomUUID();
 const approvedAtUtc=date.toISOString();
 const validUntilUtc=new Date(date.valueOf()+5*60_000).toISOString();
 // Source predicates protect against Device/Workspace/company changes
 // BETWEEN revalidation and the atomic transaction.
 const insert=db.prepare(`INSERT INTO allowance_execution_approvals
  (approval_id,application_id,workspace_id,reviewed_revision,
   request_hash,official_snapshot_hash,official_checked_at,
   manager_actor_id,manager_device_id,approved_at,valid_until_utc${first.authoritySnapshot?",authority_snapshot":""})
  SELECT ?1,a.application_id,a.workspace_id,?4,?5,?6,?7,?8,?9,?10,?11${first.authoritySnapshot?",?15":""}
  FROM allowance_applications a
  JOIN allowance_frozen_requests f ON f.application_id=a.application_id
   AND f.workspace_id=a.workspace_id
  JOIN devices d ON d.device_id=?9 AND d.workspace_id=a.workspace_id
  JOIN workspaces w ON w.workspace_id=a.workspace_id
  JOIN company_settings c ON c.workspace_id=a.workspace_id
  WHERE a.application_id=?2 AND a.workspace_id=?3
   AND a.status='review' AND a.revision=?4
   AND a.manager_actor_id=?8 AND a.request_hash=?5
   AND f.request_hash=?5 AND f.environment=a.environment
   AND f.company_settings_revision=?12 AND f.seller_invoice=?13
   AND a.environment=?14
   AND c.seller_invoice=?13 AND c.revision=?12
   AND c.amego_app_key_ciphertext IS NOT NULL
   AND d.status='active' AND d.employee_authority_state='cloud'
   AND w.status='active'
   AND NOT EXISTS (SELECT 1 FROM allowance_execution_approvals p
     WHERE p.application_id=a.application_id)
   AND NOT EXISTS (SELECT 1 FROM allowance_number_ledger l
     WHERE l.application_id=a.application_id)`)
  .bind(approvalId,t.applicationId,t.workspaceId,reviewedRevision,
    frozen.requestHash,proof.officialEvidenceHash,
    proof.officialCheckedAtUtc,first.employeeId,t.deviceId,
    approvedAtUtc,validUntilUtc,company.revision,company.sellerInvoice,
    frozen.environment, ...(first.authoritySnapshot?[first.authoritySnapshot]:[]));
 const transition=db.prepare(`UPDATE allowance_applications SET
   status='approved',revision=revision+1,
   actor_id=?1,updated_by_device_id=?2,updated_at=?3
   WHERE application_id=?4 AND workspace_id=?5 AND
     status='review' AND revision=?6 AND manager_actor_id=?1
     AND request_hash=?7
     AND EXISTS (SELECT 1 FROM allowance_execution_approvals p
       WHERE p.application_id=allowance_applications.application_id
       AND p.reviewed_revision=?6 AND p.manager_actor_id=?1
       AND p.request_hash=?7 AND p.approval_id=?8)`)
  .bind(first.employeeId,t.deviceId,approvedAtUtc,t.applicationId,
    t.workspaceId,reviewedRevision,frozen.requestHash,approvalId);
 // D1.batch executes statements in a single transaction and rolls back
 // if any statement throws (including the schema's immutable triggers).
 // Both SQL statements use identical CAS/source fences within that txn.
 // A zero-change or ambiguous response is NOT permission to retry approval.
 let result:D1Result[];
 const review=db.prepare(`UPDATE allowance_applications SET status='review',revision=revision+1,
  manager_actor_id=?1,actor_id=?1,updated_by_device_id=?2,updated_at=?3
  WHERE application_id=?4 AND workspace_id=?5 AND status='open' AND revision=?6`)
  .bind(first.employeeId,t.deviceId,approvedAtUtc,t.applicationId,t.workspaceId,t.expectedRevision);
 try{result=await db.batch(frozen.status==="open"?[review,insert,transition]:[insert,transition]);}
 catch{throw new Error("ALLOWANCE_APPROVAL_READBACK_REQUIRED");}
 if(result.some(r=>r.meta.changes!==1))
  throw new Error("ALLOWANCE_APPROVAL_READBACK_REQUIRED");
 const last=await authority.revalidate();
 if(!sameManager(first,last,t.workspaceId)||!await validDevice(db,t))
  throw new Error("ALLOWANCE_APPROVAL_READBACK_REQUIRED");
 return {approvalId,applicationId:t.applicationId,
  approvedRevision:reviewedRevision+1,managerActorId:first.employeeId,
  approvedAtUtc,validUntilUtc,requestHash:frozen.requestHash,
  officialEvidenceHash:proof.officialEvidenceHash,
  numberReserved:false,sentToAmego:false};
}
