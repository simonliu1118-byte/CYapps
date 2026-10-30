-- Optional color marker belongs to a transaction, never to its summary or amount.
ALTER TABLE transactions ADD COLUMN marker_color TEXT
  CHECK(marker_color IS NULL OR marker_color IN ('orange', 'pink', 'yellow', 'green'));
INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','14');
