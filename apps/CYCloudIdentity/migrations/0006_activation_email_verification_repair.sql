PRAGMA foreign_keys = ON;

-- 0.2.0 introduced a credential-insert trigger that marked activated_at before the
-- first-activation handler could run its guarded Employee UPDATE. That made the
-- handler's `WHERE activated_at IS NULL` update zero rows after a valid OTP was
-- consumed, leaving email_verified_at NULL (and potentially leaving enabled at its
-- prior value) even though the credential and activation audit event existed.
--
-- First activation is an explicit application transaction, and bootstrap already
-- writes its verified/active Employee state before inserting the credential. Keep
-- lifecycle state explicit and remove the conflicting trigger.
DROP TRIGGER IF EXISTS trg_employee_credential_marks_activated;

-- Record a repair audit event only for rows for which we have positive evidence of
-- a completed first-activation OTP flow. Do not repair an account if a later forced
-- Email recovery intentionally cleared verification.
INSERT INTO identity_audit_events(
  event_id,
  workspace_id,
  actor_employee_id,
  target_employee_id,
  event_type,
  detail_json,
  created_at
)
SELECT
  'audit_' || lower(hex(randomblob(16))),
  e.workspace_id,
  NULL,
  e.employee_id,
  'employee_activation_email_verification_repaired',
  '{"reason":"activation_completion_trigger_conflict"}',
  strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM employees e
WHERE e.email_verified_at IS NULL
  AND e.activated_at IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM employee_credentials c
    WHERE c.employee_id = e.employee_id
  )
  AND EXISTS (
    SELECT 1
    FROM identity_audit_events a
    WHERE a.workspace_id = e.workspace_id
      AND a.target_employee_id = e.employee_id
      AND a.event_type = 'employee_activation_completed'
  )
  AND NOT EXISTS (
    SELECT 1
    FROM identity_audit_events r
    WHERE r.workspace_id = e.workspace_id
      AND r.target_employee_id = e.employee_id
      AND r.event_type = 'employee_email_recovery_forced'
      AND r.created_at >= (
        SELECT MAX(a2.created_at)
        FROM identity_audit_events a2
        WHERE a2.workspace_id = e.workspace_id
          AND a2.target_employee_id = e.employee_id
          AND a2.event_type = 'employee_activation_completed'
      )
  );

-- The activation-completed audit event can only be emitted after successful OTP
-- verification/consumption. Use its timestamp as the missing Email verification
-- timestamp. Preserve enabled as-is so a later intentional administrator disable is
-- never undone by this repair.
UPDATE employees AS e
SET email_verified_at = (
      SELECT MAX(a.created_at)
      FROM identity_audit_events a
      WHERE a.workspace_id = e.workspace_id
        AND a.target_employee_id = e.employee_id
        AND a.event_type = 'employee_activation_completed'
    ),
    revision = revision + 1,
    updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE e.email_verified_at IS NULL
  AND e.activated_at IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM employee_credentials c
    WHERE c.employee_id = e.employee_id
  )
  AND EXISTS (
    SELECT 1
    FROM identity_audit_events a
    WHERE a.workspace_id = e.workspace_id
      AND a.target_employee_id = e.employee_id
      AND a.event_type = 'employee_activation_completed'
  )
  AND NOT EXISTS (
    SELECT 1
    FROM identity_audit_events r
    WHERE r.workspace_id = e.workspace_id
      AND r.target_employee_id = e.employee_id
      AND r.event_type = 'employee_email_recovery_forced'
      AND r.created_at >= (
        SELECT MAX(a2.created_at)
        FROM identity_audit_events a2
        WHERE a2.workspace_id = e.workspace_id
          AND a2.target_employee_id = e.employee_id
          AND a2.event_type = 'employee_activation_completed'
      )
  );
