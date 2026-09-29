PRAGMA foreign_keys = ON;

ALTER TABLE devices ADD COLUMN employee_onboarding_closed_at TEXT;

CREATE TABLE IF NOT EXISTS cloud_employees (
    employee_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    employee_no TEXT NOT NULL,
    name TEXT NOT NULL,
    email_normalized TEXT NOT NULL,
    email_verified_at TEXT,
    role TEXT NOT NULL CHECK (role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    source_device_id TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (source_device_id) REFERENCES devices(device_id) ON DELETE SET NULL,
    CHECK(length(employee_id) BETWEEN 5 AND 80),
    CHECK(length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*'),
    CHECK(length(trim(name)) BETWEEN 1 AND 120),
    CHECK(length(trim(email_normalized)) BETWEEN 3 AND 320),
    CHECK(role <> 'SUPER_ADMIN' OR enabled = 1)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_cloud_employees_workspace_employee_no
    ON cloud_employees (workspace_id, employee_no);

CREATE UNIQUE INDEX IF NOT EXISTS idx_cloud_employees_workspace_email
    ON cloud_employees (workspace_id, email_normalized);

CREATE UNIQUE INDEX IF NOT EXISTS idx_cloud_employees_single_super_admin
    ON cloud_employees (workspace_id)
    WHERE role = 'SUPER_ADMIN';

CREATE INDEX IF NOT EXISTS idx_cloud_employees_workspace_role
    ON cloud_employees (workspace_id, role, enabled);

CREATE TABLE IF NOT EXISTS device_employee_links (
    device_id TEXT NOT NULL,
    employee_id TEXT NOT NULL,
    local_employee_no TEXT NOT NULL,
    linked_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    PRIMARY KEY (device_id, local_employee_no),
    UNIQUE (device_id, employee_id),
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    FOREIGN KEY (employee_id) REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    CHECK(length(local_employee_no) = 4 AND local_employee_no NOT GLOB '*[^0-9]*')
);

CREATE INDEX IF NOT EXISTS idx_device_employee_links_employee
    ON device_employee_links (employee_id);

CREATE TABLE IF NOT EXISTS employee_link_candidates (
    candidate_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    local_employee_no TEXT NOT NULL,
    local_name TEXT NOT NULL,
    local_email_normalized TEXT NOT NULL,
    state TEXT NOT NULL DEFAULT 'pending'
        CHECK (state IN ('pending', 'linked', 'dismissed')),
    resolved_employee_id TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    resolved_at TEXT,
    UNIQUE (device_id, local_employee_no),
    FOREIGN KEY (workspace_id) REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    FOREIGN KEY (device_id) REFERENCES devices(device_id) ON DELETE RESTRICT,
    FOREIGN KEY (resolved_employee_id) REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    CHECK(length(local_employee_no) = 4 AND local_employee_no NOT GLOB '*[^0-9]*'),
    CHECK(length(trim(local_name)) BETWEEN 1 AND 120),
    CHECK(length(trim(local_email_normalized)) BETWEEN 3 AND 320)
);

CREATE INDEX IF NOT EXISTS idx_employee_link_candidates_workspace_state
    ON employee_link_candidates (workspace_id, state, created_at);
