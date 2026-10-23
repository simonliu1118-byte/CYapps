-- Forward-only: preserve approvals, ledger and transition audit.
-- A live manager may withdraw review/approved requests whose
-- complete ledger history proves no uncertain or successful send.
DROP TRIGGER allowance_application_transition_guard;
CREATE TRIGGER allowance_application_transition_guard BEFORE UPDATE ON allowance_applications
WHEN NOT (OLD.status IN ('review','approved') AND NEW.status='withdrawn'
 AND NEW.revision=OLD.revision+1
 AND NEW.application_id IS OLD.application_id AND NEW.workspace_id IS OLD.workspace_id
 AND NEW.opened_by_device_id IS OLD.opened_by_device_id AND NEW.environment IS OLD.environment
 AND NEW.reference_hash IS OLD.reference_hash AND NEW.request_hash IS OLD.request_hash
 AND NEW.requested_gross IS OLD.requested_gross AND NEW.requester_actor_id IS OLD.requester_actor_id
 AND NEW.manager_actor_id IS NULL AND NEW.created_at IS OLD.created_at
 AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.application_id=OLD.application_id
  AND l.status<>'reserved' AND NOT (l.status='occupied_external' AND EXISTS
   (SELECT 1 FROM allowance_number_rejections r WHERE r.ledger_id=l.ledger_id AND r.provider_code=l.collision_code)))) AND ((OLD.status IN ('approved','rejected','withdrawn') OR
 NEW.revision<>OLD.revision+1 OR
 NOT ((OLD.status='open' AND NEW.status IN ('review','withdrawn')) OR
      (OLD.status='review' AND NEW.status IN ('approved','rejected'))) OR
 NEW.application_id<>OLD.application_id OR NEW.workspace_id<>OLD.workspace_id OR
 NEW.opened_by_device_id<>OLD.opened_by_device_id OR
 NEW.environment<>OLD.environment OR NEW.reference_hash<>OLD.reference_hash OR
 NEW.request_hash<>OLD.request_hash OR NEW.requested_gross<>OLD.requested_gross OR
 NEW.requester_actor_id<>OLD.requester_actor_id OR NEW.created_at<>OLD.created_at OR
 (OLD.status='open' AND NEW.status='review' AND NEW.manager_actor_id IS NOT NEW.actor_id) OR
 (OLD.status='review' AND NEW.manager_actor_id IS NOT OLD.manager_actor_id) OR
 (OLD.status='open' AND NEW.status='withdrawn' AND NEW.actor_id<>OLD.requester_actor_id)) AND NOT (NEW.application_id IS OLD.application_id AND NEW.workspace_id IS OLD.workspace_id AND NEW.opened_by_device_id IS OLD.opened_by_device_id AND NEW.updated_by_device_id IS OLD.updated_by_device_id AND NEW.environment IS OLD.environment AND NEW.reference_hash IS OLD.reference_hash AND NEW.request_hash IS OLD.request_hash AND NEW.requested_gross IS OLD.requested_gross AND NEW.status IS OLD.status AND NEW.revision IS OLD.revision AND NEW.requester_actor_id IS OLD.requester_actor_id AND NEW.actor_id IS OLD.actor_id AND NEW.manager_actor_id IS OLD.manager_actor_id AND NEW.created_at IS OLD.created_at AND NEW.updated_at IS OLD.updated_at))
BEGIN SELECT RAISE(ABORT,'ALLOWANCE_APPLICATION_TRANSITION_INVALID'); END;

-- Single formal company card; label only, no additional company/credential owner.
ALTER TABLE company_settings ADD COLUMN company_name TEXT NOT NULL DEFAULT ''
  CHECK(length(company_name)<=100 AND instr(company_name,char(10))=0 AND instr(company_name,char(13))=0);

-- Name/MO changes advance the card version without invalidating financial approvals.
-- The existing revision remains the AMEGO company/key revision.
ALTER TABLE company_settings ADD COLUMN card_revision INTEGER NOT NULL DEFAULT 1 CHECK(card_revision>0);
UPDATE company_settings SET card_revision=revision;
