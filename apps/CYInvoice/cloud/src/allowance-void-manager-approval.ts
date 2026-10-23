// V3.2 E Cloud g0501: trusted manager approval, DRAFT schema20 only.
// This does not execute or authorize g0501. The public route and send gate
// remain absent while cross-device and offline/Local coordination is unproven.
import {previewCloudVoidForManager} from "./allowance-void-manager-preview";
import {freshVoidOfficialEvidence} from "./amego-allowance-void-proof";
import {activeVoidDevice,isVoidManager,sameVoidManager}
 from "./allowance-void-applications";
import type {AllowanceActorSource,CloudAllowanceContext}
 from "./allowance-frozen-store";
export interface CloudVoidApprovalTarget {
 workspaceId:string;applicationId:string;deviceId:string;
 expectedRevision:1;requestHash:string;
 expectedOfficialSnapshotHash:string;
}
export interface CloudVoidApprovalResult {
 applicationId:string;approvedRevision:2;officialSnapshotHash:string;
 approvedAtUtc:string;validUntilUtc:string;
 mayPostG0501:false;requiresFreshOfficialCheck:true;
}
const WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const HASH=/^[0-9a-f]{64}$/;
export async function approveCloudVoidAfterOfficialProof(
 db:D1Database,t:CloudVoidApprovalTarget,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date()
):Promise<CloudVoidApprovalResult>{
 if(authority.approvedOperation)throw new Error("LIVE_SESSION_REQUIRED_FOR_NEW_APPROVAL");
 if(!t||!WS.test(t.workspaceId)||!ID.test(t.applicationId)||
  !DEVICE.test(t.deviceId)||t.expectedRevision!==1||
  !HASH.test(t.requestHash)||!HASH.test(t.expectedOfficialSnapshotHash))
  throw new Error("VOID_APPROVAL_TARGET_INVALID");
 const first=await authority.revalidate();
 if(!isVoidManager(first,t.workspaceId)||!await activeVoidDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_APPROVAL_MANAGER_REQUIRED");
 // Trusted preview decrypts frozen request, verifies its keyed HMAC and makes
 // two fresh official read-only calls; it revalidates the actor after IO.
 const proof=await previewCloudVoidForManager(db,t,ctx,authority,transport,clock);
 if(proof.revision!==1||proof.requestHash!==t.requestHash||
  proof.officialSnapshotHash!==t.expectedOfficialSnapshotHash)
  throw new Error("VOID_APPROVAL_STALE");
 const current=await authority.revalidate();
 if(!sameVoidManager(first,current,t.workspaceId)||
  !await activeVoidDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_APPROVAL_AUTH_CHANGED");
 const date=clock();
 if(!(date instanceof Date)||!Number.isFinite(date.valueOf()))
  throw new Error("VOID_APPROVAL_CLOCK_INVALID");
 if(!freshVoidOfficialEvidence(proof.officialCheckedAtUtc,date))
  throw new Error("VOID_APPROVAL_OFFICIAL_STALE");
 const approvedAtUtc=date.toISOString();
 // Retained schema20 evidence field; schema21 execution does not use this
 // timestamp as an approval window. Current authority is checked per attempt.
 const validUntilUtc=new Date(date.valueOf()+900_000).toISOString();
 // One D1 transaction: INSERT immutable grant, then CAS application.
 // The draft SQL triggers independently fence company revision and Device.
 // No eventual consistency, no second authorization owner or replay loop.
 const grant=db.prepare("INSERT INTO allowance_void_approvals "+
  "(application_id,workspace_id,request_hash,official_snapshot_hash,"+
  "manager_actor_id,manager_device_id,approved_at,valid_until_utc"+(first.authoritySnapshot?",authority_snapshot":"")+") "+
  "SELECT r.application_id,r.workspace_id,r.request_hash,?4,?5,?6,?7,?8"+(first.authoritySnapshot?",?9":"")+" "+
  "FROM allowance_void_requests r "+
  "JOIN devices d ON d.device_id=?6 AND d.workspace_id=r.workspace_id "+
  "JOIN workspaces w ON w.workspace_id=r.workspace_id "+
  "JOIN company_settings c ON c.workspace_id=r.workspace_id "+
  "WHERE r.application_id=?1 AND r.workspace_id=?2 AND r.request_hash=?3 "+
  "AND r.status='open' AND r.revision=1 "+
  "AND c.seller_invoice=r.seller_invoice AND c.revision=r.company_revision "+
  "AND d.status='active' AND d.employee_authority_state='cloud' "+
  "AND w.status='active' "+
  "AND NOT EXISTS(SELECT 1 FROM allowance_void_send_claims x "+
  "WHERE x.application_id=r.application_id)")
  .bind(t.applicationId,t.workspaceId,t.requestHash,
   proof.officialSnapshotHash,first.employeeId,t.deviceId,
   approvedAtUtc,validUntilUtc,...(first.authoritySnapshot?[first.authoritySnapshot]:[]));
 const update=db.prepare("UPDATE allowance_void_requests "+
  "SET status='approved',revision=2,manager_actor_id=?4,updated_at=?5 "+
  "WHERE application_id=?1 AND workspace_id=?2 AND request_hash=?3 "+
  "AND status='open' AND revision=1 "+
  "AND EXISTS(SELECT 1 FROM allowance_void_approvals g "+
  "WHERE g.application_id=allowance_void_requests.application_id "+
  "AND g.workspace_id=?2 AND g.request_hash=?3 AND g.manager_actor_id=?4 "+
  "AND g.manager_device_id=?6 AND g.official_snapshot_hash=?7)")
  .bind(t.applicationId,t.workspaceId,t.requestHash,first.employeeId,
   approvedAtUtc,t.deviceId,proof.officialSnapshotHash);
 let outcome:D1Result[];
 try{outcome=await db.batch([grant,update]);}
 catch{throw new Error("VOID_APPROVAL_UNCONFIRMED_READBACK_ONLY");}
 if(outcome.length!==2||outcome[0].meta.changes!==1||
  outcome[1].meta.changes!==1)
  throw new Error("VOID_APPROVAL_UNCONFIRMED_READBACK_ONLY");
 // A lost reply after commit is handled by same-ID readback, never reapprove.
 return {applicationId:t.applicationId,approvedRevision:2,
  officialSnapshotHash:proof.officialSnapshotHash,approvedAtUtc,validUntilUtc,
  mayPostG0501:false,requiresFreshOfficialCheck:true};
}
