-- CYInvoice V3.2 Batch 1: proposed canonical D1 migration 0019.
-- FOR SOURCE REVIEW / SYNTHETIC CI ONLY. Do not run against original D1
-- until the user explicitly authorizes a new local Cloudflare MCP cutover.
-- Apply exactly once AFTER recorded canonical 0018. No legacy tables changed.
-- A manual triage record is NOT transaction origin evidence, a resend grant,
-- a completed allowance, or permission to allocate another allowance number.
-- Existing reconciliation rows were produced only by the full-match reader.
ALTER TABLE allowance_reconciliation_checks ADD COLUMN evidence_kind TEXT NOT NULL
 DEFAULT 'full_match_unattributed'
 CHECK(evidence_kind IN ('full_match_unattributed','external_collision'));
CREATE INDEX idx_allowance_reconciliation_ledger
 ON allowance_reconciliation_checks(workspace_id,ledger_id,evidence_kind,checked_at DESC,check_id DESC);
CREATE TRIGGER allowance_external_collision_source_guard
BEFORE INSERT ON allowance_reconciliation_checks
WHEN NEW.evidence_kind='external_collision' AND NOT EXISTS (
 SELECT 1 FROM allowance_number_ledger l
 JOIN allowance_applications a ON a.application_id=l.application_id
 JOIN devices d ON d.device_id=NEW.checked_by_device_id AND d.workspace_id=l.workspace_id
 JOIN workspaces w ON w.workspace_id=l.workspace_id
 WHERE l.ledger_id=NEW.ledger_id AND l.application_id=NEW.application_id
 AND l.workspace_id=NEW.workspace_id AND l.allowance_number=NEW.allowance_number
 AND l.status='occupied_external' AND l.collision_code IN (4040161,4040162)
 AND a.status='approved' AND a.manager_actor_id=NEW.checked_by_actor_id
 AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'
)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_EXTERNAL_COLLISION_SOURCE_INVALID'); END;
CREATE TABLE allowance_manual_review_audit (
 review_id TEXT PRIMARY KEY CHECK(length(review_id) BETWEEN 12 AND 100),
 application_id TEXT NOT NULL REFERENCES allowance_applications(application_id)
   ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 ledger_id TEXT NOT NULL REFERENCES allowance_number_ledger(ledger_id) ON DELETE RESTRICT,
 allowance_number TEXT NOT NULL CHECK(length(allowance_number)=12),
 ledger_status_at_review TEXT NOT NULL
  CHECK(ledger_status_at_review IN ('sending','occupied_external')),
 disposition TEXT NOT NULL CHECK(disposition IN
  ('full_match_unattributed','unknown_outcome','number_collision_investigation',
   'provider_rejection_investigation','external_collision_reserved')),
 next_ledger_id TEXT REFERENCES allowance_number_ledger(ledger_id) ON DELETE RESTRICT,
 evidence_check_id TEXT REFERENCES allowance_reconciliation_checks(check_id)
  ON DELETE RESTRICT,
 reviewed_by_actor_id TEXT NOT NULL CHECK(length(reviewed_by_actor_id) BETWEEN 1 AND 100),
 reviewed_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 reviewed_at TEXT NOT NULL,
 CHECK(disposition<>'full_match_unattributed' OR evidence_check_id IS NOT NULL),
 CHECK((disposition='external_collision_reserved' AND next_ledger_id IS NOT NULL
   AND evidence_check_id IS NOT NULL AND ledger_status_at_review='occupied_external')
  OR (disposition<>'external_collision_reserved' AND next_ledger_id IS NULL))
);
CREATE INDEX idx_allowance_manual_review_history
 ON allowance_manual_review_audit(workspace_id,application_id,reviewed_at DESC,review_id);
CREATE UNIQUE INDEX idx_allowance_manual_review_next_ledger
 ON allowance_manual_review_audit(next_ledger_id) WHERE next_ledger_id IS NOT NULL;
CREATE TRIGGER allowance_manual_review_source_guard
BEFORE INSERT ON allowance_manual_review_audit
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_number_ledger l
 JOIN allowance_applications a ON a.application_id=l.application_id
  AND a.workspace_id=l.workspace_id
 JOIN devices d ON d.device_id=NEW.reviewed_by_device_id
  AND d.workspace_id=a.workspace_id
 JOIN workspaces w ON w.workspace_id=a.workspace_id
 WHERE l.ledger_id=NEW.ledger_id AND l.application_id=NEW.application_id
  AND l.workspace_id=NEW.workspace_id
  AND l.allowance_number=NEW.allowance_number
  AND l.status=NEW.ledger_status_at_review
  AND a.status='approved'
  AND a.manager_actor_id=NEW.reviewed_by_actor_id
  AND d.status='active' AND d.employee_authority_state='cloud'
  AND w.status='active'
  AND (NEW.evidence_check_id IS NULL OR EXISTS (
   SELECT 1 FROM allowance_reconciliation_checks c
   WHERE c.check_id=NEW.evidence_check_id AND c.ledger_id=l.ledger_id
    AND c.application_id=NEW.application_id AND c.workspace_id=NEW.workspace_id
    AND c.allowance_number=NEW.allowance_number
    AND (NEW.disposition<>'full_match_unattributed'
      OR c.evidence_kind='full_match_unattributed')
    AND (NEW.disposition<>'external_collision_reserved'
      OR c.evidence_kind='external_collision')
  ))
  AND (NEW.next_ledger_id IS NULL OR EXISTS (
   SELECT 1 FROM allowance_number_ledger n WHERE n.ledger_id=NEW.next_ledger_id
    AND n.application_id=l.application_id AND n.workspace_id=l.workspace_id
    AND n.seller_invoice=l.seller_invoice AND n.environment=l.environment
    AND n.allowance_day=l.allowance_day AND n.request_hash=l.request_hash
    AND n.attempt=l.attempt+1 AND n.status='reserved'
    AND n.created_by_device_id=NEW.reviewed_by_device_id
    AND n.created_at=NEW.reviewed_at AND l.collision_code IN (4040161,4040162)
  ))
 )
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_MANUAL_REVIEW_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_manual_review_no_update
BEFORE UPDATE ON allowance_manual_review_audit
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_MANUAL_REVIEW_APPEND_ONLY'); END;
CREATE TRIGGER allowance_manual_review_no_delete
BEFORE DELETE ON allowance_manual_review_audit
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_MANUAL_REVIEW_APPEND_ONLY'); END;
