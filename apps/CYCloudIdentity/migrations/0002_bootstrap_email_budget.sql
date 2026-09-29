PRAGMA foreign_keys = ON;

CREATE TABLE bootstrap_requests (
    bootstrap_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL UNIQUE,
    workspace_code TEXT NOT NULL,
    workspace_display_name TEXT NOT NULL,
    employee_id TEXT NOT NULL UNIQUE,
    employee_no TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    email_normalized TEXT NOT NULL,
    credential_algorithm TEXT NOT NULL,
    credential_verifier TEXT NOT NULL,
    applications_json TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    consumed_at TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (length(bootstrap_id) BETWEEN 5 AND 80),
    CHECK (length(workspace_id) BETWEEN 5 AND 80),
    CHECK (length(workspace_code) BETWEEN 4 AND 32),
    CHECK (workspace_code = upper(workspace_code)),
    CHECK (workspace_code NOT GLOB '*[^A-Z0-9_-]*'),
    CHECK (length(trim(workspace_display_name)) BETWEEN 1 AND 120),
    CHECK (length(employee_id) BETWEEN 5 AND 80),
    CHECK (length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*'),
    CHECK (length(trim(employee_name)) BETWEEN 1 AND 120),
    CHECK (
      length(trim(email_normalized)) BETWEEN 3 AND 320
      AND instr(email_normalized, '@') > 1
    ),
    CHECK (length(trim(credential_algorithm)) BETWEEN 3 AND 64),
    CHECK (length(credential_verifier) BETWEEN 80 AND 512),
    CHECK (length(applications_json) BETWEEN 2 AND 12000),
    CHECK (expires_at > created_at)
);

CREATE INDEX idx_bootstrap_requests_active
    ON bootstrap_requests(consumed_at, expires_at, created_at DESC);

CREATE TABLE email_delivery_budget (
    usage_date_utc TEXT PRIMARY KEY,
    reserved_count INTEGER NOT NULL DEFAULT 0 CHECK (reserved_count >= 0),
    sent_count INTEGER NOT NULL DEFAULT 0 CHECK (sent_count >= 0),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (length(usage_date_utc) = 10),
    CHECK (reserved_count <= 100000),
    CHECK (sent_count <= 100000)
);
