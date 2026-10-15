// Draft-only internal manager review approval. No HTTP route is installed.
// Only a trusted Worker integration may supply the three callbacks below:
// never build them from a client-provided "approved" or "official" boolean.
// The concrete company settings, frozen application and AMEGO official
// verifier must be integrated and audited BEFORE enabling any g0401 route.
export interface AllowanceApprovalTarget {
  workspaceId: string;
  workItemId: string;
  deviceId: string;
  expectedRevision: number;
}
export interface ServerFrozenAllowanceRequest {
  referenceHash: string; // canonical frozen Work Item reference, persisted by server
  originalInvoiceNumber: string;
  sellerInvoice: string; // from server-owned company settings
  environment: "production" | "test";
  allowanceDay: string;
  templateSha256: string; // frozen, unnumbered g0401 payload
  requestedGrossAmount: number; // whole TWD, precise integer
}
export interface VerifiedOfficialAllowanceReview {
  originalInvoiceNumber: string;
  // Verified gateway context, not browser-provided fields.
  sellerInvoice: string;
  environment: "production" | "test";
  checkedAtUtc: string;
  officialSnapshotSha256: string;
  invoiceUploadStatus: number;
  invoiceCancelled: boolean;
  allowanceSummaryPresent: boolean;
  existingAllowanceCount: number;
  originalInvoiceGrossAmount: number;
  // The trusted official verifier must resolve invoice item allocations;
  // no amounts from client or a cross-invoice allowance summary count here.
  originalItemsAndRequestFullyVerified: boolean;
}
export interface AllowanceReviewAuthority {
  revalidateManager(): Promise<{
    workspaceId: string; employeeId: string; role: "SUPER_ADMIN"|"ADMIN"|"USER";
  } | null>;
  loadFrozenApplication(target: Readonly<AllowanceApprovalTarget>): Promise<ServerFrozenAllowanceRequest>;
  queryAndVerifyOfficialInvoice(frozen: Readonly<ServerFrozenAllowanceRequest>):
    Promise<VerifiedOfficialAllowanceReview>;
}
export interface ApprovedAllowanceGrant {
  approvalId: string;
  workItemId: string;
  approvedRevision: number;
  approvedByEmployeeId: string;
  approvedAt: string;
  validUntilUtc: string;
  templateSha256: string;
  officialSnapshotSha256: string;
}

function assertTarget(t: AllowanceApprovalTarget): void {
  if (!/^[a-zA-Z0-9_-]{10,100}$/.test(t.workItemId) ||
      !/^dev_[0-9a-f-]{36}$/.test(t.deviceId) ||
      !t.workspaceId || typeof t.workspaceId !== "string" ||
      !Number.isSafeInteger(t.expectedRevision) || t.expectedRevision < 1)
    throw new Error("ALLOWANCE_APPROVAL_TARGET_INVALID");
}
function assertFrozen(f: ServerFrozenAllowanceRequest): void {
  if (!/^[0-9a-f]{64}$/.test(f.referenceHash) ||
      !/^[0-9]{8}$/.test(f.sellerInvoice) ||
      !/^[A-Z]{2}[0-9]{8}$/.test(f.originalInvoiceNumber) ||
      !["production","test"].includes(f.environment) ||
      !/^[0-9]{8}$/.test(f.allowanceDay) ||
      !/^[0-9a-f]{64}$/.test(f.templateSha256) ||
      !Number.isSafeInteger(f.requestedGrossAmount) || f.requestedGrossAmount <= 0)
    throw new Error("ALLOWANCE_FROZEN_REQUEST_INVALID");
  const y=Number(f.allowanceDay.slice(0,4));
  const m=Number(f.allowanceDay.slice(4,6));
  const d=Number(f.allowanceDay.slice(6));
  const dt=new Date(Date.UTC(y,m-1,d));
  if (dt.getUTCFullYear()!==y || dt.getUTCMonth()+1!==m || dt.getUTCDate()!==d)
    throw new Error("ALLOWANCE_FROZEN_DATE_INVALID");
}
function assertOfficial(f: ServerFrozenAllowanceRequest, evidence: VerifiedOfficialAllowanceReview,
  checkedAt: number): void {
  const officialAt=Date.parse(evidence.checkedAtUtc);
  if (evidence.originalInvoiceNumber!==f.originalInvoiceNumber ||
      evidence.sellerInvoice!==f.sellerInvoice ||
      evidence.environment!==f.environment ||
      !/^[0-9a-f]{64}$/.test(evidence.officialSnapshotSha256) ||
      !Number.isFinite(officialAt) || officialAt > checkedAt + 1000 ||
      checkedAt - officialAt > 60_000 ||
      evidence.invoiceUploadStatus!==99 || evidence.invoiceCancelled ||
      evidence.allowanceSummaryPresent!==true ||
      !Number.isSafeInteger(evidence.existingAllowanceCount) ||
      evidence.existingAllowanceCount!==0 ||
      !Number.isSafeInteger(evidence.originalInvoiceGrossAmount) ||
      evidence.originalInvoiceGrossAmount < f.requestedGrossAmount ||
      evidence.originalItemsAndRequestFullyVerified!==true)
    throw new Error("ALLOWANCE_OFFICIAL_REVIEW_NOT_AUTHORITATIVE");
}

// The only writer of the approval grant; append-only, so expired decisions
// remain attributable to the exact approving CYID employee.
// No call here allocates a number or sends anything to AMEGO.
// Revalidation is intentionally performed both sides of the external query;
// the SQL condition separately fences the Device and Work Item at write time.
export async function approveAllowanceAfterOfficialReview(
  db: D1Database, target: AllowanceApprovalTarget, authority: AllowanceReviewAuthority,
): Promise<ApprovedAllowanceGrant> {
  assertTarget(target);
  const first=await authority.revalidateManager();
  if (!first || first.workspaceId!==target.workspaceId ||
      !["ADMIN","SUPER_ADMIN"].includes(first.role) ||
      !first.employeeId || first.employeeId.length>100)
    throw new Error("ALLOWANCE_MANAGER_AUTH_REQUIRED");
  const frozen=await authority.loadFrozenApplication(target);
  assertFrozen(frozen);
  // Bind the official query to the same approved company and AMEGO environment.
  const snapshot=await authority.queryAndVerifyOfficialInvoice(frozen);
  const time=Date.now();
  assertOfficial(frozen,snapshot,time);
  const second=await authority.revalidateManager();
  if (!second || second.workspaceId!==first.workspaceId ||
      second.employeeId!==first.employeeId ||
      !["ADMIN","SUPER_ADMIN"].includes(second.role))
    throw new Error("ALLOWANCE_MANAGER_AUTH_CHANGED");
  const approvedAt=new Date().toISOString();
  const validUntilUtc=new Date(Date.parse(approvedAt)+15*60_000).toISOString();
  const approvalId="apr_"+crypto.randomUUID();
  // The existing review row is never itself considered an approval. It must
  // match this fresh official check, Device authority and immutable revision.
  // No new approval may quietly restart an already sending/confirmed attempt.
  const change=await db.prepare(`INSERT INTO allowance_execution_approvals
    (approval_id,workspace_id,work_item_id,approved_revision,seller_invoice,
     environment,allowance_day,template_sha256,official_snapshot_sha256,
     official_checked_at,approved_by_employee_id,approved_by_device_id,
     approved_at,valid_until_utc)
    SELECT ?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14
    WHERE EXISTS (
      SELECT 1 FROM work_items wi
      JOIN devices d ON d.device_id=?12 AND d.workspace_id=wi.workspace_id
      JOIN workspaces w ON w.workspace_id=wi.workspace_id
      WHERE wi.work_item_id=?3 AND wi.workspace_id=?2
        AND wi.kind='ALLOWANCE' AND wi.status='review' AND wi.revision=?4
        AND wi.reference_hash=?15 AND wi.environment=?6 AND d.status='active'
        AND d.employee_authority_state='cloud' AND w.status='active')
    AND NOT EXISTS (
      SELECT 1 FROM allowance_number_ledger existing
      WHERE existing.workspace_id=?2 AND existing.work_item_id=?3
        AND existing.status IN ('sending','confirmed'))
    AND NOT EXISTS (
      SELECT 1 FROM allowance_execution_approvals prior
      WHERE prior.workspace_id=?2 AND prior.work_item_id=?3
        AND prior.approved_revision=?4
        AND (prior.template_sha256<>?8 OR prior.seller_invoice<>?5
          OR prior.environment<>?6 OR prior.allowance_day<>?7))
    `).bind(approvalId,target.workspaceId,target.workItemId,target.expectedRevision,
      frozen.sellerInvoice,frozen.environment,frozen.allowanceDay,frozen.templateSha256,
      snapshot.officialSnapshotSha256,snapshot.checkedAtUtc,first.employeeId,
      target.deviceId,approvedAt,validUntilUtc,frozen.referenceHash).run();
  if (change.meta.changes!==1)
    throw new Error("ALLOWANCE_APPROVAL_CONFLICT_OR_REVOKED");
  return {approvalId,workItemId:target.workItemId,approvedRevision:target.expectedRevision,
    approvedByEmployeeId:first.employeeId,approvedAt,validUntilUtc,
    templateSha256:frozen.templateSha256,
    officialSnapshotSha256:snapshot.officialSnapshotSha256};
}
