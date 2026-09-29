PRAGMA defer_foreign_keys = ON;

-- Device revoke / retire keeps the Device row for history. Schema 11 only
-- extends the immutable security-event vocabulary so every successful or denied
-- revoke attempt can be recorded without overloading an unrelated event type.
CREATE TABLE security_audit_events_schema11 (
    event_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'pairing_email', 'pairing_issued', 'pairing_verified', 'pairing_claim_denied', 'device_joined',
        'invitation_issued', 'invitation_delivery_failed', 'invitation_revoked',
        'invitation_verified', 'invitation_claim_denied', 'device_revoked'
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

INSERT INTO security_audit_events_schema11 (
    event_id, workspace_id, event_type, outcome, actor_device_id,
    actor_employee_id, target_device_id, pairing_id, invitation_id,
    reason_code, request_id, occurred_at
)
SELECT event_id, workspace_id, event_type, outcome, actor_device_id,
       actor_employee_id, target_device_id, pairing_id, invitation_id,
       reason_code, request_id, occurred_at
  FROM security_audit_events;

DROP TABLE security_audit_events;
ALTER TABLE security_audit_events_schema11 RENAME TO security_audit_events;

CREATE INDEX idx_security_audit_workspace_time
    ON security_audit_events (workspace_id, occurred_at DESC);

PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
