import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

// Execute the V3 CYID-only Work Item D1 SQL against isolated SQLite.
const built = await build({ entryPoints:["src/work-items.ts"], bundle:true,
  write:false, format:"esm", platform:"node", target:"node24" });
const {createWorkItem,transitionWorkItem,getWorkItem,reportUploadIssue,listWorkItems}=await import(
  "data:text/javascript;base64,"+Buffer.from(built.outputFiles[0].text).toString("base64"));
const ws="synthetic_ws";
const devA="dev_11111111-1111-4111-8111-111111111111";
const devB="dev_22222222-2222-4222-8222-222222222222";
const itemId="wi_synthetic_first";
const hash="a".repeat(64);
function fixture() {
  const db=new DatabaseSync(":memory:");
  const migrations=readdirSync("migrations").filter(n=>/^[0-9]{4}_.*\.sql$/.test(n)).sort();
  for (const name of migrations.filter(n=>n<"0014")) db.exec(readFileSync("migrations/"+name,"utf8"));
  db.prepare("INSERT INTO workspaces (workspace_id,display_name) VALUES (?,'Synthetic')")
    .run(ws);
  for (const d of [devA,devB]) db.prepare(`
    INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
    VALUES (?,?,'Synthetic','active','cloud')`).run(d,ws);
  db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
  for (const name of migrations.filter(n=>n>="0014")) db.exec(readFileSync("migrations/"+name,"utf8"));
  // Canonical 0016 is applied exactly once by the migration loop above.
  assert.equal(db.prepare(
    "SELECT COUNT(*) AS n FROM sqlite_master WHERE name='cloud_employees'").get().n,0);
  let beforeWrite;
  class Statement {
    constructor(sql){this.sql=sql;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return db.prepare(this.sql).get(...this.args)??null;}
    async all(){return {results:db.prepare(this.sql).all(...this.args)};}
    async run(){
      if(beforeWrite){const f=beforeWrite;beforeWrite=null;f();}
      return {meta:{changes:db.prepare(this.sql).run(...this.args).changes}};
    }
  }
  return {db,env:{prepare:sql=>new Statement(sql)},race(fn){beforeWrite=fn;}};
}
{
  const {db,env,race}=fixture();
  const fresh={itemId,workspaceId:ws,deviceId:devA,actorId:"synthetic_actor",
    environment:"test",kind:"VOID",referenceHash:hash};
  assert.equal(await createWorkItem(env,fresh),true);
  let state=await getWorkItem(env,ws,itemId);
  assert.equal(state.status,"open");
  assert.equal(state.revision,1);
  await assert.rejects(()=>createWorkItem(env,{...fresh,
    itemId:"wi_duplicate_reference"}),/UNIQUE|constraint/i);
  assert.equal(await createWorkItem(env,{...fresh,itemId:"wi_wrong_scope",
    workspaceId:"other_workspace",referenceHash:"b".repeat(64)}),false);
  const review={itemId,workspaceId:ws,deviceId:devA,actorId:"synthetic_actor",
    expectedRevision:1,from:"open",to:"review"};
  assert.equal(await transitionWorkItem(env,review),true);
  assert.equal(await transitionWorkItem(env,review),false,"stale concurrent editor must not override");
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_item_audit_events WHERE work_item_id=?")
    .get(itemId).n,2,"stale CAS cannot append a second event");
  state=await getWorkItem(env,ws,itemId);
  assert.equal(state.status,"review");assert.equal(state.revision,2);
  await assert.rejects(()=>transitionWorkItem(env,{...review,expectedRevision:2,
    from:"review",to:"open"}),/INVALID_WORK_ITEM_TRANSITION/);
  const close={...review,expectedRevision:2,from:"review",to:"closed"};
  race(()=>db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devA));
  assert.equal(await transitionWorkItem(env,close),false,"revoked in-flight writer must fail");
  state=await getWorkItem(env,ws,itemId);
  assert.equal(state.status,"review");assert.equal(state.revision,2);
  assert.equal(await transitionWorkItem(env,{...close,deviceId:devB,
    actorId:"synthetic_other"}),true,"another active Device can review/close");
  assert.equal(await transitionWorkItem(env,{...close,deviceId:devB}),false);
  state=await getWorkItem(env,ws,itemId);
  assert.equal(state.status,"closed");assert.equal(state.revision,3);
  assert.equal(state.updated_by_device_id,devB);
  assert.equal(state.reviewed_by_actor_id,"synthetic_actor");
  assert.equal(state.closed_by_actor_id,"synthetic_other");
  const history=db.prepare(
    "SELECT event_type,from_status,to_status,revision,actor_id,device_id "+
    "FROM work_item_audit_events WHERE work_item_id=? ORDER BY revision").all(itemId);
  assert.deepEqual(history.map(e=>[
    e.event_type,e.from_status,e.to_status,e.revision,e.actor_id,e.device_id
  ]),[
    ["CREATED",null,"open",1,"synthetic_actor",devA],
    ["TRANSITION","open","review",2,"synthetic_actor",devA],
    ["TRANSITION","review","closed",3,"synthetic_other",devB]
  ]);
  assert.throws(()=>db.prepare("UPDATE work_item_audit_events SET actor_id='forged'").run(),
    /WORK_ITEM_AUDIT_APPEND_ONLY/);
  assert.throws(()=>db.prepare("DELETE FROM work_item_audit_events").run(),
    /WORK_ITEM_AUDIT_APPEND_ONLY/);
  assert.throws(()=>db.prepare("DELETE FROM work_items WHERE work_item_id=?").run(itemId),
    /WORK_ITEM_NOT_DELETABLE/);
  assert.throws(()=>db.prepare("UPDATE work_items SET status='open' WHERE work_item_id=?").run(itemId),
    /WORK_ITEM_ARCHIVED_IMMUTABLE/);
  assert.equal(await getWorkItem(env,"other_workspace",itemId),null);
  assert.equal(await createWorkItem(env,{...fresh,itemId:"wi_revoked_creator",
    referenceHash:"c".repeat(64)}),false);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
  db.close();
}
console.log("V3 Work Item storage: CYID_ONLY, source de-dup, CAS revision and revoked Device fencing passed");
{
  const {db,env}=fixture();
  assert.equal(readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort().at(-1),
    "0020_allowance_void_coordination.sql","V3.2-C schema must be canonical");
  assert.throws(()=>db.prepare(`INSERT INTO work_items(
    work_item_id,workspace_id,opened_by_device_id,updated_by_device_id,
    environment,kind,reference_hash,actor_id,created_at,updated_at)
    VALUES('wi_fake_allowance','synthetic_ws',?,?, 'test','ALLOWANCE',?, 'synthetic_actor','now','now')`
  ).run(devA,devA,"b".repeat(64)),/CHECK constraint failed/);
  await assert.rejects(()=>createWorkItem(env,{itemId:"wi_fake_allowance_2",workspaceId:ws,
    deviceId:devA,actorId:"synthetic_actor",environment:"test",
    kind:"ALLOWANCE",referenceHash:"b".repeat(64)}),/INVALID_WORK_ITEM/);
  const issue={itemId:"wi_report_one",workspaceId:ws,deviceId:devA,
    actorId:"synthetic_reporter",environment:"test",referenceHash:"c".repeat(64),
    issueCode:"upload_failed"};
  assert.equal(await reportUploadIssue(env,issue),true);
  await assert.rejects(()=>reportUploadIssue(env,{...issue,itemId:"wi_report_duplicate"}),
    /UNIQUE|constraint/i);
  await assert.rejects(()=>reportUploadIssue(env,{...issue,itemId:"wi_report_invalid",
    issueCode:"official_success"}),/INVALID_UPLOAD_ISSUE/);
  const pending=await listWorkItems(env,ws,{limit:10});
  assert.equal(pending.items.length,1);
  assert.deepEqual(Object.keys(pending.items[0]).sort(),[
    "work_item_id","environment","kind","issue_code","status","revision","created_at","updated_at"
  ].sort(),"Manager list excludes sensitive invoice/device/actor details");
  await assert.rejects(()=>listWorkItems(env,ws,{limit:51}),/INVALID_WORK_ITEM_FILTER/);
  await assert.rejects(()=>listWorkItems(env,ws,{limit:10,kind:"ALLOWANCE"}),/INVALID_WORK_ITEM_FILTER/);
  const begin={itemId:issue.itemId,workspaceId:ws,deviceId:devB,
    actorId:"synthetic_manager",expectedRevision:1,from:"open",to:"review"};
  assert.equal(await transitionWorkItem(env,begin),true);
  assert.equal(await transitionWorkItem(env,begin),false,"stale manager CAS");
  const close={...begin,expectedRevision:2,from:"review",to:"closed"};
  assert.equal(await transitionWorkItem(env,close),false,"upload archive requires manager reason");
  assert.equal(await transitionWorkItem(env,{...close,resolutionCode:"needs_followup"}),true);
  const item=await getWorkItem(env,ws,issue.itemId);
  assert.equal(item.status,"closed");
  assert.equal(item.revision,3);
  assert.equal(item.reported_by_actor_id,"synthetic_reporter");
  assert.equal(item.reviewed_by_actor_id,"synthetic_manager");
  assert.equal(item.closed_by_actor_id,"synthetic_manager");
  assert.equal(item.resolution_code,"needs_followup");
  assert.deepEqual(db.prepare(
    "SELECT event_type,revision,from_status,to_status,resolution_code "+
    "FROM work_item_audit_events WHERE work_item_id=? ORDER BY revision").all(issue.itemId)
    .map(e=>[e.event_type,e.revision,e.from_status,e.to_status,e.resolution_code]),[
      ["CREATED",1,null,"open",null],
      ["TRANSITION",2,"open","review",null],
      ["TRANSITION",3,"review","closed","needs_followup"]
    ],"upload issue closure reason and reviewer must be auditable");
  assert.throws(()=>db.prepare("DELETE FROM work_items").run(),/WORK_ITEM_NOT_DELETABLE/);
  assert.throws(()=>db.prepare("UPDATE work_items SET status='open' WHERE work_item_id=?")
    .run(issue.itemId),/WORK_ITEM_ARCHIVED_IMMUTABLE/);
  assert.deepEqual((await listWorkItems(env,ws,{limit:10})).items,[]);
  assert.equal((await listWorkItems(env,ws,{limit:10,status:"closed"})).items.length,1);
  assert.equal(await getWorkItem(env,"other_workspace",issue.itemId),null);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
  db.close();
}
console.log("CYInvoice V3.1 non-allowance Work Item isolation passed");
