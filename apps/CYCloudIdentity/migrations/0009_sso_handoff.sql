-- CYCloud Identity 0.4.0 — single sign-on portal handoff (SSO_PORTAL_DESIGN.md phase 1).
--
-- applications.sso_return_url: the one registered HTTPS callback for an App. The
-- provider stores only the canonical form produced by the registration endpoint
-- and compares it by exact string equality. NULL means the App does not use SSO.
--
-- sso_handoffs: one-time codes issued by the core account portal for one target
-- App. Only the SHA-256 digest of the code is stored; the raw code, the portal
-- Session token and the new App Session token never reach this table, Audit or
-- logs. Rows live 60 seconds and are purged by the bounded cleanup that runs on
-- every issue (no Cron, PROJECT_RULES §8).

ALTER TABLE applications ADD COLUMN sso_return_url TEXT
    CHECK (sso_return_url IS NULL
        OR (length(sso_return_url) BETWEEN 12 AND 512
            AND substr(sso_return_url, 1, 8) = 'https://'));

CREATE TABLE sso_handoffs (
    code_hash TEXT PRIMARY KEY CHECK (length(code_hash) = 64),
    workspace_id TEXT NOT NULL,
    employee_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    portal_application_id TEXT NOT NULL,
    issued_session_hash TEXT NOT NULL CHECK (length(issued_session_hash) = 64),
    credential_version INTEGER NOT NULL CHECK (credential_version >= 1),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    used_at TEXT,
    FOREIGN KEY (employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (workspace_id, application_id)
        REFERENCES workspace_applications(workspace_id, application_id) ON DELETE RESTRICT,
    FOREIGN KEY (issued_session_hash)
        REFERENCES identity_sessions(session_hash) ON DELETE CASCADE,
    CHECK (application_id <> portal_application_id),
    CHECK (expires_at > created_at)
);

-- Bounded cleanup scans by expiry; used rows also expire within 60 seconds.
CREATE INDEX idx_sso_handoffs_expiry
    ON sso_handoffs(expires_at);
-- Cascades from Employee / portal Session deletion.
CREATE INDEX idx_sso_handoffs_employee
    ON sso_handoffs(workspace_id, employee_id);
CREATE INDEX idx_sso_handoffs_portal_session
    ON sso_handoffs(issued_session_hash);
