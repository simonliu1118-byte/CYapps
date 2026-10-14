import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

// Execute real D1 SQL against SQLite, for both Built-in and CYID_ONLY.
const built = await build({ entryPoints:["src/work-items.ts"], bundle:true,
  write:false, format:"esm", platform:"node", target:"node24" });
const {createWorkItem,transitionWorkItem,getWorkItem}=await import(
  "data:text/javascript;base64,"+Buffer.from(built.outputFiles[0].text).toString("base64"));
const ws="synthetic_ws";
const devA="dev_11111111-1111-4111-8111-111111111111";
const devB="dev_22222222-2222-4222-8222-222222222222";
const itemId="wi_synthetic_first";
const hash="a".repeat(64);
function fixture(cyidOnly) {
  const db=new DatabaseSync(":memory:");
  const migrations=readdirSync("migrations").filter(n=>/^[0-9]{4}_.*\.sql$/.test(n)).sort();
  for (const name of migrations.filter(n=>n<"0014")) db.exec(readFileSync("migrations/"+name,"utf8"));
  db.prepare("INSERT INTO workspaces (workspace_id,display_name) VALUES (?,'Synthetic')")
    .run(ws);
  for (const d of [devA,devB]) db.prepare(`
    INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
    VALUES (?,?,'Synthetic','active','cloud')`).run(d,ws);
  if(cyidOnly) db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
  for (const name of migrations.filter(n=>n>="0014")) db.exec(readFileSync("migrations/"+name,"utf8"));
  if(cyidOnly) assert.equal(db.prepare(
    "SELECT COUNT(*) AS n FROM sqlite_master WHERE name='cloud_employees'").get().n,0);
  let beforeWrite;
  class Statement {
    constructor(sql){this.sql=sql;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return db.prepare(this.sql).get(...this.args)??null;}
    async run(){
      if(beforeWrite){const f=beforeWrite;beforeWrite=null;f();}
      return {meta:{changes:db.prepare(this.sql).run(...this.args).changes}};
    }
  }
  return {db,env:{prepare:sql=>new Statement(sql)},race(fn){beforeWrite=fn;}};
}
for(const cyidOnly of [false,true]){
  const {db,env,race}=fixture(cyidOnly);
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
  assert.equal(await getWorkItem(env,"other_workspace",itemId),null);
  assert.equal(await createWorkItem(env,{...fresh,itemId:"wi_revoked_creator",
    referenceHash:"c".repeat(64)}),false);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
  db.close();
}
console.log("V3 Work Item storage: Built-in/CYID_ONLY, source de-dup, CAS revision and revoked Device fencing passed");
