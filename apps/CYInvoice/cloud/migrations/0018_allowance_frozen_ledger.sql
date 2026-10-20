-- CYInvoice V3.2-C6 CANONICAL D1 MIGRATION 0018. Apply once after 0017.
-- Requires canonical schema16 + canonical 0017, in this order.
-- Selected and re-keyed from historical PR #440 (old 0016/0018 drafts).
-- No legacy work_items.ALLOWANCE kind and no ordinary invoice mirror.
-- Encrypted envelopes and opaque hashes must be constructed and authenticated
-- by the future Worker; D1 shape checks are NOT proof of AES-GCM or CYID rights.

-- Keep C1 request identity and applicant Device in one Workspace. The schema16
-- work_items CHECK cannot represent allowance applications.
CREATE TRIGGER allowance_application_source_fence
BEFORE INSERT ON allowance_applications
WHEN NOT EXISTS (
 SELECT 1 FROM workspaces w JOIN devices d ON d.workspace_id=w.workspace_id
 WHERE w.workspace_id=NEW.workspace_id AND w.status='active'
 AND d.device_id=NEW.opened_by_device_id AND d.status='active'
 AND d.employee_authority_state='cloud'
 AND NEW.updated_by_device_id=NEW.opened_by_device_id
 AND NEW.actor_id=NEW.requester_actor_id
 AND NEW.status='open' AND NEW.revision=1)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_SOURCE_DEVICE_INVALID'); END;

CREATE TABLE allowance_frozen_requests (
 application_id TEXT PRIMARY KEY REFERENCES allowance_applications(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8 AND seller_invoice NOT GLOB '*[^0-9]*'),
 environment TEXT NOT NULL CHECK(environment IN ('test','production')),
 company_settings_revision INTEGER NOT NULL CHECK(company_settings_revision>0),
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64 AND request_hash NOT GLOB '*[^0-9a-f]*'),
 -- A keyed, server-produced fingerprint, never a plaintext invoice number or
 -- unhashed buyer ID. Used ONLY to serialize unresolved g0401 attempts.
 original_invoice_fingerprint TEXT NOT NULL CHECK(
   length(original_invoice_fingerprint)=64 AND original_invoice_fingerprint NOT GLOB '*[^0-9a-f]*'),
 allowance_day TEXT NOT NULL CHECK(length(allowance_day)=8 AND allowance_day NOT GLOB '*[^0-9]*'),
 -- AES-GCM envelope containing original invoice/edited lines/other PII.
 -- Never accept a client-created ciphertext or store an App Key in this table.
 template_ciphertext TEXT NOT NULL CHECK(length(template_ciphertext)>48),
 encrypted_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 frozen_at TEXT NOT NULL
);
CREATE INDEX idx_allowance_frozen_invoice
 ON allowance_frozen_requests(seller_invoice,environment,original_invoice_fingerprint);
CREATE TRIGGER allowance_frozen_insert_guard BEFORE INSERT ON allowance_frozen_requests
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_applications a
 JOIN devices d ON d.device_id=a.opened_by_device_id AND d.workspace_id=a.workspace_id
 JOIN workspaces w ON w.workspace_id=a.workspace_id
 JOIN company_settings c ON c.workspace_id=a.workspace_id
 WHERE a.application_id=NEW.application_id AND a.workspace_id=NEW.workspace_id
 AND a.environment=NEW.environment AND a.request_hash=NEW.request_hash
 AND a.status='open' AND a.revision=1
 AND NEW.encrypted_by_device_id=a.opened_by_device_id
 AND c.seller_invoice=NEW.seller_invoice
 AND c.revision=NEW.company_settings_revision
 AND c.amego_app_key_ciphertext IS NOT NULL
 AND d.status='active' AND d.employee_authority_state='cloud'
 AND w.status='active')
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_FROZEN_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_frozen_no_update BEFORE UPDATE ON allowance_frozen_requests
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_FROZEN_IMMUTABLE'); END;
CREATE TRIGGER allowance_frozen_no_delete BEFORE DELETE ON allowance_frozen_requests
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_FROZEN_IMMUTABLE'); END;

-- No transition to review without a frozen, server-authenticated envelope.
CREATE TRIGGER allowance_review_requires_frozen
BEFORE UPDATE OF status ON allowance_applications
WHEN OLD.status='open' AND NEW.status='review' AND NOT EXISTS (
 SELECT 1 FROM allowance_frozen_requests f
 WHERE f.application_id=OLD.application_id AND f.workspace_id=OLD.workspace_id
 AND f.request_hash=OLD.request_hash AND f.environment=OLD.environment)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_FROZEN_REQUIRED'); END;

-- Manager authority and official AMEGO preflight MUST still be verified by
-- the future Worker, immediately before writing an approval.
CREATE TABLE allowance_execution_approvals (
 approval_id TEXT PRIMARY KEY,
 application_id TEXT NOT NULL REFERENCES allowance_applications(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 reviewed_revision INTEGER NOT NULL CHECK(reviewed_revision>0),
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64 AND request_hash NOT GLOB '*[^0-9a-f]*'),
 official_snapshot_hash TEXT NOT NULL CHECK(
 length(official_snapshot_hash)=64 AND official_snapshot_hash NOT GLOB '*[^0-9a-f]*'),
 official_checked_at TEXT NOT NULL,
 manager_actor_id TEXT NOT NULL CHECK(length(manager_actor_id) BETWEEN 1 AND 100),
 manager_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 approved_at TEXT NOT NULL,
 valid_until_utc TEXT NOT NULL CHECK(valid_until_utc>approved_at),
 UNIQUE(application_id,reviewed_revision)
);
CREATE TRIGGER allowance_approval_source_guard
BEFORE INSERT ON allowance_execution_approvals
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_applications a
 JOIN allowance_frozen_requests f ON f.application_id=a.application_id
 JOIN devices d ON d.device_id=NEW.manager_device_id AND d.workspace_id=a.workspace_id
 JOIN workspaces w ON w.workspace_id=a.workspace_id
 JOIN company_settings c ON c.workspace_id=a.workspace_id
 WHERE a.application_id=NEW.application_id AND a.workspace_id=NEW.workspace_id
 AND a.status='review' AND a.revision=NEW.reviewed_revision
 AND a.manager_actor_id=NEW.manager_actor_id
 AND a.request_hash=NEW.request_hash AND f.request_hash=NEW.request_hash
 AND c.seller_invoice=f.seller_invoice AND c.revision=f.company_settings_revision
 AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active')
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPROVAL_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_approval_no_update BEFORE UPDATE ON allowance_execution_approvals
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPROVAL_APPEND_ONLY'); END;
CREATE TRIGGER allowance_approval_no_delete BEFORE DELETE ON allowance_execution_approvals
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPROVAL_APPEND_ONLY'); END;
CREATE TRIGGER allowance_approved_requires_grant BEFORE UPDATE OF status ON allowance_applications
WHEN OLD.status='review' AND NEW.status='approved' AND NOT EXISTS (
 SELECT 1 FROM allowance_execution_approvals p
 WHERE p.application_id=OLD.application_id AND p.workspace_id=OLD.workspace_id
 AND p.reviewed_revision=OLD.revision AND p.manager_actor_id=OLD.manager_actor_id
 AND p.request_hash=OLD.request_hash AND p.valid_until_utc>NEW.updated_at)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPROVAL_REQUIRED'); END;
CREATE TRIGGER allowance_manager_final_actor_guard BEFORE UPDATE OF status ON allowance_applications
WHEN OLD.status='review' AND NEW.status IN ('approved','rejected')
 AND NEW.actor_id IS NOT OLD.manager_actor_id
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_MANAGER_MISMATCH'); END;

-- An allocation is an immutable record of an attempt, not official evidence.
-- Future Worker owns number encoding and must compare request hash to frozen
-- encrypted payload. No public endpoint or g0401 caller is added by C2.
CREATE TABLE allowance_number_ledger (
 ledger_id TEXT PRIMARY KEY,
 application_id TEXT NOT NULL REFERENCES allowance_applications(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8 AND seller_invoice NOT GLOB '*[^0-9]*'),
 environment TEXT NOT NULL CHECK(environment IN ('test','production')),
 allowance_day TEXT NOT NULL CHECK(length(allowance_day)=8 AND allowance_day NOT GLOB '*[^0-9]*'),
 sequence INTEGER NOT NULL CHECK(sequence BETWEEN 1 AND 34695),
 allowance_number TEXT NOT NULL CHECK(length(allowance_number)=12 AND
   allowance_number NOT GLOB '*[^0-9A-Z]*'),
 attempt INTEGER NOT NULL CHECK(attempt>0),
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64 AND request_hash NOT GLOB '*[^0-9a-f]*'),
 status TEXT NOT NULL CHECK(status IN ('reserved','sending','occupied_external','confirmed')),
 collision_code INTEGER,
 created_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 created_at TEXT NOT NULL,
 updated_at TEXT NOT NULL,
 CHECK((status='occupied_external' AND collision_code IN (4040161,4040162))
    OR (status<>'occupied_external' AND collision_code IS NULL)),
 UNIQUE(seller_invoice,environment,allowance_day,sequence),
 UNIQUE(seller_invoice,environment,allowance_number),
 UNIQUE(application_id,attempt)
);
CREATE INDEX idx_allowance_ledger_application
 ON allowance_number_ledger(workspace_id,application_id,attempt DESC);
CREATE TRIGGER allowance_ledger_reservation_guard BEFORE INSERT ON allowance_number_ledger
WHEN NEW.status<>'reserved' OR NEW.collision_code IS NOT NULL OR
 substr(NEW.allowance_number,1,9)<>'A'||NEW.allowance_day OR
 NOT EXISTS (
  SELECT 1 FROM allowance_applications a
  JOIN allowance_frozen_requests f ON f.application_id=a.application_id
  JOIN allowance_execution_approvals p ON p.application_id=a.application_id
  JOIN devices d ON d.device_id=NEW.created_by_device_id AND d.workspace_id=a.workspace_id
  JOIN workspaces w ON w.workspace_id=a.workspace_id
  JOIN company_settings c ON c.workspace_id=a.workspace_id
  WHERE a.application_id=NEW.application_id AND a.workspace_id=NEW.workspace_id
  AND a.status='approved' AND a.revision=p.reviewed_revision+1
  AND a.request_hash=NEW.request_hash AND f.request_hash=NEW.request_hash
  AND f.seller_invoice=NEW.seller_invoice AND f.environment=NEW.environment
  AND f.allowance_day=NEW.allowance_day
  AND p.request_hash=NEW.request_hash
  AND p.manager_actor_id=a.manager_actor_id
  AND p.valid_until_utc>NEW.created_at
  AND c.seller_invoice=f.seller_invoice AND c.revision=f.company_settings_revision
  AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'
 ) OR
 (NEW.attempt=1 AND EXISTS(SELECT 1 FROM allowance_number_ledger l
  WHERE l.application_id=NEW.application_id)) OR
 (NEW.attempt<>1 AND NOT EXISTS(
  SELECT 1 FROM allowance_number_ledger prev WHERE prev.application_id=NEW.application_id
  AND prev.attempt=NEW.attempt-1 AND prev.status='occupied_external'
  AND prev.collision_code IN (4040161,4040162))) OR
 (NEW.attempt<>1 AND NEW.attempt<2)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_RESERVATION_INVALID'); END;
CREATE TRIGGER allowance_ledger_no_delete BEFORE DELETE ON allowance_number_ledger
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_LEDGER_PERMANENT'); END;
CREATE TRIGGER allowance_ledger_transition_guard BEFORE UPDATE ON allowance_number_ledger
WHEN NEW.ledger_id IS NOT OLD.ledger_id OR NEW.application_id IS NOT OLD.application_id OR
 NEW.workspace_id IS NOT OLD.workspace_id OR NEW.seller_invoice IS NOT OLD.seller_invoice OR
 NEW.environment IS NOT OLD.environment OR NEW.allowance_day IS NOT OLD.allowance_day OR
 NEW.sequence IS NOT OLD.sequence OR NEW.allowance_number IS NOT OLD.allowance_number OR
 NEW.attempt IS NOT OLD.attempt OR NEW.request_hash IS NOT OLD.request_hash OR
 NEW.created_by_device_id IS NOT OLD.created_by_device_id OR NEW.created_at IS NOT OLD.created_at OR
 NOT ((OLD.status='reserved' AND NEW.status='sending' AND NEW.collision_code IS NULL) OR
      (OLD.status='sending' AND NEW.status IN ('confirmed','occupied_external')))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_LEDGER_TRANSITION_INVALID'); END;
CREATE TRIGGER allowance_ledger_send_fence BEFORE UPDATE OF status ON allowance_number_ledger
WHEN OLD.status='reserved' AND NEW.status='sending' AND (
 NOT EXISTS (
  SELECT 1 FROM allowance_applications a
  JOIN allowance_frozen_requests f ON f.application_id=a.application_id
  JOIN devices d ON d.device_id=NEW.created_by_device_id AND d.workspace_id=a.workspace_id
  JOIN workspaces w ON w.workspace_id=a.workspace_id
  WHERE a.application_id=NEW.application_id AND a.workspace_id=NEW.workspace_id
  AND a.status='approved' AND a.request_hash=NEW.request_hash
  AND f.request_hash=NEW.request_hash AND d.status='active'
  AND d.employee_authority_state='cloud' AND w.status='active'
 ) OR EXISTS (
  SELECT 1 FROM allowance_frozen_requests source
  JOIN allowance_frozen_requests competing
   ON competing.seller_invoice=source.seller_invoice
   AND competing.environment=source.environment
   AND competing.original_invoice_fingerprint=source.original_invoice_fingerprint
   AND competing.application_id<>source.application_id
  JOIN allowance_number_ledger other
   ON other.application_id=competing.application_id AND other.status='sending'
  WHERE source.application_id=NEW.application_id
 ))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_ORIGINAL_INVOICE_IN_FLIGHT'); END;

-- A readback is only evidence that a lookup happened. The future Core/Worker
-- must compare original number, date, buyer, totals and every item before
-- recording confirmed; code=0 and a matching number alone are not proof.
CREATE TABLE allowance_reconciliation_checks (
 check_id TEXT PRIMARY KEY,
 ledger_id TEXT NOT NULL REFERENCES allowance_number_ledger(ledger_id) ON DELETE RESTRICT,
 application_id TEXT NOT NULL REFERENCES allowance_applications(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 allowance_number TEXT NOT NULL CHECK(length(allowance_number)=12),
 official_snapshot_hash TEXT NOT NULL CHECK(
 length(official_snapshot_hash)=64 AND official_snapshot_hash NOT GLOB '*[^0-9a-f]*'),
 checked_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 checked_by_actor_id TEXT NOT NULL CHECK(length(checked_by_actor_id) BETWEEN 1 AND 100),
 checked_at TEXT NOT NULL
);
CREATE TRIGGER allowance_reconcile_source_guard BEFORE INSERT ON allowance_reconciliation_checks
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_number_ledger l
 JOIN allowance_applications a ON a.application_id=l.application_id
 WHERE l.ledger_id=NEW.ledger_id AND l.application_id=NEW.application_id
 AND l.workspace_id=NEW.workspace_id AND l.allowance_number=NEW.allowance_number
 AND l.status IN ('sending','confirmed','occupied_external'))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_RECONCILIATION_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_reconcile_no_update BEFORE UPDATE ON allowance_reconciliation_checks
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_RECONCILIATION_APPEND_ONLY'); END;
CREATE TRIGGER allowance_reconcile_no_delete BEFORE DELETE ON allowance_reconciliation_checks
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_RECONCILIATION_APPEND_ONLY'); END;
