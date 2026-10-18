-- V3.1 draft 0016: non-allowance Work Item metadata only.
-- NOT an invoice ledger, official AMEGO status or a migration approval.
-- The canonical 0015 slot belongs to V3.0 company_settings.
-- Do not apply to original D1; first confirm the original D1 backup and schema.
CREATE TABLE work_items (
    work_item_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    updated_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    environment TEXT NOT NULL CHECK (environment IN ('production', 'test')),
    kind TEXT NOT NULL CHECK (kind IN ('VOID', 'UPLOAD_ISSUE')),
    -- Upload issues are UNVERIFIED local Device reports, never AMEGO evidence.
    issue_code TEXT,
    reference_hash TEXT NOT NULL CHECK (
      length(reference_hash) = 64 AND reference_hash NOT GLOB '*[^0-9a-f]*'),
    status TEXT NOT NULL DEFAULT 'open'
      CHECK (status IN ('open', 'review', 'closed')),
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision > 0),
    actor_id TEXT NOT NULL CHECK (length(actor_id) BETWEEN 1 AND 100),
    reported_by_actor_id TEXT,
    reviewed_by_actor_id TEXT,
    closed_by_actor_id TEXT,
    -- No ALLOWANCE, ALLOWANCE_VOID, g0401 or g0501 columns in V3.1.
    resolution_code TEXT CHECK (resolution_code IN (
      'resolved_locally','checked_official','duplicate_report',
      'not_actionable','needs_followup')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    CHECK (
      (kind='UPLOAD_ISSUE' AND issue_code IN (
        'invoice_list_failed','query_failed','remote_not_found',
        'unknown_remote_not_found','reconciliation_failed','ambiguous_match',
        'local_write_failed','upload_failed','duplicate_order_id'
      ))
      OR (kind<>'UPLOAD_ISSUE' AND issue_code IS NULL)
    ),
    CHECK (
      (kind='UPLOAD_ISSUE' AND reported_by_actor_id IS NOT NULL)
      OR (kind<>'UPLOAD_ISSUE' AND reported_by_actor_id IS NULL
        AND reviewed_by_actor_id IS NULL AND closed_by_actor_id IS NULL
        AND resolution_code IS NULL)
    ),
    CHECK(kind<>'UPLOAD_ISSUE' OR status<>'closed'
       OR (reviewed_by_actor_id IS NOT NULL
         AND closed_by_actor_id IS NOT NULL AND resolution_code IS NOT NULL)),
    UNIQUE (workspace_id, environment, kind, reference_hash)
);
CREATE INDEX idx_work_items_workspace_status
    ON work_items (workspace_id, status, updated_at DESC);

-- D1 archived reports cannot be erased or reopened by the application.
CREATE TRIGGER upload_issue_closed_immutable
BEFORE UPDATE ON work_items
WHEN OLD.kind='UPLOAD_ISSUE' AND OLD.status='closed'
BEGIN SELECT RAISE(ABORT,'UPLOAD_ISSUE_ARCHIVED_IMMUTABLE'); END;
CREATE TRIGGER upload_issue_no_delete
BEFORE DELETE ON work_items
WHEN OLD.kind='UPLOAD_ISSUE'
BEGIN SELECT RAISE(ABORT,'UPLOAD_ISSUE_NOT_DELETABLE'); END;

-- V3.1 audit: same-transaction insert/transition events. D1 source draft only.
CREATE TABLE work_item_audit_events (
  event_id INTEGER PRIMARY KEY AUTOINCREMENT,
  work_item_id TEXT NOT NULL REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  event_type TEXT NOT NULL CHECK(event_type IN ('CREATED','TRANSITION')),
  from_status TEXT CHECK(from_status IS NULL OR from_status IN ('open','review','closed')),
  to_status TEXT NOT NULL CHECK(to_status IN ('open','review','closed')),
  revision INTEGER NOT NULL CHECK(revision>0),
  actor_id TEXT NOT NULL CHECK(length(actor_id) BETWEEN 1 AND 100),
  device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  created_at TEXT NOT NULL,
  UNIQUE(work_item_id,revision)
);
CREATE INDEX idx_work_item_audit_workspace
  ON work_item_audit_events(workspace_id,work_item_id,revision);
CREATE TRIGGER work_item_audit_created AFTER INSERT ON work_items
BEGIN
  INSERT INTO work_item_audit_events
    (work_item_id,workspace_id,event_type,from_status,to_status,revision,actor_id,device_id,created_at)
  VALUES
    (NEW.work_item_id,NEW.workspace_id,'CREATED',NULL,NEW.status,NEW.revision,
     NEW.actor_id,NEW.updated_by_device_id,NEW.created_at);
END;
CREATE TRIGGER work_item_audit_transition AFTER UPDATE ON work_items
BEGIN
  INSERT INTO work_item_audit_events
    (work_item_id,workspace_id,event_type,from_status,to_status,revision,actor_id,device_id,created_at)
  VALUES
    (NEW.work_item_id,NEW.workspace_id,'TRANSITION',OLD.status,NEW.status,NEW.revision,
     NEW.actor_id,NEW.updated_by_device_id,NEW.updated_at);
END;
CREATE TRIGGER work_item_status_guard BEFORE UPDATE ON work_items
WHEN NEW.revision<>OLD.revision+1 OR
     NOT ((OLD.status='open' AND NEW.status='review') OR
          (OLD.status='review' AND NEW.status='closed')) OR
     NEW.work_item_id<>OLD.work_item_id OR NEW.workspace_id<>OLD.workspace_id OR
     NEW.environment<>OLD.environment OR NEW.kind<>OLD.kind OR
     NEW.reference_hash<>OLD.reference_hash OR NEW.created_at<>OLD.created_at
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_TRANSITION_INVALID'); END;
CREATE TRIGGER work_item_delete_guard BEFORE DELETE ON work_items
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_NOT_DELETABLE'); END;
CREATE TRIGGER work_item_audit_no_update BEFORE UPDATE ON work_item_audit_events
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER work_item_audit_no_delete BEFORE DELETE ON work_item_audit_events
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_AUDIT_APPEND_ONLY'); END;
