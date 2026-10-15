import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {build} from "esbuild";
import {createHash} from "node:crypto";
const built=await build({entryPoints:["src/allowance-applications.ts"],bundle:true,
  platform:"node",format:"esm",target:"node24",write:false});
const {createFrozenAllowanceApplication:create,loadFrozenAllowanceApplication:load}=
  await import("data:text/javascript;base64,"+
    Buffer.from(built.outputFiles[0].text).toString("base64"));
const ws="ws_synthetic",dev="dev_11111111-1111-4111-8111-111111111111";
const key="aa".repeat(32);
function fixture(){
  const sql=new DatabaseSync(":memory:");
  sql.exec("PRAGMA foreign_keys=ON;"+
    "CREATE TABLE workspaces(workspace_id TEXT PRIMARY KEY,status TEXT NOT NULL DEFAULT 'active');"+
    "CREATE TABLE devices(device_id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),"+
    "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud',token_hash TEXT);");
  sql.prepare("INSERT INTO workspaces(workspace_id) VALUES(?)").run(ws);
  sql.prepare("INSERT INTO devices(device_id,workspace_id) VALUES(?,?)").run(dev,ws);
  for(const file of ["0015_work_items.sql","0017_company_settings.sql","0018_allowance_applications.sql"])
    sql.exec(readFileSync("drafts/"+file,"utf8"));
  sql.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,"+
    "revision,changed_by_employee_id,changed_by_device_id,changed_at) VALUES(?,?,'synthetic_envelope',1,?,?,?)")
    .run(ws,"12345678","super_admin",dev,"2026-10-15");
  class Statement{
    constructor(q){this.q=q;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return sql.prepare(this.q).get(...this.args)??null;}
    async all(){return {results:sql.prepare(this.q).all(...this.args)};}
    async run(){return {meta:{changes:sql.prepare(this.q).run(...this.args).changes}};}
  }
  return {sql,db:{prepare:q=>new Statement(q),async batch(items){
    sql.exec("BEGIN IMMEDIATE");
    try{const res=items.map(x=>({meta:{changes:sql.prepare(x.q).run(...x.args).changes}}));
      sql.exec("COMMIT");return res;}
    catch(e){sql.exec("ROLLBACK");throw e;}
  }}};
}
const fx=fixture(),runtime={environment:"test",companyEncryptionKeyHex:key};
let actor={workspaceId:ws,employeeId:"cyid_employee",workspaceRole:"USER"};
const authority={revalidate:async()=>actor};
const input={workspaceId:ws,deviceId:dev,workItemId:"wi_frozen_allowance_1",
  originalInvoiceNumber:"AB12345678",allowanceDay:"20261015",
  requestedGrossAmount:50,reason:"Synthetic partial refund",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"",AllowanceDate:"20261015",
    ProductItem:[{OriginalInvoiceNumber:"AB12345678",OriginalDescription:"Synthetic item",
      Quantity:1,Amount:50,Tax:0,TaxType:1}],TotalAmount:50,TaxAmount:0})};
const item=await create(fx.db,input,authority,runtime);
assert.equal(item.workItemId,input.workItemId);
const stored=fx.sql.prepare("SELECT * FROM allowance_applications").get();
assert.equal(stored.applicant_employee_id,actor.employeeId);
assert.equal(stored.original_invoice_number,input.originalInvoiceNumber);
assert.equal(stored.template_ciphertext.includes("Synthetic item"),false);
assert.equal(Object.hasOwn(stored,"template_json"),false);
assert.equal(fx.sql.prepare("SELECT kind FROM work_items").get().kind,"ALLOWANCE");
await assert.rejects(()=>load(fx.db,ws,input.workItemId,1,key),/NOT_REVIEWABLE/);
fx.sql.prepare("UPDATE work_items SET status='review',revision=2").run();
const frozen=await load(fx.db,ws,input.workItemId,2,key);
assert.equal(frozen.templateJson,input.unnumberedTemplateJson);
assert.equal(frozen.referenceHash,item.referenceHash);
assert.equal(frozen.requestedGrossAmount,50);
await assert.rejects(()=>load(fx.db,ws,input.workItemId,2,"bb".repeat(32)),/AUTH_FAILED/);
await assert.rejects(()=>create(fx.db,input,authority,runtime),/READBACK_REQUIRED/);
const invalid={...input,workItemId:"wi_template_invalid",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"A20261015001",
    AllowanceDate:"20261015",ProductItem:[{OriginalInvoiceNumber:"AB12345678"}]})};
await assert.rejects(()=>create(fx.db,invalid,authority,runtime),/TEMPLATE_MISMATCH/);
const foreign={...input,workItemId:"wi_wrong_invoice",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"",AllowanceDate:"20261015",
    ProductItem:[{OriginalInvoiceNumber:"XY12345678"}]})};
await assert.rejects(()=>create(fx.db,foreign,authority,runtime),/TEMPLATE_MISMATCH/);
actor={...actor,workspaceRole:"USER",employeeId:""};
await assert.rejects(()=>create(fx.db,{...input,workItemId:"wi_no_user_credential"},
  authority,runtime),/AUTH_REQUIRED/);
actor={...actor,employeeId:"cyid_employee"};
fx.sql.prepare("UPDATE devices SET status='revoked'").run();
await assert.rejects(()=>create(fx.db,{...input,workItemId:"wi_revoked_device"},
  authority,runtime),/UNKNOWN_READBACK_REQUIRED/);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,1,
  "A rejected Device cannot leave a half-created application");
fx.sql.prepare("UPDATE devices SET status='active'").run();
fx.sql.prepare("UPDATE company_settings SET revision=2").run();
await assert.rejects(()=>load(fx.db,ws,input.workItemId,2,key),/COMPANY_CHANGED/);
assert.throws(()=>fx.sql.prepare("DELETE FROM allowance_applications").run(),/IMMUTABLE/);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,1);
assert.deepEqual(fx.sql.prepare("PRAGMA foreign_key_check").all(),[]);
// Protected route: the CYID identity Workspace must be explicitly converted
// to the Business Workspace binding; client scope and seller fields are refused.
fx.sql.prepare("UPDATE company_settings SET revision=1").run();
const bundle=await build({entryPoints:["src/allowance-applications-route.ts"],
  bundle:true,format:"esm",platform:"node",target:"node24",write:false});
const {handleAllowanceApplications:route}=await import("data:text/javascript;base64,"+
  Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const token="cydev_"+"a".repeat(64);
fx.sql.prepare("UPDATE devices SET token_hash=?").run(createHash("sha256").update(token).digest("hex"));
let who={workspaceId:"identity_synthetic",employeeId:"cyid_employee",
  workspaceRole:"USER",emailVerified:true};
const env={
  DB:fx.db,CYID_ENABLED:"true",
  IDENTITY:{fetch:async()=>Response.json({})},
  IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_synthetic",
  IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
  ALLOWANCE_OPERATION_ENVIRONMENT:"test",
  COMPANY_SETTINGS_KEY_HEX:key,
  cyIdContext:{revalidate:async()=>who}
};
const createBody={
  employeeNo:"1234",password:"SYNTHETIC_PASS",itemId:"wi_allowance_route_1",
  originalInvoiceNumber:input.originalInvoiceNumber,allowanceDay:input.allowanceDay,
  requestedGrossAmount:input.requestedGrossAmount,reason:input.reason,
  unnumberedTemplateJson:input.unnumberedTemplateJson
};
async function call(action,body,config=env,bearer=token){
  const r=await route(new Request("https://cyinvoice.synthetic/v1/allowance-applications/"+action,{
    method:"POST",headers:{"authorization":"Bearer "+bearer,
      "content-type":"application/json"},body:JSON.stringify(body)
  }),config);
  return {status:r.status,data:await r.json()};
}
assert.equal((await call("create",createBody,{...env,cyIdContext:undefined})).status,401);
assert.equal((await call("create",{...createBody,workspaceId:"ws_attacker"})).status,400);
assert.equal((await call("create",{...createBody,environment:"production"})).status,400);
assert.equal((await call("create",createBody,{...env,ALLOWANCE_OPERATION_ENVIRONMENT:undefined})).status,503);
const created=await call("create",createBody);
assert.equal(created.status,201,JSON.stringify(created));
assert.deepEqual(Object.keys(created.data).sort(),["itemId","ok","revision","status"]);
const view=await call("get",{itemId:createBody.itemId});
assert.equal(view.status,200,JSON.stringify(view));
assert.equal(view.data.application.originalInvoiceNumber,input.originalInvoiceNumber);
assert.equal(view.data.application.applicantEmployeeId,"cyid_employee");
assert.equal(JSON.stringify(view).includes("Synthetic item"),false);
assert.equal(JSON.stringify(view).includes("synthetic_envelope"),false);
assert.equal((await call("create",createBody)).status,409);
assert.equal((await call("get",{itemId:createBody.itemId})).status,200,
  "Lost create response must be reconciled by the same Work Item ID");
who={...who,employeeId:"other_cyid_employee"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,404);
who={...who,workspaceRole:"ADMIN"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,200);
// Review is a read-only preview of the tamper-checked frozen Work Item.
// It must never be mistaken for approval or a verified official invoice.
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:1})).status,409);
fx.sql.prepare("UPDATE work_items SET status='review',revision=2 WHERE work_item_id=?")
  .run(createBody.itemId);
const review=await call("review-preview",{itemId:createBody.itemId,expectedRevision:2});
assert.equal(review.status,200,JSON.stringify(review));
assert.equal(review.data.review.canApprove,false);
assert.equal(review.data.review.officialVerification,"NOT_PERFORMED");
assert.equal(review.data.review.items.length,1);
assert.equal(review.data.review.items[0].description,"Synthetic item");
assert.equal(review.data.review.applicantEmployeeId,"cyid_employee");
for(const forbidden of ["SYNTHETIC_APP_KEY","SYNTHETIC_MO","v1:","BuyerEmailAddress",
  "unnumberedTemplateJson","templateJson"])
  assert.equal(JSON.stringify(review.data).includes(forbidden),false);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:1})).status,409);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:2,officialApproved:true})).status,400);
const page1=await call("list",{limit:1});
assert.equal(page1.status,200,JSON.stringify(page1));
assert.equal(page1.data.applications.length,1);
assert.notEqual(page1.data.nextCursor,null);
const page2=await call("list",{limit:1,...page1.data.nextCursor});
assert.equal(page2.status,200);
assert.equal(page2.data.applications.length,1);
assert.equal(page2.data.nextCursor,null);
assert.notEqual(page1.data.applications[0].itemId,page2.data.applications[0].itemId);
assert.equal((await call("list",{limit:51})).status,400);
assert.equal((await call("list",{limit:10,workspaceId:"ws_other"})).status,400);
who={...who,workspaceRole:"USER"};
assert.equal((await call("list",{limit:1})).status,403);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:2})).status,403);
who={...who,workspaceRole:"ADMIN"};
who={...who,workspaceId:"attacker_identity_workspace"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,403);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,2);
assert.deepEqual(fx.sql.prepare("PRAGMA foreign_key_check").all(),[]);
fx.sql.close();
console.log("Draft allowance applications: immutable encrypted template, atomic SQL, scoped CYID protected routes and readback passed");
