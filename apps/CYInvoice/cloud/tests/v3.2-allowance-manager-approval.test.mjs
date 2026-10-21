import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";
import {createHash} from "node:crypto";

// Real internal TS and canonical schema16 + REVIEW-ONLY draft17/18 running
// in SQLite. Fake AMEGO HTTP and fake Worker-owned CYID authority only.
// No public route, real Cloudflare D1, or actual financial mutation.
async function bundle(file){
 const result=await build({entryPoints:["src/"+file],bundle:true,
  write:false,format:"esm",platform:"node",target:"node24"});
 return import("data:text/javascript;base64,"+
  Buffer.from(result.outputFiles[0].text).toString("base64"));
}
const {approveCloudAllowanceAfterOfficialProof:approve}=
 await bundle("allowance-manager-approval.ts");
const {createEncryptedCloudAllowance:create}=
 await bundle("allowance-frozen-store.ts");
const {encryptCompanyBusinessData:seal}=
 await bundle("company-settings.ts");
const ws="ws_c3b2b",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const key="aa".repeat(32),ctx={encryptionKeyHex:key,environment:"test"};
const now=new Date("2026-10-19T08:00:00.000Z");
const invoice="AB12345678",appId="application_c3b2b_one";
const template=JSON.stringify({AllowanceNumber:"",AllowanceType:2,
 AllowanceDate:"20261019",TotalAmount:50,TaxAmount:0,
 BuyerIdentifier:"0000000000",BuyerName:"Synthetic buyer",
 ProductItem:[{OriginalInvoiceNumber:invoice,OriginalInvoiceDate:20261019,
  OriginalDescription:"Synthetic refund",Quantity:1,UnitPrice:50,
  Amount:50,Tax:0,TaxType:1}]});
const input={workspaceId:ws,applicationId:appId,deviceId:devA,
 environment:"test",originalInvoiceNumber:invoice,
 allowanceDay:"20261019",requestedGross:50,unnumberedTemplateJson:template};
const reviewer={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const employee={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const identity=(person=reviewer)=>({revalidate:async()=>person});
const target={workspaceId:ws,applicationId:appId,deviceId:devB,expectedRevision:2};
const officialInvoice={invoice_number:invoice,status:99,cancel_date:0,
 total_amount:100,invoice_date:20261019,detail_vat:1,tax_amount:5,
 product_item:[{description:"Synthetic official line"}],allowance:[],wait:[]};
function fakeHttp(record=officialInvoice,onCall=()=>{}){
 let count=0;
 return {get count(){return count;},send:async (url,opts)=>{
  count++;onCall(url);
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.deepEqual(JSON.parse(form.get("data")),{type:"invoice",invoice_number:invoice});
  assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+"SYNTHETIC_APP_KEY_DO_NOT_USE").digest("hex"));
  return Response.json({code:0,data:[record]});
 }};
}
async function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0019_allowance_manual_review_audit.sql");
 for(const name of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'Synthetic')").run(ws);
 for(const dev of [devA,devB])
  sql.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
   VALUES (?,?,'Synthetic','active','cloud')`).run(dev,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const name of migrations.filter(x=>x>="0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 const encryptedKey=await seal(key,ws,"amego","SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings
   (workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
    changed_by_employee_id,changed_by_device_id,changed_at)
   VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:00:00.000Z')`)
  .run(ws,encryptedKey,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 class Statement{
  constructor(q){this.sql=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.sql).get(...this.args)??null;}
  async run(){return {meta:{changes:sql.prepare(this.sql).run(...this.args).changes}};}
 }
 let beforeBatch=null;
 const db={prepare:q=>new Statement(q),async batch(parts){
  if(beforeBatch){const fn=beforeBatch;beforeBatch=null;fn();}
  sql.exec("BEGIN IMMEDIATE");
  try{
   const data=parts.map(part=>({meta:{
    changes:sql.prepare(part.sql).run(...part.args).changes}}));
   sql.exec("COMMIT");return data;
  }catch(e){sql.exec("ROLLBACK");throw e;}
 }};
 await create(db,input,ctx,identity(employee));
 sql.prepare(`UPDATE allowance_applications
  SET status='review',revision=2,manager_actor_id='manager',
   actor_id='manager',updated_by_device_id=?,updated_at='2026-10-19T07:30:00.000Z'
  WHERE application_id=? AND status='open' AND revision=1`).run(devB,appId);
 return {sql,db,close:()=>sql.close(),
  setBatchRace:callback=>beforeBatch=callback};
}
const grants=sql=>sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_approvals").get().n;
const ledger=sql=>sql.prepare("SELECT COUNT(*) AS n FROM allowance_number_ledger").get().n;
{
 const {sql,db,close}=await fixture();
 try{
  const fake=fakeHttp();
  const grant=await approve(db,target,ctx,identity(),fake.send,()=>now);
  assert.match(grant.approvalId,/^apr_[0-9a-f-]{36}$/);
  assert.equal(grant.applicationId,appId);
  assert.equal(grant.approvedRevision,3);
  assert.equal(grant.managerActorId,"manager");
  assert.equal(grant.requestHash.length,64);
  assert.equal(grant.officialEvidenceHash.length,64);
  assert.equal(grant.sentToAmego,false);
  assert.equal(grant.numberReserved,false);
  assert.equal(fake.count,1,"one fresh signed read-only official query");
  assert.equal(grants(sql),1);
  assert.equal(ledger(sql),0);
  assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM work_items").get().n,0);
  const state=sql.prepare("SELECT status,revision FROM allowance_applications").get();
  assert.equal(state.status,"approved");
  assert.equal(state.revision,3);
  assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM allowance_application_audit_events").get().n,3);
  const saved=sql.prepare("SELECT * FROM allowance_execution_approvals").get();
  assert.equal(saved.official_snapshot_hash,grant.officialEvidenceHash);
  assert.equal(saved.approval_id,grant.approvalId);
  assert.equal(saved.reviewed_revision,2);
  assert.throws(()=>sql.prepare("DELETE FROM allowance_execution_approvals").run(),
   /ALLOWANCE_APPROVAL_APPEND_ONLY/);
  assert.throws(()=>sql.prepare("UPDATE allowance_execution_approvals SET request_hash='forged'").run(),
   /ALLOWANCE_APPROVAL_APPEND_ONLY/);
  const duplicate=fakeHttp();
  await assert.rejects(()=>approve(db,target,ctx,identity(),duplicate.send,()=>now),
   /ALLOWANCE_APPROVAL_STALE/);
  assert.equal(duplicate.count,0);
  assert.equal(grants(sql),1);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  for(const actor of [employee,null,{...reviewer,workspaceRole:"USER"},
   {...reviewer,workspaceId:"ws_elsewhere"}]){
   const fake=fakeHttp();
   await assert.rejects(()=>approve(db,target,ctx,identity(actor),fake.send,()=>now),
    /ALLOWANCE_APPROVAL_MANAGER_REQUIRED/);
   assert.equal(fake.count,0);
  }
  const wrongRev=fakeHttp();
  await assert.rejects(()=>approve(db,{...target,expectedRevision:1},
   ctx,identity(),wrongRev.send,()=>now),/ALLOWANCE_APPROVAL_TARGET_INVALID/);
  assert.equal(wrongRev.count,0);
  sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB);
  const revoked=fakeHttp();
  await assert.rejects(()=>approve(db,target,ctx,identity(),revoked.send,()=>now),
   /ALLOWANCE_APPROVAL_MANAGER_REQUIRED/);
  assert.equal(revoked.count,0);
  assert.equal(grants(sql),0);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  let count=0;
  const changeRole={revalidate:async()=>++count>=4?employee:reviewer};
  const fake=fakeHttp();
  await assert.rejects(()=>approve(db,target,ctx,changeRole,fake.send,()=>now),
   /ALLOWANCE_APPROVAL_AUTH_OR_REVISION_CHANGED/);
  assert.equal(grants(sql),0);
  assert.equal(fake.count,1);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  const rotate=fakeHttp(officialInvoice,()=>{
   sql.prepare("UPDATE company_settings SET revision=revision+1 WHERE workspace_id=?").run(ws);
  });
  await assert.rejects(()=>approve(db,target,ctx,identity(),rotate.send,()=>now),
   /ALLOWANCE_APPROVAL_COMPANY_CHANGED/);
  assert.equal(grants(sql),0);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  const changed=fakeHttp(officialInvoice,()=>{
   sql.prepare(`UPDATE allowance_applications SET status='rejected',revision=3,
    actor_id='manager',updated_by_device_id=?,updated_at='2026-10-19T08:00:00.000Z'
    WHERE application_id=? AND status='review' AND revision=2`).run(devB,appId);
  });
  await assert.rejects(()=>approve(db,target,ctx,identity(),changed.send,()=>now),
   /ALLOWANCE_APPROVAL_AUTH_OR_REVISION_CHANGED/);
  assert.equal(grants(sql),0);
 }finally{close();}
}
{
 const {sql,db,setBatchRace,close}=await fixture();
 try{
  setBatchRace(()=>sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  await assert.rejects(()=>approve(db,target,ctx,identity(),fakeHttp().send,()=>now),
   /ALLOWANCE_APPROVAL_READBACK_REQUIRED/);
  assert.equal(grants(sql),0,"revocation before transactional insert cannot leave grant");
  assert.equal(sql.prepare("SELECT status FROM allowance_applications").get().status,"review");
  assert.equal(ledger(sql),0);
 }finally{close();}
}
{
 const {sql,db,close}=await fixture();
 try{
  await assert.rejects(()=>approve(db,target,ctx,identity(),
    fakeHttp({...officialInvoice,allowance:undefined}).send,()=>now),
    /ALLOWANCE_OFFICIAL_PROOF_INVOICE_INCOMPLETE_OR_PENDING/);
  await assert.rejects(()=>approve(db,target,ctx,identity(),
    fakeHttp({...officialInvoice,status:91}).send,()=>now),
    /ALLOWANCE_OFFICIAL_PROOF_INVOICE_INCOMPLETE_OR_PENDING/);
  assert.equal(grants(sql),0);
  assert.equal(ledger(sql),0);
 }finally{close();}
}
console.log("CYInvoice C3b2b: real internal manager Grant CAS, official proof, rollback, role/device/revision checks, immutable audit and no-send passed");
