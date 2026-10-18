import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";

// Use real migrations and isolated SQLite, not any Cloudflare Worker, remote D1,
// application secret, or historical live Identity/Device data.
const migrations = readdirSync("migrations").filter(name => /^[0-9]{4}_.*[.]sql$/.test(name) && name < "0016").sort();
assert.equal(migrations.at(-1), "0015_company_settings.sql");
assert.deepEqual(migrations.slice(-3), [
  "0013_cyid_invitation_actor.sql",
  "0014_invitation_recovery_metadata.sql",
  "0015_company_settings.sql"
]);
// This historical recovery fixture intentionally ends at storage15.
const optionalScript = readFileSync("maintenance/enable-cyweb-recovery-issuer.sql", "utf8");
const retireScript = readFileSync("maintenance/retire-built-in-for-cyid-only.sql", "utf8");

function apply(db, sql) {
  db.exec("BEGIN");
  try {
    db.exec(sql);
    const invalid = db.prepare("PRAGMA foreign_key_check").all();
    assert.deepEqual(invalid, [], "no foreign-key violations");
    db.exec("COMMIT");
  } catch (error) {
    db.exec("ROLLBACK");
    throw error;
  }
}

function seed() {
  const db = new DatabaseSync(":memory:");
  for (const name of migrations.filter(n => n < "0014")) db.exec(readFileSync("migrations/" + name, "utf8"));
  db.exec(`
    INSERT INTO workspaces (workspace_id, display_name)
      VALUES ('workspace_fixture', 'Synthetic test workspace');
    INSERT INTO devices (device_id, workspace_id, display_name, token_hash,
      status, employee_authority_state)
      VALUES ('device_fixture', 'workspace_fixture', 'Synthetic device', 'digest_fixture',
        'active', 'cloud');
    INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name,
      email_normalized, role, enabled)
      VALUES ('built_in_fixture', 'workspace_fixture', '0001', 'Synthetic owner',
        'test@example.test', 'SUPER_ADMIN', 1);
    INSERT INTO device_invitations (invitation_id, workspace_id, code_hash,
      issued_by_device_id, issued_by_employee_id, delivery_state, expires_at, created_at)
      VALUES ('inv_legacy', 'workspace_fixture', 'digest_legacy',
        'device_fixture', 'built_in_fixture', 'sent', '2030-01-01', '2026-10-01');
    INSERT INTO device_invitations (invitation_id, workspace_id, code_hash,
      issued_by_device_id, issued_by_cyid_employee_id, delivery_state, expires_at, created_at)
      VALUES ('inv_cyid_device', 'workspace_fixture', 'digest_cyid',
        'device_fixture', 'external_cyid_fixture', 'sent', '2030-01-01', '2026-10-02');
    INSERT INTO security_audit_events (event_id, workspace_id, event_type,
      outcome, actor_device_id, actor_employee_id, invitation_id, occurred_at)
      VALUES ('evt_legacy', 'workspace_fixture', 'invitation_issued',
        'success', 'device_fixture', 'built_in_fixture', 'inv_legacy', '2026-10-01');
  `);
  return db;
}

function count(db, table) {
  return db.prepare("SELECT COUNT(*) AS n FROM " + table).get().n;
}
function historical(db) {
  return {
    legacy: db.prepare("SELECT invitation_id, issued_by_device_id, issued_by_employee_id, delivery_state, expires_at FROM device_invitations WHERE invitation_id='inv_legacy'").get(),
    cyid: db.prepare("SELECT invitation_id, issued_by_device_id, issued_by_cyid_employee_id, delivery_state, expires_at FROM device_invitations WHERE invitation_id='inv_cyid_device'").get(),
    audit: db.prepare("SELECT event_id, event_type, actor_device_id, actor_employee_id, invitation_id, occurred_at FROM security_audit_events WHERE event_id='evt_legacy'").get(),
  };
}

// Canonical forward migration keeps existing Built-in/self-hosted integrity.
const builtIn = seed();
const historicalBuiltIn = historical(builtIn);
apply(builtIn, readFileSync("migrations/0014_invitation_recovery_metadata.sql", "utf8"));
assert.deepEqual(historical(builtIn), historicalBuiltIn);
assert.equal(count(builtIn, "device_invitations"), 2);
assert.equal(builtIn.prepare("SELECT issuer_channel,purpose,recovery_operation_id FROM device_invitations WHERE invitation_id='inv_legacy'").get().issuer_channel, "DEVICE");
assert.equal(builtIn.prepare("SELECT purpose FROM device_invitations WHERE invitation_id='inv_cyid_device'").get().purpose, "JOIN");
assert.equal(builtIn.prepare("SELECT actor_provider FROM security_audit_events WHERE event_id='evt_legacy'").get().actor_provider, "UNKNOWN_LEGACY");
assert.equal(builtIn.prepare("PRAGMA foreign_key_list('device_invitations')").all().some(r => r.table === "cloud_employees"), true);
assert.throws(() => builtIn.prepare(`INSERT INTO device_invitations
  (invitation_id,workspace_id,code_hash,issued_by_device_id,issued_by_cyid_employee_id,
   delivery_state,expires_at,created_at,issuer_channel,purpose,recovery_operation_id)
  VALUES ('inv_denied','workspace_fixture','digest_denied',NULL,'external_cyid_fixture',
          'pending','2030-01-01','2026-10-03','CYWEB','RECOVERY','operation_denied')`).run(),
  /NOT NULL constraint failed/, "Builtin must not admit an invitation without a Device issuer");
assert.throws(() => apply(builtIn, optionalScript),
  /no such table: cyid_storage_profile/, "Optional schema relaxation must reject Built-in");
assert.equal(builtIn.prepare("SELECT COUNT(*) AS n FROM sqlite_master WHERE name='cyweb_recovery_storage_profile'").get().n, 0);
builtIn.close();

// CYID_ONLY original D1 was already retired under the earlier 0013 schema;
// only now do we apply canonical 0014 and separately approved V3 preparation.
const cyidOnly = seed();
apply(cyidOnly, retireScript);
const historicalCyid = historical(cyidOnly);
assert.equal(count(cyidOnly, "cyid_storage_profile"), 1);
apply(cyidOnly, readFileSync("migrations/0014_invitation_recovery_metadata.sql", "utf8"));
assert.deepEqual(historical(cyidOnly), historicalCyid);
assert.equal(cyidOnly.prepare("PRAGMA foreign_key_list('device_invitations')").all().some(r => r.table === "cloud_employees"), false);
apply(cyidOnly, optionalScript);
assert.deepEqual(historical(cyidOnly), historicalCyid);
assert.equal(count(cyidOnly, "cyweb_recovery_storage_profile"), 1);
assert.equal(cyidOnly.prepare("SELECT issuer_channel,purpose FROM device_invitations WHERE invitation_id='inv_legacy'").get().issuer_channel, "DEVICE");
assert.equal(cyidOnly.prepare("SELECT actor_provider FROM security_audit_events WHERE event_id='evt_legacy'").get().actor_provider, "UNKNOWN_LEGACY");

const operationId = "11111111-1111-4111-8111-111111111111";
const insert = cyidOnly.prepare(`INSERT INTO device_invitations (
  invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_cyid_employee_id,
  delivery_state, expires_at, created_at, issuer_channel, purpose,
  issuer_credential_version, issuer_employee_revision, recovery_operation_id)
  VALUES (?, 'workspace_fixture', ?, ?, 'external_cyid_fixture',
          'pending', '2030-01-01', '2026-10-13',
          ?, ?, 3, 7, ?)`);
assert.throws(() => insert.run("inv_bad_device", "hash_bad_device", "device_fixture", "CYWEB", "RECOVERY", "operation_bad_device"),
  /CHECK constraint failed/, "Recovery cannot impersonate a physical Device issuer");
assert.throws(() => insert.run("inv_bad_purpose", "hash_bad_purpose", null, "CYWEB", "JOIN", "operation_bad_purpose"),
  /CHECK constraint failed/, "CYWEB cannot create ordinary Device JOIN grants");
assert.throws(() => insert.run("inv_bad_join", "hash_bad_join", null, "DEVICE", "JOIN", "operation_bad_join"),
  /CHECK constraint failed/, "Device issuer must actually exist for regular JOIN");
assert.equal(insert.run("inv_11111111-1111-4111-8111-111111111111", "hash_recovery", null, "CYWEB", "RECOVERY", operationId).changes, 1);
assert.throws(() => insert.run("inv_duplicate", "hash_duplicate", null, "CYWEB", "RECOVERY", operationId),
  /UNIQUE constraint failed/, "one issuer operation must not create two invitation rows");
assert.equal(count(cyidOnly, "device_invitations"), 3);
cyidOnly.prepare(`INSERT INTO security_audit_events
  (event_id,workspace_id,event_type,outcome,actor_employee_id,invitation_id,
   occurred_at,actor_provider,issuer_channel,purpose)
  VALUES ('evt_recovery','workspace_fixture','recovery_invitation_issued','success',
          'external_cyid_fixture','inv_11111111-1111-4111-8111-111111111111',
          '2026-10-13','CYID','CYWEB','RECOVERY')`).run();
assert.equal(count(cyidOnly, "security_audit_events"), 2);
assert.deepEqual(cyidOnly.prepare("PRAGMA foreign_key_check").all(), []);
assert.throws(() => apply(cyidOnly, optionalScript), /already exists/i, "profile initialization is one-shot");
cyidOnly.close();

console.log("CYInvoice V3 recovery invitation schema: Built-in FK preserved, CYID-only issuer relaxation guarded, historical rows and audit intact, operation-id unique, FK clean");
