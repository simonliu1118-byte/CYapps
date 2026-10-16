-- Draft-only exceptional ALLOWANCE applications, never normal invoices.
-- Depends on 0015 work_items and 0017 company_settings. No deployment.
-- Complete unnumbered g0401 template is AES-GCM encrypted; neither customer
-- data nor official API payload may be stored as plaintext in this table.
CREATE TABLE allowance_applications (
  work_item_id TEXT PRIMARY KEY REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8
    AND seller_invoice NOT GLOB '*[^0-9]*'),
  company_settings_revision INTEGER NOT NULL CHECK(company_settings_revision > 0),
  environment TEXT NOT NULL CHECK(environment IN ('production','test')),
  original_invoice_number TEXT NOT NULL CHECK(length(original_invoice_number)=10),
  allowance_day TEXT NOT NULL CHECK(length(allowance_day)=8),
  requested_gross_amount INTEGER NOT NULL CHECK(requested_gross_amount > 0),
  application_reason TEXT NOT NULL CHECK(length(application_reason) BETWEEN 1 AND 500),
  applicant_employee_id TEXT NOT NULL CHECK(length(applicant_employee_id) BETWEEN 1 AND 100),
  applicant_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  template_sha256 TEXT NOT NULL CHECK(length(template_sha256)=64
    AND template_sha256 NOT GLOB '*[^0-9a-f]*'),
  template_ciphertext TEXT NOT NULL CHECK(length(template_ciphertext)>48),
  reference_hash TEXT NOT NULL CHECK(length(reference_hash)=64
    AND reference_hash NOT GLOB '*[^0-9a-f]*'),
  created_at TEXT NOT NULL,
  UNIQUE(workspace_id,environment,reference_hash)
);
-- The second statement of a D1.batch must be allowed ONLY when the first
-- Work Item INSERT actually created this exact applicant's source row.
-- A prior unrelated Work Item ID, stale Device or company revision MUST abort
-- the whole batch, not leave a committed orphan application.
CREATE TRIGGER allowance_application_source_guard
BEFORE INSERT ON allowance_applications
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM work_items wi
    JOIN devices d ON d.device_id=NEW.applicant_device_id AND d.workspace_id=wi.workspace_id
    JOIN workspaces w ON w.workspace_id=wi.workspace_id
    JOIN company_settings c ON c.workspace_id=wi.workspace_id
    WHERE wi.work_item_id=NEW.work_item_id AND wi.workspace_id=NEW.workspace_id
      AND wi.kind='ALLOWANCE' AND wi.status='open' AND wi.revision=1
      AND wi.reference_hash=NEW.reference_hash AND wi.environment=NEW.environment
      AND wi.actor_id=NEW.applicant_employee_id
      AND wi.opened_by_device_id=NEW.applicant_device_id
      AND wi.created_at=NEW.created_at AND wi.updated_at=NEW.created_at
      AND c.seller_invoice=NEW.seller_invoice AND c.revision=NEW.company_settings_revision
      AND c.amego_app_key_ciphertext IS NOT NULL
      AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'
  ) THEN RAISE(ABORT,'ALLOWANCE_APPLICATION_SOURCE_MISMATCH') END;
END;
CREATE TRIGGER allowance_application_no_update
BEFORE UPDATE ON allowance_applications
BEGIN SELECT RAISE(ABORT, 'ALLOWANCE_APPLICATION_IMMUTABLE'); END;
CREATE TRIGGER allowance_application_no_delete
BEFORE DELETE ON allowance_applications
BEGIN SELECT RAISE(ABORT, 'ALLOWANCE_APPLICATION_IMMUTABLE'); END;


-- Final cross-device send gate. Approval of two distinct Work Items can race
-- while AMEGO's first g0401 is still pending. Since the provider result can
-- arrive later, only one Work Item for the same seller/environment/original
-- invoice may cross reserved -> sending. SQLite serializes trigger checks.
-- This does not add a new table, sync service or automatic refund logic.
CREATE TRIGGER allowance_original_invoice_one_inflight
BEFORE UPDATE OF status ON allowance_number_ledger
WHEN OLD.status='reserved' AND NEW.status='sending'
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM allowance_applications source
    WHERE source.work_item_id=NEW.work_item_id
      AND source.workspace_id=NEW.workspace_id
      AND source.seller_invoice=NEW.seller_invoice
      AND source.environment=NEW.environment
  ) THEN RAISE(ABORT,'ALLOWANCE_INVOICE_SOURCE_MISSING') END;
  SELECT CASE WHEN EXISTS (
    SELECT 1 FROM allowance_applications source
    JOIN allowance_applications competing
      ON competing.workspace_id=source.workspace_id
      AND competing.seller_invoice=source.seller_invoice
      AND competing.environment=source.environment
      AND competing.original_invoice_number=source.original_invoice_number
      AND competing.work_item_id<>source.work_item_id
    JOIN allowance_number_ledger other
      ON other.workspace_id=competing.workspace_id
      AND other.work_item_id=competing.work_item_id
      AND other.seller_invoice=competing.seller_invoice
      AND other.environment=competing.environment
      AND other.status IN ('sending','confirmed')
    WHERE source.work_item_id=NEW.work_item_id
      AND source.workspace_id=NEW.workspace_id
      AND source.seller_invoice=NEW.seller_invoice
      AND source.environment=NEW.environment
  ) THEN RAISE(ABORT,'ALLOWANCE_ORIGINAL_INVOICE_IN_FLIGHT') END;
END;
