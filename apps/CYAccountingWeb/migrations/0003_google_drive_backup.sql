PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS backup_integrations (
    provider TEXT PRIMARY KEY,
    encrypted_refresh_token TEXT NOT NULL,
    folder_id TEXT NOT NULL DEFAULT '',
    account_email TEXT NOT NULL DEFAULT '',
    account_name TEXT NOT NULL DEFAULT '',
    connected_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS backup_oauth_states (
    state_hash TEXT PRIMARY KEY CHECK(length(state_hash) = 64),
    employee_no TEXT NOT NULL CHECK(length(employee_no) = 4),
    redirect_uri TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_backup_oauth_states_expires_at
ON backup_oauth_states(expires_at);

CREATE TABLE IF NOT EXISTS backup_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider TEXT NOT NULL,
    trigger_kind TEXT NOT NULL CHECK(trigger_kind IN ('scheduled', 'manual')),
    status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
    started_at TEXT NOT NULL,
    completed_at TEXT NOT NULL,
    file_id TEXT NOT NULL DEFAULT '',
    file_name TEXT NOT NULL DEFAULT '',
    data_sha256 TEXT NOT NULL DEFAULT '',
    file_sha256 TEXT NOT NULL DEFAULT '',
    row_count INTEGER NOT NULL DEFAULT 0,
    byte_size INTEGER NOT NULL DEFAULT 0,
    error_message TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_backup_runs_started_at
ON backup_runs(started_at DESC);

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '3');
