-- CYInvoice V3 CYID Cloud allowance numbering DRAFT. Not a production migration.
-- 0015 Work Items must be reviewed before this table may become canonical.
-- Exceptional allowance application ONLY: no normal invoice rows, no raw
-- AMEGO request/response bodies and no plaintext company credentials in D1.
-- App side supplies only SHA256 digests of the unnumbered immutable request template and official review snapshot.
-- This grant must eventually be written only by an authenticated CYID manager
-- AFTER server-side official invoice/allowance amount validation.
-- No public approval writer exists in this Draft: an unapproved Work Item
-- must never be able to allocate or send a number merely because it is review.
CREATE TABLE allowance_execution_approvals (
  approval_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  work_item_id TEXT NOT NULL REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
  approved_revision INTEGER NOT NULL CHECK(approved_revision > 0),
  seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8
    AND seller_invoice NOT GLOB '*[^0-9]*'),
  environment TEXT NOT NULL CHECK(environment IN ('production','test')),
  allowance_day TEXT NOT NULL CHECK(length(allowance_day)=8
    AND allowance_day NOT GLOB '*[^0-9]*'),
  template_sha256 TEXT NOT NULL CHECK(length(template_sha256)=64
    AND template_sha256 NOT GLOB '*[^0-9a-f]*'),
  official_snapshot_sha256 TEXT NOT NULL CHECK(length(official_snapshot_sha256)=64
    AND official_snapshot_sha256 NOT GLOB '*[^0-9a-f]*'),
  official_checked_at TEXT NOT NULL,
  approved_by_employee_id TEXT NOT NULL CHECK(length(approved_by_employee_id) BETWEEN 1 AND 100),
  approved_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  approved_at TEXT NOT NULL,
  valid_until_utc TEXT NOT NULL CHECK(valid_until_utc > approved_at)
);
-- Append-only decisions: a fresh official check can renew an expired grant
-- without erasing the prior approving employee or the original timestamp.
CREATE INDEX idx_allowance_approvals_work_item
  ON allowance_execution_approvals(workspace_id,work_item_id,approved_revision,valid_until_utc DESC);
CREATE TABLE allowance_number_ledger (
  ledger_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  work_item_id TEXT NOT NULL REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
  seller_invoice TEXT NOT NULL CHECK (
    length(seller_invoice) = 8 AND seller_invoice NOT GLOB '*[^0-9]*'),
  environment TEXT NOT NULL CHECK(environment IN ('production', 'test')),
  allowance_day TEXT NOT NULL CHECK (
    length(allowance_day) = 8 AND allowance_day NOT GLOB '*[^0-9]*'),
  sequence INTEGER NOT NULL CHECK(sequence BETWEEN 1 AND 34695),
  allowance_number TEXT NOT NULL CHECK(length(allowance_number) = 12),
  attempt INTEGER NOT NULL CHECK(attempt > 0),
  request_sha256 TEXT NOT NULL CHECK (
    length(request_sha256) = 64 AND request_sha256 NOT GLOB '*[^0-9a-f]*'),
  status TEXT NOT NULL CHECK (
    status IN ('reserved', 'sending', 'occupied_external', 'confirmed')),
  created_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(workspace_id, seller_invoice, environment, allowance_day, sequence),
  UNIQUE(workspace_id, seller_invoice, environment, allowance_number),
  UNIQUE(workspace_id, work_item_id, attempt)
);
CREATE INDEX idx_allowance_ledger_work_item
  ON allowance_number_ledger(workspace_id, work_item_id, attempt DESC);
-- Status changes must not delete a numbered attempt. The next attempted number
-- is allowed only after a definite g0401 4040161/4040162 rejection.

-- Draft-only guard: closing an in-flight allowance would strand the original
-- number and make the official result impossible to reconcile safely.
-- "confirmed" may close normally; reserved/sending must first be resolved.
CREATE TRIGGER allowance_work_item_no_unsafe_close
BEFORE UPDATE OF status ON work_items
WHEN OLD.kind='ALLOWANCE' AND NEW.status='closed'
  AND EXISTS (SELECT 1 FROM allowance_number_ledger a
    WHERE a.workspace_id=OLD.workspace_id AND a.work_item_id=OLD.work_item_id
      AND a.status IN ('reserved','sending'))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_IN_FLIGHT_NOT_CLOSABLE'); END;

-- Number assignments are permanent even if an attempted AMEGO request fails.
CREATE TRIGGER allowance_number_ledger_no_delete
BEFORE DELETE ON allowance_number_ledger
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_NUMBER_PERMANENT'); END;
CREATE TRIGGER allowance_number_ledger_no_renumber
BEFORE UPDATE OF workspace_id,work_item_id,seller_invoice,environment,
  allowance_day,sequence,allowance_number,attempt,request_sha256,
  created_by_device_id,created_at ON allowance_number_ledger
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_NUMBER_PERMANENT'); END;
