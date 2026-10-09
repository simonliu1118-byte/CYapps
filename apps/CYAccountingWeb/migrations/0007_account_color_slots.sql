ALTER TABLE accounts
ADD COLUMN color_slot INTEGER CHECK(color_slot IS NULL OR color_slot > 0);

WITH ranked AS (
  SELECT
    id,
    ROW_NUMBER() OVER (ORDER BY created_at, id) AS slot
  FROM accounts
)
UPDATE accounts
SET color_slot = (
  SELECT slot
  FROM ranked
  WHERE ranked.id = accounts.id
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_accounts_color_slot
ON accounts(color_slot)
WHERE color_slot IS NOT NULL;

CREATE TRIGGER IF NOT EXISTS trg_accounts_assign_color_slot
AFTER INSERT ON accounts
WHEN NEW.color_slot IS NULL
BEGIN
  UPDATE accounts
  SET color_slot = (
    SELECT MIN(c.candidate)
    FROM (
      SELECT 1 AS candidate
      UNION ALL
      SELECT color_slot + 1 AS candidate
      FROM accounts
      WHERE color_slot IS NOT NULL
    ) c
    WHERE NOT EXISTS (
      SELECT 1
      FROM accounts used
      WHERE used.color_slot = c.candidate
    )
  )
  WHERE id = NEW.id;
END;

INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '7');
