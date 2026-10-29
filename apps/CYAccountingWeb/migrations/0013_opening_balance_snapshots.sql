-- Disposable, shared monthly read models. Never portable accounting/backup data.
CREATE TABLE opening_balance_snapshots (
  month TEXT PRIMARY KEY NOT NULL,
  accounts_json TEXT NOT NULL CHECK(json_valid(accounts_json)),
  created_at TEXT NOT NULL
);
CREATE TABLE opening_balance_snapshot_state (
  id INTEGER PRIMARY KEY CHECK(id = 1),
  revision INTEGER NOT NULL
);
INSERT INTO opening_balance_snapshot_state(id,revision) VALUES(1,0);

-- Transactions affect later opening amounts. Current-month entries for active
-- master accounts do not change that month's opening or its account membership.
-- Archived/orphan history can introduce/remove a displayed account this month.
CREATE TRIGGER opening_snapshot_transaction_insert AFTER INSERT ON transactions
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>substr(NEW.tx_date,1,7);
  DELETE FROM opening_balance_snapshots WHERE month=substr(NEW.tx_date,1,7)
    AND NOT EXISTS(SELECT 1 FROM accounts WHERE name=NEW.account_name AND archived_at IS NULL);
END;
CREATE TRIGGER opening_snapshot_transaction_delete AFTER DELETE ON transactions
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>substr(OLD.tx_date,1,7);
  DELETE FROM opening_balance_snapshots WHERE month=substr(OLD.tx_date,1,7)
    AND NOT EXISTS(SELECT 1 FROM accounts WHERE name=OLD.account_name AND archived_at IS NULL);
END;
CREATE TRIGGER opening_snapshot_transaction_update
AFTER UPDATE OF tx_date,account_name,kind,amount ON transactions
WHEN OLD.tx_date IS NOT NEW.tx_date OR OLD.account_name IS NOT NEW.account_name
  OR OLD.kind IS NOT NEW.kind OR OLD.amount IS NOT NEW.amount
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>MIN(substr(OLD.tx_date,1,7),substr(NEW.tx_date,1,7));
  DELETE FROM opening_balance_snapshots WHERE month=substr(OLD.tx_date,1,7)
    AND NOT EXISTS(SELECT 1 FROM accounts WHERE name=OLD.account_name AND archived_at IS NULL);
  DELETE FROM opening_balance_snapshots WHERE month=substr(NEW.tx_date,1,7)
    AND NOT EXISTS(SELECT 1 FROM accounts WHERE name=NEW.account_name AND archived_at IS NULL);
END;

CREATE TRIGGER opening_snapshot_override_insert AFTER INSERT ON opening_balance_overrides
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>=NEW.month;
END;
CREATE TRIGGER opening_snapshot_override_delete AFTER DELETE ON opening_balance_overrides
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>=OLD.month;
END;
CREATE TRIGGER opening_snapshot_override_update AFTER UPDATE ON opening_balance_overrides
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots WHERE month>=MIN(OLD.month,NEW.month);
END;

CREATE TRIGGER opening_snapshot_account_insert AFTER INSERT ON accounts
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;
CREATE TRIGGER opening_snapshot_account_delete AFTER DELETE ON accounts
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;
CREATE TRIGGER opening_snapshot_account_update AFTER UPDATE OF name,archived_at ON accounts
WHEN OLD.name IS NOT NEW.name OR OLD.archived_at IS NOT NEW.archived_at
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;

-- An accounting-book replacement must never reuse a prior book's derived data.
CREATE TRIGGER opening_snapshot_epoch_insert AFTER INSERT ON meta WHEN NEW.key='accounting_epoch'
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;
CREATE TRIGGER opening_snapshot_epoch_update AFTER UPDATE ON meta
WHEN (OLD.key='accounting_epoch' OR NEW.key='accounting_epoch') AND OLD.value IS NOT NEW.value
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;
CREATE TRIGGER opening_snapshot_epoch_delete AFTER DELETE ON meta WHEN OLD.key='accounting_epoch'
BEGIN
  UPDATE opening_balance_snapshot_state SET revision=revision+1 WHERE id=1;
  DELETE FROM opening_balance_snapshots;
END;

INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','13');
