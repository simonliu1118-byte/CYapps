-- V3 Work Item metadata only: not an invoice ledger and not official AMEGO status.
-- Pending in Draft: NOT in canonical migrations; do not apply to original D1.\n-- Move to migrations only after the specific Work Item D1 plan is approved.
CREATE TABLE work_items (
    work_item_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    updated_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    environment TEXT NOT NULL CHECK (environment IN ('production', 'test')),
    kind TEXT NOT NULL CHECK (kind IN ('VOID', 'ALLOWANCE', 'ALLOWANCE_VOID', 'UPLOAD_ISSUE')),
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
    -- Applicant withdrawal closes only the unapproved ALLOWANCE work item.
    -- Immutable audit metadata remains even though the request is not issued.
    withdrawn_by_actor_id TEXT,
    withdrawn_at TEXT,
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
    CHECK((withdrawn_by_actor_id IS NULL AND withdrawn_at IS NULL)
       OR (kind='ALLOWANCE' AND status='closed'
           AND withdrawn_by_actor_id IS NOT NULL
           AND withdrawn_at IS NOT NULL)),
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

-- A withdrawn allowance is a permanent audit event; it may not reopen,
-- change actor, or lose the original withdrawal timestamp.
CREATE TRIGGER allowance_withdrawn_immutable
BEFORE UPDATE ON work_items
WHEN OLD.kind='ALLOWANCE' AND OLD.withdrawn_at IS NOT NULL
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_WITHDRAWAL_IMMUTABLE'); END;
CREATE TRIGGER allowance_withdrawn_no_delete
BEFORE DELETE ON work_items
WHEN OLD.kind='ALLOWANCE' AND OLD.withdrawn_at IS NOT NULL
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_WITHDRAWAL_NOT_DELETABLE'); END;
