PRAGMA defer_foreign_keys = ON;

-- Preserve existing audit records and extend the vocabulary for device rename.
-- Metadata columns already exist; usage reporting needs no new table.
CREATE TABLE security_audit_events_schema12 (
    event_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'pairing_email', 'pairing_issued', 'pairing_verified', 'pairing_claim_denied', 'device_joined',
        'invitation_issued', 'invitation_delivery_failed', 'invitation_revoked',
        'invitation_verified', 'invitation_claim_denied', 'device_revoked', 'device_renamed'
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

INSERT INTO security_audit_events_schema12 (
    event_id, workspace_id, event_type, outcome, actor_device_id,
    actor_employee_id, target_device_id, pairing_id, invitation_id,
    reason_code, request_id, occurred_at
)
SELECT event_id, workspace_id, event_type, outcome, actor_device_id,
       actor_employee_id, target_device_id, pairing_id, invitation_id,
       reason_code, request_id, occurred_at
  FROM security_audit_events;

DROP TABLE security_audit_events;
ALTER TABLE security_audit_events_schema12 RENAME TO security_audit_events;

CREATE INDEX idx_security_audit_workspace_time
    ON security_audit_events (workspace_id, occurred_at DESC);

PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
