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
CREATE TRIGGER allowance_application_no_update
BEFORE UPDATE ON allowance_applications
BEGIN SELECT RAISE(ABORT, 'ALLOWANCE_APPLICATION_IMMUTABLE'); END;
CREATE TRIGGER allowance_application_no_delete
BEFORE DELETE ON allowance_applications
BEGIN SELECT RAISE(ABORT, 'ALLOWANCE_APPLICATION_IMMUTABLE'); END;
