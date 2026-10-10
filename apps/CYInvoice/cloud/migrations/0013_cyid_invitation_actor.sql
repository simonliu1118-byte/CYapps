PRAGMA defer_foreign_keys = ON;

-- A CYID actor is an external stable reference, never a Built-in credential row.
-- Preserve the Built-in FK and every historical invitation; exactly one actor source.
CREATE TABLE device_invitations_schema13 (
    invitation_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    code_hash TEXT NOT NULL UNIQUE,
    issued_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    issued_by_employee_id TEXT REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    issued_by_cyid_employee_id TEXT,
    delivery_state TEXT NOT NULL CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    expires_at TEXT NOT NULL,
    sent_at TEXT,
    revoked_at TEXT,
    consumed_at TEXT,
    consumed_by_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL,
    CHECK ((issued_by_employee_id IS NOT NULL AND issued_by_cyid_employee_id IS NULL)
        OR (issued_by_employee_id IS NULL AND issued_by_cyid_employee_id IS NOT NULL AND length(issued_by_cyid_employee_id) BETWEEN 1 AND 100))
);
INSERT INTO device_invitations_schema13 (
    invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    delivery_state, expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id, created_at
)
SELECT invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
       delivery_state, expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id, created_at
FROM device_invitations;
DROP TABLE device_invitations;
ALTER TABLE device_invitations_schema13 RENAME TO device_invitations;
CREATE INDEX idx_device_invitations_workspace_created ON device_invitations (workspace_id, created_at DESC);
PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
