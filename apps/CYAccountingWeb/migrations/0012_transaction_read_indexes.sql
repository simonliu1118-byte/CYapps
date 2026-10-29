-- Cover opening-balance account discovery and sums from the last manual anchor.
CREATE INDEX idx_transactions_opening_cover
  ON transactions(account_name, substr(tx_date, 1, 7), kind, amount);

-- Preserve both recent-window orderings, including their deterministic tie breaks.
-- Summary is covered so LIMIT does not require fetching whole transaction rows.
CREATE INDEX idx_transactions_frequent_tx_date
  ON transactions(kind, account_name, category_name, tx_date DESC, created_at DESC, id DESC, summary);
CREATE INDEX idx_transactions_frequent_created_at
  ON transactions(kind, account_name, category_name, created_at DESC, id DESC, tx_date, summary);

INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','12');
