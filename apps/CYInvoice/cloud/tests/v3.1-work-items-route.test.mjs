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
  db.exec(readFileSync("drafts/0016_non_allowance_work_items.sql","utf8"));
  const createdAt=new Date().toISOString();
  db.prepare(`INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,
    updated_by_device_id,environment,kind,reference_hash,actor_id,created_at,updated_at)
    VALUES('wi_synthetic_read',?,?,?,'test','VOID',?,'synthetic_actor',?,?)`)
    .run(ws,deviceId,deviceId,"a".repeat(64),createdAt,createdAt);
  class Statement{
    constructor(sql){this.sql=sql;this.args=[]}
    bind(...args){this.args=args;return this}
    async first(){return db.prepare(this.sql).get(...this.args)??null}
    async all(){return {results:db.prepare(this.sql).all(...this.args)}}
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
  role="ADMIN";
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
