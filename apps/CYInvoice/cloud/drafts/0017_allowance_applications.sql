-- V3.2-C1 REVIEW DRAFT ONLY. NOT A CANONICAL D1 MIGRATION.
-- Baseline: canonical 0016_non_allowance_work_items.sql (schema16).
-- This draft must not be placed under migrations/ or deployed.
-- No invoice numbers, buyer identifiers, official claims or credentials are stored.
CREATE TABLE allowance_applications (
  application_id TEXT PRIMARY KEY CHECK(length(application_id) BETWEEN 8 AND 100),
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  updated_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  environment TEXT NOT NULL CHECK(environment IN ('production','test')),
  reference_hash TEXT NOT NULL CHECK(length(reference_hash)=64 AND reference_hash NOT GLOB '*[^0-9a-f]*'),
  request_hash TEXT NOT NULL CHECK(length(request_hash)=64 AND request_hash NOT GLOB '*[^0-9a-f]*'),
  requested_gross TEXT NOT NULL CHECK(length(requested_gross) BETWEEN 1 AND 40),
  status TEXT NOT NULL DEFAULT 'open' CHECK(status IN ('open','review','approved','rejected','withdrawn')),
  revision INTEGER NOT NULL DEFAULT 1 CHECK(revision>=1),
  requester_actor_id TEXT NOT NULL CHECK(length(requester_actor_id) BETWEEN 1 AND 100),
  actor_id TEXT NOT NULL CHECK(length(actor_id) BETWEEN 1 AND 100),
  manager_actor_id TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK((status='open' AND manager_actor_id IS NULL) OR
        (status IN ('review','approved','rejected') AND manager_actor_id IS NOT NULL) OR
        (status='withdrawn' AND manager_actor_id IS NULL))
);
CREATE INDEX idx_allowance_applications_workspace_status
 ON allowance_applications(workspace_id,status,updated_at DESC);
CREATE INDEX idx_allowance_applications_reference
 ON allowance_applications(workspace_id,environment,reference_hash);
-- Deliberately no UNIQUE(reference_hash): separate explicitly authorized
-- partial allowances against one invoice are legitimate business actions.
CREATE TABLE allowance_application_audit_events (
 event_id INTEGER PRIMARY KEY AUTOINCREMENT,
 application_id TEXT NOT NULL REFERENCES allowance_applications(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 revision INTEGER NOT NULL CHECK(revision>=1),
 from_status TEXT,
 to_status TEXT NOT NULL CHECK(to_status IN ('open','review','approved','rejected','withdrawn')),
 actor_id TEXT NOT NULL,
 device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 occurred_at TEXT NOT NULL,
 UNIQUE(application_id,revision)
);
CREATE TRIGGER allowance_application_create_audit AFTER INSERT ON allowance_applications
BEGIN
 INSERT INTO allowance_application_audit_events
 (application_id,workspace_id,revision,from_status,to_status,actor_id,device_id,occurred_at)
 VALUES(NEW.application_id,NEW.workspace_id,NEW.revision,NULL,NEW.status,
        NEW.actor_id,NEW.opened_by_device_id,NEW.created_at);
END;
CREATE TRIGGER allowance_application_transition_guard BEFORE UPDATE ON allowance_applications
WHEN OLD.status IN ('approved','rejected','withdrawn') OR
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
 (OLD.status='open' AND NEW.status='withdrawn' AND NEW.actor_id<>OLD.requester_actor_id)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_TRANSITION_INVALID'); END;
CREATE TRIGGER allowance_application_transition_audit AFTER UPDATE ON allowance_applications
BEGIN
 INSERT INTO allowance_application_audit_events
 (application_id,workspace_id,revision,from_status,to_status,actor_id,device_id,occurred_at)
 VALUES(NEW.application_id,NEW.workspace_id,NEW.revision,OLD.status,NEW.status,
        NEW.actor_id,NEW.updated_by_device_id,NEW.updated_at);
END;
CREATE TRIGGER allowance_application_no_delete BEFORE DELETE ON allowance_applications
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_NOT_DELETABLE'); END;
CREATE TRIGGER allowance_application_audit_no_update BEFORE UPDATE ON allowance_application_audit_events
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER allowance_application_audit_no_delete BEFORE DELETE ON allowance_application_audit_events
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_AUDIT_APPEND_ONLY'); END;
