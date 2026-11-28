ALTER TABLE latest_candidates ADD COLUMN run_id TEXT NOT NULL DEFAULT '';
-- Recover the last usable source run for candidates created before this migration.
UPDATE latest_candidates AS c SET run_id=COALESCE((
  SELECT id FROM tracked_runs WHERE product_id=c.product_id AND
    (CASE c.source WHEN 'momo' THEN momo_status ELSE feebee_status END) IN ('ok','no_match','no_price')
  ORDER BY finished_at DESC LIMIT 1),'');
CREATE TABLE grok_tasks (
 id TEXT PRIMARY KEY, kind TEXT NOT NULL, product_id TEXT, status TEXT NOT NULL,
 event_id TEXT NOT NULL, event_json TEXT NOT NULL, conditions_json TEXT NOT NULL,
 draft_json TEXT, platform_status_json TEXT NOT NULL DEFAULT '{}', error TEXT NOT NULL DEFAULT '',
 created_at TEXT NOT NULL, created_by TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE TABLE grok_task_items (
 task_id TEXT NOT NULL REFERENCES grok_tasks(id) ON DELETE CASCADE, product_id TEXT NOT NULL,
 source TEXT NOT NULL, listing_key TEXT NOT NULL, run_id TEXT NOT NULL, snapshot_json TEXT NOT NULL,
 status TEXT NOT NULL DEFAULT 'waiting', result_json TEXT, review_json TEXT, recheck_reason TEXT,
 reviewed_at TEXT, reviewed_by TEXT, PRIMARY KEY(task_id,source,listing_key)
);
CREATE INDEX idx_grok_candidate ON grok_task_items(product_id,source,listing_key,run_id);
CREATE TABLE grok_questions (
 id TEXT PRIMARY KEY, task_id TEXT NOT NULL REFERENCES grok_tasks(id) ON DELETE CASCADE,
 source TEXT, listing_key TEXT, question TEXT NOT NULL, options_json TEXT NOT NULL,
 answer TEXT, answered_at TEXT, answered_by TEXT, created_at TEXT NOT NULL
);
CREATE INDEX idx_grok_questions_task ON grok_questions(task_id);
