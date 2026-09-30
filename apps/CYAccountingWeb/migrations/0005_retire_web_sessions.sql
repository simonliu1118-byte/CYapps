-- CYID is the sole identity/session authority after the CYAccountingWeb consumer cutover.
-- The legacy D1 table is intentionally retired with a forward migration; historical migrations remain immutable.
DROP TABLE IF EXISTS web_sessions;
