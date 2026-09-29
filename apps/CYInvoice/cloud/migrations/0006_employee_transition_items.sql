PRAGMA foreign_keys = ON;

-- One row per pre-existing Local Employee while a Device is converting from
-- Local authority to the Workspace's single Cloud Employee authority.
-- This table is conversion workflow state only; after cutover Cloud Employee
-- remains the authority and the Windows copy becomes a synchronized cache.
CREATE TABLE IF NOT EXISTS employee_transition_items (
    transition_item_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    local_employee_no TEXT NOT NULL,
    local_name TEXT NOT NULL,
    local_email_normalized TEXT NOT NULL,
    local_role TEXT NOT NULL CHECK (local_role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
    suggested_cloud_role TEXT NOT NULL CHECK (suggested_cloud_role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
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

CREATE INDEX IF NOT EXISTS idx_employee_transition_device_state
    ON employee_transition_items (device_id, state, local_employee_no);

CREATE INDEX IF NOT EXISTS idx_employee_transition_workspace_state
    ON employee_transition_items (workspace_id, state, created_at);

CREATE INDEX IF NOT EXISTS idx_employee_transition_matched_employee
    ON employee_transition_items (matched_employee_id)
    WHERE matched_employee_id IS NOT NULL;
