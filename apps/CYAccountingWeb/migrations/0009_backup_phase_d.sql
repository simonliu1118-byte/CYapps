ALTER TABLE backup_sets ADD COLUMN policy_phase TEXT NOT NULL DEFAULT 'phase_c'
    CHECK(policy_phase IN ('phase_c', 'phase_d'));
ALTER TABLE backup_sets ADD COLUMN gcs_expected INTEGER NOT NULL DEFAULT 1
    CHECK(gcs_expected IN (0, 1));

CREATE TABLE backup_restore_checks (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    id TEXT NOT NULL UNIQUE,
    attempt_id TEXT NOT NULL,
    backup_id TEXT NOT NULL REFERENCES backup_sets(backup_id),
    provider TEXT NOT NULL,
    package_sha256 TEXT NOT NULL,
    source_schema_version INTEGER NOT NULL,
    target_schema_version INTEGER NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
    result_json TEXT NOT NULL,
    actor_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX idx_backup_restore_checks_created ON backup_restore_checks(sequence DESC);

CREATE TABLE backup_policy_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    target_phase TEXT NOT NULL CHECK(target_phase = 'phase_d'),
    r2_check_id TEXT NOT NULL REFERENCES backup_restore_checks(id),
    gcs_check_id TEXT NOT NULL REFERENCES backup_restore_checks(id),
    acceptance_json TEXT NOT NULL,
    actor_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TRIGGER backup_restore_checks_no_update BEFORE UPDATE ON backup_restore_checks
BEGIN SELECT RAISE(ABORT, 'backup_restore_checks is append-only'); END;
CREATE TRIGGER backup_restore_checks_no_delete BEFORE DELETE ON backup_restore_checks
BEGIN SELECT RAISE(ABORT, 'backup_restore_checks is append-only'); END;
CREATE TRIGGER backup_policy_events_no_update BEFORE UPDATE ON backup_policy_events
BEGIN SELECT RAISE(ABORT, 'backup_policy_events is append-only'); END;
CREATE TRIGGER backup_policy_events_no_delete BEFORE DELETE ON backup_policy_events
BEGIN SELECT RAISE(ABORT, 'backup_policy_events is append-only'); END;

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '9');
