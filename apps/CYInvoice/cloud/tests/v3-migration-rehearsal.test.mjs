import assert from 'node:assert/strict';
import { copyFileSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync, backup } from 'node:sqlite';

// Only synthetic local SQLite files. No production D1, test Worker or AMEGO calls.
const migrations = readdirSync('migrations').filter(x => /^\d{4}_.*\.sql$/.test(x)).sort();
assert.equal(migrations.at(-1), '0014_invitation_recovery_metadata.sql');
const drafts = ['0015_work_items.sql', '0016_allowance_number_ledger.sql',
  '0017_company_settings.sql', '0018_allowance_applications.sql',
  '0019_allowance_void_workflow.sql'];
assert.deepEqual(readdirSync('drafts').filter(x => /^\d{4}_.*\.sql$/.test(x)).sort(), drafts);
const sql = drafts.map(x => readFileSync('drafts/' + x, 'utf8'));
const tables = sql.flatMap(x => [...x.matchAll(/CREATE TABLE\s+([a-z_][a-z_0-9]*)\s*\(/gi)].map(m => m[1]));
assert.equal(new Set(tables).size, 11);
assert.equal(tables.some(x => /^(invoices?|issued_invoices|invoice_records)$/i.test(x)), false);

const schema = db => db.prepare("SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
const history = db => [
  db.prepare("SELECT workspace_id,display_name,status FROM workspaces WHERE workspace_id='synthetic_ws'").get(),
  db.prepare("SELECT device_id,workspace_id,status FROM devices WHERE device_id='synthetic_device'").get(),
  db.prepare("SELECT invitation_id,issued_by_cyid_employee_id,issuer_channel,purpose,delivery_state FROM device_invitations WHERE invitation_id='synthetic_invite'").get(),
  db.prepare("SELECT event_id,event_type,actor_employee_id,actor_provider FROM security_audit_events WHERE event_id='synthetic_audit'").get()
];
function verify(db) {
  assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(), []);
  assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check, 'ok');
}
function apply(db, statement) {
  db.exec('BEGIN IMMEDIATE');
  try {
    db.exec(statement);
    verify(db);
    db.exec('COMMIT');
  } catch (error) {
    db.exec('ROLLBACK');
    throw error;
  }
}

const dir = mkdtempSync(join(tmpdir(), 'cyinvoice-v3-migration-'));
try {
  const baseline = new DatabaseSync(join(dir, 'baseline.sqlite'));
  try {
    for (const file of migrations.filter(x => x < '0014'))
      baseline.exec(readFileSync('migrations/' + file, 'utf8'));
    baseline.exec(
      "INSERT INTO workspaces(workspace_id,display_name) VALUES ('synthetic_ws','Historical synthetic workspace');" +
      "INSERT INTO devices(device_id,workspace_id,display_name,token_hash,status,employee_authority_state)" +
      " VALUES ('synthetic_device','synthetic_ws','Historical synthetic device','synthetic_digest','active','cloud');" +
      "INSERT INTO device_invitations(invitation_id,workspace_id,code_hash,issued_by_device_id," +
      " issued_by_cyid_employee_id,delivery_state,expires_at,created_at)" +
      " VALUES ('synthetic_invite','synthetic_ws','synthetic_invite_digest','synthetic_device'," +
      " 'synthetic_cyid_actor','sent','2030-01-01','2026-10-01');" +
      "INSERT INTO security_audit_events(event_id,workspace_id,event_type,outcome," +
      " actor_device_id,actor_employee_id,invitation_id,occurred_at)" +
      " VALUES ('synthetic_audit','synthetic_ws','invitation_issued','success'," +
      " 'synthetic_device','synthetic_cyid_actor','synthetic_invite','2026-10-01');");
    // CYID_ONLY retirement was already completed under schema 0013.
    apply(baseline, readFileSync('maintenance/retire-built-in-for-cyid-only.sql', 'utf8'));
    apply(baseline, readFileSync('migrations/0014_invitation_recovery_metadata.sql', 'utf8'));
    const original = schema(baseline);
    const historical = history(baseline);
    assert.equal(baseline.prepare("SELECT COUNT(*) AS n FROM sqlite_master WHERE name='cloud_employees'").get().n, 0);
    const backupPath = join(dir, 'pristine-pre-v3.sqlite');
    await backup(baseline, backupPath);
    const snapshot = new DatabaseSync(backupPath);
    assert.deepEqual(schema(snapshot), original);
    assert.deepEqual(history(snapshot), historical);
    verify(snapshot);
    snapshot.close();

    for (let fail = 0; fail < drafts.length; fail++) {
      const casePath = join(dir, 'failure-' + fail + '.sqlite');
      copyFileSync(backupPath, casePath);
      const trial = new DatabaseSync(casePath);
      try {
        for (let i = 0; i < drafts.length; i++) {
          if (i === fail) {
            const before = schema(trial);
            assert.throws(() => apply(trial, sql[i] + '\nSELECT * FROM synthetic_missing_table;'),
              /no such table: synthetic_missing_table/, 'failure must abort the entire draft step');
            assert.deepEqual(schema(trial), before, 'failed migration must leave no partial tables/triggers');
            assert.deepEqual(history(trial), historical);
            verify(trial);
          }
          apply(trial, sql[i]);
          assert.deepEqual(history(trial), historical, 'old invitation, workspace, device and audit must survive');
        }
        const after = schema(trial);
        for (const old of original)
          assert.deepEqual(after.find(x => x.type === old.type && x.name === old.name), old);
        const added = after.filter(x => x.type === 'table'
          && !original.some(y => y.type === 'table' && y.name === x.name))
          .map(x => x.name).sort();
        assert.deepEqual(added, [...tables].sort(), 'only reviewed exceptional D1 tables may be created');
        assert.throws(() => trial.prepare(
          "INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,updated_by_device_id," +
          "environment,kind,reference_hash,actor_id,created_at,updated_at)" +
          " VALUES ('ordinary_invoice','synthetic_ws','synthetic_device','synthetic_device'," +
          "'test','INVOICE',?,'synthetic_actor','2026-10-01','2026-10-01')"
        ).run('a'.repeat(64)), /CHECK constraint failed/);
        verify(trial);
      } finally { trial.close(); }
    }
    // Restore from the original snapshot; never reverse-drop on a live D1.
    const restoredPath = join(dir, 'restore.sqlite');
    copyFileSync(backupPath, restoredPath);
    const restored = new DatabaseSync(restoredPath);
    assert.deepEqual(schema(restored), original);
    assert.deepEqual(history(restored), historical);
    for (const name of tables)
      assert.equal(restored.prepare("SELECT COUNT(*) AS n FROM sqlite_master WHERE type='table' AND name=?")
        .get(name).n, 0);
    verify(restored);
    restored.close();
  } finally { baseline.close(); }
} finally { rmSync(dir, { recursive: true, force: true }); }
console.log('CYInvoice V3 0015-0019: five transactional rollback drills, old data/FK/integrity preserved, exceptional tables only, pristine backup/restore passed');
