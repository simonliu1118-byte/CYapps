-- Engineering DRAFT for V3.2 D/E after canonical schema19.
-- DO NOT apply to original D1 without user-approved MCP preflight, backup and budget.
-- Separate g0501 ownership is required: the canonical g0401 Ledger has a different
-- business mutation. No buyer, invoice, allowance number or App Key in plaintext.
-- Callers must store authenticated AES-GCM frozen requests and keyed HMAC evidence.
-- No Cloud g0501 may be claimed until EVERY legacy Local sender for this
-- company/environment has been inventoried, disabled and independently audited.
-- D1 cannot detect unknown offline Local installations. This cutover evidence
-- is therefore a manual external prerequisite, never an inferred zero count.
-- There is intentionally NO application/HTTP source method to activate a gate.
-- Absence of an approved cutover => NO Cloud g0501, even with manager Grant.
CREATE TABLE allowance_void_authority_gates (
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 environment TEXT NOT NULL CHECK(environment IN ('test','production')),
 seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8 AND seller_invoice NOT GLOB '*[^0-9]*'),
 company_revision INTEGER NOT NULL CHECK(company_revision>0),
 state TEXT NOT NULL CHECK(state IN ('blocked','cloud_exclusive')),
 cutover_manifest_hash TEXT,
 activating_device_id TEXT REFERENCES devices(device_id) ON DELETE RESTRICT,
 activated_at TEXT,
 PRIMARY KEY(workspace_id,environment,seller_invoice),
 CHECK((state='blocked' AND cutover_manifest_hash IS NULL AND
       activating_device_id IS NULL AND activated_at IS NULL) OR
       (state='cloud_exclusive' AND length(cutover_manifest_hash)=64 AND
        cutover_manifest_hash NOT GLOB '*[^0-9a-f]*' AND
        activating_device_id IS NOT NULL AND
        unixepoch(activated_at) IS NOT NULL))
);
CREATE TRIGGER allowance_void_gate_activation_source_guard
BEFORE INSERT ON allowance_void_authority_gates
WHEN NEW.state='cloud_exclusive' AND NOT EXISTS (
 SELECT 1 FROM workspaces w JOIN devices d ON d.workspace_id=w.workspace_id
 JOIN company_settings c ON c.workspace_id=w.workspace_id
 WHERE w.workspace_id=NEW.workspace_id AND w.status='active'
 AND c.seller_invoice=NEW.seller_invoice AND c.revision=NEW.company_revision
 AND d.device_id=NEW.activating_device_id AND d.status='active'
 AND d.employee_authority_state='cloud')
BEGIN SELECT RAISE(ABORT,'VOID_GATE_CUTOVER_SCOPE_INVALID'); END;
-- Emergency stop is one-way for this row. No silent re-activation, edit or
-- deletion; a new operational migration/reconciliation is required.
CREATE TRIGGER allowance_void_gate_no_reactivation
BEFORE UPDATE ON allowance_void_authority_gates
WHEN OLD.state<>'cloud_exclusive' OR NEW.state<>'blocked' OR
 NEW.workspace_id<>OLD.workspace_id OR NEW.environment<>OLD.environment OR
 NEW.seller_invoice<>OLD.seller_invoice OR
 NEW.company_revision<>OLD.company_revision OR
 NEW.cutover_manifest_hash IS NOT NULL OR NEW.activating_device_id IS NOT NULL OR
 NEW.activated_at IS NOT NULL
BEGIN SELECT RAISE(ABORT,'VOID_GATE_REACTIVATION_FORBIDDEN'); END;
CREATE TRIGGER allowance_void_gate_no_delete
BEFORE DELETE ON allowance_void_authority_gates
BEGIN SELECT RAISE(ABORT,'VOID_GATE_HISTORY_REQUIRED'); END;

CREATE TABLE allowance_void_requests (
 application_id TEXT PRIMARY KEY CHECK(length(application_id) BETWEEN 8 AND 100),
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 opened_by_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 environment TEXT NOT NULL CHECK(environment IN ('test','production')),
 seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8 AND seller_invoice NOT GLOB '*[^0-9]*'),
 company_revision INTEGER NOT NULL CHECK(company_revision>0),
 allowance_fingerprint TEXT NOT NULL CHECK(length(allowance_fingerprint)=64 AND allowance_fingerprint NOT GLOB '*[^0-9a-f]*'),
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64 AND request_hash NOT GLOB '*[^0-9a-f]*'),
 frozen_ciphertext TEXT NOT NULL CHECK(length(frozen_ciphertext)>48),
 requester_actor_id TEXT NOT NULL CHECK(length(requester_actor_id) BETWEEN 1 AND 100),
 status TEXT NOT NULL DEFAULT 'open' CHECK(status IN ('open','approved')),
 revision INTEGER NOT NULL DEFAULT 1 CHECK(revision IN (1,2)),
 manager_actor_id TEXT,
 created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
 CHECK((status='open' AND revision=1 AND manager_actor_id IS NULL) OR
       (status='approved' AND revision=2 AND manager_actor_id IS NOT NULL)),
 UNIQUE(workspace_id,environment,seller_invoice,allowance_fingerprint)
);
CREATE INDEX idx_allowance_void_workspace_queue
 ON allowance_void_requests(workspace_id,status,created_at DESC,application_id);
CREATE TRIGGER allowance_void_request_source_guard
BEFORE INSERT ON allowance_void_requests
WHEN NOT EXISTS (
 SELECT 1 FROM workspaces w JOIN devices d ON d.workspace_id=w.workspace_id
 JOIN company_settings c ON c.workspace_id=w.workspace_id
 WHERE w.workspace_id=NEW.workspace_id AND w.status='active'
 AND c.seller_invoice=NEW.seller_invoice AND c.revision=NEW.company_revision
 AND d.device_id=NEW.opened_by_device_id AND d.status='active'
 AND d.employee_authority_state='cloud'
 AND NEW.status='open' AND NEW.revision=1 AND NEW.manager_actor_id IS NULL)
BEGIN SELECT RAISE(ABORT,'VOID_REQUEST_DEVICE_REQUIRED'); END;

CREATE TABLE allowance_void_approvals (
 application_id TEXT PRIMARY KEY REFERENCES allowance_void_requests(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64),
 official_snapshot_hash TEXT NOT NULL CHECK(length(official_snapshot_hash)=64 AND official_snapshot_hash NOT GLOB '*[^0-9a-f]*'),
 manager_actor_id TEXT NOT NULL CHECK(length(manager_actor_id) BETWEEN 1 AND 100),
 manager_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 approved_at TEXT NOT NULL, valid_until_utc TEXT NOT NULL,
 CHECK(unixepoch(approved_at) IS NOT NULL AND unixepoch(valid_until_utc) IS NOT NULL
 AND unixepoch(valid_until_utc)-unixepoch(approved_at) BETWEEN 1 AND 900)
);
CREATE TRIGGER allowance_void_approval_source_guard
BEFORE INSERT ON allowance_void_approvals
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_void_requests r
 JOIN devices d ON d.device_id=NEW.manager_device_id AND d.workspace_id=r.workspace_id
 JOIN workspaces w ON w.workspace_id=r.workspace_id
 JOIN company_settings c ON c.workspace_id=r.workspace_id
 WHERE c.seller_invoice=r.seller_invoice AND c.revision=r.company_revision
 AND r.application_id=NEW.application_id AND r.workspace_id=NEW.workspace_id
 AND r.request_hash=NEW.request_hash AND r.status='open' AND r.revision=1
 AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active'
 AND EXISTS(SELECT 1 FROM allowance_void_authority_gates g
  WHERE g.workspace_id=r.workspace_id AND g.environment=r.environment
  AND g.seller_invoice=r.seller_invoice AND g.company_revision=r.company_revision
  AND g.state='cloud_exclusive'))
BEGIN SELECT RAISE(ABORT,'VOID_APPROVAL_SOURCE_INVALID'); END;
CREATE TRIGGER allowance_void_request_transition_guard
BEFORE UPDATE ON allowance_void_requests
WHEN OLD.status<>'open' OR NEW.status<>'approved' OR NEW.revision<>2 OR
 NEW.manager_actor_id IS NULL OR
 NEW.application_id<>OLD.application_id OR NEW.workspace_id<>OLD.workspace_id OR
 NEW.opened_by_device_id<>OLD.opened_by_device_id OR
 NEW.environment<>OLD.environment OR NEW.seller_invoice<>OLD.seller_invoice OR
 NEW.company_revision<>OLD.company_revision OR
 NEW.allowance_fingerprint<>OLD.allowance_fingerprint OR
 NEW.request_hash<>OLD.request_hash OR NEW.frozen_ciphertext<>OLD.frozen_ciphertext OR
 NEW.requester_actor_id<>OLD.requester_actor_id OR NEW.created_at<>OLD.created_at OR
 NOT EXISTS (SELECT 1 FROM allowance_void_approvals a
 WHERE a.application_id=OLD.application_id AND a.workspace_id=OLD.workspace_id
 AND a.request_hash=OLD.request_hash AND a.manager_actor_id=NEW.manager_actor_id)
BEGIN SELECT RAISE(ABORT,'VOID_REQUEST_IMMUTABLE_OR_UNAPPROVED'); END;

-- Claim must commit BEFORE the only g0501 POST. Lost response is readback-only.
CREATE TABLE allowance_void_send_claims (
 application_id TEXT PRIMARY KEY REFERENCES allowance_void_requests(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 environment TEXT NOT NULL CHECK(environment IN ('test','production')),
 seller_invoice TEXT NOT NULL CHECK(length(seller_invoice)=8),
 allowance_fingerprint TEXT NOT NULL CHECK(length(allowance_fingerprint)=64),
 request_hash TEXT NOT NULL CHECK(length(request_hash)=64),
 official_snapshot_hash TEXT NOT NULL CHECK(length(official_snapshot_hash)=64),
 manager_actor_id TEXT NOT NULL,
 manager_device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 claimed_at TEXT NOT NULL,
 UNIQUE(workspace_id,environment,seller_invoice,allowance_fingerprint)
);
CREATE TRIGGER allowance_void_claim_source_guard
BEFORE INSERT ON allowance_void_send_claims
WHEN NOT EXISTS (
 SELECT 1 FROM allowance_void_requests r
 JOIN allowance_void_approvals a ON a.application_id=r.application_id
 JOIN devices d ON d.device_id=NEW.manager_device_id AND d.workspace_id=r.workspace_id
 JOIN workspaces w ON w.workspace_id=r.workspace_id
 JOIN company_settings c ON c.workspace_id=r.workspace_id
 WHERE r.application_id=NEW.application_id AND r.workspace_id=NEW.workspace_id
 AND c.seller_invoice=r.seller_invoice AND c.revision=r.company_revision
 AND r.status='approved' AND r.revision=2
 AND r.environment=NEW.environment AND r.seller_invoice=NEW.seller_invoice
 AND r.allowance_fingerprint=NEW.allowance_fingerprint
 AND r.request_hash=NEW.request_hash
 AND a.workspace_id=r.workspace_id AND a.request_hash=r.request_hash
 AND a.official_snapshot_hash=NEW.official_snapshot_hash
 AND a.manager_actor_id=NEW.manager_actor_id
 AND a.manager_device_id=NEW.manager_device_id
 AND julianday(NEW.claimed_at) IS NOT NULL
 AND julianday(NEW.claimed_at)>=julianday(a.approved_at)
 AND julianday(NEW.claimed_at)<julianday(a.valid_until_utc)
 AND d.status='active' AND d.employee_authority_state='cloud'
 AND w.status='active'
 AND EXISTS(SELECT 1 FROM allowance_void_authority_gates g
  WHERE g.workspace_id=r.workspace_id AND g.environment=r.environment
  AND g.seller_invoice=r.seller_invoice AND g.company_revision=r.company_revision
  AND g.state='cloud_exclusive'))
BEGIN SELECT RAISE(ABORT,'VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID'); END;

CREATE TABLE allowance_void_audit (
 event_id INTEGER PRIMARY KEY AUTOINCREMENT,
 application_id TEXT NOT NULL REFERENCES allowance_void_requests(application_id) ON DELETE RESTRICT,
 workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id) ON DELETE RESTRICT,
 kind TEXT NOT NULL CHECK(kind IN ('request_created','manager_approved','sending_claimed',
  'accepted_unconfirmed','rejected_unconfirmed','unknown_result',
  'official_void_observed_unattributed','official_void_unconfirmed')),
 evidence_hash TEXT NOT NULL DEFAULT '' CHECK(evidence_hash='' OR (length(evidence_hash)=64 AND evidence_hash NOT GLOB '*[^0-9a-f]*')),
 actor_id TEXT NOT NULL CHECK(length(actor_id) BETWEEN 1 AND 100),
 device_id TEXT NOT NULL REFERENCES devices(device_id) ON DELETE RESTRICT,
 recorded_at TEXT NOT NULL
);
CREATE INDEX idx_allowance_void_audit_history
 ON allowance_void_audit(workspace_id,application_id,event_id DESC);
CREATE UNIQUE INDEX idx_allowance_void_audit_milestone
 ON allowance_void_audit(application_id,kind)
 WHERE kind IN ('request_created','manager_approved','sending_claimed');
CREATE TRIGGER allowance_void_audit_claim_required
BEFORE INSERT ON allowance_void_audit
WHEN NEW.kind NOT IN ('request_created','manager_approved','sending_claimed')
 AND NOT EXISTS (SELECT 1 FROM allowance_void_send_claims c
  WHERE c.application_id=NEW.application_id AND c.workspace_id=NEW.workspace_id)
BEGIN SELECT RAISE(ABORT,'VOID_OBSERVATION_REQUIRES_ORIGINAL_CLAIM'); END;
CREATE TRIGGER allowance_void_request_created_audit AFTER INSERT ON allowance_void_requests
BEGIN
 INSERT INTO allowance_void_audit(application_id,workspace_id,kind,actor_id,device_id,recorded_at)
 VALUES(NEW.application_id,NEW.workspace_id,'request_created',NEW.requester_actor_id,
 NEW.opened_by_device_id,NEW.created_at);
END;
CREATE TRIGGER allowance_void_approval_audit AFTER INSERT ON allowance_void_approvals
BEGIN
 INSERT INTO allowance_void_audit(application_id,workspace_id,kind,evidence_hash,actor_id,device_id,recorded_at)
 VALUES(NEW.application_id,NEW.workspace_id,'manager_approved',NEW.official_snapshot_hash,
 NEW.manager_actor_id,NEW.manager_device_id,NEW.approved_at);
END;
CREATE TRIGGER allowance_void_claim_audit AFTER INSERT ON allowance_void_send_claims
BEGIN
 INSERT INTO allowance_void_audit(application_id,workspace_id,kind,evidence_hash,actor_id,device_id,recorded_at)
 VALUES(NEW.application_id,NEW.workspace_id,'sending_claimed',NEW.official_snapshot_hash,
 NEW.manager_actor_id,NEW.manager_device_id,NEW.claimed_at);
END;
CREATE TRIGGER allowance_void_request_no_delete BEFORE DELETE ON allowance_void_requests
BEGIN SELECT RAISE(ABORT,'VOID_REQUEST_IMMUTABLE'); END;
CREATE TRIGGER allowance_void_approvals_no_update BEFORE UPDATE ON allowance_void_approvals
BEGIN SELECT RAISE(ABORT,'VOID_APPROVAL_IMMUTABLE'); END;
CREATE TRIGGER allowance_void_approvals_no_delete BEFORE DELETE ON allowance_void_approvals
BEGIN SELECT RAISE(ABORT,'VOID_APPROVAL_IMMUTABLE'); END;
CREATE TRIGGER allowance_void_claims_no_update BEFORE UPDATE ON allowance_void_send_claims
BEGIN SELECT RAISE(ABORT,'VOID_CLAIM_PERMANENT'); END;
CREATE TRIGGER allowance_void_claims_no_delete BEFORE DELETE ON allowance_void_send_claims
BEGIN SELECT RAISE(ABORT,'VOID_CLAIM_PERMANENT'); END;
CREATE TRIGGER allowance_void_audit_no_update BEFORE UPDATE ON allowance_void_audit
BEGIN SELECT RAISE(ABORT,'VOID_AUDIT_APPEND_ONLY'); END;
CREATE TRIGGER allowance_void_audit_no_delete BEFORE DELETE ON allowance_void_audit
BEGIN SELECT RAISE(ABORT,'VOID_AUDIT_APPEND_ONLY'); END;
