PRAGMA foreign_keys = ON;

-- Cloud Employee is the single account authority after a Device completes the
-- one-time Local -> Cloud conversion.  A trusted Device may exist before that
-- conversion is complete, so Device membership and Employee authority have
-- separate lifecycle states.
ALTER TABLE workspaces ADD COLUMN employee_revision INTEGER NOT NULL DEFAULT 0
    CHECK (employee_revision >= 0);

ALTER TABLE devices ADD COLUMN employee_authority_state TEXT NOT NULL DEFAULT 'transitioning'
    CHECK (employee_authority_state IN ('transitioning', 'cloud'));
ALTER TABLE devices ADD COLUMN employee_transition_started_at TEXT;
ALTER TABLE devices ADD COLUMN employee_transition_completed_at TEXT;

UPDATE devices
   SET employee_transition_started_at = COALESCE(paired_at, created_at)
 WHERE employee_transition_started_at IS NULL;

-- Existing schema-4 rows stay valid.  Credentials are nullable during the
-- conversion window; a Device cannot enter Cloud Employee authority until the
-- later conversion workflow has established all required credentials.
ALTER TABLE cloud_employees ADD COLUMN credential_verifier TEXT;
ALTER TABLE cloud_employees ADD COLUMN credential_algorithm TEXT;
ALTER TABLE cloud_employees ADD COLUMN credential_version INTEGER NOT NULL DEFAULT 0
    CHECK (credential_version >= 0);
ALTER TABLE cloud_employees ADD COLUMN credential_updated_at TEXT;
ALTER TABLE cloud_employees ADD COLUMN revision INTEGER NOT NULL DEFAULT 1
    CHECK (revision >= 1);

CREATE INDEX IF NOT EXISTS idx_devices_employee_authority_state
    ON devices (workspace_id, employee_authority_state, status);

CREATE INDEX IF NOT EXISTS idx_cloud_employees_workspace_revision
    ON cloud_employees (workspace_id, revision);

-- Schema 3 restricted OTP purpose to four literal values, while the pairing
-- Worker already scopes its authorization purpose as
-- device_pairing_authorization:<workspace>:<device>.  Rebuild the table in a
-- forward migration so the existing runtime contract is valid and future
-- Employee verification / SUPER_ADMIN transfer can share the same OTP engine.
ALTER TABLE email_otp_challenges RENAME TO email_otp_challenges_schema3;

CREATE TABLE email_otp_challenges (
    challenge_id TEXT PRIMARY KEY,
    purpose TEXT NOT NULL CHECK (
        purpose IN (
            'workspace_bootstrap',
            'workspace_recovery',
            'pairing_authorization',
            'device_pairing_authorization',
            'employee_email_verification',
            'super_admin_transfer_authorization',
            'recovery_email_change'
        )
        OR purpose GLOB 'device_pairing_authorization:*'
    ),
    scope_key TEXT CHECK (scope_key IS NULL OR length(scope_key) BETWEEN 1 AND 240),
    email_normalized TEXT NOT NULL CHECK (
        length(trim(email_normalized)) BETWEEN 3 AND 320
        AND instr(email_normalized, '@') > 1
    ),
    otp_digest TEXT NOT NULL CHECK (length(otp_digest) = 64),
    delivery_state TEXT NOT NULL DEFAULT 'pending'
        CHECK (delivery_state IN ('pending', 'sent', 'failed')),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    max_attempts INTEGER NOT NULL DEFAULT 5 CHECK (max_attempts BETWEEN 1 AND 20),
    expires_at TEXT NOT NULL,
    resend_after TEXT NOT NULL,
    sent_at TEXT,
    consumed_at TEXT,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
);

INSERT INTO email_otp_challenges (
    challenge_id, purpose, scope_key, email_normalized, otp_digest,
    delivery_state, attempt_count, max_attempts, expires_at, resend_after,
    sent_at, consumed_at, created_at, updated_at
)
SELECT challenge_id,
       purpose,
       CASE
           WHEN purpose GLOB 'device_pairing_authorization:*'
           THEN substr(purpose, length('device_pairing_authorization:') + 1)
           ELSE NULL
       END,
       email_normalized,
       otp_digest,
       delivery_state,
       attempt_count,
       max_attempts,
       expires_at,
       resend_after,
       sent_at,
       consumed_at,
       created_at,
       updated_at
  FROM email_otp_challenges_schema3;

DROP TABLE email_otp_challenges_schema3;

CREATE INDEX idx_email_otp_email_purpose_created
    ON email_otp_challenges (email_normalized, purpose, created_at DESC);

CREATE INDEX idx_email_otp_expiry
    ON email_otp_challenges (expires_at);

CREATE INDEX idx_email_otp_purpose_scope_created
    ON email_otp_challenges (purpose, scope_key, created_at DESC);
