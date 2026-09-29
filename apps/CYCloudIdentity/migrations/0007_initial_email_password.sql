-- CYCloud Identity 0.3.0
-- One-time initial Email credential used only to enter the mandatory first-password-change flow.
-- It is deliberately separate from employee_credentials so a pending account never looks activated.

CREATE TABLE employee_initial_credentials (
  employee_id TEXT PRIMARY KEY NOT NULL
    REFERENCES employees(employee_id) ON DELETE CASCADE,
  algorithm TEXT NOT NULL,
  verifier TEXT NOT NULL,
  exchange_token_digest TEXT,
  exchange_expires_at TEXT,
  issued_at TEXT NOT NULL,
  sent_at TEXT,
  revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1)
);

CREATE INDEX idx_employee_initial_credentials_exchange
  ON employee_initial_credentials(exchange_token_digest, exchange_expires_at)
  WHERE exchange_token_digest IS NOT NULL;
