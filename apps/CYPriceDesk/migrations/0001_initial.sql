CREATE TABLE price_records (
  id TEXT PRIMARY KEY,               -- crypto.randomUUID()
  product TEXT NOT NULL, spec TEXT NOT NULL DEFAULT '',
  platform TEXT NOT NULL DEFAULT '', seller TEXT NOT NULL DEFAULT '',
  url TEXT NOT NULL DEFAULT '',
  price REAL, shipping REAL,         -- NULL = 未知
  checked_at TEXT,                   -- ISO UTC；NULL = 待查價
  note TEXT NOT NULL DEFAULT '',
  revision INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL, created_by TEXT NOT NULL,
  updated_at TEXT NOT NULL, updated_by TEXT NOT NULL
);
CREATE INDEX idx_price_records_group ON price_records(product, spec);
CREATE TABLE audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL, actor_employee_id TEXT NOT NULL, actor_name TEXT NOT NULL,
  action TEXT NOT NULL,              -- record.create / record.update / record.delete / record.import
  entity_id TEXT, before_json TEXT, after_json TEXT
);
CREATE INDEX idx_audit_at ON audit_log(at DESC);
