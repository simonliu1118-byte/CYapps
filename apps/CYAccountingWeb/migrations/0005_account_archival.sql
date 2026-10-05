ALTER TABLE accounts ADD COLUMN archived_at TEXT;

CREATE INDEX IF NOT EXISTS idx_accounts_archived
ON accounts(archived_at, sort_order, id);

-- Legacy builds physically deleted account master rows while transactions and
-- opening balances retained the account name. Recreate those historical-only
-- names as archived account records so they can be restored explicitly.
INSERT OR IGNORE INTO accounts(name, sort_order, is_default, created_at, archived_at)
SELECT historical.name, 0, 0, datetime('now'), datetime('now')
FROM (
  SELECT DISTINCT account_name AS name
  FROM transactions
  WHERE trim(account_name) <> ''
  UNION
  SELECT DISTINCT account_name AS name
  FROM opening_balances
  WHERE trim(account_name) <> ''
) historical;

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '5');
