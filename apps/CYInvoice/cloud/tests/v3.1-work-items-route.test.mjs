import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
import { build } from "esbuild";

// Execute V3.1 non-allowance CYID-only Work Item D1 draft SQL in isolated SQLite.
// Never run this SQL against a real Cloudflare D1 in CI.
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
  db.exec(readFileSync("drafts/0016_non_allowance_work_items.sql","utf8"));
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
  assert.equal(fakeAllowance.body.error.code,"ALLOWANCE_RESERVED_FOR_3_2");
  const fakeVoid=await call("create",{...first,itemId:"wi_fake_allowance_void",
    kind:"ALLOWANCE_VOID",referenceHash:"e".repeat(64)});
  assert.equal(fakeVoid.status,409);
  assert.equal(fakeVoid.body.error.code,"ALLOWANCE_RESERVED_FOR_3_2");
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  const created=await call("create",first);
  assert.equal(created.status,200,JSON.stringify(created));
  assert.equal(created.body.item.status,"open");
  assert.equal(created.body.item.revision,1);
  assert.equal((await call("create",first)).status,409);
  const second=await call("create",{...first,itemId:"wi_endpoint_second",
    referenceHash:"b".repeat(64)});
  assert.equal(second.status,200,JSON.stringify(second));
  // Manager Work Item listing is limited, cursor-paged, and never leaks
  // original invoice, arbitrary referenceHash, actor, or Device metadata.
  assert.equal((await call("list",{limit:51})).status,400);
  assert.equal((await call("list",{limit:1,workspaceId:"ws_attacker"})).status,400);
  assert.equal((await call("list",{limit:1,cursor:{createdAt:"not-a-date",itemId:first.itemId}})).status,400);
  assert.equal((await call("list",{kind:"UPLOAD_ISSUE"})).status,200,
    "No upload issue yet, but managers can filter for reported local problems");
  const managerAuthenticated=await call("list",{
    limit:1,employeeNo:"0001",password:"SyntheticPass12"
  });
  assert.equal(managerAuthenticated.status,200,JSON.stringify(managerAuthenticated));
  const firstPage=await call("list",{limit:1});
  assert.equal(firstPage.status,200,JSON.stringify(firstPage));
  assert.equal(firstPage.body.items.length,1);
  assert.ok(firstPage.body.nextCursor);
  const nextPage=await call("list",{limit:1,cursor:firstPage.body.nextCursor});
  assert.equal(nextPage.status,200,JSON.stringify(nextPage));
  assert.equal(nextPage.body.items.length,1);
  assert.equal(nextPage.body.nextCursor,null);
  assert.notEqual(firstPage.body.items[0].work_item_id,nextPage.body.items[0].work_item_id);
  for(const entry of [...firstPage.body.items,...nextPage.body.items]){
    assert.deepEqual(Object.keys(entry).sort(),
      ["work_item_id","environment","kind","issue_code","status","revision","created_at","updated_at"].sort());
    assert.equal(entry.kind,"VOID");
  }
  const noAllowance=await call("list",{kind:"ALLOWANCE"});
  assert.equal(noAllowance.status,400,"3.1 queue must not expose 3.2 allowance kinds");
  // A regular CYID USER can submit a bounded local-problem report, but
  // neither user nor manager can pass a fabricated official invoice status.
  permission=false;
  const reportRequest={itemId:"wi_upload_issue_01",environment:"test",
    referenceHash:"c".repeat(64),issueCode:"query_failed"};
  const reported=await call("report-upload-issue",reportRequest);
  assert.equal(reported.status,200,JSON.stringify(reported));
  assert.equal(reported.body.reported,true);
  assert.equal(reported.body.officialVerified,false);
  assert.equal((await call("report-upload-issue",reportRequest)).status,409,
    "same event cannot be duplicated by another Work Item");
  assert.equal((await call("report-upload-issue",{...reportRequest,
    itemId:"wi_invalid_code",issueCode:"invoice_success"})).status,400);
  assert.equal((await call("report-upload-issue",{...reportRequest,
    itemId:"wi_extra_fields",invoiceNumber:"AB12345678"})).status,400,
    "invoice business data is never sent to D1");
  assert.equal((await call("create",{...reportRequest,
    itemId:"wi_forged_generic",kind:"UPLOAD_ISSUE"})).status,403,
    "generic manager create remains manager-only");
  assert.equal((await call("list",{limit:1})).status,403);
  permission=true;
  const reportedPage=await call("list",{kind:"UPLOAD_ISSUE"});
  assert.equal(reportedPage.status,200);
  assert.equal(reportedPage.body.items.length,1);
  assert.equal(reportedPage.body.items[0].kind,"UPLOAD_ISSUE");
  assert.equal(reportedPage.body.items[0].issue_code,"query_failed");
  assert.equal(db.prepare(
    "SELECT issue_code FROM work_items WHERE work_item_id=?")
    .get(reportRequest.itemId).issue_code,"query_failed");
  assert.equal(db.prepare("SELECT COUNT(*) n FROM work_items WHERE kind='UPLOAD_ISSUE'")
    .get().n,1);
  const reportReview=await call("transition",{itemId:reportRequest.itemId,
    expectedRevision:1,from:"open",to:"review"});
  assert.equal(reportReview.status,200,JSON.stringify(reportReview));
  assert.equal((await call("transition",{itemId:reportRequest.itemId,
    expectedRevision:2,from:"review",to:"closed"})).status,409,
    "Upload anomaly archive requires a manager reason");
  const reportClose=await call("transition",{itemId:reportRequest.itemId,
    expectedRevision:2,from:"review",to:"closed",resolutionCode:"needs_followup"});
  assert.equal(reportClose.status,200,JSON.stringify(reportClose));
  const audit=db.prepare(
    "SELECT reported_by_actor_id,reviewed_by_actor_id,closed_by_actor_id,"+
    "resolution_code,status,revision FROM work_items WHERE work_item_id=?")
    .get(reportRequest.itemId);
  assert.equal(audit.status,"closed");
  assert.equal(audit.revision,3);
  assert.equal(audit.reported_by_actor_id,"synthetic_manager");
  assert.equal(audit.reviewed_by_actor_id,"synthetic_manager");
  assert.equal(audit.closed_by_actor_id,"synthetic_manager");
  assert.equal(audit.resolution_code,"needs_followup");
  assert.deepEqual(db.prepare("SELECT event_type,to_status,revision FROM work_item_audit_events "+
    "WHERE work_item_id=? ORDER BY revision").all(reportRequest.itemId)
    .map(e=>[e.event_type,e.to_status,e.revision]),[
      ["CREATED","open",1],["TRANSITION","review",2],["TRANSITION","closed",3]
    ]);
  assert.throws(()=>db.prepare(
    "UPDATE work_items SET status='open' WHERE work_item_id=?")
    .run(reportRequest.itemId),/WORK_ITEM_ARCHIVED_IMMUTABLE|WORK_ITEM_TRANSITION_INVALID/);
  assert.throws(()=>db.prepare("DELETE FROM work_items WHERE work_item_id=?")
    .run(reportRequest.itemId),/WORK_ITEM_NOT_DELETABLE|WORK_ITEM_NOT_DELETABLE/);

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
