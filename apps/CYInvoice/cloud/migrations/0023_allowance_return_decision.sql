-- Forward-only decision metadata. Original frozen requests, grants and evidence remain unchanged.
-- Source only: deployment/migration requires a separate maintenance instruction.
ALTER TABLE allowance_applications ADD COLUMN returned_at TEXT;
ALTER TABLE allowance_applications ADD COLUMN return_ciphertext TEXT;
ALTER TABLE allowance_void_requests ADD COLUMN returned_at TEXT;
ALTER TABLE allowance_void_requests ADD COLUMN return_ciphertext TEXT;
CREATE INDEX allowance_execution_events_workspace_cursor ON allowance_execution_events(workspace_id,event_id);

DROP TRIGGER allowance_application_transition_guard;
CREATE TRIGGER allowance_application_transition_guard BEFORE UPDATE ON allowance_applications
WHEN NOT (
 NEW.application_id IS OLD.application_id AND NEW.workspace_id IS OLD.workspace_id
 AND NEW.opened_by_device_id IS OLD.opened_by_device_id AND NEW.environment IS OLD.environment
 AND NEW.reference_hash IS OLD.reference_hash AND NEW.request_hash IS OLD.request_hash
 AND NEW.requested_gross IS OLD.requested_gross AND NEW.requester_actor_id IS OLD.requester_actor_id
 AND NEW.created_at IS OLD.created_at AND (
  (NEW.status IS OLD.status AND NEW.revision IS OLD.revision AND NEW.actor_id IS OLD.actor_id
   AND NEW.manager_actor_id IS OLD.manager_actor_id AND NEW.updated_at IS OLD.updated_at
   AND NEW.updated_by_device_id IS OLD.updated_by_device_id
   AND NEW.returned_at IS OLD.returned_at AND NEW.return_ciphertext IS OLD.return_ciphertext)
  OR (OLD.returned_at IS NULL AND NEW.revision=OLD.revision+1 AND (
   (OLD.status='open' AND NEW.status='review' AND NEW.manager_actor_id=NEW.actor_id)
   OR (OLD.status='review' AND NEW.status IN ('approved','rejected') AND NEW.manager_actor_id=OLD.manager_actor_id)
   OR (OLD.status IN ('open','review') AND NEW.status='withdrawn' AND NEW.manager_actor_id IS NULL
    AND NEW.returned_at IS NOT NULL AND NEW.return_ciphertext IS NOT NULL
    AND NOT EXISTS(SELECT 1 FROM allowance_execution_approvals g WHERE g.application_id=OLD.application_id)
    AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.application_id=OLD.application_id))
  ))
 ))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_TRANSITION_INVALID'); END;

-- The legacy void table has only open/approved and revision 1/2. Preserve that
-- storage contract; a returned decision on the SAME request row is terminal.
-- API/status readers project it as withdrawn; it can never acquire a grant.
CREATE TRIGGER allowance_void_return_guard BEFORE UPDATE ON allowance_void_requests
WHEN (NEW.returned_at IS NOT OLD.returned_at OR NEW.return_ciphertext IS NOT OLD.return_ciphertext)
 AND NOT (OLD.returned_at IS NULL AND OLD.status='open' AND NEW.status='open'
 AND NEW.returned_at IS NOT NULL AND NEW.return_ciphertext IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM allowance_void_approvals g WHERE g.application_id=OLD.application_id))
BEGIN SELECT RAISE(ABORT,'VOID_RETURN_NOT_PENDING'); END;
CREATE TRIGGER allowance_void_returned_no_approval BEFORE INSERT ON allowance_void_approvals
WHEN EXISTS(SELECT 1 FROM allowance_void_requests r WHERE r.application_id=NEW.application_id AND r.returned_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'VOID_APPLICATION_RETURNED'); END;
CREATE TRIGGER allowance_return_no_rewrite BEFORE UPDATE ON allowance_applications
WHEN OLD.returned_at IS NOT NULL AND (NEW.returned_at IS NOT OLD.returned_at OR NEW.return_ciphertext IS NOT OLD.return_ciphertext)
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_RETURN_PERMANENT'); END;
