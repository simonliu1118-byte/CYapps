-- CYID is the sole Session authority. No accounting or audit table is dropped.
DROP TABLE IF EXISTS web_sessions;

-- Derived operational state / lease, deliberately not portable accounting data
-- or recovery evidence. A restored workspace rechecks its own provider copies.
CREATE TABLE backup_health_state (
  id INTEGER PRIMARY KEY CHECK(id = 1),
  status TEXT NOT NULL CHECK(status IN ('unknown','healthy','failed')),
  checked_at TEXT,
  details_json TEXT NOT NULL DEFAULT '[]',
  incident_id TEXT,
  email_sent_at TEXT,
  email_error_code TEXT NOT NULL DEFAULT '',
  lease_token TEXT NOT NULL DEFAULT '',
  lease_until TEXT NOT NULL DEFAULT ''
);
INSERT INTO backup_health_state(id,status) VALUES(1,'unknown');
INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','11');
