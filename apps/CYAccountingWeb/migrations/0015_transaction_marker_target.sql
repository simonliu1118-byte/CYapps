-- Marker placement is presentation-only metadata, independent of posted accounting values.
-- Pre-existing highlights remain on the transaction summary.
ALTER TABLE transactions ADD COLUMN marker_target TEXT NOT NULL DEFAULT 'summary'
  CHECK(marker_target IN ('summary', 'category'));
INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','15');
