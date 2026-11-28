ALTER TABLE tracked_products ADD COLUMN deleted_at TEXT;
CREATE INDEX idx_grok_tasks_product_status ON grok_tasks(product_id, status);
