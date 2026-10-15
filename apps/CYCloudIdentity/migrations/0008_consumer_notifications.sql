-- One bounded delivery slot per Workspace / App / notification type. No Email
-- address, business payload, credential or Session is copied into this table.
CREATE TABLE consumer_notifications (
  workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),
  application_id TEXT NOT NULL REFERENCES applications(application_id),
  notification_type TEXT NOT NULL CHECK(notification_type = 'backup_health_failed'),
  event_id TEXT NOT NULL,
  lease_token TEXT NOT NULL DEFAULT '',
  lease_until TEXT NOT NULL DEFAULT '',
  sent_at TEXT,
  updated_at TEXT NOT NULL,
  PRIMARY KEY(workspace_id, application_id, notification_type)
);
