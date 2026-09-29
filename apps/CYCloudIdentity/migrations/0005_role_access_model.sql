PRAGMA foreign_keys = ON;

ALTER TABLE employees
  ADD COLUMN role_key TEXT NOT NULL DEFAULT 'USER'
  CHECK (role_key IN ('USER', 'ADMIN'));

ALTER TABLE employees
  ADD COLUMN identity_admin INTEGER NOT NULL DEFAULT 0
  CHECK (identity_admin IN (0, 1));

ALTER TABLE employees
  ADD COLUMN activated_at TEXT;

-- Any Employee with a credential has completed first activation. Email verification
-- may later be cleared by administrator-driven Email recovery, so lifecycle state
-- must no longer be inferred from email_verified_at alone.
UPDATE employees
   SET activated_at = COALESCE(email_verified_at, updated_at, created_at)
 WHERE EXISTS (
       SELECT 1
         FROM employee_credentials c
        WHERE c.employee_id = employees.employee_id
   );

-- Preserve the strongest legacy coarse role during the one-time cutover. Role is
-- now Workspace-scoped, so any prior effective ADMIN mapping promotes the Employee
-- to Workspace ADMIN. Application entry remains separately constrained below.
UPDATE employees
   SET role_key = 'ADMIN'
 WHERE employee_id IN (
       SELECT DISTINCT eg.employee_id
         FROM employee_identity_groups eg
         JOIN identity_groups g
           ON g.group_id = eg.group_id
          AND g.workspace_id = eg.workspace_id
         JOIN identity_group_application_access ga
           ON ga.group_id = eg.group_id
          AND ga.workspace_id = eg.workspace_id
        WHERE g.status = 'active'
          AND ga.enabled = 1
          AND ga.application_role_key = 'ADMIN'
   );

-- The protected Super Admin pointer remains the effective SUPER_ADMIN authority.
-- Keep its fallback ordinary role as ADMIN so a former Super Admin remains an
-- administrator after a later protected authority transfer.
UPDATE employees
   SET role_key = 'ADMIN'
 WHERE employee_id IN (
       SELECT super_admin_employee_id
         FROM workspaces
        WHERE super_admin_employee_id IS NOT NULL
   );

-- Materialize every active legacy Group application grant as a direct Employee
-- grant before runtime stops consulting Group-derived access. Existing explicit
-- direct grants remain authoritative and are only re-enabled when legacy access
-- was active at cutover time.
INSERT INTO employee_application_access(
  workspace_id, employee_id, application_id, enabled, created_at, updated_at
)
SELECT DISTINCT
       eg.workspace_id,
       eg.employee_id,
       ga.application_id,
       1,
       strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
       strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
  FROM employee_identity_groups eg
  JOIN identity_groups g
    ON g.group_id = eg.group_id
   AND g.workspace_id = eg.workspace_id
  JOIN identity_group_application_access ga
    ON ga.group_id = eg.group_id
   AND ga.workspace_id = eg.workspace_id
 WHERE g.status = 'active'
   AND ga.enabled = 1
ON CONFLICT(workspace_id, employee_id, application_id) DO UPDATE SET
  enabled = 1,
  updated_at = excluded.updated_at;

CREATE INDEX idx_employees_workspace_role
  ON employees(workspace_id, role_key, enabled, employee_no);

CREATE INDEX idx_employees_workspace_identity_admin
  ON employees(workspace_id, identity_admin, enabled, employee_no);

CREATE TRIGGER trg_employee_identity_admin_requires_admin_insert
BEFORE INSERT ON employees
WHEN NEW.identity_admin = 1 AND NEW.role_key <> 'ADMIN'
BEGIN
  SELECT RAISE(ABORT, 'identity admin capability requires ADMIN role');
END;

CREATE TRIGGER trg_employee_identity_admin_requires_admin_update
BEFORE UPDATE OF role_key, identity_admin ON employees
WHEN NEW.identity_admin = 1 AND NEW.role_key <> 'ADMIN'
BEGIN
  SELECT RAISE(ABORT, 'identity admin capability requires ADMIN role');
END;

-- Credential presence is the durable proof that first activation was completed.
-- This also covers bootstrap-created credentials on a fresh database where this
-- migration has already been applied before the first Workspace exists.
CREATE TRIGGER trg_employee_credential_marks_activated
AFTER INSERT ON employee_credentials
WHEN (SELECT activated_at FROM employees WHERE employee_id = NEW.employee_id) IS NULL
BEGIN
  UPDATE employees
     SET activated_at = COALESCE(email_verified_at, NEW.updated_at, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
   WHERE employee_id = NEW.employee_id;
END;

-- Every current/future Super Admin keeps ADMIN as its ordinary fallback role.
-- SUPER_ADMIN itself is still derived exclusively from the protected Workspace
-- pointer and is never stored as an editable Employee role value.
CREATE TRIGGER trg_workspace_super_admin_fallback_role
AFTER UPDATE OF super_admin_employee_id ON workspaces
WHEN NEW.super_admin_employee_id IS NOT NULL
BEGIN
  UPDATE employees
     SET role_key = 'ADMIN',
         updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
   WHERE workspace_id = NEW.workspace_id
     AND employee_id = NEW.super_admin_employee_id;
END;
