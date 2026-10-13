-- CYInvoice CYID-only deployment maintenance plan (NOT a canonical migration).
-- NEVER place this script in cloud/migrations; Built-in/self-hosted D1 must keep Employee authority.
-- This script is NOT executed by any GitHub workflow, CI, or deployment automatically.
-- Requires separate approved active D1/Workspace scope, private backup and read-only inventory.
-- Run in a reviewed atomic maintenance window only AFTER compatible Worker has been deployed.
-- Keep old Built-in actor IDs as historical strings; do not map them to CYID IDs.

PRAGMA foreign_keys = ON;
PRAGMA defer_foreign_keys = ON;

-- Immutable marker distinguishes a deliberately retired CYID-only storage profile
-- from an accidental absence of Employee tables. An existing marker means stop.
CREATE TABLE cyid_storage_profile (
    profile TEXT PRIMARY KEY CHECK (profile = 'CYID_ONLY'),
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    retired_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

-- Fail closed against extra Workspaces or unfinished conversion records.
-- Runtime CYID_ENABLED, active D1 ID, backup and CYID scope are preconditions
-- verified separately: they cannot be determined from D1 schema alone.
CREATE TABLE cyid_retirement_guard (
    passed INTEGER NOT NULL CHECK (passed = 1)
);
INSERT INTO cyid_retirement_guard(passed)
SELECT CASE WHEN
    (SELECT COUNT(*) FROM workspaces) = 1
    AND (SELECT COUNT(*) FROM workspaces WHERE status = 'active') = 1
    AND (SELECT COUNT(*) FROM employee_transition_items WHERE state <> 'ready') = 0
    AND (SELECT COUNT(*) FROM devices WHERE employee_authority_state = 'transitioning') = 0
THEN 1 ELSE 0 END;
DROP TABLE cyid_retirement_guard;

-- Preserve old invitation actor IDs and rows as historical strings, NOT CYID
-- authorities; 0013 used a Built-in FK only for issued_by_employee_id.
CREATE TABLE device_invitations_cyid_only (
    invitation_id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
    code_hash TEXT NOT NULL UNIQUE,
    issued_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
    issued_by_employee_id TEXT,
    issued_by_cyid_employee_id TEXT,
    delivery_state TEXT NOT NULL CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    expires_at TEXT NOT NULL,
    sent_at TEXT,
    revoked_at TEXT,
    consumed_at TEXT,
    consumed_by_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL,
    CHECK ((issued_by_employee_id IS NOT NULL AND issued_by_cyid_employee_id IS NULL)
        OR (issued_by_employee_id IS NULL AND issued_by_cyid_employee_id IS NOT NULL
            AND length(issued_by_cyid_employee_id) BETWEEN 1 AND 100))
);
INSERT INTO device_invitations_cyid_only (
    invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    issued_by_cyid_employee_id, delivery_state, expires_at, sent_at, revoked_at,
    consumed_at, consumed_by_device_id, created_at)
SELECT invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    issued_by_cyid_employee_id, delivery_state, expires_at, sent_at, revoked_at,
    consumed_at, consumed_by_device_id, created_at
FROM device_invitations;
DROP TABLE device_invitations;
ALTER TABLE device_invitations_cyid_only RENAME TO device_invitations;
CREATE INDEX idx_device_invitations_workspace_created
    ON device_invitations (workspace_id, created_at DESC);

-- These are Built-in Local -> Cloud conversion records, not CYID accounts.
-- Historical security_audit_events.actor_employee_id remains untouched.
DROP TABLE employee_link_candidates;
DROP TABLE employee_transition_items;
DROP TABLE device_employee_links;

-- OTP is shared by CYID Device pairing; never drop the whole OTP table.
-- Retire only now-unused Built-in Employee/account-specific challenge entries.
DELETE FROM email_otp_challenges WHERE purpose IN (
    'employee_email_verification',
    'employee_password_reset',
    'employee_password_recovery',
    'super_admin_transfer_authorization'
);

DROP TABLE cloud_employees;

INSERT INTO cyid_storage_profile (profile, workspace_id)
SELECT 'CYID_ONLY', workspace_id FROM workspaces WHERE status = 'active';

PRAGMA defer_foreign_keys = OFF;
PRAGMA foreign_key_check;
