PRAGMA foreign_keys = ON;

CREATE TABLE identity_security_policies (
    workspace_id TEXT PRIMARY KEY,
    otp_resend_cooldown_seconds INTEGER NOT NULL DEFAULT 60
        CHECK (otp_resend_cooldown_seconds BETWEEN 30 AND 600),
    otp_max_attempts INTEGER NOT NULL DEFAULT 5
        CHECK (otp_max_attempts BETWEEN 3 AND 10),
    otp_max_sent_per_email_purpose_hour INTEGER NOT NULL DEFAULT 5
        CHECK (otp_max_sent_per_email_purpose_hour BETWEEN 1 AND 20),
    email_daily_limit INTEGER NOT NULL DEFAULT 100
        CHECK (email_daily_limit BETWEEN 1 AND 10000),
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    updated_by_employee_id TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (updated_by_employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id)
        ON UPDATE RESTRICT
        ON DELETE SET NULL
);

CREATE TABLE workspace_email_delivery_budget (
    workspace_id TEXT NOT NULL,
    usage_date_utc TEXT NOT NULL,
    reserved_count INTEGER NOT NULL DEFAULT 0 CHECK (reserved_count >= 0),
    sent_count INTEGER NOT NULL DEFAULT 0 CHECK (sent_count >= 0),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (workspace_id, usage_date_utc),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE CASCADE,
    CHECK (length(usage_date_utc) = 10)
);

CREATE INDEX idx_workspace_email_budget_date
    ON workspace_email_delivery_budget(usage_date_utc, workspace_id);
