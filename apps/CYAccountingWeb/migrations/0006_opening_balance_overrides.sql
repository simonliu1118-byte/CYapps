CREATE TABLE opening_balance_overrides (
    month TEXT NOT NULL,
    account_name TEXT NOT NULL,
    amount INTEGER NOT NULL,
    reason TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by_employee_id TEXT NOT NULL,
    updated_by_employee_no TEXT NOT NULL,
    updated_by_name TEXT NOT NULL,
    updated_by_role TEXT NOT NULL,
    PRIMARY KEY(month, account_name)
);

CREATE INDEX idx_opening_balance_overrides_account_month
ON opening_balance_overrides(account_name, month);

CREATE TABLE opening_balance_audit (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    month TEXT NOT NULL,
    account_name TEXT NOT NULL,
    action TEXT NOT NULL CHECK(action IN ('set', 'clear', 'migration')),
    previous_amount INTEGER,
    new_amount INTEGER,
    reason TEXT NOT NULL,
    actor_employee_id TEXT NOT NULL,
    actor_employee_no TEXT NOT NULL,
    actor_name TEXT NOT NULL,
    actor_role TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX idx_opening_balance_audit_account_month
ON opening_balance_audit(account_name, month, id);

CREATE TRIGGER opening_balance_audit_no_update
BEFORE UPDATE ON opening_balance_audit
BEGIN
    SELECT RAISE(ABORT, 'opening_balance_audit is append-only');
END;

CREATE TRIGGER opening_balance_audit_no_delete
BEFORE DELETE ON opening_balance_audit
BEGIN
    SELECT RAISE(ABORT, 'opening_balance_audit is append-only');
END;

INSERT INTO opening_balance_overrides(
    month, account_name, amount, reason, created_at, updated_at,
    updated_by_employee_id, updated_by_employee_no, updated_by_name, updated_by_role
)
SELECT
    month, account_name, amount, '舊版期初餘額移轉',
    created_at, updated_at, 'SYSTEM', 'SYSTEM', '系統移轉', 'SYSTEM'
FROM opening_balances;

INSERT INTO opening_balance_audit(
    month, account_name, action, previous_amount, new_amount, reason,
    actor_employee_id, actor_employee_no, actor_name, actor_role, created_at
)
SELECT
    month, account_name, 'migration', NULL, amount, '舊版期初餘額移轉',
    'SYSTEM', 'SYSTEM', '系統移轉', 'SYSTEM', updated_at
FROM opening_balances;

DROP TABLE opening_balances;

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '6');
