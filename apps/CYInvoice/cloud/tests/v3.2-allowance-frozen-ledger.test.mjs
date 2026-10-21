import assert from "node:assert/strict";
import {readFileSync,readdirSync,mkdtempSync,copyFileSync,rmSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {randomBytes,createCipheriv,createDecipheriv} from "node:crypto";
import {DatabaseSync} from "node:sqlite";

// C2 synthetic canonical migration check from schema16; no remote D1 operations.
// No Worker, D1 network, credentials, official lookup or AMEGO g0401/g0501.
const migrationFiles=readdirSync("migrations")
 .filter(n=>/^\d{4}_.*\.sql$/.test(n)).sort();
assert.equal(migrationFiles.at(-1),"0019_allowance_manual_review_audit.sql",
 "canonical allowance migration order must end with 0018");
const c1=readFileSync("migrations/0017_allowance_applications.sql","utf8");
const c2=readFileSync("migrations/0018_allowance_frozen_ledger.sql","utf8");
const temp=mkdtempSync(join(tmpdir(),"cyinvoice-c2-"));
const source=join(temp,"schema16.sqlite"),backup=join(temp,"schema16-backup.sqlite");
const db=new DatabaseSync(source);
const sha=x=>x.repeat(64);
const ws="ws_c2",dev="dev_c2",now="2026-10-19T07:00:00.000Z";
const encKey=randomBytes(32);
function encryptTemplate(text) {
 const nonce=randomBytes(12),c=createCipheriv("aes-256-gcm",encKey,nonce);
 const cipher=Buffer.concat([c.update(text,"utf8"),c.final()]);
 return Buffer.concat([nonce,c.getAuthTag(),cipher]).toString("base64");
}
function decryptTemplate(base64) {
 const payload=Buffer.from(base64,"base64");
 const d=createDecipheriv("aes-256-gcm",encKey,payload.subarray(0,12));
 d.setAuthTag(payload.subarray(12,28));
 return Buffer.concat([d.update(payload.subarray(28)),d.final()]).toString("utf8");
}
function setup() {
 for(const name of migrationFiles.filter(n=>n<"0014"))
  db.exec(readFileSync("migrations/"+name,"utf8"));
 db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'synthetic')").run(ws);
 db.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
 VALUES (?,?,'synthetic','active','cloud')`).run(dev,ws);
 db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const name of migrationFiles.filter(n=>n>="0014"&&n<"0017"))
  db.exec(readFileSync("migrations/"+name,"utf8"));
 db.prepare(`INSERT INTO company_settings
 (workspace_id,seller_invoice,amego_app_key_ciphertext,revision,changed_by_employee_id,
 changed_by_device_id,changed_at) VALUES (?,'12345678','synthetic_non_secret',1,'admin',?,?)`)
  .run(ws,dev,now);
}
const objects=()=>db.prepare(`SELECT type,name,sql FROM sqlite_master
 WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name`).all();
const integrity=()=>{
 assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
 assert.equal(db.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
};
const addApplication=(id,hash=sha("a"))=>db.prepare(`INSERT INTO allowance_applications
 (application_id,workspace_id,opened_by_device_id,updated_by_device_id,
 environment,reference_hash,request_hash,requested_gross,
 requester_actor_id,actor_id,created_at,updated_at)
 VALUES (?,?,?,?,'test',?,?,'50','employee','employee',?,?)`)
 .run(id,ws,dev,dev,hash,sha("b"),now,now);
const addFrozen=(id,fingerprint=sha("c"),version=1)=>{
 const privateText=JSON.stringify({OriginalInvoiceNumber:"AB12345678",
  ProductItem:[{OriginalDescription:"synthetic confidential buyer line",Amount:"50"}]});
 const encrypted=encryptTemplate(privateText);
 db.prepare(`INSERT INTO allowance_frozen_requests
 (application_id,workspace_id,seller_invoice,environment,
 company_settings_revision,request_hash,original_invoice_fingerprint,
 allowance_day,template_ciphertext,encrypted_by_device_id,frozen_at)
 VALUES (?,?,'12345678','test',?,?,?,'20261019',?,?,?)`)
  .run(id,ws,version,sha("b"),fingerprint,encrypted,dev,now);
 return {privateText,encrypted};
};
const transition=(id,status,manager,actor,revision)=>{
 return db.prepare(`UPDATE allowance_applications
 SET status=?,revision=revision+1,manager_actor_id=?,actor_id=?,updated_at=?
 WHERE application_id=? AND revision=?`)
 .run(status,manager,actor,now,id,revision);
};
const approve=(id)=>{
 assert.equal(transition(id,"review","admin","admin",1).changes,1);
 assert.throws(()=>transition(id,"approved","admin","admin",2),/ALLOWANCE_APPROVAL_REQUIRED/);
 db.prepare(`INSERT INTO allowance_execution_approvals
 (approval_id,application_id,workspace_id,reviewed_revision,request_hash,
 official_snapshot_hash,official_checked_at,manager_actor_id,manager_device_id,
 approved_at,valid_until_utc)
 VALUES(?,?,?,2,?,?,?,'admin',?,?,?)`)
 .run("approval_"+id,id,ws,sha("b"),sha("d"),now,dev,now,"2026-11-01T00:00:00.000Z");
 assert.equal(transition(id,"approved","admin","admin",2).changes,1);
 assert.equal(transition(id,"approved","admin","admin",2).changes,0,"stale CAS cannot approve twice");
};
const reserve=(id,seq,number,attempt,ledger)=>db.prepare(`INSERT INTO allowance_number_ledger
 (ledger_id,application_id,workspace_id,seller_invoice,environment,
 allowance_day,sequence,allowance_number,attempt,request_hash,status,
 created_by_device_id,created_at,updated_at)
 VALUES(?,?,?,'12345678','test','20261019',?,?,?,?,'reserved',?,?,?)`)
 .run(ledger,id,ws,seq,number,attempt,sha("b"),dev,now,now);
const change=(ledger,status,code=null)=>db.prepare(`UPDATE allowance_number_ledger
 SET status=?,collision_code=?,updated_at=? WHERE ledger_id=? AND status=?`)
 .run(status,code,now,ledger,status==="sending"?"reserved":"sending");
try {
 setup();
 const baseline=objects();
 copyFileSync(source,backup);
 db.exec("BEGIN IMMEDIATE");
 try {db.exec(c1+"\n"+c2+"\nSELECT * FROM nonexistent_c2;");db.exec("COMMIT");}
 catch(e){db.exec("ROLLBACK"); assert.match(String(e),/no such table/);}
 assert.deepEqual(objects(),baseline,"failed schema change must atomically roll back");
 db.exec("BEGIN IMMEDIATE");
 try {db.exec(c1+"\n"+c2);db.exec("COMMIT");}
 catch(e){db.exec("ROLLBACK");throw e;}
 for(const original of baseline)assert.deepEqual(
  objects().find(o=>o.name===original.name&&o.type===original.type),original,
  "all historical schema objects must remain intact: "+original.name);
 const first="application_c2_first",second="application_c2_second";
 assert.throws(()=>db.prepare(`INSERT INTO allowance_applications
 (application_id,workspace_id,opened_by_device_id,updated_by_device_id,
 environment,reference_hash,request_hash,requested_gross,requester_actor_id,
 actor_id,created_at,updated_at) VALUES ('app_bad','another_workspace',?,?,'test',?,?,'50',
 'employee','employee',?,?)`).run(dev,dev,sha("a"),sha("b"),now,now),
 /ALLOWANCE_SOURCE_DEVICE_INVALID|FOREIGN KEY/);
 addApplication(first);
 assert.throws(()=>transition(first,"review","admin","admin",1),/ALLOWANCE_FROZEN_REQUIRED/);
 assert.throws(()=>addFrozen(first,sha("c"),2),/ALLOWANCE_FROZEN_SOURCE_INVALID/);
 const frozen=addFrozen(first);
 assert.equal(decryptTemplate(frozen.encrypted),frozen.privateText);
 assert.ok(!frozen.encrypted.includes("AB12345678"));
 assert.throws(()=>db.prepare("UPDATE allowance_frozen_requests SET template_ciphertext='tampered'").run(),
  /ALLOWANCE_FROZEN_IMMUTABLE/);
 assert.throws(()=>db.prepare("DELETE FROM allowance_frozen_requests").run(),
  /ALLOWANCE_FROZEN_IMMUTABLE/);
 assert.throws(()=>reserve(first,1,"A20261019001",1,"attempt_before_approval"),
  /ALLOWANCE_RESERVATION_INVALID/);
 approve(first);
 assert.throws(()=>db.prepare("DELETE FROM allowance_execution_approvals").run(),
  /ALLOWANCE_APPROVAL_APPEND_ONLY/);
 assert.throws(()=>db.prepare("UPDATE allowance_execution_approvals SET request_hash=?").run(sha("e")),
  /ALLOWANCE_APPROVAL_APPEND_ONLY/);
 reserve(first,1,"A20261019001",1,"ledger_one");
 assert.throws(()=>reserve(first,2,"A20261019002",2,"ledger_premature"),
  /ALLOWANCE_RESERVATION_INVALID/);
 assert.throws(()=>db.prepare("DELETE FROM allowance_number_ledger").run(),
  /ALLOWANCE_LEDGER_PERMANENT/);
 assert.throws(()=>db.prepare("UPDATE allowance_number_ledger SET allowance_number='A20261019003'").run(),
  /ALLOWANCE_LEDGER_TRANSITION_INVALID/);
 assert.equal(change("ledger_one","sending").changes,1);
 assert.equal(change("ledger_one","sending").changes,0,"never regain a second send claim");
 assert.throws(()=>change("ledger_one","reserved"),/ALLOWANCE_LEDGER_TRANSITION_INVALID/);
 // A different legally approved request may concern the same invoice;
 // it must not SEND while the original number outcome remains unknown.
 addApplication(second,sha("f"));
 addFrozen(second,sha("c"));
 approve(second);
 reserve(second,2,"A20261019002",1,"ledger_two");
 assert.throws(()=>change("ledger_two","sending"),/ALLOWANCE_ORIGINAL_INVOICE_IN_FLIGHT/);
 assert.equal(change("ledger_one","occupied_external",4040161).changes,1);
 assert.equal(change("ledger_two","sending").changes,1);
 assert.equal(change("ledger_two","confirmed").changes,1);
 assert.throws(()=>db.prepare("UPDATE allowance_number_ledger SET status='sending' WHERE ledger_id='ledger_two'").run(),/ALLOWANCE_LEDGER_TRANSITION_INVALID/);
 // Only a definitive collision can unlock attempt #2; older number stays.
 reserve(first,3,"A20261019003",2,"ledger_three");
 assert.equal(db.prepare(`SELECT COUNT(*) n FROM allowance_number_ledger
 WHERE application_id=?`).get(first).n,2);
 assert.throws(()=>reserve(first,4,"A20261019004",3,"ledger_four"),
  /ALLOWANCE_RESERVATION_INVALID/);
 assert.throws(()=>db.prepare(`INSERT INTO allowance_reconciliation_checks
 (check_id,ledger_id,application_id,workspace_id,allowance_number,official_snapshot_hash,
 checked_by_device_id,checked_by_actor_id,checked_at)
 VALUES('bad','ledger_three',?,?, 'A20261019003',?,?,'admin',?)`)
 .run(first,ws,sha("e"),dev,now),/ALLOWANCE_RECONCILIATION_SOURCE_INVALID/);
 db.prepare(`INSERT INTO allowance_reconciliation_checks
 (check_id,ledger_id,application_id,workspace_id,allowance_number,official_snapshot_hash,
 checked_by_device_id,checked_by_actor_id,checked_at)
 VALUES('lookup_one','ledger_one',?,?, 'A20261019001',?,?,'admin',?)`)
 .run(first,ws,sha("d"),dev,now);
 assert.throws(()=>db.prepare("DELETE FROM allowance_reconciliation_checks").run(),
  /ALLOWANCE_RECONCILIATION_APPEND_ONLY/);
 assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items").get().n,0,
  "allowances never mutate non-allowance schema16 Work Items");
 assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_application_audit_events").get().n,6);
 assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals").get().n,2);
 integrity();
} finally {db.close();}
const restored=new DatabaseSync(backup);
try {
 assert.equal(restored.prepare(`SELECT COUNT(*) n FROM sqlite_master
 WHERE name='allowance_frozen_requests'`).get().n,0);
 assert.equal(restored.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
 assert.deepEqual(restored.prepare("PRAGMA foreign_key_check").all(),[]);
}finally{restored.close();rmSync(temp,{recursive:true,force:true});}
console.log("CYInvoice C2: schema16->draft17/18, encrypted freeze, grant, ledger one-shot, collisions, audit and backup passed");
