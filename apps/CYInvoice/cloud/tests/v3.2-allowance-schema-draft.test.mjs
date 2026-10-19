import assert from "node:assert/strict";
import {readFileSync,readdirSync,mkdtempSync,copyFileSync,rmSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {DatabaseSync} from "node:sqlite";
// No remote D1 or Worker. Canonical schema16 is replayed only in a disposable DB.
const files=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
assert.equal(files.at(-1),"0016_non_allowance_work_items.sql");
const draft=readFileSync("drafts/0017_allowance_applications.sql","utf8");
const root=mkdtempSync(join(tmpdir(),"cyinvoice-c1-"));
const before=join(root,"schema16.sqlite"), after=join(root,"schema17-trial.sqlite");
const db=new DatabaseSync(before);
try {
 for(const f of files.filter(x=>x<"0014")) db.exec(readFileSync("migrations/"+f,"utf8"));
 db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES ('ws_test','synthetic')").run();
 db.prepare("INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state) VALUES ('dev_test','ws_test','synthetic','active','cloud')").run();
 db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of files.filter(x=>x>="0014"))db.exec(readFileSync("migrations/"+f,"utf8"));
 const oldObjects=()=>db.prepare("SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
 const beforeObjects=oldObjects();
 assert.throws(()=>{db.exec("BEGIN IMMEDIATE");try{db.exec(draft+"\nSELECT * FROM missing_c1_test;");db.exec("COMMIT");}catch(e){db.exec("ROLLBACK");throw e;}},/no such table/);
 assert.deepEqual(oldObjects(),beforeObjects,"failed migration has no partial DDL");
 copyFileSync(before,after);
 db.exec("BEGIN IMMEDIATE");
 try{db.exec(draft);db.exec("COMMIT");}catch(e){db.exec("ROLLBACK");throw e;}
 for(const o of beforeObjects)assert.deepEqual(oldObjects().find(x=>x.type===o.type&&x.name===o.name),o);
 const insert=(id)=>db.prepare(`INSERT INTO allowance_applications
 (application_id,workspace_id,opened_by_device_id,updated_by_device_id,environment,reference_hash,request_hash,requested_gross,requester_actor_id,actor_id,created_at,updated_at)
 VALUES(?,'ws_test','dev_test','dev_test','test',?,?, '50','employee','employee','now','now')`)
 .run(id,"a".repeat(64),"b".repeat(64));
 insert("application_one");insert("application_two"); // separate partial allowances permitted
 assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,2);
 assert.throws(()=>db.prepare("DELETE FROM allowance_applications").run(),/ALLOWANCE_APPLICATION_NOT_DELETABLE/);
 assert.throws(()=>db.prepare("UPDATE allowance_application_audit_events SET actor_id='forged'").run(),/ALLOWANCE_APPLICATION_AUDIT_APPEND_ONLY/);
 assert.throws(()=>db.prepare("UPDATE allowance_applications SET status='approved',revision=2,manager_actor_id='admin' WHERE application_id='application_one'").run(),/ALLOWANCE_APPLICATION_TRANSITION_INVALID/);
 const transition=db.prepare("UPDATE allowance_applications SET status=?,revision=revision+1,manager_actor_id=?,actor_id=?,updated_at=? WHERE application_id='application_one' AND revision=? AND status=?");
 assert.equal(transition.run("review","admin","admin","later",1,"open").changes,1);
 assert.equal(transition.run("review","admin","admin","later",1,"open").changes,0,"CAS stale revision");
 assert.equal(transition.run("approved","admin","admin","later2",2,"review").changes,1);
 assert.throws(()=>transition.run("rejected","admin","admin","later3",3,"approved"),/ALLOWANCE_APPLICATION_TRANSITION_INVALID/);
 assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_application_audit_events WHERE application_id='application_one'").get().n,3);
 assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
 assert.equal(db.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
}finally{db.close();}
const restored=new DatabaseSync(after);
try{
 assert.equal(restored.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE name='allowance_applications'").get().n,0,"restore original schema16 backup");
 assert.deepEqual(restored.prepare("PRAGMA foreign_key_check").all(),[]);
}finally{restored.close();rmSync(root,{recursive:true,force:true});}
console.log("CYInvoice C1: schema16->draft17 isolated SQL, rollback, CAS and append-only audit passed");
