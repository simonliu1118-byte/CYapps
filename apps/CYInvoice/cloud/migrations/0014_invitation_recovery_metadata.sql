PRAGMA defer_foreign_keys = ON;

-- Distinguish explicitly revoked pairing grants from successfully used codes.
-- During a full recovery cutover any outstanding old pairing authorizations
-- must also stop granting new Device tokens.
ALTER TABLE device_pairing_codes ADD COLUMN revoked_at TEXT;

-- V3 invitation metadata shared by independent Built-in and CYID-only storage.
-- Pure additive fields keep every historical issuer Device/Employee FK intact.
-- The optional CYID_ONLY issuer-device relaxation is a separate, reviewed
-- maintenance operation; ordinary Built-in migrations MUST NOT weaken the FK.
ALTER TABLE device_invitations ADD COLUMN issuer_channel TEXT NOT NULL DEFAULT 'DEVICE'
    CHECK (issuer_channel IN ('DEVICE', 'CYWEB'));
ALTER TABLE device_invitations ADD COLUMN purpose TEXT NOT NULL DEFAULT 'JOIN'
    CHECK (purpose IN ('JOIN', 'RECOVERY'));
ALTER TABLE device_invitations ADD COLUMN issuer_credential_version INTEGER
    CHECK (issuer_credential_version IS NULL OR issuer_credential_version >= 1);
ALTER TABLE device_invitations ADD COLUMN issuer_employee_revision INTEGER
    CHECK (issuer_employee_revision IS NULL OR issuer_employee_revision >= 1);
-- Stable opaque operation ID allows a private issuer to look up an ambiguous
-- delivery result without generating or emailing a second invitation.
ALTER TABLE device_invitations ADD COLUMN recovery_operation_id TEXT;
CREATE UNIQUE INDEX idx_device_invitations_recovery_operation
    ON device_invitations (workspace_id, recovery_operation_id)
    WHERE recovery_operation_id IS NOT NULL;

-- Future RECOVERY events are explicit, not mislabeled as normal Device issuance.
-- Old actor provider cannot be inferred from employee number / identifier.
CREATE TABLE security_audit_events_schema14 (
    event_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    event_type TEXT NOT NULL CHECK (event_type IN (
        'pairing_email', 'pairing_issued', 'pairing_verified', 'pairing_claim_denied', 'device_joined',
        'invitation_issued', 'invitation_delivery_failed', 'invitation_revoked',
        'invitation_verified', 'invitation_claim_denied', 'device_revoked', 'device_renamed',
        'recovery_invitation_issued', 'recovery_invitation_delivery_failed',
        'recovery_invitation_revoked', 'recovery_invitation_verified',
        'recovery_invitation_claim_denied', 'recovery_device_joined'
    )),
    outcome TEXT NOT NULL CHECK (outcome IN ('success', 'denied', 'failed')),
    actor_device_id TEXT,
    actor_employee_id TEXT,
    target_device_id TEXT,
    pairing_id TEXT,
    invitation_id TEXT,
    reason_code TEXT,
    request_id TEXT,
    occurred_at TEXT NOT NULL,
    actor_provider TEXT NOT NULL DEFAULT 'UNKNOWN_LEGACY'
        CHECK (actor_provider IN ('UNKNOWN_LEGACY', 'CYID', 'BUILT_IN', 'DEVICE')),
    issuer_channel TEXT
        CHECK (issuer_channel IS NULL OR issuer_channel IN ('DEVICE', 'CYWEB')),
    purpose TEXT CHECK (purpose IS NULL OR purpose IN ('JOIN', 'RECOVERY'))
);
INSERT INTO security_audit_events_schema14 (
    event_id, workspace_id, event_type, outcome, actor_device_id,
    actor_employee_id, target_device_id, pairing_id, invitation_id,
    reason_code, request_id, occurred_at
)
SELECT event_id, workspace_id, event_type, outcome, actor_device_id,
       actor_employee_id, target_device_id, pairing_id, invitation_id,
       reason_code, request_id, occurred_at
  FROM security_audit_events;
DROP TABLE security_audit_events;
ALTER TABLE security_audit_events_schema14 RENAME TO security_audit_events;
CREATE INDEX idx_security_audit_workspace_time
    ON security_audit_events (workspace_id, occurred_at DESC);

PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
