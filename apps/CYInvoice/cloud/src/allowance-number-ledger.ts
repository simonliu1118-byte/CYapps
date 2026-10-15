// V3 CYID Cloud allowance number reservations. DRAFT: no public endpoint.
// This module cannot approve a request or send AMEGO g0401. The caller MUST
// revalidate CYID manager authority, official remaining allowance balance
// and frozen unnumbered template SHA256 before invoking reserve + markSending.
// Do not store invoice listings, raw AMEGO payload or API keys in business D1.

export type AllowanceEnvironment = "production" | "test";
export type AllowanceLedgerStatus = "reserved" | "sending" | "occupied_external" | "confirmed";
export interface AllowanceReservationRequest {
  workspaceId: string;
  workItemId: string;
  deviceId: string;
  expectedRevision: number;
  sellerInvoice: string; // derive server-side from protected company settings
  environment: AllowanceEnvironment;
  allowanceDay: string; // yyyyMMdd from approved immutable request
  requestSha256: string; // SHA256 of immutable unnumbered g0401 template, not raw payload
}
export interface AllowanceNumberReservation {
  ledgerId: string;
  workItemId: string;
  allowanceNumber: string;
  sequence: number;
  attempt: number;
  status: AllowanceLedgerStatus;
  requestSha256: string;
}
type LedgerRow = {
  ledger_id: string;
  work_item_id: string;
  allowance_number: string;
  sequence: number;
  attempt: number;
  status: AllowanceLedgerStatus;
  request_sha256: string;
  seller_invoice: string;
  environment: AllowanceEnvironment;
  allowance_day: string;
};

const DIGITS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const MAX_DAILY = 999 + 26 * 36 * 36; // AyyyyMMdd001..999,A00..ZZZ
function numberFor(day: string, sequence: number): string {
  if (!/^\d{8}$/.test(day) || sequence < 1 || sequence > MAX_DAILY)
    throw new Error("ALLOWANCE_SEQUENCE_INVALID");
  if (sequence <= 999) return `A${day}${sequence.toString().padStart(3, "0")}`;
  const value = sequence - 1000;
  return `A${day}${String.fromCharCode(65 + Math.floor(value / 1296))}${DIGITS[Math.floor((value % 1296) / 36)]}${DIGITS[value % 36]}`;
}
function validate(input: AllowanceReservationRequest): void {
  if (!/^[A-Za-z0-9_-]{10,100}$/.test(input.workItemId) ||
      !/^dev_[0-9a-f-]{36}$/.test(input.deviceId) ||
      typeof input.workspaceId !== "string" || input.workspaceId.length === 0 ||
      !Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 1 ||
      !/^[0-9]{8}$/.test(input.sellerInvoice) ||
      !["production", "test"].includes(input.environment) ||
      !/^[0-9]{8}$/.test(input.allowanceDay) ||
      !/^[0-9a-f]{64}$/.test(input.requestSha256))
    throw new Error("ALLOWANCE_RESERVATION_INVALID");
  const year = Number(input.allowanceDay.slice(0, 4));
  const month = Number(input.allowanceDay.slice(4, 6));
  const day = Number(input.allowanceDay.slice(6));
  const dt = new Date(Date.UTC(year, month - 1, day));
  if (dt.getUTCFullYear() !== year || dt.getUTCMonth() + 1 !== month || dt.getUTCDate() !== day)
    throw new Error("ALLOWANCE_DATE_INVALID");
}
function toReservation(row: LedgerRow): AllowanceNumberReservation {
  return { ledgerId: row.ledger_id, workItemId: row.work_item_id,
    allowanceNumber: row.allowance_number, sequence: row.sequence,
    attempt: row.attempt, status: row.status, requestSha256: row.request_sha256 };
}
async function latest(db: D1Database, input: AllowanceReservationRequest): Promise<LedgerRow | null> {
  return db.prepare(`SELECT ledger_id, work_item_id, allowance_number, sequence, attempt,
      status, request_sha256, seller_invoice, environment, allowance_day
    FROM allowance_number_ledger WHERE workspace_id=?1 AND work_item_id=?2
    ORDER BY attempt DESC LIMIT 1`).bind(input.workspaceId, input.workItemId).first<LedgerRow>();
}
function requireSame(row: LedgerRow, input: AllowanceReservationRequest): void {
  if (row.seller_invoice !== input.sellerInvoice || row.environment !== input.environment ||
      row.allowance_day !== input.allowanceDay || row.request_sha256 !== input.requestSha256)
    throw new Error("ALLOWANCE_WORK_ITEM_IMMUTABLE");
}

// A single INSERT statement controls the number assignment. The SQL-time
// MAX(sequence) check and UNIQUE constraints make concurrent Device claims
// conflict rather than allocate the same number. No AMEGO call happens here.
// Existing reserved attempts are idempotently returned (but only the one
// MarkSending transition winner may send). Already sending/confirmed are NOT
// reissued, and occupied_external is the only state that permits next attempt.
export async function reserveCloudAllowanceNumber(
  db: D1Database, input: AllowanceReservationRequest,
): Promise<AllowanceNumberReservation> {
  validate(input);
  const prior = await latest(db, input);
  if (prior) {
    requireSame(prior, input);
    if (prior.status === "sending" || prior.status === "confirmed")
      throw new Error("ALLOWANCE_PRIOR_SEND_REQUIRES_RECONCILIATION");
    if (prior.status === "reserved") {
      // No new allocation. The eventual MarkSending is still fenced.
      return toReservation(prior);
    }
    if (prior.status !== "occupied_external")
      throw new Error("ALLOWANCE_STATUS_INVALID");
  }
  const nextRow = await db.prepare(`
    SELECT COALESCE(MAX(sequence),0)+1 AS next_sequence FROM allowance_number_ledger
    WHERE workspace_id=?1 AND seller_invoice=?2 AND environment=?3 AND allowance_day=?4
    `).bind(input.workspaceId,input.sellerInvoice,input.environment,input.allowanceDay)
    .first<{next_sequence: number}>();
  const next = nextRow?.next_sequence ?? 1;
  const number = numberFor(input.allowanceDay,next); // fail at daily capacity
  const attempt = (prior?.attempt ?? 0) + 1;
  const ledgerId = `al_${crypto.randomUUID()}`;
  const when = new Date().toISOString();
  let inserted: D1Result;
  try {
    inserted = await db.prepare(`INSERT INTO allowance_number_ledger
      (ledger_id,workspace_id,work_item_id,seller_invoice,environment,
       allowance_day,sequence,allowance_number,attempt,request_sha256,status,
       created_by_device_id,created_at,updated_at)
      SELECT ?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,'reserved',?11,?12,?12
      WHERE ?7 = (SELECT COALESCE(MAX(sequence),0)+1 FROM allowance_number_ledger
        WHERE workspace_id=?2 AND seller_invoice=?4 AND environment=?5 AND allowance_day=?6)
        AND ?9 = (SELECT COALESCE(MAX(attempt),0)+1 FROM allowance_number_ledger
          WHERE workspace_id=?2 AND work_item_id=?3)
        AND EXISTS (
          SELECT 1 FROM work_items wi
          JOIN devices d ON d.workspace_id=wi.workspace_id AND d.device_id=?11
          JOIN workspaces w ON w.workspace_id=wi.workspace_id
          WHERE wi.work_item_id=?3 AND wi.workspace_id=?2
            AND wi.kind='ALLOWANCE' AND wi.status='review'
            AND wi.revision=?13 AND wi.environment=?5
            AND d.status='active' AND w.status='active')
        AND EXISTS (
          SELECT 1 FROM allowance_execution_approvals ap
          WHERE ap.workspace_id=?2 AND ap.work_item_id=?3
            AND ap.approved_revision=?13 AND ap.seller_invoice=?4
            AND ap.environment=?5 AND ap.allowance_day=?6
            AND ap.template_sha256=?10 AND ap.valid_until_utc > ?12
            )
      `).bind(ledgerId,input.workspaceId,input.workItemId,input.sellerInvoice,
      input.environment,input.allowanceDay,next,number,attempt,
      input.requestSha256,input.deviceId,when,input.expectedRevision).run();
  } catch {
    // Concurrent allocation, revoked Device, or malformed schema: never
    // construct a second successful reservation from an uncertain write.
    throw new Error("ALLOWANCE_NUMBER_ALLOCATION_UNCERTAIN_READBACK_REQUIRED");
  }
  if (inserted.meta.changes !== 1)
    throw new Error("ALLOWANCE_ALLOCATION_AUTHORITY_OR_REVISION_CHANGED");
  return {ledgerId,workItemId:input.workItemId,allowanceNumber:number,
    sequence:next,attempt,status:"reserved",requestSha256:input.requestSha256};
}

async function changeStatus(
  db: D1Database, input: AllowanceReservationRequest,
  reservation: AllowanceNumberReservation,
  from: AllowanceLedgerStatus, to: AllowanceLedgerStatus,
): Promise<boolean> {
  validate(input);
  if (reservation.workItemId !== input.workItemId ||
      reservation.requestSha256 !== input.requestSha256 ||
      reservation.allowanceNumber !== numberFor(input.allowanceDay,reservation.sequence))
    throw new Error("ALLOWANCE_OPERATION_MISMATCH");
  const result = await db.prepare(`UPDATE allowance_number_ledger SET status=?1,updated_at=?2
    WHERE ledger_id=?3 AND work_item_id=?4 AND workspace_id=?5
      AND allowance_number=?6 AND request_sha256=?7 AND status=?8
      AND EXISTS (
        SELECT 1 FROM work_items wi
        JOIN devices d ON d.workspace_id=wi.workspace_id AND d.device_id=?9
        JOIN workspaces w ON w.workspace_id=wi.workspace_id
        WHERE wi.work_item_id=?4 AND wi.workspace_id=?5
          AND wi.kind='ALLOWANCE' AND wi.status='review' AND wi.environment=?10
          AND wi.revision=?11 AND d.status='active' AND w.status='active')
      AND (
        ?1 <> 'sending' OR EXISTS (
          SELECT 1 FROM allowance_execution_approvals ap
          WHERE ap.workspace_id=?5 AND ap.work_item_id=?4
            AND ap.approved_revision=?11 AND ap.template_sha256=?7
            AND ap.environment=?10 AND ap.valid_until_utc > ?2
            ))
    `).bind(to,new Date().toISOString(),reservation.ledgerId,input.workItemId,
      input.workspaceId,reservation.allowanceNumber,input.requestSha256,from,
      input.deviceId,input.environment,input.expectedRevision).run();
  return result.meta.changes===1;
}

// Call immediately before sending the already-frozen original g0401 payload.
// If false, the caller MUST NOT send. A crash AFTER true is ambiguous and
// must be reconciled by original number, never automatically resent.
export async function markCloudAllowanceSending(
  db: D1Database,input: AllowanceReservationRequest,reservation: AllowanceNumberReservation,
): Promise<boolean> {
  return changeStatus(db,input,reservation,"reserved","sending");
}

// IMPORTANT: caller may use this transition ONLY on the definite rejection
// of that attempt's FIRST g0401 request. Never after timeout or lost reply.
export async function markCloudAllowanceExternalCollision(
  db:D1Database,input:AllowanceReservationRequest,
  reservation:AllowanceNumberReservation,confirmedCode:number,
):Promise<boolean> {
  if (confirmedCode!==4040161 && confirmedCode!==4040162)
    throw new Error("ALLOWANCE_COLLISION_NOT_CONFIRMED");
  return changeStatus(db,input,reservation,"sending","occupied_external");
}

// g0401 returned acceptance and official query established operation state;
// do not claim government upload success from code=0 alone.
export async function markCloudAllowanceConfirmed(
  db:D1Database,input:AllowanceReservationRequest,reservation:AllowanceNumberReservation,
):Promise<boolean> {
  return changeStatus(db,input,reservation,"sending","confirmed");
}
