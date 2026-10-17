import assert from 'node:assert/strict';
import {copyFileSync,mkdtempSync,readFileSync,readdirSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {DatabaseSync,backup} from 'node:sqlite';

// V3.0 schema checkpoint ONLY: original storage13 -> CYID_ONLY retired
// business D1 -> recovery canonical 0014 -> company card canonical 0015.
// No remote D1, deploy, real Device Token, customer data or Worker.
const migrationFiles=readdirSync('migrations').filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
assert.equal(migrationFiles.at(-1),'0015_company_settings.sql');
assert.deepEqual(migrationFiles.slice(-3), [
  '0013_cyid_invitation_actor.sql',
  '0014_invitation_recovery_metadata.sql',
  '0015_company_settings.sql'
]);
const originalMigrations = migrationFiles.filter(file => file < '0014');
const drFile='migrations/0014_invitation_recovery_metadata.sql';
const cardFile='migrations/0015_company_settings.sql';
const scripts=[drFile,cardFile].map(x=>readFileSync(x,'utf8'));
const expectedTables=['company_settings','company_settings_audit_events'];

const schema=db=>db.prepare("SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
const oldFacts=db=>[
  db.prepare("SELECT workspace_id,display_name,status FROM workspaces WHERE workspace_id='synthetic_ws'").get(),
  db.prepare("SELECT device_id,workspace_id,status FROM devices WHERE device_id='synthetic_device'").get(),
  db.prepare("SELECT invitation_id,issued_by_cyid_employee_id,delivery_state FROM device_invitations WHERE invitation_id='synthetic_invite'").get(),
  db.prepare("SELECT event_id,event_type,actor_employee_id FROM security_audit_events WHERE event_id='synthetic_audit'").get(),
  db.prepare("SELECT profile,workspace_id FROM cyid_storage_profile").get()
];
function validate(db) {
  assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(),[]);
  assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check,'ok');
}
function transact(db,sql) {
  db.exec('BEGIN IMMEDIATE');
  try {
    db.exec(sql);
    validate(db);
    db.exec('COMMIT');
  } catch(e) {
    db.exec('ROLLBACK');
    throw e;
  }
}
const temp=mkdtempSync(join(tmpdir(),'cyinvoice-v30-schema-'));
try {
  const originalPath=join(temp,'before-v30.sqlite');
  const db=new DatabaseSync(originalPath);
  try {
    for(const file of originalMigrations) db.exec(readFileSync('migrations/'+file,'utf8'));
    db.exec(
      "INSERT INTO workspaces(workspace_id,display_name) VALUES ('synthetic_ws','Historical test workspace');"+
      "INSERT INTO devices(device_id,workspace_id,display_name,token_hash,status,employee_authority_state)"+
      " VALUES ('synthetic_device','synthetic_ws','Test device','fake_digest','active','cloud');"+
      "INSERT INTO device_invitations(invitation_id,workspace_id,code_hash,issued_by_device_id,"+
      " issued_by_cyid_employee_id,delivery_state,expires_at,created_at)"+
      " VALUES ('synthetic_invite','synthetic_ws','fake_invite','synthetic_device',"+
      " 'synthetic_actor','sent','2030-01-01','2026-10-01');"+
      "INSERT INTO security_audit_events(event_id,workspace_id,event_type,outcome,"+
      " actor_device_id,actor_employee_id,invitation_id,occurred_at)"+
      " VALUES ('synthetic_audit','synthetic_ws','invitation_issued','success',"+
      " 'synthetic_device','synthetic_actor','synthetic_invite','2026-10-01');"
    );
    // Match the current company database profile: CYID_ONLY with legacy
    // Employee tables already retired; do not re-create them.
    transact(db,readFileSync('maintenance/retire-built-in-for-cyid-only.sql','utf8'));
    assert.equal(db.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE name='cloud_employees'").get().n,0);
    validate(db);
    const before=schema(db), facts=oldFacts(db);
    const safeSnapshot=join(temp,'safe-snapshot.sqlite');
    await backup(db,safeSnapshot);
    const snap=new DatabaseSync(safeSnapshot);
    try {assert.deepEqual(schema(snap),before);assert.deepEqual(oldFacts(snap),facts);validate(snap);}
    finally {snap.close();}

    // Each canonical 3.0 migration must be atomic if it fails partway.
    for(let fail=0;fail<scripts.length;fail++){
      const trialPath=join(temp,'failure-'+fail+'.sqlite');
      copyFileSync(safeSnapshot,trialPath);
      const trial=new DatabaseSync(trialPath);
      try {
        for(let step=0;step<scripts.length;step++){
          if(step===fail){
            const beforeStep=schema(trial);
            assert.throws(()=>transact(trial,scripts[step]+'\nSELECT * FROM nonexistent_v30_test_table;'),
              /no such table: nonexistent_v30_test_table/);
            assert.deepEqual(schema(trial),beforeStep,'failed migration must not leave partial schema');
            assert.deepEqual(oldFacts(trial),facts,'failed migration must not change old rows');
            validate(trial);
          }
          transact(trial,scripts[step]);
          assert.deepEqual(oldFacts(trial),facts,'preserve existing CYID-only Workspace/Device/invite/audit');
          validate(trial);
        }
        assert.equal(trial.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE name='cloud_employees'").get().n,0);
        const newSchema=schema(trial);
        const intentionallyExtended=new Set([
          'device_pairing_codes','device_invitations','security_audit_events'
        ]);
        for(const item of before) {
          const actual=newSchema.find(x=>x.type===item.type&&x.name===item.name);
          assert.ok(actual,'pre-existing schema objects must not disappear: '+item.name);
          if (!intentionallyExtended.has(item.name))
            assert.deepEqual(actual,item,'unrelated schema objects must remain unchanged');
        }
        // Recovery 0014 intentionally ADDs columns to pairing/invites and
        // widens security Audit event types; historical rows remain identical.
        // These three table DDLs must change, not remain byte-for-byte equal.
        for(const table of expectedTables)
          assert.ok(trial.prepare("SELECT name FROM sqlite_master WHERE type='table' AND name=?").get(table));
        assert.ok(trial.prepare("PRAGMA table_info(device_pairing_codes)").all().some(x=>x.name==='revoked_at'));
        assert.ok(trial.prepare("PRAGMA table_info(device_invitations)").all().some(x=>x.name==='recovery_operation_id'));
        assert.ok(trial.prepare("PRAGMA table_info(security_audit_events)").all().some(x=>x.name==='actor_provider'));
        trial.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,revision,changed_by_employee_id,changed_by_device_id,changed_at) VALUES(?,?,?,?,?,?)")
          .run('synthetic_ws','12345678',1,'synthetic_actor','synthetic_device','2026-10-17');
        trial.prepare("INSERT INTO company_settings_audit_events(event_id,workspace_id,revision,actor_employee_id,actor_device_id,changed_fields,created_at) VALUES(?,?,?,?,?,?,?)")
          .run('synthetic_card_audit','synthetic_ws',1,'synthetic_actor','synthetic_device','seller_invoice','2026-10-17');
        assert.throws(()=>trial.prepare("DELETE FROM company_settings_audit_events").run(),/COMPANY_AUDIT_APPEND_ONLY/);
        assert.throws(()=>trial.prepare("UPDATE company_settings_audit_events SET changed_fields='other'").run(),/COMPANY_AUDIT_APPEND_ONLY/);
        validate(trial);
      } finally {trial.close();}
    }
    // Rollback strategy is restoring the prior backed-up database, never
    // destructively dropping the new tables on the live D1.
    const restoredPath=join(temp,'restored.sqlite');
    copyFileSync(safeSnapshot,restoredPath);
    const restored=new DatabaseSync(restoredPath);
    try {
      assert.deepEqual(schema(restored),before);
      assert.deepEqual(oldFacts(restored),facts);
      for(const table of expectedTables)
        assert.equal(restored.prepare("SELECT name FROM sqlite_master WHERE type='table' AND name=?").get(table),undefined);
      validate(restored);
    } finally {restored.close();}
  } finally {db.close();}
} finally {rmSync(temp,{recursive:true,force:true});}
console.log('CYInvoice 3.0: CYID_ONLY storage13 + recovery/company canonical migrations, rollback/restore, historic data and FK passed');