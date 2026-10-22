import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";
import {createHash} from "node:crypto";

// CI only: real storage SQL plus synthetic company/CYID/AMEGO transport.
// No live Cloudflare D1, Worker route, or official AMEGO transaction.
async function bundle(file) {
 const b=await build({entryPoints:["src/"+file],bundle:true,
   write:false,format:"esm",platform:"node",target:"node24"});
 return import("data:text/javascript;base64,"+
  Buffer.from(b.outputFiles[0].text).toString("base64"));
}
const {createEncryptedCloudAllowance:create}=await bundle("allowance-frozen-store.ts");
const {previewCloudAllowanceForManager:preview}=await bundle("allowance-manager-preview.ts");
const {encryptCompanyBusinessData:seal}=await bundle("company-settings.ts");
const ws="ws_c3b",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const key="aa".repeat(32),env={encryptionKeyHex:key,environment:"test"};
const originalInvoice="AB12345678",when=new Date("2026-10-19T08:00:00.000Z");
const appId="application_c3b_preview_one";
const template=JSON.stringify({AllowanceNumber:"",AllowanceType:2,AllowanceDate:"20261019",
 BuyerIdentifier:"0000000000",BuyerName:"Synthetic private buyer",
 TotalAmount:50,TaxAmount:0,ProductItem:[{
 OriginalInvoiceNumber:originalInvoice,OriginalInvoiceDate:"20261019",
 OriginalDescription:"Synthetic refund",Quantity:1,UnitPrice:50,
 Amount:50,Tax:0,TaxType:1
 }]});
const createInput={workspaceId:ws,applicationId:appId,deviceId:devA,
 environment:"test",originalInvoiceNumber:originalInvoice,
 allowanceDay:"20261019",requestedGross:50,unnumberedTemplateJson:template};
const applicant={workspaceId:ws,employeeId:"applicant",workspaceRole:"USER"};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const authority=(actor=manager)=>({revalidate:async()=>actor});
const target={workspaceId:ws,applicationId:appId,deviceId:devB,expectedRevision:2};
const fakeRecord={invoice_number:originalInvoice,status:99,cancel_date:0,
 total_amount:100,invoice_date:"20261019",detail_vat:1,tax_amount:5,
 product_item:[{description:"Synthetic official item"}],allowance:[]};
async function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(n=>/^\d{4}_.*\.sql$/.test(n)).sort();
 assert.equal(migrations.at(-1),"0020_allowance_void_coordination.sql");
 for(const name of migrations.filter(n=>n<"0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'Synthetic')").run(ws);
 for(const dev of [devA,devB])
  sql.prepare(`INSERT INTO devices (device_id,workspace_id,display_name,status,employee_authority_state)
    VALUES (?,?,'Synthetic','active','cloud')`).run(dev,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const name of migrations.filter(n=>n>="0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 const ciphertext=await seal(key,ws,"amego","SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings
  (workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
   changed_by_employee_id,changed_by_device_id,changed_at)
  VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:00:00.000Z')`)
  .run(ws,ciphertext,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 class Statement{
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async run(){return {meta:{changes:sql.prepare(this.q).run(...this.args).changes}};}
 }
 const db={prepare:q=>new Statement(q),async batch(parts){
  sql.exec("BEGIN IMMEDIATE");
  try{const r=parts.map(p=>({meta:{changes:sql.prepare(p.q).run(...p.args).changes}}));
      sql.exec("COMMIT");return r;}
  catch(e){sql.exec("ROLLBACK");throw e;}
 }};
 await create(db,createInput,env,authority(applicant));
 sql.prepare(`UPDATE allowance_applications SET status='review',revision=2,
 manager_actor_id='manager',actor_id='manager',updated_by_device_id=?,
 updated_at='2026-10-19T07:30:00.000Z'
 WHERE application_id=? AND status='open' AND revision=1`).run(devB,appId);
 return {sql,db,close:()=>sql.close()};
}
function transportFor(record=fakeRecord,onCall=()=>{}) {
 let count=0;
 return {get calls(){return count;},transport:async (url,opts)=>{
  count++;onCall();
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(form.get("data"),JSON.stringify({type:"invoice",invoice_number:originalInvoice}));
  const signature=createHash("md5").update(
   form.get("data")+form.get("time")+"SYNTHETIC_APP_KEY_DO_NOT_USE")
   .digest("hex");
  assert.equal(form.get("sign"),signature);
  return Response.json({code:0,data:[record]});
 }};
}
{
 const {sql,db,close}=await fixture();
 try{
  const fake=transportFor();
  const result=await preview(db,target,env,authority(),fake.transport,()=>when);
  assert.equal(fake.calls,1,"read-only official query exactly once");
  assert.equal(result.applicationId,appId);
  assert.equal(result.revision,2);
  assert.equal(result.originalInvoiceNumber,originalInvoice);
  assert.equal(result.officialUploadStatus,99);
  assert.equal(result.officialAllowanceCount,0);
  assert.equal(result.officialSummaryComplete,true);
  assert.equal(result.canApprove,false);
  assert.equal(result.canAllocateNumber,false);
  assert.equal(result.safeToInferRemainingBalance,false);
  assert.equal(result.requiresIndependentOfficialReconciliation,true);
  assert.ok(!Object.hasOwn(result,"unnumberedTemplateJson"));
  assert.ok(!JSON.stringify(result).includes("SYNTHETIC_APP_KEY"));
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals").get().n,0);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,0);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
  const noSummary=transportFor({...fakeRecord,allowance:undefined});
  const p=await preview(db,target,env,authority(),noSummary.transport,()=>when);
  assert.equal(p.officialSummaryComplete,false);
  assert.equal(p.canApprove,false);
  const partial=transportFor({...fakeRecord,allowance:[{allowance_number:"A20261019001"}]});
  const q=await preview(db,target,env,authority(),partial.transport,()=>when);
  assert.equal(q.officialAllowanceCount,1);
  assert.equal(q.officialSummaryComplete,false);
  assert.equal(q.officialAllowances[0].metadataComplete,false);
  const pending=transportFor({...fakeRecord,wait:[{invoice_type:"D0401"}]});
  const h=await preview(db,target,env,authority(),pending.transport,()=>when);
  assert.equal(h.officialMutationPending,true);
  assert.equal(h.officialSummaryComplete,false);
  const failed=transportFor({...fakeRecord,status:91});
  const f=await preview(db,target,env,authority(),failed.transport,()=>when);
  assert.equal(f.officialSummaryComplete,false);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  const unauthorized=transportFor();
  await assert.rejects(()=>preview(db,target,env,authority(applicant),
    unauthorized.transport,()=>when),/ALLOWANCE_REVIEW_MANAGER_REQUIRED/);
  assert.equal(unauthorized.calls,0,"employee cannot trigger official lookup");
  await assert.rejects(()=>preview(db,{...target,workspaceId:"ws_other"},env,
    authority({...manager,workspaceId:"ws_other"}),unauthorized.transport,()=>when),
    /ALLOWANCE_REVIEW_MANAGER_REQUIRED/);
  assert.equal(unauthorized.calls,0);
  await assert.rejects(()=>preview(db,{...target,expectedRevision:1},env,
    authority(),unauthorized.transport,()=>when),/ALLOWANCE_REVIEW_STALE/);
  assert.equal(unauthorized.calls,0);
  sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB);
  await assert.rejects(()=>preview(db,target,env,authority(),
    unauthorized.transport,()=>when),/ALLOWANCE_REVIEW_MANAGER_REQUIRED/);
  assert.equal(unauthorized.calls,0);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try {
  let authReads=0;
  const actor={revalidate:async()=>++authReads<=3?manager:applicant};
  await assert.rejects(()=>preview(db,target,env,actor,
    transportFor().transport,()=>when),/ALLOWANCE_REVIEW_AUTH_OR_REVISION_CHANGED/);
  let rotate=transportFor(fakeRecord,()=>{
   sql.prepare("UPDATE company_settings SET revision=revision+1 WHERE workspace_id=?").run(ws);
  });
  await assert.rejects(()=>preview(db,target,env,authority(),
   rotate.transport,()=>when),/ALLOWANCE_REVIEW_COMPANY_CHANGED/);
  assert.equal(rotate.calls,1,"company rotation after query never approves");
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try {
  const change=transportFor(fakeRecord,()=>{
   sql.prepare(`UPDATE allowance_applications SET status='rejected',revision=3,
    actor_id='manager',updated_by_device_id=?,updated_at='2026-10-19T08:00:00.000Z'
    WHERE application_id=? AND status='review'`).run(devB,appId);
  });
  await assert.rejects(()=>preview(db,target,env,authority(),
    change.transport,()=>when),/ALLOWANCE_REVIEW_AUTH_OR_REVISION_CHANGED/);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals").get().n,0);
 }finally{close();}
}
console.log("CYInvoice C3b1: official read-only manager preview, scoped credentials, no grant, active Device/revision fencing passed");
