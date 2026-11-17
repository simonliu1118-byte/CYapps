-- WO-1.4.0 redesign: per-platform own prices, shared keyword with per-source overrides, manual comparisons.
-- The production database held no business data when this was written (WO-1.4.0 §0), so tables are rebuilt instead of migrated.
-- next_run_at / last_run_at stay Unix epoch milliseconds; other times are ISO UTC text (checked_at in manual_comparisons is a Taipei YYYY-MM-DD date).
DROP TABLE tracked_products;
CREATE TABLE tracked_products (
  id TEXT PRIMARY KEY,               -- crypto.randomUUID()
  name TEXT NOT NULL,
  model TEXT NOT NULL,
  attributes_json TEXT NOT NULL DEFAULT '{}',   -- e.g. {"尺寸":"0.5吋"}
  package_quantity REAL NOT NULL,
  package_unit TEXT NOT NULL,
  keyword TEXT NOT NULL,             -- shared query keyword
  momo_keyword TEXT NOT NULL DEFAULT '',        -- override for momo; empty = use keyword
  feebee_keyword TEXT NOT NULL DEFAULT '',      -- override for Feebee (shopee and coupang share one query); empty = use keyword
  my_price_shopee REAL, my_price_momo REAL, my_price_coupang REAL,   -- NULL = not set, that platform is not compared
  my_url_shopee TEXT NOT NULL DEFAULT '', my_url_momo TEXT NOT NULL DEFAULT '', my_url_coupang TEXT NOT NULL DEFAULT '',
  cyweb_item_no TEXT NOT NULL DEFAULT '',       -- stored and shown only; linked to CY Web in a later version
  active INTEGER NOT NULL DEFAULT 1,
  next_run_at INTEGER NOT NULL,
  last_run_at INTEGER,
  revision INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL, created_by TEXT NOT NULL,
  updated_at TEXT NOT NULL, updated_by TEXT NOT NULL
);
CREATE INDEX idx_tracked_due ON tracked_products(active, next_run_at);
DROP TABLE my_price_changes;
CREATE TABLE my_price_changes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  product_id TEXT NOT NULL,
  platform TEXT NOT NULL,            -- shopee / momo / coupang
  at TEXT NOT NULL,
  actor_employee_id TEXT NOT NULL, actor_name TEXT NOT NULL,
  old_price REAL, new_price REAL,
  note TEXT NOT NULL DEFAULT ''
);
CREATE INDEX idx_my_price_changes_product ON my_price_changes(product_id, at);
CREATE TABLE manual_comparisons (
  id TEXT PRIMARY KEY,               -- crypto.randomUUID()
  product_id TEXT NOT NULL,
  platform TEXT NOT NULL,            -- shopee / momo / coupang / other
  seller TEXT NOT NULL DEFAULT '',
  url TEXT NOT NULL DEFAULT '',
  price REAL NOT NULL,
  checked_at TEXT NOT NULL,          -- YYYY-MM-DD (Taipei)
  note TEXT NOT NULL DEFAULT '',
  revision INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL, created_by TEXT NOT NULL,
  updated_at TEXT NOT NULL, updated_by TEXT NOT NULL
);
CREATE INDEX idx_manual_comparisons_product ON manual_comparisons(product_id, checked_at);
DROP TABLE price_records;
