-- CYID_ONLY V3 recovery schema preparation; never an automatic migration.
-- Requires a separately approved, atomic private maintenance window, backup,
-- current original D1 readback, CYID enabled, and compatible Worker runtime.
-- Do not run against a Built-in/self-hosted database or CYID's own D1.
-- Intended AFTER canonical 0014; source does not trigger this script.
PRAGMA foreign_keys = ON;
PRAGMA defer_foreign_keys = ON;

CREATE TABLE cyweb_recovery_storage_profile (
    profile TEXT PRIMARY KEY CHECK (profile = 'CYWEB_RECOVERY'),
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    enabled_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

-- An invalid state must abort the whole enclosing transaction.
CREATE TABLE cyweb_recovery_guard (passed INTEGER NOT NULL CHECK (passed = 1));
INSERT INTO cyweb_recovery_guard (passed)
SELECT CASE WHEN
    (SELECT COUNT(*) FROM workspaces) = 1
    AND (SELECT COUNT(*) FROM workspaces WHERE status = 'active') = 1
    AND (SELECT COUNT(*) FROM cyid_storage_profile WHERE profile='CYID_ONLY') = 1
    AND NOT EXISTS (
      SELECT 1 FROM sqlite_master WHERE type = 'table'
        AND name IN ('cloud_employees', 'device_employee_links',
                     'employee_link_candidates', 'employee_transition_items')
    )
    AND (SELECT COUNT(*) FROM device_invitations
         WHERE issuer_channel <> 'DEVICE' OR purpose <> 'JOIN') = 0
THEN 1 ELSE 0 END;
DROP TABLE cyweb_recovery_guard;

-- Only a CYWEB-issued RECOVERY invitation can exist without a Device issuer.
-- Existing DEVICE invitations keep the original Device/Workspace FKs and actor.
CREATE TABLE device_invitations_cyweb_recovery (
    invitation_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    code_hash TEXT NOT NULL UNIQUE,
    issued_by_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
    issued_by_employee_id TEXT,
    issued_by_cyid_employee_id TEXT,
    delivery_state TEXT NOT NULL CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    expires_at TEXT NOT NULL,
    sent_at TEXT,
    revoked_at TEXT,
    consumed_at TEXT,
    consumed_by_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL,
    issuer_channel TEXT NOT NULL DEFAULT 'DEVICE' CHECK (issuer_channel IN ('DEVICE','CYWEB')),
    purpose TEXT NOT NULL DEFAULT 'JOIN' CHECK (purpose IN ('JOIN','RECOVERY')),
    issuer_credential_version INTEGER CHECK (issuer_credential_version IS NULL OR issuer_credential_version >= 1),
    issuer_employee_revision INTEGER CHECK (issuer_employee_revision IS NULL OR issuer_employee_revision >= 1),
    recovery_operation_id TEXT,
    CHECK ((issued_by_employee_id IS NOT NULL AND issued_by_cyid_employee_id IS NULL)
        OR (issued_by_employee_id IS NULL AND issued_by_cyid_employee_id IS NOT NULL
            AND length(issued_by_cyid_employee_id) BETWEEN 1 AND 100)),
    CHECK (
        (issuer_channel = 'DEVICE' AND purpose = 'JOIN' AND issued_by_device_id IS NOT NULL)
        OR
        (issuer_channel = 'CYWEB' AND purpose = 'RECOVERY'
          AND issued_by_device_id IS NULL AND issued_by_employee_id IS NULL
          AND issued_by_cyid_employee_id IS NOT NULL
          AND issuer_credential_version IS NOT NULL
          AND issuer_employee_revision IS NOT NULL
          AND recovery_operation_id IS NOT NULL)
    )
);
INSERT INTO device_invitations_cyweb_recovery (
    invitation_id, workspace_id, code_hash, issued_by_device_id,
    issued_by_employee_id, issued_by_cyid_employee_id, delivery_state,
    expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id,
    created_at, issuer_channel, purpose, issuer_credential_version,
    issuer_employee_revision, recovery_operation_id
)
SELECT invitation_id, workspace_id, code_hash, issued_by_device_id,
       issued_by_employee_id, issued_by_cyid_employee_id, delivery_state,
       expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id,
       created_at, issuer_channel, purpose, issuer_credential_version,
       issuer_employee_revision, recovery_operation_id
  FROM device_invitations;
DROP TABLE device_invitations;
ALTER TABLE device_invitations_cyweb_recovery RENAME TO device_invitations;
CREATE INDEX idx_device_invitations_workspace_created
    ON device_invitations (workspace_id, created_at DESC);
CREATE UNIQUE INDEX idx_device_invitations_recovery_operation
    ON device_invitations (workspace_id, recovery_operation_id)
    WHERE recovery_operation_id IS NOT NULL;
-- At most one outstanding CYWEB recovery grant per business Workspace.
-- An ambiguous pending email cannot be superseded by another operationId:
-- explicit revocation is required before issuing a new invitation.
CREATE UNIQUE INDEX idx_device_invitations_single_open_recovery
    ON device_invitations (workspace_id)
    WHERE issuer_channel = 'CYWEB' AND purpose = 'RECOVERY'
      AND consumed_at IS NULL AND revoked_at IS NULL;

INSERT INTO cyweb_recovery_storage_profile (profile, workspace_id)
SELECT 'CYWEB_RECOVERY', workspace_id FROM workspaces WHERE status = 'active';

-- D1 batch abort guard for the atomic recovery cutover. The claim must
-- INSERT a value satisfying CHECK (passed=1) before any business mutation,
-- then DELETE it as the final statement of the very same D1 transaction.
-- An invalid invitation/profile/token aborts every write rather than leaving
-- a partially joined Device or partially revoked fleet.
CREATE TABLE cyweb_recovery_claim_guard (
    passed INTEGER NOT NULL CHECK (passed = 1)
);
-- Similarly, a guarded sent-state transition + success audit must be atomic.
-- A failed or stale authorization must never turn a pending code into claimable.
CREATE TABLE cyweb_recovery_issuer_guard (
    passed INTEGER NOT NULL CHECK (passed = 1)
);

PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
