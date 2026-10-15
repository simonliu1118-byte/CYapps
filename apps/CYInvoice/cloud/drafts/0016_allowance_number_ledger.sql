-- CYInvoice V3 CYID Cloud allowance numbering DRAFT. Not a production migration.
-- 0015 Work Items must be reviewed before this table may become canonical.
-- Exceptional allowance application ONLY: no normal invoice rows, no raw
-- AMEGO request/response bodies and no plaintext company credentials in D1.
-- App side supplies only a SHA256 digest of the separately frozen request.
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
