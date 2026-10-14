-- V3 Work Item metadata only: not an invoice ledger and not official AMEGO status.
-- Pending in Draft: never apply to the original D1 without separate approval.
CREATE TABLE work_items (
    work_item_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    updated_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    environment TEXT NOT NULL CHECK (environment IN ('production', 'test')),
    kind TEXT NOT NULL CHECK (kind IN ('VOID', 'ALLOWANCE', 'ALLOWANCE_VOID')),
    reference_hash TEXT NOT NULL CHECK (
      length(reference_hash) = 64 AND reference_hash NOT GLOB '*[^0-9a-f]*'),
    status TEXT NOT NULL DEFAULT 'open'
      CHECK (status IN ('open', 'review', 'closed')),
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision > 0),
    actor_id TEXT NOT NULL CHECK (length(actor_id) BETWEEN 1 AND 100),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (workspace_id, environment, kind, reference_hash)
);
CREATE INDEX idx_work_items_workspace_status
    ON work_items (workspace_id, status, updated_at DESC);
