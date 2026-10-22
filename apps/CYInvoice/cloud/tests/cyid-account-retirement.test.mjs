import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';

const migrations = fs.readdirSync('migrations').filter(name => /^\d{4}_.*\.sql$/.test(name)).sort();
assert.equal(migrations.at(-1), '0021_allowance_automatic_execution.sql',
  'CYID-only retirement is opt-in maintenance, not an automatic Built-in migration');
const maintenance = fs.readFileSync('maintenance/retire-built-in-for-cyid-only.sql', 'utf8');
assert.match(maintenance, /CREATE TABLE cyid_storage_profile/);
assert.doesNotMatch(maintenance, /DROP TABLE (workspaces|devices|device_pairing_codes|security_audit_events|email_otp_challenges)/i);

function makeDatabase({ secondWorkspace = false, transitioning = false, pending = false } = {}) {
  const db = new DatabaseSync(':memory:');
  // Rehearse the original, already-completed CYID_ONLY retirement at the
  // original storage13 checkpoint, before newly proposed V3.0 additions.
  for (const migration of migrations.filter(name => name < '0014'))
    db.exec(fs.readFileSync('migrations/' + migration, 'utf8'));
  db.exec(`
    INSERT INTO workspaces (workspace_id, display_name, recovery_email, recovery_email_verified_at)
      VALUES ('workspace_synthetic', 'Synthetic', 'owner@example.test', '2026-10-01');
    INSERT INTO devices (device_id, workspace_id, display_name, token_hash, status, employee_authority_state)
      VALUES ('device_synthetic', 'workspace_synthetic', 'Original Device', 'token_digest_synthetic',
        'active', '${transitioning ? 'transitioning' : 'cloud'}');
    INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name, email_normalized, role, enabled,
      credential_algorithm, credential_verifier, credential_version)
      VALUES ('built_in_owner', 'workspace_synthetic', '0001', 'Synthetic', 'owner@example.test',
        'SUPER_ADMIN', 1, 'pbkdf2-sha256', 'synthetic_verifier', 1);
    INSERT INTO device_employee_links (device_id, employee_id, local_employee_no)
      VALUES ('device_synthetic', 'built_in_owner', '0001');
    INSERT INTO employee_link_candidates (candidate_id, workspace_id, device_id, local_employee_no, local_name,
      local_email_normalized, state, resolved_employee_id)
      VALUES ('candidate_synthetic', 'workspace_synthetic', 'device_synthetic', '0001', 'Synthetic',
        'owner@example.test', 'linked', 'built_in_owner');
    INSERT INTO employee_transition_items (transition_item_id, workspace_id, device_id, local_employee_no,
      local_name, local_email_normalized, local_role, suggested_cloud_role, state, match_kind, matched_employee_id)
      VALUES ('transition_synthetic', 'workspace_synthetic', 'device_synthetic', '0001', 'Synthetic',
        'owner@example.test', 'SUPER_ADMIN', 'SUPER_ADMIN', '${pending ? 'conflict' : 'ready'}',
        'bootstrap_owner', 'built_in_owner');
    INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id,
      issued_by_employee_id, delivery_state, expires_at, created_at)
      VALUES ('inv_legacy', 'workspace_synthetic', 'legacy_hash', 'device_synthetic',
        'built_in_owner', 'sent', '2030-01-01', '2026-01-01');
    INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id,
      issued_by_cyid_employee_id, delivery_state, expires_at, created_at)
      VALUES ('inv_cyid', 'workspace_synthetic', 'cyid_hash', 'device_synthetic',
        'cyid_actor', 'sent', '2030-01-01', '2026-01-01');
    INSERT INTO security_audit_events (event_id, workspace_id, event_type, outcome,
      actor_device_id, actor_employee_id, occurred_at)
      VALUES ('audit_legacy', 'workspace_synthetic', 'device_joined', 'success', 'device_synthetic',
        'built_in_owner', '2026-01-01');
    INSERT INTO email_otp_challenges (challenge_id, purpose, scope_key, email_normalized, otp_digest,
      expires_at, resend_after) VALUES
      ('otp_device', 'device_pairing_authorization', 'workspace_synthetic:device_synthetic:cyid_actor',
        'owner@example.test', '${'a'.repeat(64)}', '2030-01-01', '2026-01-01'),
      ('otp_old', 'employee_password_recovery', 'workspace_synthetic', 'owner@example.test',
        '${'b'.repeat(64)}', '2030-01-01', '2026-01-01');
  `);
  if (secondWorkspace)
    db.exec("INSERT INTO workspaces (workspace_id, display_name) VALUES ('another_workspace', 'Another')");
  assert.equal(db.prepare('PRAGMA foreign_key_check').all().length, 0);
  return db;
}
function tableExists(db, name) {
  return db.prepare("SELECT 1 AS ok FROM sqlite_master WHERE type='table' AND name=?").get(name)?.ok === 1;
}
function transact(db) {
  db.exec('BEGIN');
  try {
    db.exec(maintenance);
    assert.equal(db.prepare('PRAGMA foreign_key_check').all().length, 0);
    db.exec('COMMIT');
  } catch (error) {
    db.exec('ROLLBACK');
    throw error;
  }
}

// Retiring Chihyuan's CYID-only storage must preserve all Device and historical
// Invitation/Audit rows, but remove the Built-in account tables and credentials.
const db = makeDatabase();
const beforeDevice = db.prepare("SELECT * FROM devices WHERE device_id='device_synthetic'").get();
const beforeInvitations = db.prepare('SELECT * FROM device_invitations ORDER BY invitation_id').all();
const beforeAudit = db.prepare("SELECT * FROM security_audit_events WHERE event_id='audit_legacy'").get();
transact(db);
for (const table of ['cloud_employees', 'device_employee_links', 'employee_link_candidates', 'employee_transition_items'])
  assert.equal(tableExists(db, table), false, table + ' must be fully retired');
for (const table of ['workspaces', 'devices', 'device_pairing_codes', 'device_invitations',
  'email_otp_challenges', 'security_audit_events', 'cyid_storage_profile'])
  assert.equal(tableExists(db, table), true, table + ' must be preserved');
assert.deepEqual(db.prepare("SELECT * FROM devices WHERE device_id='device_synthetic'").get(), beforeDevice);
assert.deepEqual(db.prepare('SELECT * FROM device_invitations ORDER BY invitation_id').all(), beforeInvitations);
assert.deepEqual(db.prepare("SELECT * FROM security_audit_events WHERE event_id='audit_legacy'").get(), beforeAudit);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM email_otp_challenges WHERE purpose='device_pairing_authorization'").get().n, 1);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM email_otp_challenges WHERE purpose='employee_password_recovery'").get().n, 0);
const retiredRecovery = db.prepare("SELECT recovery_email, recovery_email_verified_at FROM workspaces WHERE workspace_id='workspace_synthetic'").get();
assert.equal(retiredRecovery.recovery_email, null);
assert.equal(retiredRecovery.recovery_email_verified_at, null);
assert.equal(db.prepare("SELECT profile FROM cyid_storage_profile").get().profile, 'CYID_ONLY');
assert.equal(db.prepare("PRAGMA foreign_key_check").all().length, 0);
assert.throws(() => transact(db), /already exists/i, 'maintenance must refuse a second execution');
db.close();

// When not explicitly selected, a Built-in/self-hosted database remains entirely
// intact, with working account table FKs and the original legacy invitation FK.
const builtIn = makeDatabase();
assert.equal(tableExists(builtIn, 'cloud_employees'), true);
assert.equal(builtIn.prepare('SELECT COUNT(*) AS n FROM cloud_employees').get().n, 1);
assert.equal(builtIn.prepare("SELECT recovery_email FROM workspaces WHERE workspace_id='workspace_synthetic'").get().recovery_email, 'owner@example.test');
assert.equal(builtIn.prepare("PRAGMA foreign_key_list('device_invitations')").all()
  .some(row => row.table === 'cloud_employees'), true);
builtIn.close();

// Fail closed on unexpected tenant count and incomplete employee migration;
// the transaction wrapper is required for no partial schema change on failure.
// Schema 0013 already enforces a single Workspace at storage level.
assert.throws(() => makeDatabase({ secondWorkspace: true }), /UNIQUE constraint failed/i);
for (const scenario of [
  { transitioning: true }, { pending: true },
]) {
  const invalid = makeDatabase(scenario);
  assert.throws(() => transact(invalid), /CHECK constraint failed/i);
  assert.equal(tableExists(invalid, 'cloud_employees'), true);
  assert.equal(tableExists(invalid, 'cyid_storage_profile'), false);
  assert.equal(invalid.prepare('PRAGMA foreign_key_check').all().length, 0);
  invalid.close();
}

console.log('CYID-only optional D1 retirement: 1 success, 1 Built-in preservation, 3 fail-closed scenarios passed');
