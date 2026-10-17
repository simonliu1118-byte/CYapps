-- CYInvoice V3.0 canonical additive migration; not deployed until explicitly approved.
-- Stage after existing storage13 and recovery 0014. V3.1/3.2 Draft
-- identifiers in legacy PR #440 must be rebased around this 0015 slot.
-- Existing company/Device/CYID_ONLY history is never rebuilt or dropped.
-- Secrets are AES-GCM ciphertext. KEK is an independently injected Worker secret,
-- NEVER this database, Git, any Audit, or a public artifact.
CREATE TABLE company_settings (
  workspace_id TEXT PRIMARY KEY REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8
    AND seller_invoice NOT GLOB '*[^0-9]*'),
  amego_app_key_ciphertext TEXT,
  mo_excel_password_ciphertext TEXT,
  revision INTEGER NOT NULL CHECK(revision > 0),
  changed_by_employee_id TEXT NOT NULL CHECK(length(changed_by_employee_id) BETWEEN 1 AND 100),
  changed_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  changed_at TEXT NOT NULL
);
CREATE TABLE company_settings_audit_events (
  event_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL REFERENCES company_settings(workspace_id) ON DELETE RESTRICT,
  revision INTEGER NOT NULL CHECK(revision > 0),
  actor_employee_id TEXT NOT NULL CHECK(length(actor_employee_id) BETWEEN 1 AND 100),
  actor_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  changed_fields TEXT NOT NULL,
  created_at TEXT NOT NULL,
  UNIQUE(workspace_id, revision)
);

-- Audit history must not be silently changed by a later runtime path.
CREATE TRIGGER company_settings_audit_no_update
BEFORE UPDATE ON company_settings_audit_events
BEGIN SELECT RAISE(ABORT, 'COMPANY_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER company_settings_audit_no_delete
BEFORE DELETE ON company_settings_audit_events
BEGIN SELECT RAISE(ABORT, 'COMPANY_AUDIT_APPEND_ONLY'); END;
