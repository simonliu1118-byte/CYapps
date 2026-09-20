PRAGMA foreign_keys = ON;

ALTER TABLE devices ADD COLUMN token_hash TEXT;
ALTER TABLE devices ADD COLUMN token_created_at TEXT;
ALTER TABLE devices ADD COLUMN pairing_id TEXT;
ALTER TABLE devices ADD COLUMN revoked_at TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS idx_devices_token_hash
    ON devices (token_hash)
    WHERE token_hash IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS idx_devices_pairing_id
    ON devices (pairing_id)
    WHERE pairing_id IS NOT NULL;

-- CYInvoice Cloud Phase 1 supports one workspace per development database.
-- The constant-expression unique index enforces that invariant even if two
-- first-workspace bootstrap requests race.
CREATE UNIQUE INDEX IF NOT EXISTS idx_single_workspace
    ON workspaces ((1));

CREATE TABLE IF NOT EXISTS device_pairing_codes (
    pairing_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    code_hash TEXT NOT NULL UNIQUE,
    created_by_device_id TEXT NOT NULL,
    claimed_device_id TEXT,
    expires_at TEXT NOT NULL,
    used_at TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (created_by_device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    FOREIGN KEY (claimed_device_id) REFERENCES devices(device_id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS idx_device_pairing_workspace_expiry
    ON device_pairing_codes (workspace_id, expires_at);
