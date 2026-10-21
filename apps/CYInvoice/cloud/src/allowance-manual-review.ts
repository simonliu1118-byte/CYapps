// V3.2: manager-only audit classification of an existing g0401 attempt.
// No Ledger transition, official confirmation, number allocation or provider POST.
import type {AllowanceActor,AllowanceActorSource} from "./allowance-frozen-store";
export type TriageKind="full_match_unattributed"|"unknown_outcome"|
 "number_collision_investigation"|"provider_rejection_investigation";
interface Target{workspaceId:string;applicationId:string;deviceId:string;expectedRevision:number;}
const codes=new Set<string>(["full_match_unattributed","unknown_outcome",
 "number_collision_investigation","provider_rejection_investigation"]);
function manager(actor:AllowanceActor|null,t:Target,initial:AllowanceActor):boolean{
 return !!actor&&actor.workspaceId===t.workspaceId&&actor.employeeId===initial.employeeId&&
 actor.workspaceRole===initial.workspaceRole&&
 (actor.workspaceRole==="ADMIN"||actor.workspaceRole==="SUPER_ADMIN");
}
export async function appendAllowanceManualReview(db:D1Database,t:Target,
 authority:AllowanceActorSource,reviewId:string,disposition:TriageKind,
 clock:()=>Date=()=>new Date()){
 if(!/^[A-Za-z0-9_-]{12,100}$/.test(reviewId)||!codes.has(disposition)||
 !Number.isSafeInteger(t.expectedRevision)||t.expectedRevision<3)
  throw new Error("ALLOWANCE_MANUAL_REVIEW_INVALID");
 const actor=await authority.revalidate();
 if(!actor||!manager(actor,t,actor))throw new Error("ALLOWANCE_MANAGER_REQUIRED");
 const l=await db.prepare(`SELECT l.ledger_id,l.allowance_number,l.status,
 a.manager_actor_id,
 (SELECT c.check_id FROM allowance_reconciliation_checks c
  WHERE c.ledger_id=l.ledger_id AND c.workspace_id=l.workspace_id
  AND (?4<>'full_match_unattributed' OR c.evidence_kind='full_match_unattributed')
  ORDER BY c.checked_at DESC,c.check_id DESC LIMIT 1) AS check_id
 FROM allowance_number_ledger l JOIN allowance_applications a
   ON a.application_id=l.application_id AND a.workspace_id=l.workspace_id
 WHERE a.workspace_id=?1 AND a.application_id=?2 AND a.status='approved'
 AND a.revision=?3 ORDER BY l.attempt DESC LIMIT 1`)
 .bind(t.workspaceId,t.applicationId,t.expectedRevision,disposition)
 .first<{ledger_id:string;allowance_number:string;status:string;
 manager_actor_id:string;check_id:string|null}>();
 if(!l||!["sending","occupied_external"].includes(l.status)||
 l.manager_actor_id!==actor.employeeId||
 (disposition==="full_match_unattributed"&&!l.check_id))
  throw new Error("ALLOWANCE_MANUAL_REVIEW_NOT_ELIGIBLE");
 const fresh=await authority.revalidate();
 if(!manager(fresh,t,actor))throw new Error("ALLOWANCE_AUTHORITY_CHANGED");
 // One D1 INSERT...SELECT: immutable audit, never a financial action.
 // Repeating an ambiguous INSERT is forbidden; inspect manual-history instead.
 const r=await db.prepare(`INSERT INTO allowance_manual_review_audit
 (review_id,application_id,workspace_id,ledger_id,allowance_number,
  ledger_status_at_review,disposition,evidence_check_id,reviewed_by_actor_id,
  reviewed_by_device_id,reviewed_at)
 SELECT ?1,l.application_id,l.workspace_id,l.ledger_id,l.allowance_number,
 l.status,?2,?3,?4,?5,?6
 FROM allowance_number_ledger l JOIN allowance_applications a
 ON a.application_id=l.application_id AND a.workspace_id=l.workspace_id
 JOIN devices d ON d.device_id=?5 AND d.workspace_id=l.workspace_id
 JOIN workspaces w ON w.workspace_id=l.workspace_id
 WHERE l.ledger_id=?7 AND l.application_id=?8 AND l.workspace_id=?9
 AND l.allowance_number=?10 AND l.status=?11 AND a.status='approved'
 AND a.revision=?12 AND a.manager_actor_id=?4 AND d.status='active'
 AND d.employee_authority_state='cloud' AND w.status='active'
 AND (?3 IS NULL OR EXISTS(
 SELECT 1 FROM allowance_reconciliation_checks c WHERE c.check_id=?3
 AND c.ledger_id=l.ledger_id AND c.application_id=l.application_id
 AND c.workspace_id=l.workspace_id AND c.allowance_number=l.allowance_number))
 AND (?2<>'full_match_unattributed' OR ?3 IS NOT NULL)`)
 .bind(reviewId,disposition,l.check_id,actor.employeeId,t.deviceId,
 clock().toISOString(),l.ledger_id,t.applicationId,t.workspaceId,
 l.allowance_number,l.status,t.expectedRevision).run();
 if(r.meta.changes!==1)throw new Error("ALLOWANCE_MANUAL_REVIEW_CONFLICT");
 return {reviewId,applicationId:t.applicationId,ledgerId:l.ledger_id,
  ledgerStatus:l.status,disposition,officiallyConfirmed:false as const,
  proofOfThisPost:false as const,safeToAutoRetry:false as const,
  canAllocateAnotherNumber:false as const};
}
export async function listAllowanceManualReviews(db:D1Database,t:Target,
 authority:AllowanceActorSource){
 const actor=await authority.revalidate();
 if(!actor||!manager(actor,t,actor))throw new Error("ALLOWANCE_MANAGER_REQUIRED");
 const rows=await db.prepare(`SELECT r.review_id,r.ledger_id,r.allowance_number,
 r.ledger_status_at_review,r.disposition,r.reviewed_at,
 r.reviewed_by_actor_id,r.reviewed_by_device_id,r.next_ledger_id,n.allowance_number AS next_number,
 (r.evidence_check_id IS NOT NULL) AS has_readback
 FROM allowance_manual_review_audit r JOIN allowance_applications a
 ON a.application_id=r.application_id AND a.workspace_id=r.workspace_id
 LEFT JOIN allowance_number_ledger n ON n.ledger_id=r.next_ledger_id
 JOIN devices d ON d.device_id=?3 AND d.workspace_id=r.workspace_id
 JOIN workspaces w ON w.workspace_id=r.workspace_id
 WHERE r.workspace_id=?1 AND r.application_id=?2
 AND a.status='approved' AND a.revision=?4
 AND a.manager_actor_id=?5 AND d.status='active'
 AND d.employee_authority_state='cloud' AND w.status='active'
 ORDER BY r.reviewed_at DESC,r.review_id DESC LIMIT 30`)
 .bind(t.workspaceId,t.applicationId,t.deviceId,t.expectedRevision,actor.employeeId)
 .all<{review_id:string;ledger_id:string;allowance_number:string;
 ledger_status_at_review:string;disposition:TriageKind|"external_collision_reserved";reviewed_at:string;
 reviewed_by_actor_id:string;reviewed_by_device_id:string;has_readback:number;
 next_ledger_id:string|null;next_number:string|null}>();
 const fresh=await authority.revalidate();
 if(!manager(fresh,t,actor))throw new Error("ALLOWANCE_AUTHORITY_CHANGED");
 return rows.results.map(x=>({reviewId:x.review_id,ledgerId:x.ledger_id,
  allowanceNumber:x.allowance_number,ledgerStatus:x.ledger_status_at_review,
  disposition:x.disposition,reviewedAt:x.reviewed_at,
  managerActorId:x.reviewed_by_actor_id,managerDeviceId:x.reviewed_by_device_id,
  nextLedgerId:x.next_ledger_id,nextAllowanceNumber:x.next_number,
  recordedReadback:!!x.has_readback,officiallyConfirmed:false as const,
  proofOfThisPost:false as const,safeToAutoRetry:false as const,
  canAllocateAnotherNumber:false as const}));
}
