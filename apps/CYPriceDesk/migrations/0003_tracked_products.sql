-- WO-1.3.0 tracked products: scheduled price checks, same-spec comparison and own selling price history.
-- next_run_at / last_run_at are Unix epoch milliseconds (compared against Date.now()); other times are ISO UTC text.
CREATE TABLE tracked_products (
  id TEXT PRIMARY KEY,               -- crypto.randomUUID()
  name TEXT NOT NULL,
  model TEXT NOT NULL,
  attributes_json TEXT NOT NULL DEFAULT '{}',   -- e.g. {"尺寸":"0.5吋"}
  package_quantity REAL NOT NULL,
  package_unit TEXT NOT NULL,
  my_price REAL,                     -- NULL = 未設定
  my_url TEXT NOT NULL DEFAULT '',
  momo_keyword TEXT NOT NULL DEFAULT '',
  feebee_keyword TEXT NOT NULL DEFAULT '',
  active INTEGER NOT NULL DEFAULT 1,
  next_run_at INTEGER NOT NULL,
  last_run_at INTEGER,
  revision INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL, created_by TEXT NOT NULL,
  updated_at TEXT NOT NULL, updated_by TEXT NOT NULL
);
CREATE INDEX idx_tracked_due ON tracked_products(active, next_run_at);
CREATE TABLE my_price_changes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  product_id TEXT NOT NULL,
  at TEXT NOT NULL,
  actor_employee_id TEXT NOT NULL, actor_name TEXT NOT NULL,
  old_price REAL, new_price REAL,
  note TEXT NOT NULL DEFAULT ''
);
CREATE INDEX idx_my_price_changes_product ON my_price_changes(product_id, at);
CREATE TABLE listing_judgments (
  product_id TEXT NOT NULL,
  source TEXT NOT NULL,              -- momo / shopee / coupang
  listing_key TEXT NOT NULL,         -- momo: item code; Feebee: source link
  verdict TEXT NOT NULL,             -- same / other_package / not_same
  package_quantity REAL,
  package_unit TEXT NOT NULL DEFAULT '',
  title_snapshot TEXT NOT NULL,      -- a changed title sends the listing back to 待確認
  decided_at TEXT NOT NULL,
  decided_by TEXT NOT NULL,
  PRIMARY KEY (product_id, source, listing_key)
);
CREATE TABLE tracked_runs (
  id TEXT PRIMARY KEY,
  product_id TEXT NOT NULL,
  started_at TEXT NOT NULL,
  finished_at TEXT NOT NULL,
  momo_status TEXT NOT NULL,         -- upstream status, or skipped when no keyword is set
  feebee_status TEXT NOT NULL,
  trigger TEXT NOT NULL              -- schedule / manual
);
CREATE INDEX idx_tracked_runs_product ON tracked_runs(product_id, started_at);
CREATE TABLE price_observations (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  source TEXT NOT NULL,
  listing_key TEXT NOT NULL,
  seller TEXT NOT NULL DEFAULT '',
  title TEXT NOT NULL,
  price REAL NOT NULL,
  url TEXT NOT NULL DEFAULT '',
  observed_at TEXT NOT NULL
);
CREATE INDEX idx_price_observations_product ON price_observations(product_id, observed_at);
CREATE TABLE group_summaries (
  run_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  group_key TEXT NOT NULL,           -- same / other:<quantity>:<unit>
  label TEXT NOT NULL,
  count INTEGER NOT NULL,
  min_price REAL NOT NULL, median_price REAL NOT NULL, max_price REAL NOT NULL,
  PRIMARY KEY (run_id, group_key)
);
CREATE INDEX idx_group_summaries_product ON group_summaries(product_id);
CREATE TABLE latest_candidates (
  product_id TEXT NOT NULL,
  source TEXT NOT NULL,
  listing_key TEXT NOT NULL,
  title TEXT NOT NULL,
  price REAL,
  seller TEXT NOT NULL DEFAULT '',
  url TEXT NOT NULL DEFAULT '',
  grp TEXT NOT NULL,                 -- same / other / pending / unrelated / excluded
  parsed_json TEXT NOT NULL,
  PRIMARY KEY (product_id, source, listing_key)
);
