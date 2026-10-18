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
