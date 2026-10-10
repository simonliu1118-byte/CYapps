CREATE TABLE excel_import_runs (
    id TEXT PRIMARY KEY,
    start_date TEXT NOT NULL,
    end_exclusive TEXT NOT NULL,
    reason TEXT NOT NULL,
    source_sha256 TEXT NOT NULL,
    backup_id TEXT NOT NULL,
    deleted_count INTEGER NOT NULL,
    inserted_count INTEGER NOT NULL,
    actor_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TRIGGER excel_import_runs_no_update BEFORE UPDATE ON excel_import_runs
BEGIN SELECT RAISE(ABORT, 'excel_import_runs is append-only'); END;
CREATE TRIGGER excel_import_runs_no_delete BEFORE DELETE ON excel_import_runs
BEGIN SELECT RAISE(ABORT, 'excel_import_runs is append-only'); END;

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '8');
