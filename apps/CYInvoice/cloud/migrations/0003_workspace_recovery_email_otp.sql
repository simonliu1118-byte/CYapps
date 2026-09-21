PRAGMA foreign_keys = ON;

ALTER TABLE workspaces ADD COLUMN recovery_email TEXT;
ALTER TABLE workspaces ADD COLUMN recovery_email_verified_at TEXT;

CREATE TABLE IF NOT EXISTS email_otp_challenges (
    challenge_id TEXT PRIMARY KEY,
    purpose TEXT NOT NULL CHECK (purpose IN (
        'workspace_bootstrap',
        'workspace_recovery',
        'pairing_authorization',
        'recovery_email_change'
    )),
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

CREATE INDEX IF NOT EXISTS idx_email_otp_email_purpose_created
    ON email_otp_challenges (email_normalized, purpose, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_email_otp_expiry
    ON email_otp_challenges (expires_at);
