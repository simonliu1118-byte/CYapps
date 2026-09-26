PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS legacy_migration_runs (
    run_id TEXT PRIMARY KEY,
    source_schema_version INTEGER NOT NULL CHECK(source_schema_version IN (1, 2)),
    source_file_name TEXT NOT NULL,
    source_file_size INTEGER NOT NULL CHECK(source_file_size >= 0),
    source_file_sha256 TEXT NOT NULL CHECK(length(source_file_sha256) = 64),
    structure_sha256 TEXT NOT NULL CHECK(length(structure_sha256) = 64),
    dataset_sha256 TEXT NOT NULL CHECK(length(dataset_sha256) = 64),
    expected_accounts INTEGER NOT NULL CHECK(expected_accounts >= 0),
    expected_groups INTEGER NOT NULL CHECK(expected_groups >= 0),
    expected_categories INTEGER NOT NULL CHECK(expected_categories >= 0),
    expected_transactions INTEGER NOT NULL CHECK(expected_transactions >= 0),
    expected_opening_balances INTEGER NOT NULL CHECK(expected_opening_balances >= 0),
    imported_transactions INTEGER NOT NULL DEFAULT 0 CHECK(imported_transactions >= 0),
    imported_opening_balances INTEGER NOT NULL DEFAULT 0 CHECK(imported_opening_balances >= 0),
    status TEXT NOT NULL CHECK(status IN ('importing', 'completed', 'aborted', 'failed')),
    employee_id TEXT NOT NULL,
    employee_no TEXT NOT NULL,
    employee_name TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    completed_at TEXT,
    last_error TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_legacy_migration_runs_status_created
ON legacy_migration_runs(status, created_at DESC);

CREATE TABLE IF NOT EXISTS legacy_migration_chunks (
    run_id TEXT NOT NULL,
    chunk_kind TEXT NOT NULL CHECK(chunk_kind IN ('transactions', 'opening_balances')),
    chunk_index INTEGER NOT NULL CHECK(chunk_index >= 0),
    expected_rows INTEGER NOT NULL CHECK(expected_rows >= 0),
    expected_sha256 TEXT NOT NULL CHECK(length(expected_sha256) = 64),
    status TEXT NOT NULL DEFAULT 'pending' CHECK(status IN ('pending', 'imported')),
    imported_at TEXT,
    PRIMARY KEY(run_id, chunk_kind, chunk_index),
    FOREIGN KEY(run_id) REFERENCES legacy_migration_runs(run_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_legacy_migration_chunks_run_status
ON legacy_migration_chunks(run_id, status, chunk_kind, chunk_index);

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '5');
