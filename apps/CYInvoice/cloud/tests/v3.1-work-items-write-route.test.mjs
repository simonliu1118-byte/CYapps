import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {createHash} from "node:crypto";
import {build} from "esbuild";

const built=await build({entryPoints:["src/work-items-route.ts"],bundle:true,
  write:false,format:"esm",platform:"node",target:"node24"});
const {handleWorkItems}=await import("data:text/javascript;base64,"+
  Buffer.from(built.outputFiles[0].text).toString("base64"));
const ws="synthetic_ws";
const deviceId="dev_11111111-1111-4111-8111-111111111111";
const token="cydev_"+"a".repeat(64);
const digest=createHash("sha256").update(token).digest("hex");
const db=new DatabaseSync(":memory:");
try{
  const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
  for(const name of migrations.filter(x=>x<"0014"))
    db.exec(readFileSync("migrations/"+name,"utf8"));
  db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'Synthetic')").run(ws);
  db.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state,token_hash)
    VALUES (?,?,'Synthetic','active','cloud',?)`).run(deviceId,ws,digest);
  db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
  for(const name of migrations.filter(x=>x>="0014"))
    db.exec(readFileSync("migrations/"+name,"utf8"));
  const createdAt=new Date().toISOString();
  db.prepare(`INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,
    updated_by_device_id,environment,kind,reference_hash,actor_id,created_at,updated_at)
    VALUES('wi_synthetic_read',?,?,?,'test','VOID',?,'synthetic_actor',?,?)`)
    .run(ws,deviceId,deviceId,"a".repeat(64),createdAt,createdAt);
  let beforeRun=null,afterRun=null;
  class Statement{
    constructor(sql){this.sql=sql;this.args=[]}
    bind(...args){this.args=args;return this}
    async first(){return db.prepare(this.sql).get(...this.args)??null}
    async all(){return {results:db.prepare(this.sql).all(...this.args)}}
    async run(){
      if(beforeRun){const f=beforeRun;beforeRun=null;f()}
      const changes=db.prepare(this.sql).run(...this.args).changes;
      if(afterRun){const f=afterRun;afterRun=null;f()}
      return {meta:{changes}};
    }
  }
  const d1={prepare:sql=>new Statement(sql)};
  let role="ADMIN", flips=false, revalidations=0;
  const env={DB:d1,SCHEMA_VERSION:"16",WORK_ITEMS_ENABLED:"true",
    CYID_ENABLED:"true",IDENTITY:{fetch:async()=>Response.json({})},
    IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_ws",
    IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
    cyIdContext:{revalidate:async()=>{
      revalidations++;
      if(flips&&revalidations%2===0)role="USER";
      return {workspaceId:"identity_ws",employeeId:"synthetic_actor",workspaceRole:role};
    }}
  };
  async function call(action,body={},extra={},bearer=token){
    const response=await handleWorkItems(new Request(
      "https://synthetic.test/v1/work-items/"+action,{
        method:"POST",headers:{"authorization":"Bearer "+bearer,
          "content-type":"application/json"},
        body:JSON.stringify({employeeNo:"0001",password:"TestPass12",...body})
      }),{...env,...extra});
    return {status:response.status,body:await response.json()};
  }
  assert.equal((await call("list",{}, {WORK_ITEMS_ENABLED:"false"})).status,503);
  assert.equal((await call("list",{}, {SCHEMA_VERSION:"15"})).status,503);
  assert.equal((await call("list",{kind:"ALLOWANCE"})).status,400);
  assert.equal((await call("list",{workspaceId:"wrong"})).status,400);
  assert.equal((await call("list",{limit:51})).status,400);
  assert.equal((await call("list",{cursor:{createdAt:"not a date",itemId:"wi_synthetic_read"}})).status,400);
  assert.equal((await call("list",{},{},"wrong")).status,401);
  role="USER";
  assert.equal((await call("list")).status,403);
  // USER may submit ONLY metadata; official AMEGO operations are never called.
  const postedVoid=await call("create",{
    itemId:"wi_new_void_user_001",environment:"test",kind:"VOID",
    referenceHash:"b".repeat(64)});
  assert.equal(postedVoid.status,200,JSON.stringify(postedVoid));
  assert.equal(postedVoid.body.officialVerified,false);
  assert.equal(postedVoid.body.providerActionPerformed,false);
  assert.equal(db.prepare("SELECT status,kind FROM work_items WHERE work_item_id=?")
    .get("wi_new_void_user_001").status,"open");
  assert.equal((await call("create",{itemId:"wi_new_void_user_002",
    environment:"test",kind:"ALLOWANCE",referenceHash:"e".repeat(64)})).status,409);
  assert.equal((await call("create",{itemId:"wi_new_void_user_003",
    environment:"test",kind:"VOID",referenceHash:"b".repeat(64)})).status,409,
    "Duplicate source cannot produce a second open request");
  assert.equal((await call("create",{itemId:"wi_new_void_user_004",
    environment:"test",kind:"VOID",referenceHash:"secret-value"})).status,400);
  assert.equal((await call("create",{itemId:"wi_new_void_user_005",
    environment:"test",kind:"VOID",referenceHash:"e".repeat(64),
    workspaceId:"outside"})).status,400);
  const reported=await call("report-upload-issue",{
    itemId:"wi_upload_local_0001",environment:"test",
    referenceHash:"c".repeat(64),issueCode:"upload_failed"});
  assert.equal(reported.status,200,JSON.stringify(reported));
  assert.equal(reported.body.officialVerified,false);
  assert.equal((await call("report-upload-issue",{
    itemId:"wi_upload_local_0002",environment:"test",referenceHash:"d".repeat(64),
    issueCode:"official_success"})).status,400);
  assert.equal((await call("transition",{itemId:"wi_new_void_user_001",
    expectedRevision:1,from:"open",to:"review"})).status,403,
    "USER cannot approve/review their own paper-void request");

  role="ADMIN";
  const started=await call("transition",{itemId:"wi_new_void_user_001",
    expectedRevision:1,from:"open",to:"review"});
  assert.equal(started.status,200,JSON.stringify(started));
  assert.equal((await call("transition",{itemId:"wi_new_void_user_001",
    expectedRevision:1,from:"open",to:"review"})).status,409,
    "Concurrent managers cannot both update revision 1");
  const closed=await call("transition",{itemId:"wi_new_void_user_001",
    expectedRevision:2,from:"review",to:"closed"});
  assert.equal(closed.status,200,JSON.stringify(closed));
  const storedVoid=db.prepare("SELECT revision,status,closed_by_actor_id FROM work_items WHERE work_item_id=?")
    .get("wi_new_void_user_001");
  assert.equal(storedVoid.revision,3);
  assert.equal(storedVoid.status,"closed");
  assert.equal(storedVoid.closed_by_actor_id,"synthetic_actor");
  assert.equal((await call("transition",{itemId:"wi_new_void_user_001",
    expectedRevision:3,from:"review",to:"closed"})).status,409);
  assert.equal((await call("transition",{itemId:"wi_upload_local_0001",
    expectedRevision:1,from:"open",to:"review"})).status,200);
  assert.equal((await call("transition",{itemId:"wi_upload_local_0001",
    expectedRevision:2,from:"review",to:"closed",
    resolutionCode:"needs_followup"})).status,200);
  assert.equal(db.prepare("SELECT resolution_code FROM work_items WHERE work_item_id=?")
    .get("wi_upload_local_0001").resolution_code,"needs_followup");
  assert.equal((await call("transition",{itemId:"wi_upload_local_0001",
    expectedRevision:3,from:"review",to:"closed",
    resolutionCode:"checked_official"})).status,409,
    "Closed Upload Issue is immutable");
  // A Device revoked AFTER CYID login but BEFORE SQL update must not write.
  beforeRun=()=>db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(deviceId);
  const rejected=await call("create",{itemId:"wi_revoked_writer_01",
    environment:"test",kind:"VOID",referenceHash:"d".repeat(64)});
  assert.equal(rejected.status,409);
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items WHERE work_item_id=?")
    .get("wi_revoked_writer_01").n,0);
  beforeRun=null;
  db.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(deviceId);
  // A D1 result lost after COMMIT must be marked UNKNOWN, not retried.
  afterRun=()=>{throw new Error("Synthetic D1 reply lost")};
  const unknown=await call("report-upload-issue",{itemId:"wi_unknown_reply_01",
    environment:"test",issueCode:"query_failed",referenceHash:"e".repeat(64)});
  assert.equal(unknown.status,503);
  assert.equal(unknown.body.error.code,"WORK_ITEM_RESULT_UNKNOWN");
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items WHERE work_item_id=?")
    .get("wi_unknown_reply_01").n,1,"Unknown response may have committed already");
  afterRun=null;
  const queue=await call("list",{limit:1});
  assert.equal(queue.status,200,JSON.stringify(queue));
  assert.equal(queue.body.items.length,1);
  assert.deepEqual(Object.keys(queue.body.items[0]).sort(),[
    "work_item_id","environment","kind","issue_code","status","revision","created_at","updated_at"
  ].sort(),"manager list may not expose business/source fields");
  assert.equal(JSON.stringify(queue.body).includes("a".repeat(64)),false);
  const item=await call("get",{itemId:"wi_synthetic_read"});
  assert.equal(item.status,200,JSON.stringify(item));
  assert.equal(item.body.item.work_item_id,"wi_synthetic_read");
  assert.deepEqual(Object.keys(item.body.item).sort(), [
    "work_item_id","workspace_id","environment","kind","reference_hash",
    "status","revision"
  ].sort(),"manager ID readback must not expose actor, Device, audit or full source data");
  assert.equal(JSON.stringify(item.body.item).includes("synthetic_actor"),false,
    "internal employee identity stays in the original D1 audit only");
  assert.equal(JSON.stringify(item.body.item).includes(deviceId),false,
    "Device ID stays in the original D1 audit only");
  assert.equal((await call("get",{itemId:"wi_other_missing"})).status,404);
  assert.equal((await call("get",{itemId:"wi_synthetic_read",workspaceId:"wrong"})).status,400);
  flips=true;revalidations=0;
  assert.equal((await call("list")).status,409,"revoked CYID role during read must not leak data");
  flips=false;role="ADMIN";
  db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(deviceId);
  assert.equal((await call("list")).status,401);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
}finally{db.close()}
console.log("CYInvoice V3.1 manager-only read queue gate, privacy, revocation and CAS source tests passed");
