-- V3.1 non-allowance exceptional Work Item schema. DRAFT ONLY.
-- Deliberately outside migrations/: original CYID-only business D1 is unchanged.
-- Not a full invoice ledger or evidence of official AMEGO status.
CREATE TABLE work_items (
  work_item_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
  opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  updated_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
  environment TEXT NOT NULL CHECK(environment IN ('production','test')),
  kind TEXT NOT NULL CHECK(kind IN ('VOID','UPLOAD_ISSUE')),
  issue_code TEXT,
  reference_hash TEXT NOT NULL CHECK(
    length(reference_hash)=64 AND reference_hash NOT GLOB '*[^0-9a-f]*'),
  status TEXT NOT NULL DEFAULT 'open' CHECK(status IN ('open','review','closed')),
  revision INTEGER NOT NULL DEFAULT 1 CHECK(revision>0),
  actor_id TEXT NOT NULL CHECK(length(actor_id) BETWEEN 1 AND 100),
  reported_by_actor_id TEXT,
  reviewed_by_actor_id TEXT,
  closed_by_actor_id TEXT,
  resolution_code TEXT CHECK(resolution_code IN (
    'resolved_locally','checked_official','duplicate_report',
    'not_actionable','needs_followup')),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK(
    (kind='UPLOAD_ISSUE' AND issue_code IN (
      'invoice_list_failed','query_failed','remote_not_found',
      'unknown_remote_not_found','reconciliation_failed','ambiguous_match',
      'local_write_failed','upload_failed','duplicate_order_id'
    ) AND reported_by_actor_id IS NOT NULL)
    OR (kind='VOID' AND issue_code IS NULL AND reported_by_actor_id IS NULL
        AND resolution_code IS NULL)
  ),
  CHECK(
    (status='open' AND reviewed_by_actor_id IS NULL AND closed_by_actor_id IS NULL)
    OR (status='review' AND reviewed_by_actor_id IS NOT NULL
        AND closed_by_actor_id IS NULL)
    OR (status='closed' AND reviewed_by_actor_id IS NOT NULL
        AND closed_by_actor_id IS NOT NULL
        AND (kind='VOID' OR resolution_code IS NOT NULL))
  ),
  UNIQUE(workspace_id,environment,kind,reference_hash)
);
CREATE INDEX idx_work_items_workspace_status
  ON work_items(workspace_id,status,updated_at DESC);
CREATE INDEX idx_work_items_workspace_created
  ON work_items(workspace_id,created_at DESC,work_item_id DESC);

-- Closed records cannot change; no application deletion of audit cases.
CREATE TRIGGER work_item_closed_immutable
BEFORE UPDATE ON work_items
WHEN OLD.status='closed'
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_ARCHIVED_IMMUTABLE'); END;
CREATE TRIGGER work_item_no_delete
BEFORE DELETE ON work_items
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_NOT_DELETABLE'); END;

-- Append-only non-allowance manual workflow history, DRAFT for the eventual
-- original D1 schema16 review. Creation/transition+event share one SQLite
-- transaction, so a lost HTTP response cannot leave partial audit history.
-- Never store invoice/order/buyer data or provider confirmation here.
CREATE TABLE work_item_audit_events (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    work_item_id TEXT NOT NULL REFERENCES work_items(work_item_id) ON DELETE RESTRICT,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    revision INTEGER NOT NULL CHECK(revision >= 1),
    event_type TEXT NOT NULL CHECK(event_type IN ('CREATED','TRANSITION')),
    from_status TEXT CHECK(from_status IS NULL OR from_status IN ('open','review')),
    to_status TEXT NOT NULL CHECK(to_status IN ('open','review','closed')),
    actor_id TEXT NOT NULL CHECK(length(actor_id) BETWEEN 1 AND 100),
    device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    resolution_code TEXT,
    occurred_at TEXT NOT NULL,
    UNIQUE(work_item_id,revision)
);
CREATE INDEX idx_work_item_audit_by_workspace
    ON work_item_audit_events(workspace_id,work_item_id,revision);
CREATE TRIGGER work_item_audit_created
AFTER INSERT ON work_items
BEGIN
    INSERT INTO work_item_audit_events
        (work_item_id,workspace_id,revision,event_type,from_status,to_status,
         actor_id,device_id,resolution_code,occurred_at)
    VALUES
        (NEW.work_item_id,NEW.workspace_id,NEW.revision,'CREATED',NULL,NEW.status,
         NEW.actor_id,NEW.updated_by_device_id,NULL,NEW.created_at);
END;
-- Protect manual workflow invariants even if a future server endpoint makes
-- a direct SQL UPDATE without going through the canonical CAS function.
CREATE TRIGGER work_item_transition_guard
BEFORE UPDATE ON work_items
WHEN OLD.status <> 'closed' AND (
    NEW.revision <> OLD.revision + 1 OR
    NOT ((OLD.status='open' AND NEW.status='review') OR
         (OLD.status='review' AND NEW.status='closed')) OR
    NEW.work_item_id <> OLD.work_item_id OR
    NEW.workspace_id <> OLD.workspace_id OR
    NEW.opened_by_device_id <> OLD.opened_by_device_id OR
    NEW.environment <> OLD.environment OR NEW.kind <> OLD.kind OR
    NEW.issue_code IS NOT OLD.issue_code OR
    NEW.reference_hash <> OLD.reference_hash OR
    NEW.reported_by_actor_id IS NOT OLD.reported_by_actor_id OR
    NEW.created_at <> OLD.created_at OR
    (OLD.status='open' AND (NEW.reviewed_by_actor_id IS NOT NEW.actor_id OR
      NEW.closed_by_actor_id IS NOT NULL)) OR
    (OLD.status='review' AND (NEW.reviewed_by_actor_id IS NOT OLD.reviewed_by_actor_id OR
      NEW.closed_by_actor_id IS NOT NEW.actor_id))
)
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_TRANSITION_INVALID'); END;
CREATE TRIGGER work_item_audit_transition
AFTER UPDATE ON work_items
BEGIN
    INSERT INTO work_item_audit_events
        (work_item_id,workspace_id,revision,event_type,from_status,to_status,
         actor_id,device_id,resolution_code,occurred_at)
    VALUES
        (NEW.work_item_id,NEW.workspace_id,NEW.revision,'TRANSITION',OLD.status,NEW.status,
         NEW.actor_id,NEW.updated_by_device_id,NEW.resolution_code,NEW.updated_at);
END;
CREATE TRIGGER work_item_audit_append_only_update
BEFORE UPDATE ON work_item_audit_events
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER work_item_audit_append_only_delete
BEFORE DELETE ON work_item_audit_events
BEGIN SELECT RAISE(ABORT,'WORK_ITEM_AUDIT_APPEND_ONLY'); END;
