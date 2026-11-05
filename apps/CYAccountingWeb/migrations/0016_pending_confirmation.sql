ALTER TABLE transactions ADD COLUMN pending_confirmation INTEGER NOT NULL DEFAULT 0
    CHECK(pending_confirmation IN (0,1));
CREATE INDEX idx_transactions_pending_confirmation ON transactions(tx_date DESC,id DESC)
    WHERE pending_confirmation = 1;

-- Confirmation can reclassify a locked pending entry. Keep that narrow action
-- auditable without preventing normal later transaction deletion.
CREATE TABLE transaction_confirmation_audit (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    transaction_id INTEGER NOT NULL,
    previous_category TEXT NOT NULL,
    previous_summary TEXT NOT NULL,
    category_name TEXT NOT NULL,
    summary TEXT NOT NULL,
    actor_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TRIGGER transaction_confirmation_audit_no_update BEFORE UPDATE ON transaction_confirmation_audit
BEGIN SELECT RAISE(ABORT, 'transaction confirmation audit is append-only'); END;
CREATE TRIGGER transaction_confirmation_audit_no_delete BEFORE DELETE ON transaction_confirmation_audit
BEGIN SELECT RAISE(ABORT, 'transaction confirmation audit is append-only'); END;
INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','16');
