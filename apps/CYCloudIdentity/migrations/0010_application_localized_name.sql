-- CYCloud Identity 0.4.2 — optional Chinese display name for registered Apps.
--
-- applications.display_name_zh: display-only label shown by the core account
-- portal. The English display_name stays the system name; IDs, access and
-- registration matching never use this column. NULL means not set.

ALTER TABLE applications ADD COLUMN display_name_zh TEXT
    CHECK (display_name_zh IS NULL
        OR (length(trim(display_name_zh)) BETWEEN 1 AND 60
            AND display_name_zh = trim(display_name_zh)
            AND display_name_zh NOT GLOB '*[' || char(1) || '-' || char(31) || char(127) || ']*'));
