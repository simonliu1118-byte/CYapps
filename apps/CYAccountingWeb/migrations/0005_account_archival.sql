ALTER TABLE accounts ADD COLUMN archived_at TEXT;

CREATE INDEX IF NOT EXISTS idx_accounts_archived
ON accounts(archived_at, sort_order, id);

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '5');
