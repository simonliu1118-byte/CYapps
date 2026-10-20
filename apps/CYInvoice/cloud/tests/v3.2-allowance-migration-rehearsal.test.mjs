import assert from "node:assert/strict";
import {mkdtempSync,readFileSync,readdirSync,rmSync,copyFileSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {DatabaseSync} from "node:sqlite";

// V3.2-C migration engineering rehearsal ONLY. Source under drafts remains
// intentionally review-only. No remote D1, Worker or Cloudflare network.
// This tests real non-empty schema16 CYID-only database, backup and restore.
const dir=mkdtempSync(join(tmpdir(),"cyinvoice-c-migration-"));
const live=join(dir,"original-schema16.sqlite"),backup=join(dir,"backup-schema16.sqlite");
const restored=join(dir,"restored-schema16.sqlite");
const db=new DatabaseSync(live);
const files=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
assert.equal(files.at(-1),"0018_allowance_frozen_ledger.sql",
  "canonical migrations must contain reviewed 0017 and 0018 in order");
function tables(c){
 return c.prepare("SELECT name,type,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
}
function scalar(c,q){return c.prepare(q).get().n;}
function migration(c,file){
 c.exec("BEGIN IMMEDIATE");
 try{c.exec(readFileSync(file,"utf8"));c.exec("COMMIT");}
 catch(e){c.exec("ROLLBACK");throw e;}
}
try{
 db.exec("PRAGMA foreign_keys=ON");
 for(const f of files.filter(x=>x<"0014"))
  db.exec(readFileSync("migrations/"+f,"utf8"));
 db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES('ws_c_migration','synthetic')")
  .run();
 db.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,
 employee_authority_state) VALUES
 ('dev_11111111-1111-4111-8111-111111111111','ws_c_migration',
 'synthetic','active','cloud')`).run();
 db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of files.filter(x=>x>="0014"&&x<"0017"))
  db.exec(readFileSync("migrations/"+f,"utf8"));
 db.prepare(`INSERT INTO company_settings(
 workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
 changed_by_employee_id,changed_by_device_id,changed_at)
 VALUES('ws_c_migration','12345678','synthetic_ciphertext',1,
 'synthetic','dev_11111111-1111-4111-8111-111111111111',
 '2026-10-20T00:00:00.000Z')`).run();
 const before=tables(db);
 assert.equal(db.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
 assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
 // Backup must be written before ANY schema17/18 statement is evaluated.
 db.exec("PRAGMA wal_checkpoint(TRUNCATE)");
 copyFileSync(live,backup);
 const original=new DatabaseSync(backup,{readOnly:true});
 try{
  assert.deepEqual(tables(original),before);
  assert.equal(scalar(original,"SELECT COUNT(*) n FROM workspaces"),1);
  assert.equal(scalar(original,"SELECT COUNT(*) n FROM devices"),1);
 }finally{original.close();}
 // A deliberate failure while executing 0017 rolls ALL its changes back.
 const d17=readFileSync("migrations/0017_allowance_applications.sql","utf8");
 const d18=readFileSync("migrations/0018_allowance_frozen_ledger.sql","utf8");
 assert.throws(()=>{
  db.exec("BEGIN IMMEDIATE");
  try{db.exec(d17+"\nSELECT name FROM __missing_migration_audit_table;");
   db.exec("COMMIT");}
  catch(e){db.exec("ROLLBACK");throw e;}
 },/no such table/);
 assert.deepEqual(tables(db),before,"failed 0017 must not leak partial triggers/tables");
 // Verify sequential migration boundaries, including a failed 0018.
 migration(db,"migrations/0017_allowance_applications.sql");
 const after17=tables(db);
 assert.ok(after17.some(x=>x.name==="allowance_applications"));
 assert.ok(after17.some(x=>x.name==="allowance_application_audit_events"));
 assert.ok(!after17.some(x=>x.name==="allowance_number_ledger"));
 assert.throws(()=>{
  db.exec("BEGIN IMMEDIATE");
  try{db.exec(d18+"\nSELECT name FROM __missing_migration_audit_table;");
   db.exec("COMMIT");}
  catch(e){db.exec("ROLLBACK");throw e;}
 },/no such table/);
 assert.deepEqual(tables(db),after17,"failed 0018 must not partially install Ledger");
 migration(db,"migrations/0018_allowance_frozen_ledger.sql");
 const after18=tables(db);
 for(const name of ["allowance_applications","allowance_application_audit_events",
  "allowance_frozen_requests","allowance_execution_approvals",
  "allowance_number_ledger","allowance_reconciliation_checks"])
  assert.ok(after18.some(x=>x.name===name),"missing schema18 "+name);
 for(const entry of before)
  assert.deepEqual(after18.find(x=>x.type===entry.type&&x.name===entry.name),
   entry,"existing official CYID/Device/workspace object changed "+entry.name);
 assert.equal(scalar(db,"SELECT COUNT(*) n FROM workspaces"),1);
 assert.equal(scalar(db,"SELECT COUNT(*) n FROM devices"),1);
 assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
 assert.equal(db.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
 // Never infer that a real D1 is at schema18 from this synthetic execution.
 // Restoring a backup is a disaster-recovery action, not inverse migration
 // SQL: SQLite/D1 does not offer safe downgrade for live financial records.
 copyFileSync(backup,restored);
 const recovery=new DatabaseSync(restored,{readOnly:true});
 try{
  assert.deepEqual(tables(recovery),before);
  assert.equal(recovery.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  assert.deepEqual(recovery.prepare("PRAGMA foreign_key_check").all(),[]);
  assert.equal(scalar(recovery,"SELECT COUNT(*) n FROM allowance_applications WHERE 1=0"),0);
 }catch(e){
  if(/no such table: allowance_applications/.test(e.message)){
   assert.ok(!tables(recovery).some(x=>x.name==="allowance_applications"));
  }else throw e;
 }finally{recovery.close();}
 console.log("CYInvoice C migration rehearsal: nonempty schema16 backup, atomic draft17/18, rollback after injected failure, unchanged CYID tables and restored DB passed");
}finally{db.close();rmSync(dir,{recursive:true,force:true});}
