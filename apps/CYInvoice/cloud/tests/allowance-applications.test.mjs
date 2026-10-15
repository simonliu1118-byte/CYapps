import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {build} from "esbuild";
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
    "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud');");
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
fx.sql.close();
console.log("Draft allowance applications: immutable encrypted template, USER identity, D1 atomic create, company revision guard passed");
