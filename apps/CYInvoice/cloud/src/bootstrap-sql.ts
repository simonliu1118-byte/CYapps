export const BOOTSTRAP_WORKSPACE_INSERT_SQL = `INSERT INTO workspaces (
  workspace_id, display_name, status, recovery_email, recovery_email_verified_at,
  created_at, updated_at
) VALUES (?1, ?2, 'active', ?3, ?4, ?4, ?4)`;

export const BOOTSTRAP_DEVICE_INSERT_SQL = `INSERT INTO devices (
  device_id, workspace_id, display_name, token_hash, client_version,
  status, created_at, updated_at
) VALUES (?1, ?2, ?3, ?4, ?5, 'active', ?6, ?6)`;
