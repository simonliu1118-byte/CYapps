PRAGMA foreign_keys = ON;

-- Password recovery reuses the existing Email OTP engine, but keeps a distinct
-- purpose so verification codes can never be replayed across account actions.
ALTER TABLE email_otp_challenges RENAME TO email_otp_challenges_schema7;

CREATE TABLE email_otp_challenges (
    challenge_id TEXT PRIMARY KEY,
    purpose TEXT NOT NULL CHECK (
        purpose IN (
            'workspace_bootstrap',
            'workspace_recovery',
            'pairing_authorization',
            'device_pairing_authorization',
            'employee_email_verification',
            'employee_password_reset',
            'employee_password_recovery',
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
SELECT challenge_id, purpose, scope_key, email_normalized, otp_digest,
       delivery_state, attempt_count, max_attempts, expires_at, resend_after,
       sent_at, consumed_at, created_at, updated_at
  FROM email_otp_challenges_schema7;

DROP TABLE email_otp_challenges_schema7;

CREATE INDEX idx_email_otp_email_purpose_created
    ON email_otp_challenges (email_normalized, purpose, created_at DESC);
CREATE INDEX idx_email_otp_expiry
    ON email_otp_challenges (expires_at);
CREATE INDEX idx_email_otp_purpose_scope_created
    ON email_otp_challenges (purpose, scope_key, created_at DESC);
