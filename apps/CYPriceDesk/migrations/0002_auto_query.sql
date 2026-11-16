-- WO-1.2.0 auto query: shared result cache, per-upstream throttle state and manual listing classifications.
-- Times in search_cache / upstream_state are Unix epoch milliseconds.
CREATE TABLE search_cache (
  source TEXT NOT NULL,              -- momo / feebee (shopee and coupang share one feebee row)
  keyword TEXT NOT NULL,
  page INTEGER NOT NULL,
  payload_json TEXT NOT NULL,
  fetched_at INTEGER NOT NULL,
  PRIMARY KEY (source, keyword, page)
);
CREATE TABLE upstream_state (
  upstream TEXT PRIMARY KEY,         -- momo / feebee
  last_request_at INTEGER NOT NULL DEFAULT 0,
  blocked_until INTEGER NOT NULL DEFAULT 0
);
INSERT INTO upstream_state (upstream) VALUES ('momo'), ('feebee');
CREATE TABLE listing_classifications (
  source TEXT NOT NULL,              -- momo / shopee / coupang
  listing_key TEXT NOT NULL,         -- momo: item code; Feebee: source link
  model TEXT NOT NULL DEFAULT '',
  quantity REAL,
  unit TEXT NOT NULL DEFAULT '',
  attributes_json TEXT NOT NULL DEFAULT '{}',
  decision TEXT NOT NULL,            -- include / exclude
  updated_at TEXT NOT NULL,
  updated_by TEXT NOT NULL,
  PRIMARY KEY (source, listing_key)
);
