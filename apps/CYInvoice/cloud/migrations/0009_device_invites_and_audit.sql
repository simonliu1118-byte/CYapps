PRAGMA foreign_keys = ON;

ALTER TABLE devices ADD COLUMN invitation_id TEXT;
CREATE UNIQUE INDEX idx_devices_invitation_id ON devices (invitation_id) WHERE invitation_id IS NOT NULL;

CREATE TABLE device_invitations (
    invitation_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    code_hash TEXT NOT NULL UNIQUE,
    issued_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    issued_by_employee_id TEXT NOT NULL REFERENCES cloud_employees(employee_id) ON DELETE RESTRICT,
    delivery_state TEXT NOT NULL CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    expires_at TEXT NOT NULL,
    sent_at TEXT,
    revoked_at TEXT,
    consumed_at TEXT,
    consumed_by_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL
);

CREATE INDEX idx_device_invitations_workspace_created
    ON device_invitations (workspace_id, created_at DESC);

CREATE TABLE security_audit_events (
    event_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'pairing_email', 'pairing_issued', 'pairing_verified', 'pairing_claim_denied', 'device_joined',
        'invitation_issued', 'invitation_delivery_failed', 'invitation_revoked',
        'invitation_verified', 'invitation_claim_denied'
    )),
    outcome TEXT NOT NULL CHECK (outcome IN ('success', 'denied', 'failed')),
    actor_device_id TEXT,
    actor_employee_id TEXT,
    target_device_id TEXT,
    pairing_id TEXT,
    invitation_id TEXT,
    reason_code TEXT,
    request_id TEXT,
    occurred_at TEXT NOT NULL
);

CREATE INDEX idx_security_audit_workspace_time
    ON security_audit_events (workspace_id, occurred_at DESC);
