PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS web_sessions (
    session_hash TEXT PRIMARY KEY CHECK(length(session_hash) = 64),
    employee_id TEXT NOT NULL,
    employee_no TEXT NOT NULL CHECK(length(employee_no) = 4),
    employee_name TEXT NOT NULL,
    role TEXT NOT NULL CHECK(role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
    credential_version INTEGER NOT NULL DEFAULT 0,
    employee_revision INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_web_sessions_expires_at ON web_sessions(expires_at);
CREATE INDEX IF NOT EXISTS idx_web_sessions_employee_id ON web_sessions(employee_id);

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '2');
