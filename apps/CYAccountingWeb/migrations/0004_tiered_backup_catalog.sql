PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS backup_sets (
    backup_id TEXT PRIMARY KEY,
    app_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    app_version TEXT NOT NULL,
    schema_version INTEGER NOT NULL,
    format TEXT NOT NULL,
    format_version INTEGER NOT NULL,
    data_sha256 TEXT NOT NULL CHECK(length(data_sha256) = 64),
    manifest_sha256 TEXT NOT NULL CHECK(length(manifest_sha256) = 64),
    package_sha256 TEXT NOT NULL CHECK(length(package_sha256) = 64),
    data_byte_length INTEGER NOT NULL CHECK(data_byte_length >= 0),
    manifest_byte_length INTEGER NOT NULL CHECK(manifest_byte_length >= 0),
    total_byte_length INTEGER NOT NULL CHECK(total_byte_length >= 0),
    record_count INTEGER NOT NULL CHECK(record_count >= 0),
    trigger_kind TEXT NOT NULL CHECK(trigger_kind IN ('scheduled', 'manual'))
);

CREATE INDEX IF NOT EXISTS idx_backup_sets_created_at
ON backup_sets(created_at DESC);

CREATE TABLE IF NOT EXISTS backup_copies (
    backup_id TEXT NOT NULL,
    provider TEXT NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('pending', 'success', 'failed')),
    started_at TEXT NOT NULL,
    completed_at TEXT,
    verified_at TEXT,
    storage_prefix TEXT NOT NULL DEFAULT '',
    version_token TEXT NOT NULL DEFAULT '',
    last_error TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (backup_id, provider),
    FOREIGN KEY (backup_id) REFERENCES backup_sets(backup_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_backup_copies_provider_status
ON backup_copies(provider, status, completed_at DESC);

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '4');
