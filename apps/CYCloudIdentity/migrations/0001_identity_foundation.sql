PRAGMA foreign_keys = ON;

CREATE TABLE workspaces (
    workspace_id TEXT PRIMARY KEY,
    workspace_code TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 120),
    status TEXT NOT NULL DEFAULT 'bootstrap' CHECK (status IN ('bootstrap', 'active', 'disabled')),
    super_admin_employee_id TEXT,
    recovery_email_normalized TEXT,
    recovery_email_verified_at TEXT,
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (length(workspace_id) BETWEEN 5 AND 80),
    CHECK (length(workspace_code) BETWEEN 4 AND 32),
    CHECK (workspace_code = upper(workspace_code)),
    CHECK (workspace_code NOT GLOB '*[^A-Z0-9_-]*'),
    CHECK (status <> 'active' OR super_admin_employee_id IS NOT NULL),
    CHECK (
      recovery_email_normalized IS NULL
      OR (
        length(trim(recovery_email_normalized)) BETWEEN 3 AND 320
        AND instr(recovery_email_normalized, '@') > 1
      )
    ),
    FOREIGN KEY (super_admin_employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id)
        ON UPDATE RESTRICT
        ON DELETE RESTRICT
);

CREATE TABLE employees (
    employee_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    employee_no TEXT NOT NULL,
    name TEXT NOT NULL,
    email_normalized TEXT NOT NULL,
    email_verified_at TEXT,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    CHECK (length(employee_id) BETWEEN 5 AND 80),
    CHECK (length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*'),
    CHECK (length(trim(name)) BETWEEN 1 AND 120),
    CHECK (
      length(trim(email_normalized)) BETWEEN 3 AND 320
      AND instr(email_normalized, '@') > 1
    )
);

CREATE UNIQUE INDEX idx_employees_employee_workspace
    ON employees(employee_id, workspace_id);
CREATE UNIQUE INDEX idx_employees_workspace_employee_no
    ON employees(workspace_id, employee_no);
CREATE UNIQUE INDEX idx_employees_workspace_email
    ON employees(workspace_id, email_normalized);
CREATE INDEX idx_employees_workspace_enabled
    ON employees(workspace_id, enabled, employee_no);

CREATE TRIGGER trg_workspace_super_admin_must_be_enabled
BEFORE UPDATE OF super_admin_employee_id, status ON workspaces
WHEN NEW.super_admin_employee_id IS NOT NULL
 AND NOT EXISTS (
    SELECT 1
      FROM employees e
     WHERE e.employee_id = NEW.super_admin_employee_id
       AND e.workspace_id = NEW.workspace_id
       AND e.enabled = 1
 )
BEGIN
    SELECT RAISE(ABORT, 'workspace super admin must be an enabled employee in the same workspace');
END;

CREATE TRIGGER trg_employee_current_super_admin_cannot_disable
BEFORE UPDATE OF enabled ON employees
WHEN OLD.enabled = 1
 AND NEW.enabled = 0
 AND EXISTS (
    SELECT 1
      FROM workspaces w
     WHERE w.workspace_id = OLD.workspace_id
       AND w.super_admin_employee_id = OLD.employee_id
 )
BEGIN
    SELECT RAISE(ABORT, 'current workspace super admin cannot be disabled');
END;

CREATE TRIGGER trg_employee_current_super_admin_cannot_delete
BEFORE DELETE ON employees
WHEN EXISTS (
    SELECT 1
      FROM workspaces w
     WHERE w.workspace_id = OLD.workspace_id
       AND w.super_admin_employee_id = OLD.employee_id
 )
BEGIN
    SELECT RAISE(ABORT, 'current workspace super admin cannot be deleted');
END;

CREATE TABLE identity_groups (
    group_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    group_key TEXT NOT NULL,
    display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 120),
    description TEXT CHECK (description IS NULL OR length(description) <= 500),
    status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'disabled')),
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE CASCADE,
    CHECK (length(group_id) BETWEEN 5 AND 80),
    CHECK (length(group_key) BETWEEN 1 AND 64),
    CHECK (group_key = upper(group_key)),
    CHECK (group_key NOT GLOB '*[^A-Z0-9_-]*')
);

CREATE UNIQUE INDEX idx_identity_groups_group_workspace
    ON identity_groups(group_id, workspace_id);
CREATE UNIQUE INDEX idx_identity_groups_workspace_key
    ON identity_groups(workspace_id, group_key);
CREATE INDEX idx_identity_groups_workspace_status
    ON identity_groups(workspace_id, status, display_name);

CREATE TABLE employee_identity_groups (
    workspace_id TEXT NOT NULL,
    employee_id TEXT NOT NULL,
    group_id TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (workspace_id, employee_id, group_id),
    FOREIGN KEY (employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (group_id, workspace_id)
        REFERENCES identity_groups(group_id, workspace_id) ON DELETE CASCADE
);

CREATE INDEX idx_employee_identity_groups_group
    ON employee_identity_groups(workspace_id, group_id, employee_id);

CREATE TABLE employee_credentials (
    employee_id TEXT PRIMARY KEY,
    algorithm TEXT NOT NULL CHECK (length(trim(algorithm)) BETWEEN 3 AND 64),
    verifier TEXT NOT NULL CHECK (length(verifier) BETWEEN 80 AND 512),
    credential_version INTEGER NOT NULL DEFAULT 1 CHECK (credential_version >= 1),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (employee_id) REFERENCES employees(employee_id) ON DELETE CASCADE
);

CREATE TABLE applications (
    application_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 120),
    status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'disabled')),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (length(application_id) BETWEEN 2 AND 64),
    CHECK (application_id = upper(application_id)),
    CHECK (application_id NOT GLOB '*[^A-Z0-9_-]*')
);

CREATE TABLE workspace_applications (
    workspace_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (workspace_id, application_id),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (application_id) REFERENCES applications(application_id) ON DELETE RESTRICT
);

CREATE INDEX idx_workspace_applications_enabled
    ON workspace_applications(workspace_id, enabled, application_id);

CREATE TABLE employee_application_access (
    workspace_id TEXT NOT NULL,
    employee_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (workspace_id, employee_id, application_id),
    FOREIGN KEY (employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (workspace_id, application_id)
        REFERENCES workspace_applications(workspace_id, application_id) ON DELETE RESTRICT
);

CREATE INDEX idx_employee_application_access_application
    ON employee_application_access(workspace_id, application_id, enabled, employee_id);

CREATE TABLE identity_group_application_access (
    workspace_id TEXT NOT NULL,
    group_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (workspace_id, group_id, application_id),
    FOREIGN KEY (group_id, workspace_id)
        REFERENCES identity_groups(group_id, workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (workspace_id, application_id)
        REFERENCES workspace_applications(workspace_id, application_id) ON DELETE RESTRICT
);

CREATE INDEX idx_identity_group_application_access_application
    ON identity_group_application_access(workspace_id, application_id, enabled, group_id);

CREATE TABLE identity_sessions (
    session_hash TEXT PRIMARY KEY CHECK (length(session_hash) = 64),
    workspace_id TEXT NOT NULL,
    employee_id TEXT NOT NULL,
    application_id TEXT NOT NULL,
    credential_version INTEGER NOT NULL CHECK (credential_version >= 1),
    employee_revision INTEGER NOT NULL CHECK (employee_revision >= 1),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    revoked_at TEXT,
    FOREIGN KEY (employee_id, workspace_id)
        REFERENCES employees(employee_id, workspace_id) ON DELETE CASCADE,
    FOREIGN KEY (workspace_id, application_id)
        REFERENCES workspace_applications(workspace_id, application_id) ON DELETE RESTRICT,
    CHECK (expires_at > created_at)
);

CREATE INDEX idx_identity_sessions_employee_active
    ON identity_sessions(workspace_id, employee_id, application_id, expires_at, revoked_at);
CREATE INDEX idx_identity_sessions_expiry
    ON identity_sessions(expires_at, revoked_at);

CREATE TABLE email_otp_challenges (
    challenge_id TEXT PRIMARY KEY,
    purpose TEXT NOT NULL CHECK (purpose IN (
        'workspace_bootstrap',
        'workspace_recovery',
        'employee_email_verification',
        'employee_password_reset',
        'super_admin_transfer_authorization',
        'recovery_email_change'
    )),
    scope_key TEXT CHECK (scope_key IS NULL OR length(scope_key) BETWEEN 1 AND 240),
    email_normalized TEXT NOT NULL CHECK (
        length(trim(email_normalized)) BETWEEN 3 AND 320
        AND instr(email_normalized, '@') > 1
    ),
    otp_digest TEXT NOT NULL CHECK (length(otp_digest) = 64),
    delivery_state TEXT NOT NULL DEFAULT 'pending'
        CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    max_attempts INTEGER NOT NULL DEFAULT 5 CHECK (max_attempts BETWEEN 1 AND 20),
    expires_at TEXT NOT NULL,
    resend_after TEXT NOT NULL,
    sent_at TEXT,
    consumed_at TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

CREATE INDEX idx_email_otp_email_purpose_created
    ON email_otp_challenges(email_normalized, purpose, created_at DESC);
CREATE INDEX idx_email_otp_purpose_scope_created
    ON email_otp_challenges(purpose, scope_key, created_at DESC);
CREATE INDEX idx_email_otp_expiry
    ON email_otp_challenges(expires_at);
CREATE INDEX idx_email_otp_sent_at
    ON email_otp_challenges(sent_at);

CREATE TABLE identity_audit_events (
    event_id TEXT PRIMARY KEY,
    workspace_id TEXT,
    actor_employee_id TEXT,
    target_employee_id TEXT,
    application_id TEXT,
    event_type TEXT NOT NULL CHECK (length(trim(event_type)) BETWEEN 1 AND 120),
    detail_json TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE SET NULL,
    FOREIGN KEY (actor_employee_id) REFERENCES employees(employee_id) ON DELETE SET NULL,
    FOREIGN KEY (target_employee_id) REFERENCES employees(employee_id) ON DELETE SET NULL,
    FOREIGN KEY (application_id) REFERENCES applications(application_id) ON DELETE SET NULL,
    CHECK (detail_json IS NULL OR length(detail_json) <= 8000)
);

CREATE INDEX idx_identity_audit_workspace_created
    ON identity_audit_events(workspace_id, created_at DESC);
CREATE INDEX idx_identity_audit_actor_created
    ON identity_audit_events(actor_employee_id, created_at DESC);
