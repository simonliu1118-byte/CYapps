CREATE TABLE backup_recovery_previews (
  id TEXT PRIMARY KEY,
  backup_id TEXT NOT NULL,
  provider TEXT NOT NULL,
  package_sha256 TEXT NOT NULL,
  context_sha256 TEXT NOT NULL,
  workspace_id TEXT NOT NULL,
  reason TEXT NOT NULL,
  actor_json TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE backup_recovery_runs (
  id TEXT PRIMARY KEY,
  preview_id TEXT NOT NULL UNIQUE,
  backup_id TEXT NOT NULL,
  provider TEXT NOT NULL,
  package_sha256 TEXT NOT NULL,
  reason TEXT NOT NULL,
  actor_json TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE backup_recovery_events (
  sequence INTEGER PRIMARY KEY AUTOINCREMENT,
  id TEXT NOT NULL UNIQUE,
  run_id TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('started','verified','failed')),
  result_json TEXT NOT NULL,
  actor_json TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TRIGGER backup_recovery_previews_no_update BEFORE UPDATE ON backup_recovery_previews BEGIN SELECT RAISE(ABORT,'backup_recovery_previews is append-only'); END;
CREATE TRIGGER backup_recovery_previews_no_delete BEFORE DELETE ON backup_recovery_previews BEGIN SELECT RAISE(ABORT,'backup_recovery_previews is append-only'); END;
CREATE TRIGGER backup_recovery_runs_no_update BEFORE UPDATE ON backup_recovery_runs BEGIN SELECT RAISE(ABORT,'backup_recovery_runs is append-only'); END;
CREATE TRIGGER backup_recovery_runs_no_delete BEFORE DELETE ON backup_recovery_runs BEGIN SELECT RAISE(ABORT,'backup_recovery_runs is append-only'); END;
CREATE TRIGGER backup_recovery_events_no_update BEFORE UPDATE ON backup_recovery_events BEGIN SELECT RAISE(ABORT,'backup_recovery_events is append-only'); END;
CREATE TRIGGER backup_recovery_events_no_delete BEFORE DELETE ON backup_recovery_events BEGIN SELECT RAISE(ABORT,'backup_recovery_events is append-only'); END;
INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','10');
