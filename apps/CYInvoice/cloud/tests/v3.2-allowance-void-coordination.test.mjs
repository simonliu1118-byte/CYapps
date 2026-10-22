import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync,readdirSync} from "node:fs";

// Isolated D1 draft contract test: never touches Cloudflare or real AMEGO.
const sql=new DatabaseSync(":memory:");
const ws="ws_void_test";
const employee="dev_11111111-1111-4111-8111-111111111111";
const manager="dev_22222222-2222-4222-8222-222222222222";
const a="a".repeat(64),b="b".repeat(64),proof="c".repeat(64);
const app="void_app_one";
const opened="2026-10-21T09:00:00.000Z";
const approved="2026-10-21T09:02:00.000Z";
const claimed="2026-10-21T09:05:00.000Z";
const expires="2026-10-21T09:17:00.000Z";
const count=name=>sql.prepare("SELECT COUNT(*) n FROM "+name).get().n;
const objects=()=>sql.prepare("SELECT name,type,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
const insert=(id,fingerprint=a,device=employee)=>sql.prepare(
 "INSERT INTO allowance_void_requests "+
 "(application_id,workspace_id,opened_by_device_id,environment,seller_invoice,company_revision,"+
 "allowance_fingerprint,request_hash,frozen_ciphertext,requester_actor_id,created_at,updated_at) "+
 "VALUES(?,?,?,'test','12345678',1,?,?,?,'employee',?,?)")
 .run(id,ws,device,fingerprint,b,"s".repeat(65),opened,opened);
const grant=()=>sql.prepare(
 "INSERT INTO allowance_void_approvals "+
 "(application_id,workspace_id,request_hash,official_snapshot_hash,manager_actor_id,"+
 "manager_device_id,approved_at,valid_until_utc) VALUES(?,?,?,?,?,?,?,?)")
 .run(app,ws,b,proof,"manager",manager,approved,expires);
const claim=(hash=proof)=>sql.prepare(
 "INSERT INTO allowance_void_send_claims "+
 "(application_id,workspace_id,environment,seller_invoice,allowance_fingerprint,"+
 "request_hash,official_snapshot_hash,manager_actor_id,manager_device_id,claimed_at) "+
 "VALUES(?,?,'test','12345678',?,?,?,'manager',?,?)")
 .run(app,ws,a,b,hash,manager,claimed);
try{
 sql.exec("PRAGMA foreign_keys=ON");
 const migrations=readdirSync("migrations")
  .filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0022_allowance_unsent_withdrawal.sql",
  "0020 is canonical; schema promotion must not expose a g0501 sender");

 // Historical schema20 constraints remain unchanged on disk. The forward
 // migration and current automatic execution are exercised separately.
 for(const f of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES(?,'Synthetic')").run(ws);
 for(const dev of [employee,manager])
  sql.prepare("INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state) "+
   "VALUES(?,?,'Synthetic','active','cloud')").run(dev,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of migrations.filter(x=>x>="0014"&&x<"0020"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 sql.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,revision,"+
  "changed_by_employee_id,changed_by_device_id,changed_at) "+
  "VALUES(?,'12345678','SYNTHETIC_NOT_SECRET',1,'manager',?,'2026-10-21T08:00:00.000Z')")
  .run(ws,manager);
 const before=objects();
 const ddl=readFileSync("migrations/0020_allowance_void_coordination.sql","utf8");
 assert.throws(()=>{
  sql.exec("BEGIN IMMEDIATE");
  try{sql.exec(ddl+"\nSELECT * FROM __void_injected_failure;");sql.exec("COMMIT");}
  catch(err){sql.exec("ROLLBACK");throw err;}
 },/no such table/);
 assert.deepEqual(objects(),before,"No partial DDL after failure");
 sql.exec("BEGIN IMMEDIATE");
 try{sql.exec(ddl);sql.exec("COMMIT");}catch(err){sql.exec("ROLLBACK");throw err;}
 for(const obj of before)assert.deepEqual(
  objects().find(x=>x.name===obj.name&&x.type===obj.type),obj,
  "Existing schema19 and identity objects must stay identical");
 assert.throws(()=>insert("void_other_device",a,"no_device"),/VOID_REQUEST_DEVICE_REQUIRED/);
 insert(app);
 assert.equal(count("allowance_void_requests"),1);
 assert.equal(count("allowance_void_audit"),1);
 assert.throws(()=>insert("void_competing"),/UNIQUE constraint failed/);
 assert.throws(()=>sql.prepare("UPDATE allowance_void_requests "+
  "SET status='approved',revision=2,manager_actor_id='manager' WHERE application_id=?").run(app),
  /VOID_REQUEST_IMMUTABLE_OR_UNAPPROVED/);
 assert.throws(()=>claim(),/VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID/);
 sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(manager);
 assert.throws(()=>grant(),/VOID_APPROVAL_SOURCE_INVALID/);
 sql.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(manager);
 // A malformed or overlong manager approval cannot become a valid send claim.
 assert.throws(()=>sql.prepare("INSERT INTO allowance_void_approvals "+
  "(application_id,workspace_id,request_hash,official_snapshot_hash,manager_actor_id,"+
  "manager_device_id,approved_at,valid_until_utc) VALUES(?,?,?,?,?,?,?,?)")
  .run(app,ws,b,proof,"manager",manager,approved,"2026-10-21T09:18:00.000Z"),
  /CHECK constraint failed/);
 grant();
 assert.throws(()=>sql.prepare("UPDATE allowance_void_requests SET request_hash=? WHERE application_id=?")
  .run(a,app),/VOID_REQUEST_IMMUTABLE_OR_UNAPPROVED/);
 assert.equal(sql.prepare("UPDATE allowance_void_requests "+
  "SET status='approved',revision=2,manager_actor_id='manager',updated_at=? "+
  "WHERE application_id=? AND revision=1 AND status='open'").run(approved,app).changes,1);
 assert.equal(sql.prepare("UPDATE allowance_void_requests "+
  "SET status='approved',revision=2 WHERE application_id=? AND revision=1").run(app).changes,0);
 assert.throws(()=>claim(a),/VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID/);
 sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(manager);
 assert.throws(()=>claim(),/VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID/);
 sql.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(manager);
 sql.prepare("UPDATE company_settings SET revision=2 WHERE workspace_id=?").run(ws);
 assert.throws(()=>claim(),/VOID_CLAIM_AUTH_OR_EVIDENCE_INVALID/);
 sql.prepare("UPDATE company_settings SET revision=1 WHERE workspace_id=?").run(ws);
 claim();
 assert.equal(count("allowance_void_send_claims"),1);
 assert.throws(()=>claim(),/UNIQUE constraint failed/);
 assert.throws(()=>insert("void_after_lost_reply"),/UNIQUE constraint failed/);
 assert.throws(()=>sql.prepare("DELETE FROM allowance_void_send_claims").run(),/VOID_CLAIM_PERMANENT/);
 assert.throws(()=>sql.prepare("UPDATE allowance_void_send_claims SET claimed_at=?").run(expires),/VOID_CLAIM_PERMANENT/);
 assert.throws(()=>sql.prepare("DELETE FROM allowance_void_requests").run(),/VOID_REQUEST_IMMUTABLE/);
 sql.prepare("INSERT INTO allowance_void_audit(application_id,workspace_id,kind,evidence_hash,"+
  "actor_id,device_id,recorded_at) "+
  "VALUES(?,?,'official_void_observed_unattributed',?,'manager',?,?)")
  .run(app,ws,proof,manager,claimed);
 assert.equal(count("allowance_void_audit"),4);
 assert.equal(sql.prepare("SELECT status FROM allowance_void_requests WHERE application_id=?")
  .get(app).status,"approved","D0501/99 does not automatically confirm/close");
 assert.throws(()=>sql.prepare("UPDATE allowance_void_audit SET kind='unknown_result'").run(),
  /VOID_AUDIT_APPEND_ONLY/);
 assert.throws(()=>sql.prepare("DELETE FROM allowance_void_audit").run(),/VOID_AUDIT_APPEND_ONLY/);
 assert.equal(sql.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
 assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 console.log("V3.2 E schema20: manager grant, persistent unique claim, no replay & immutable audit OK");
}finally{sql.close();}
