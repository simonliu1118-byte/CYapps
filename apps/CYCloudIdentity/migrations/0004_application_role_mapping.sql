PRAGMA foreign_keys = ON;

ALTER TABLE workspace_applications
ADD COLUMN compatibility_role_mode TEXT
CHECK (
  compatibility_role_mode IS NULL
  OR compatibility_role_mode = 'USER_ADMIN'
);

ALTER TABLE identity_group_application_access
ADD COLUMN application_role_key TEXT
CHECK (
  application_role_key IS NULL
  OR application_role_key IN ('USER', 'ADMIN')
);

CREATE INDEX idx_group_app_access_role
    ON identity_group_application_access(
      workspace_id,
      application_id,
      enabled,
      application_role_key,
      group_id
    );
