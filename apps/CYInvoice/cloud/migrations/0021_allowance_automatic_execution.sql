-- Forward-only continuation of applied schema20. No historical row is deleted.
-- Business requests/approvals remain the owners; execution fields are their
-- processing/problem/completed projection. Attempts/evidence are not a queue.
ALTER TABLE allowance_execution_approvals ADD COLUMN authority_snapshot TEXT;
ALTER TABLE allowance_void_approvals ADD COLUMN authority_snapshot TEXT;
ALTER TABLE allowance_applications ADD COLUMN execution_state TEXT NOT NULL DEFAULT 'processing'
 CHECK(execution_state IN ('processing','completed','problem'));
ALTER TABLE allowance_applications ADD COLUMN next_check_at TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_applications ADD COLUMN first_error TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_applications ADD COLUMN last_error TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_applications ADD COLUMN request_id TEXT NOT NULL DEFAULT '';
CREATE INDEX idx_allowance_applications_execution ON allowance_applications(workspace_id,status,execution_state,next_check_at);
ALTER TABLE allowance_void_requests ADD COLUMN execution_state TEXT NOT NULL DEFAULT 'processing'
 CHECK(execution_state IN ('processing','completed','problem'));
ALTER TABLE allowance_void_requests ADD COLUMN next_check_at TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_void_requests ADD COLUMN first_error TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_void_requests ADD COLUMN last_error TEXT NOT NULL DEFAULT '';
ALTER TABLE allowance_void_requests ADD COLUMN request_id TEXT NOT NULL DEFAULT '';
CREATE INDEX idx_allowance_void_requests_execution ON allowance_void_requests(workspace_id,status,execution_state,next_check_at);
DROP TRIGGER allowance_application_transition_guard;
CREATE TRIGGER allowance_application_transition_guard BEFORE UPDATE ON allowance_applications
WHEN (OLD.status IN ('approved','rejected','withdrawn') OR
 NEW.revision<>OLD.revision+1 OR
 NOT ((OLD.status='open' AND NEW.status IN ('review','withdrawn')) OR
      (OLD.status='review' AND NEW.status IN ('approved','rejected'))) OR
 NEW.application_id<>OLD.application_id OR NEW.workspace_id<>OLD.workspace_id OR
 NEW.opened_by_device_id<>OLD.opened_by_device_id OR
 NEW.environment<>OLD.environment OR NEW.reference_hash<>OLD.reference_hash OR
 NEW.request_hash<>OLD.request_hash OR NEW.requested_gross<>OLD.requested_gross OR
 NEW.requester_actor_id<>OLD.requester_actor_id OR NEW.created_at<>OLD.created_at OR
 (OLD.status='open' AND NEW.status='review' AND NEW.manager_actor_id IS NOT NEW.actor_id) OR
 (OLD.status='review' AND NEW.manager_actor_id IS NOT OLD.manager_actor_id) OR
 (OLD.status='open' AND NEW.status='withdrawn' AND NEW.actor_id<>OLD.requester_actor_id)) AND NOT (NEW.application_id IS OLD.application_id AND NEW.workspace_id IS OLD.workspace_id AND NEW.opened_by_device_id IS OLD.opened_by_device_id AND NEW.updated_by_device_id IS OLD.updated_by_device_id AND NEW.environment IS OLD.environment AND NEW.reference_hash IS OLD.reference_hash AND NEW.request_hash IS OLD.request_hash AND NEW.requested_gross IS OLD.requested_gross AND NEW.status IS OLD.status AND NEW.revision IS OLD.revision AND NEW.requester_actor_id IS OLD.requester_actor_id AND NEW.actor_id IS OLD.actor_id AND NEW.manager_actor_id IS OLD.manager_actor_id AND NEW.created_at IS OLD.created_at AND NEW.updated_at IS OLD.updated_at)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_TRANSITION_INVALID'); END;
DROP TRIGGER allowance_application_transition_audit;
CREATE TRIGGER allowance_application_transition_audit AFTER UPDATE ON allowance_applications WHEN NEW.revision<>OLD.revision
BEGIN
 INSERT INTO allowance_application_audit_events
 (application_id,workspace_id,revision,from_status,to_status,actor_id,device_id,occurred_at)
 VALUES(NEW.application_id,NEW.workspace_id,NEW.revision,OLD.status,NEW.status,
        NEW.actor_id,NEW.updated_by_device_id,NEW.updated_at);
END;
DROP TRIGGER allowance_void_request_transition_guard;
CREATE TRIGGER allowance_void_request_transition_guard BEFORE UPDATE ON allowance_void_requests
WHEN (OLD.status<>'open' OR NEW.status<>'approved' OR NEW.revision<>2 OR
 NEW.manager_actor_id IS NULL OR
 NEW.application_id<>OLD.application_id OR NEW.workspace_id<>OLD.workspace_id OR
 NEW.opened_by_device_id<>OLD.opened_by_device_id OR
 NEW.environment<>OLD.environment OR NEW.seller_invoice<>OLD.seller_invoice OR
 NEW.company_revision<>OLD.company_revision OR
 NEW.allowance_fingerprint<>OLD.allowance_fingerprint OR
 NEW.request_hash<>OLD.request_hash OR NEW.frozen_ciphertext<>OLD.frozen_ciphertext OR
 NEW.requester_actor_id<>OLD.requester_actor_id OR NEW.created_at<>OLD.created_at OR
 NOT EXISTS (SELECT 1 FROM allowance_void_approvals a
 WHERE a.application_id=OLD.application_id AND a.workspace_id=OLD.workspace_id
 AND a.request_hash=OLD.request_hash AND a.manager_actor_id=NEW.manager_actor_id)) AND NOT (NEW.application_id IS OLD.application_id AND NEW.workspace_id IS OLD.workspace_id AND NEW.opened_by_device_id IS OLD.opened_by_device_id AND NEW.environment IS OLD.environment AND NEW.seller_invoice IS OLD.seller_invoice AND NEW.company_revision IS OLD.company_revision AND NEW.allowance_fingerprint IS OLD.allowance_fingerprint AND NEW.request_hash IS OLD.request_hash AND NEW.frozen_ciphertext IS OLD.frozen_ciphertext AND NEW.requester_actor_id IS OLD.requester_actor_id AND NEW.status IS OLD.status AND NEW.revision IS OLD.revision AND NEW.manager_actor_id IS OLD.manager_actor_id AND NEW.created_at IS OLD.created_at AND NEW.updated_at IS OLD.updated_at)
BEGIN SELECT RAISE(ABORT,'VOID_REQUEST_IMMUTABLE_OR_UNAPPROVED'); END;
DROP TRIGGER allowance_ledger_reservation_guard;
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
DROP TRIGGER allowance_void_claim_source_guard;
CREATE TRIGGER allowance_void_claim_source_guard
BEFORE INSERT ON allowance_void_send_claims
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_void_requests r
 JOIN allowance_void_approvals a ON a.application_id=r.application_id
 JOIN devices d ON d.device_id=NEW.manager_device_id AND d.workspace_id=r.workspace_id
 JOIN workspaces w ON w.workspace_id=r.workspace_id
 JOIN company_settings c ON c.workspace_id=r.workspace_id
 WHERE r.application_id=NEW.application_id AND r.workspace_id=NEW.workspace_id
 AND c.seller_invoice=r.seller_invoice AND c.revision=r.company_revision
 AND r.status='approved' AND r.revision=2
 AND r.environment=NEW.environment AND r.seller_invoice=NEW.seller_invoice
 AND r.allowance_fingerprint=NEW.allowance_fingerprint
 AND r.request_hash=NEW.request_hash
 AND a.workspace_id=r.workspace_id AND a.request_hash=r.request_hash
 AND a.official_snapshot_hash=NEW.official_snapshot_hash
 AND a.manager_actor_id=NEW.manager_actor_id
 AND a.manager_device_id=NEW.manager_device_id
 AND julianday(NEW.claimed_at) IS NOT NULL
 AND julianday(NEW.claimed_at)>=julianday(a.approved_at)
 AND d.status='active' AND d.employee_authority_state='cloud'
 AND w.status='active'
)
BEGIN SELECT RAISE(ABORT,'VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID'); END;
CREATE TABLE allowance_number_rejections (
 ledger_id TEXT PRIMARY KEY REFERENCES allowance_number_ledger(ledger_id),
 provider_code INTEGER NOT NULL CHECK(provider_code IN (4040161,4040162)),
 recorded_at TEXT NOT NULL
);
CREATE TRIGGER allowance_rejection_source BEFORE INSERT ON allowance_number_rejections
WHEN NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.ledger_id=NEW.ledger_id
 AND l.status='occupied_external' AND l.collision_code=NEW.provider_code)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_REJECTION_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_rejections_no_update BEFORE UPDATE ON allowance_number_rejections
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_REJECTION_PERMANENT'); END;
CREATE TRIGGER allowance_rejections_no_delete BEFORE DELETE ON allowance_number_rejections
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_REJECTION_PERMANENT'); END;
CREATE TABLE allowance_void_send_attempts (
 application_id TEXT NOT NULL REFERENCES allowance_void_send_claims(application_id),
 attempt INTEGER NOT NULL CHECK(attempt BETWEEN 1 AND 3),
 started_at TEXT NOT NULL, response_received INTEGER NOT NULL DEFAULT 0 CHECK(response_received IN (0,1)),
 provider_code INTEGER, PRIMARY KEY(application_id,attempt)
);
CREATE TRIGGER allowance_void_attempt_update_guard BEFORE UPDATE ON allowance_void_send_attempts
WHEN OLD.response_received<>0 OR NEW.application_id<>OLD.application_id
 OR NEW.attempt<>OLD.attempt OR NEW.started_at<>OLD.started_at OR NEW.response_received<>1
BEGIN SELECT RAISE(ABORT,'VOID_ATTEMPT_IMMUTABLE'); END;
CREATE TRIGGER allowance_void_attempt_no_delete BEFORE DELETE ON allowance_void_send_attempts
BEGIN SELECT RAISE(ABORT,'VOID_ATTEMPT_PERMANENT'); END;
CREATE TABLE allowance_execution_events (
 event_id INTEGER PRIMARY KEY AUTOINCREMENT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),
 application_id TEXT NOT NULL, operation TEXT NOT NULL CHECK(operation IN ('issue','void')),
 stage TEXT NOT NULL, attempt INTEGER, provider_code INTEGER, http_status INTEGER,
 response_received INTEGER, request_id TEXT NOT NULL, error_code TEXT NOT NULL DEFAULT '',
 recorded_at TEXT NOT NULL
);
CREATE INDEX idx_allowance_execution_events_operation
 ON allowance_execution_events(workspace_id,application_id,event_id);
CREATE TRIGGER allowance_execution_events_no_update BEFORE UPDATE ON allowance_execution_events
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_EVENT_PERMANENT'); END;
CREATE TRIGGER allowance_execution_events_no_delete BEFORE DELETE ON allowance_execution_events
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_EVENT_PERMANENT'); END;

-- Bounded runtime projection uses independent ordered cursors for each kind.
CREATE INDEX idx_allowance_application_runtime_page ON allowance_applications(workspace_id,application_id);
CREATE INDEX idx_allowance_void_runtime_page ON allowance_void_requests(workspace_id,application_id);
