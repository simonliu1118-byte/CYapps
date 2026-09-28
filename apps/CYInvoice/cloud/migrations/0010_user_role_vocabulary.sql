PRAGMA defer_foreign_keys = ON;
PRAGMA legacy_alter_table = ON;

-- CYInvoice's canonical application roles are SUPER_ADMIN / ADMIN / USER.
-- Rebuild the two role-constrained tables so storage no longer accepts EMPLOYEE.
-- Any pre-release development row that still contains EMPLOYEE is normalized once
-- during this migration; runtime code does not keep an EMPLOYEE compatibility alias.

ALTER TABLE cloud_employees RENAME TO cloud_employees_schema9;

CREATE TABLE cloud_employees (
    employee_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    employee_no TEXT NOT NULL,
    name TEXT NOT NULL,
    email_normalized TEXT NOT NULL,
    email_verified_at TEXT,
    role TEXT NOT NULL CHECK (role IN ('SUPER_ADMIN', 'ADMIN', 'USER')),
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    source_device_id TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    credential_verifier TEXT,
    credential_algorithm TEXT,
    credential_version INTEGER NOT NULL DEFAULT 0 CHECK (credential_version >= 0),
    credential_updated_at TEXT,
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (source_device_id) REFERENCES devices(device_id) ON DELETE SET NULL,
    CHECK(length(employee_id) BETWEEN 5 AND 80),
    CHECK(length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*'),
    CHECK(length(trim(name)) BETWEEN 1 AND 120),
    CHECK(length(trim(email_normalized)) BETWEEN 3 AND 320),
    CHECK(role <> 'SUPER_ADMIN' OR enabled = 1)
);

INSERT INTO cloud_employees (
    employee_id, workspace_id, employee_no, name, email_normalized,
    email_verified_at, role, enabled, source_device_id, created_at, updated_at,
    credential_verifier, credential_algorithm, credential_version,
    credential_updated_at, revision
)
SELECT employee_id,
       workspace_id,
       employee_no,
       name,
       email_normalized,
       email_verified_at,
       CASE role WHEN 'EMPLOYEE' THEN 'USER' ELSE role END,
       enabled,
       source_device_id,
       created_at,
       updated_at,
       credential_verifier,
       credential_algorithm,
       credential_version,
       credential_updated_at,
       revision
  FROM cloud_employees_schema9;

DROP TABLE cloud_employees_schema9;

CREATE UNIQUE INDEX idx_cloud_employees_workspace_employee_no
    ON cloud_employees (workspace_id, employee_no);
CREATE UNIQUE INDEX idx_cloud_employees_workspace_email
    ON cloud_employees (workspace_id, email_normalized);
CREATE UNIQUE INDEX idx_cloud_employees_single_super_admin
    ON cloud_employees (workspace_id)
    WHERE role = 'SUPER_ADMIN';
CREATE INDEX idx_cloud_employees_workspace_role
    ON cloud_employees (workspace_id, role, enabled);
CREATE INDEX idx_cloud_employees_workspace_revision
    ON cloud_employees (workspace_id, revision);

ALTER TABLE employee_transition_items RENAME TO employee_transition_items_schema9;

CREATE TABLE employee_transition_items (
    transition_item_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    local_employee_no TEXT NOT NULL,
    local_name TEXT NOT NULL,
    local_email_normalized TEXT NOT NULL,
    local_role TEXT NOT NULL CHECK (local_role IN ('SUPER_ADMIN', 'ADMIN', 'USER')),
    suggested_cloud_role TEXT NOT NULL CHECK (suggested_cloud_role IN ('SUPER_ADMIN', 'ADMIN', 'USER')),
    state TEXT NOT NULL CHECK (state IN (
        'bootstrap_owner_pending',
        'new_email_pending',
        'matched_existing',
        'credential_pending',
        'conflict',
        'ready'
    )),
    match_kind TEXT NOT NULL CHECK (match_kind IN (
        'none',
        'same_employee',
        'employee_no_only',
        'email_only',
        'split',
        'bootstrap_owner'
    )),
    matched_employee_id TEXT,
    employee_no_match_id TEXT,
    email_match_id TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    resolved_at TEXT,
    local_enabled INTEGER NOT NULL DEFAULT 1 CHECK (local_enabled IN (0, 1)),
    UNIQUE (device_id, local_employee_no),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    FOREIGN KEY (matched_employee_id) REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    FOREIGN KEY (employee_no_match_id) REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    FOREIGN KEY (email_match_id) REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    CHECK(length(local_employee_no) = 4 AND local_employee_no NOT GLOB '*[^0-9]*'),
    CHECK(length(trim(local_name)) BETWEEN 1 AND 120),
    CHECK(length(trim(local_email_normalized)) BETWEEN 3 AND 320)
);

INSERT INTO employee_transition_items (
    transition_item_id, workspace_id, device_id, local_employee_no, local_name,
    local_email_normalized, local_role, suggested_cloud_role, state, match_kind,
    matched_employee_id, employee_no_match_id, email_match_id,
    created_at, updated_at, resolved_at, local_enabled
)
SELECT transition_item_id,
       workspace_id,
       device_id,
       local_employee_no,
       local_name,
       local_email_normalized,
       CASE local_role WHEN 'EMPLOYEE' THEN 'USER' ELSE local_role END,
       CASE suggested_cloud_role WHEN 'EMPLOYEE' THEN 'USER' ELSE suggested_cloud_role END,
       state,
       match_kind,
       matched_employee_id,
       employee_no_match_id,
       email_match_id,
       created_at,
       updated_at,
       resolved_at,
       local_enabled
  FROM employee_transition_items_schema9;

DROP TABLE employee_transition_items_schema9;

CREATE INDEX idx_employee_transition_device_state
    ON employee_transition_items (device_id, state, local_employee_no);
CREATE INDEX idx_employee_transition_workspace_state
    ON employee_transition_items (workspace_id, state, created_at);
CREATE INDEX idx_employee_transition_matched_employee
    ON employee_transition_items (matched_employee_id)
    WHERE matched_employee_id IS NOT NULL;

PRAGMA legacy_alter_table = OFF;
PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
