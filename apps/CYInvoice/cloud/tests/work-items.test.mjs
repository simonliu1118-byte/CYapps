import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import { build } from "esbuild";

// Execute the V3 CYID-only Work Item D1 SQL against isolated SQLite.
const built = await build({ entryPoints:["src/work-items.ts"], bundle:true,
  write:false, format:"esm", platform:"node", target:"node24" });
const {createWorkItem,transitionWorkItem,getWorkItem}=await import(
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
  // New table is draft-only; do not alter the reviewed canonical migration set.
  db.exec(readFileSync("drafts/0015_work_items.sql","utf8"));
  assert.equal(db.prepare(
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
console.log("V3 Work Item storage: CYID_ONLY, source de-dup, CAS revision and revoked Device fencing passed");

const routeBundle=await build({entryPoints:["src/work-items-route.ts"],bundle:true,
  write:false,format:"esm",platform:"node",target:"node24"});
const {handleWorkItems}=await import(
  "data:text/javascript;base64,"+Buffer.from(routeBundle.outputFiles[0].text).toString("base64"));
const token="cydev_"+"a".repeat(64);
const tokenHash=createHash("sha256").update(token).digest("hex");
{
  const fx=fixture();
  const {db,env,race}=fx;
  db.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(tokenHash,devA);
  const principal={employeeId:"synthetic_manager",workspaceRole:"ADMIN"};
  let permission=true;
  const authorized = {
    ...env, DB:env, CYID_ENABLED:"true", IDENTITY:{fetch:async()=>Response.json({})},
    IDENTITY_APPLICATION_ID:"CYINVOICE", IDENTITY_WORKSPACE_ID:"synthetic_identity",
    IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
    cyIdContext:{revalidate:async()=>permission?principal:{...principal,workspaceRole:"USER"}},
  };
  const credentials={};
  async function call(action, body, bearer=token) {
    const response=await handleWorkItems(new Request(
      "https://cyinvoice.synthetic/v1/work-items/"+action,{
        method:"POST",headers:{"authorization":"Bearer "+bearer,
          "content-type":"application/json"},
        body:JSON.stringify({...credentials,...body}),
      }),authorized);
    return {status:response.status,body:await response.json()};
  }
  const legacy=await handleWorkItems(new Request("https://cyinvoice.synthetic/v1/work-items/create",{
    method:"POST",headers:{"authorization":"Bearer "+token,"content-type":"application/json"},
    body:JSON.stringify({itemId:"wi_legacy_disabled",kind:"VOID",environment:"test",referenceHash:hash}),
  }),{...authorized,CYID_ENABLED:"false"});
  assert.equal(legacy.status,409);
  assert.equal((await legacy.json()).error.code,"CYID_REQUIRED");
  const first={itemId:"wi_endpoint_first",kind:"VOID",environment:"test",referenceHash:hash};
  assert.equal((await call("create",first,"wrong")).status,401);
  const fakeAllowance=await call("create",{...first,itemId:"wi_fake_allowance",
    kind:"ALLOWANCE",referenceHash:"f".repeat(64)});
  assert.equal(fakeAllowance.status,409);
  assert.equal(fakeAllowance.body.error.code,"ALLOWANCE_APPLICATION_REQUIRED");
  const fakeVoid=await call("create",{...first,itemId:"wi_fake_allowance_void",
    kind:"ALLOWANCE_VOID",referenceHash:"e".repeat(64)});
  assert.equal(fakeVoid.status,409);
  assert.equal(fakeVoid.body.error.code,"ALLOWANCE_VOID_APPLICATION_REQUIRED");
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  const created=await call("create",first);
  assert.equal(created.status,200,JSON.stringify(created));
  assert.equal(created.body.item.status,"open");
  assert.equal(created.body.item.revision,1);
  assert.equal((await call("create",first)).status,409);
  const seen=await call("get",{itemId:first.itemId});
  assert.equal(seen.status,200);
  assert.equal(seen.body.item.environment,"test");
  const change={itemId:first.itemId,expectedRevision:1,from:"open",to:"review"};
  assert.equal((await call("transition",change)).status,200);
  assert.equal((await call("transition",change)).status,409);
  assert.equal((await call("transition",{...change,expectedRevision:2,from:"review",to:"open"})).status,400);
  permission=false;
  assert.equal((await call("transition",{...change,expectedRevision:2,
    from:"review",to:"closed"})).status,403);
  permission=true;
  race(()=>db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devA));
  assert.equal((await call("transition",{...change,expectedRevision:2,
    from:"review",to:"closed"})).status,409);
  assert.equal(db.prepare("SELECT status FROM work_items WHERE work_item_id=?")
    .get(first.itemId).status,"review");
  assert.equal((await call("get",{itemId:first.itemId})).status,401);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
  db.close();
}
console.log("V3 Work Item authenticated routes: CYID_ONLY, manager role, CAS, revocation and privacy passed");
