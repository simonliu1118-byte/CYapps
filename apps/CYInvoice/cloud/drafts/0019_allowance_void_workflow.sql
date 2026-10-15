-- CYInvoice V3 DRAFT 0019: immutable cross-device allowance void source.
-- Depends on 0015 work_items and 0017 company_settings.
-- Not a production migration. No normal successful invoice rows.
-- AMEGO g0501 is a separate one-shot operation; never infer completion
-- from the response to POST /json/g0501.
CREATE TABLE allowance_void_requests (
  work_item_id TEXT PRIMARY KEY REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8 AND seller_invoice NOT GLOB '*[^0-9]*'),
  company_settings_revision INTEGER NOT NULL CHECK(company_settings_revision>0),
  environment TEXT NOT NULL CHECK(environment IN ('test','production')),
  allowance_number TEXT NOT NULL CHECK(length(allowance_number) BETWEEN 1 AND 16 AND
    allowance_number=trim(allowance_number)),
  original_invoice_number TEXT NOT NULL CHECK(length(original_invoice_number)=10),
  reason TEXT NOT NULL CHECK(length(reason) BETWEEN 1 AND 500),
  applicant_employee_id TEXT NOT NULL CHECK(length(applicant_employee_id) BETWEEN 1 AND 100),
  applicant_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  reference_hash TEXT NOT NULL CHECK(length(reference_hash)=64 AND
    reference_hash NOT GLOB '*[^0-9a-f]*'),
  created_at TEXT NOT NULL,
  UNIQUE(workspace_id,environment,seller_invoice,allowance_number)
);
CREATE TRIGGER allowance_void_request_source_guard BEFORE INSERT ON allowance_void_requests
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM work_items wi
    JOIN workspaces w ON w.workspace_id=wi.workspace_id
    JOIN devices d ON d.device_id=NEW.applicant_device_id AND d.workspace_id=w.workspace_id
    JOIN company_settings c ON c.workspace_id=w.workspace_id
    WHERE wi.work_item_id=NEW.work_item_id AND wi.workspace_id=NEW.workspace_id
      AND wi.kind='ALLOWANCE_VOID' AND wi.status='open' AND wi.revision=1
      AND wi.environment=NEW.environment AND wi.reference_hash=NEW.reference_hash
      AND wi.actor_id=NEW.applicant_employee_id
      AND wi.opened_by_device_id=NEW.applicant_device_id
      AND wi.created_at=NEW.created_at AND wi.updated_at=NEW.created_at
      AND d.status='active' AND d.employee_authority_state='cloud'
      AND w.status='active' AND c.revision=NEW.company_settings_revision
      AND c.seller_invoice=NEW.seller_invoice AND c.amego_app_key_ciphertext IS NOT NULL
  ) THEN RAISE(ABORT,'ALLOWANCE_VOID_SOURCE_MISMATCH') END;
END;
CREATE TRIGGER allowance_void_request_no_update BEFORE UPDATE ON allowance_void_requests
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_REQUEST_IMMUTABLE'); END;
CREATE TRIGGER allowance_void_request_no_delete BEFORE DELETE ON allowance_void_requests
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_REQUEST_IMMUTABLE'); END;

-- Manager's official verification evidence remains append-only. An expired
-- approval needs a new row and fresh official status, never a modified timestamp.
CREATE TABLE allowance_void_approvals (
  approval_id TEXT PRIMARY KEY,
  work_item_id TEXT NOT NULL REFERENCES allowance_void_requests(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  expected_revision INTEGER NOT NULL CHECK(expected_revision>0),
  official_snapshot_sha256 TEXT NOT NULL CHECK(length(official_snapshot_sha256)=64 AND
    official_snapshot_sha256 NOT GLOB '*[^0-9a-f]*'),
  official_checked_at TEXT NOT NULL,
  approved_by_employee_id TEXT NOT NULL CHECK(length(approved_by_employee_id) BETWEEN 1 AND 100),
  approved_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  approved_at TEXT NOT NULL,
  valid_until_utc TEXT NOT NULL CHECK(valid_until_utc>approved_at)
);
CREATE INDEX idx_allowance_void_approval ON allowance_void_approvals(work_item_id,approved_at DESC);
CREATE TRIGGER allowance_void_approval_source_guard BEFORE INSERT ON allowance_void_approvals
BEGIN
  SELECT CASE WHEN NOT EXISTS(
    SELECT 1 FROM allowance_void_requests r
    JOIN work_items wi ON wi.work_item_id=r.work_item_id AND wi.workspace_id=r.workspace_id
    JOIN devices d ON d.device_id=NEW.approved_by_device_id AND d.workspace_id=r.workspace_id
    JOIN workspaces w ON w.workspace_id=r.workspace_id
    WHERE r.work_item_id=NEW.work_item_id AND r.workspace_id=NEW.workspace_id
      AND wi.kind='ALLOWANCE_VOID' AND wi.status='review'
      AND wi.revision=NEW.expected_revision
      AND wi.reference_hash=r.reference_hash AND wi.environment=r.environment
      AND d.status='active' AND d.employee_authority_state='cloud'
      AND w.status='active'
  ) THEN RAISE(ABORT,'ALLOWANCE_VOID_APPROVAL_SOURCE_CHANGED') END;
END;
CREATE TRIGGER allowance_void_approval_no_update BEFORE UPDATE ON allowance_void_approvals
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER allowance_void_approval_no_delete BEFORE DELETE ON allowance_void_approvals
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_AUDIT_APPEND_ONLY'); END;

-- Once an attempt is claimed, it is PERMANENT and cannot be replayed. Even
-- if the worker crashes before calling AMEGO, only original-number readback
-- or supervised manual reconciliation is allowed.
CREATE TABLE allowance_void_send_claims (
  work_item_id TEXT PRIMARY KEY REFERENCES allowance_void_requests(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  approval_id TEXT NOT NULL REFERENCES allowance_void_approvals(approval_id) ON DELETE RESTRICT,
  allowance_number TEXT NOT NULL,
  expected_revision INTEGER NOT NULL CHECK(expected_revision>0),
  claimed_by_employee_id TEXT NOT NULL CHECK(length(claimed_by_employee_id) BETWEEN 1 AND 100),
  claimed_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  claimed_at TEXT NOT NULL
);
CREATE TRIGGER allowance_void_send_guard BEFORE INSERT ON allowance_void_send_claims
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM allowance_void_requests r
    JOIN work_items wi ON wi.work_item_id=r.work_item_id AND wi.workspace_id=r.workspace_id
    JOIN allowance_void_approvals a ON a.work_item_id=r.work_item_id
    JOIN devices d ON d.device_id=NEW.claimed_by_device_id AND d.workspace_id=r.workspace_id
    JOIN workspaces w ON w.workspace_id=r.workspace_id
    JOIN company_settings c ON c.workspace_id=r.workspace_id
    WHERE wi.work_item_id=NEW.work_item_id AND r.workspace_id=NEW.workspace_id
      AND wi.kind='ALLOWANCE_VOID' AND wi.status='review' AND wi.revision=NEW.expected_revision
      AND wi.environment=r.environment AND wi.reference_hash=r.reference_hash
      AND r.allowance_number=NEW.allowance_number
      AND a.approval_id=NEW.approval_id AND a.workspace_id=NEW.workspace_id
      AND a.expected_revision=NEW.expected_revision
      AND a.valid_until_utc>NEW.claimed_at AND a.approved_at<=NEW.claimed_at
      AND c.seller_invoice=r.seller_invoice AND c.revision=r.company_settings_revision
      AND d.status='active' AND d.employee_authority_state='cloud'
      AND w.status='active'
  ) THEN RAISE(ABORT,'ALLOWANCE_VOID_SEND_NOT_AUTHORIZED') END;
END;
CREATE TRIGGER allowance_void_claim_no_update BEFORE UPDATE ON allowance_void_send_claims
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_SEND_ONCE'); END;
CREATE TRIGGER allowance_void_claim_no_delete BEFORE DELETE ON allowance_void_send_claims
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_SEND_ONCE'); END;

-- No Work Item may be marked closed merely because g0501 returned code=0.
-- The later reconciler must first record trusted official cancellation evidence.
CREATE TABLE allowance_void_official_checks (
  check_id TEXT PRIMARY KEY,
  work_item_id TEXT NOT NULL REFERENCES allowance_void_requests(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  allowance_number TEXT NOT NULL,
  official_type TEXT NOT NULL CHECK(length(official_type) BETWEEN 1 AND 16),
  official_upload_status INTEGER NOT NULL CHECK(official_upload_status>=0),
  official_snapshot_sha256 TEXT NOT NULL CHECK(length(official_snapshot_sha256)=64 AND
    official_snapshot_sha256 NOT GLOB '*[^0-9a-f]*'),
  checked_at TEXT NOT NULL,
  checked_by_employee_id TEXT NOT NULL CHECK(length(checked_by_employee_id) BETWEEN 1 AND 100),
  checked_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT
);
CREATE TRIGGER allowance_void_check_source_guard BEFORE INSERT ON allowance_void_official_checks
BEGIN
  SELECT CASE WHEN NOT EXISTS(
    SELECT 1 FROM allowance_void_requests r
    JOIN devices d ON d.device_id=NEW.checked_by_device_id AND d.workspace_id=r.workspace_id
    JOIN workspaces w ON w.workspace_id=r.workspace_id
    WHERE r.work_item_id=NEW.work_item_id AND r.workspace_id=NEW.workspace_id
      AND r.allowance_number=NEW.allowance_number
      AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'
  ) THEN RAISE(ABORT,'ALLOWANCE_VOID_OFFICIAL_CHECK_SCOPE_CHANGED') END;
END;
CREATE TRIGGER allowance_void_check_no_update BEFORE UPDATE ON allowance_void_official_checks
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER allowance_void_check_no_delete BEFORE DELETE ON allowance_void_official_checks
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_VOID_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER allowance_void_no_unsupported_close BEFORE UPDATE OF status ON work_items
WHEN OLD.kind='ALLOWANCE_VOID' AND NEW.status='closed'
BEGIN
  SELECT CASE WHEN NOT EXISTS(
    SELECT 1 FROM allowance_void_official_checks c
    JOIN allowance_void_requests r ON r.work_item_id=c.work_item_id
    WHERE c.work_item_id=OLD.work_item_id AND c.workspace_id=OLD.workspace_id
      AND c.allowance_number=r.allowance_number
      AND c.official_type IN ('D0501','B0501','B0201','B0202')
      AND c.official_upload_status=99
  ) THEN RAISE(ABORT,'ALLOWANCE_VOID_OFFICIAL_PROOF_REQUIRED') END;
END;
