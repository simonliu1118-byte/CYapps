import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";
import {createHash} from "node:crypto";
// C3c2 ONLY synthetic source execution. No Cloudflare D1, Worker route,
// official AMEGO network or g0401/g0501 financial mutations.
async function bundle(path){
 const b=await build({entryPoints:["src/"+path],bundle:true,write:false,
  format:"esm",platform:"node",target:"node24"});
 return import("data:text/javascript;base64,"+
  Buffer.from(b.outputFiles[0].text).toString("base64"));
}
const {executeCloudAllowanceExactlyOnce:execute}=await bundle("allowance-one-shot-coordinator.ts");
const {approveCloudAllowanceAfterOfficialProof:approve}=
 await bundle("allowance-manager-approval.ts");
const {reserveApprovedCloudAllowanceNumber:reserve}=
 await bundle("allowance-number-reservation.ts");
const {createEncryptedCloudAllowance:create}=await bundle("allowance-frozen-store.ts");
const {encryptCompanyBusinessData:seal}=await bundle("company-settings.ts");

const ws="ws_c3c2",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const context={encryptionKeyHex:"aa".repeat(32),environment:"test"};
const employee={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const authority=(a=manager)=>({revalidate:async()=>a});
const time=new Date("2026-10-19T08:00:00.000Z"),clock=()=>time;
const invoice="AB12345678";
const template=JSON.stringify({AllowanceNumber:"",AllowanceType:2,
 AllowanceDate:"20261019",TotalAmount:50,TaxAmount:0,
 BuyerName:"FAKE BUYER",BuyerIdentifier:"0000000000",
 ProductItem:[{OriginalInvoiceNumber:invoice,OriginalInvoiceDate:20261019,
  OriginalDescription:"FAKE ITEM",Quantity:1,UnitPrice:50,
  Amount:50,Tax:0,TaxType:1}]});
const app1="application_c3c2_one",app2="application_c3c2_two";
const input=id=>({workspaceId:ws,applicationId:id,deviceId:devA,
 environment:"test",originalInvoiceNumber:invoice,allowanceDay:"20261019",
 requestedGross:50,unnumberedTemplateJson:template});
const approvalTarget=id=>({workspaceId:ws,applicationId:id,
 deviceId:devB,expectedRevision:2});
const reserveTarget=id=>({...approvalTarget(id),expectedRevision:3});
const sendTarget=(id,number)=>({...reserveTarget(id),
 ledgerId:number.ledgerId,allowanceNumber:number.allowanceNumber});
const invoiceRecord={invoice_number:invoice,status:99,cancel_date:0,
 total_amount:100,invoice_date:20261019,detail_vat:1,tax_amount:5,
 product_item:[{description:"FAKE ORIGINAL"}],allowance:[],wait:[]};
function fakeOfficial(row=invoiceRecord,onCall=()=>{}){
 let calls=0;
 return {get count(){return calls;},transport:async (url,opts)=>{
  calls++;onCall(url);
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.deepEqual(JSON.parse(form.get("data")),
   {type:"invoice",invoice_number:invoice});
  assert.equal(form.get("sign"),
   createHash("md5").update(form.get("data")+form.get("time")+
     "SYNTHETIC_APP_KEY_DO_NOT_USE").digest("hex"));
  return Response.json({code:0,data:[row]});
 }};
}
async function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0018_allowance_frozen_ledger.sql");
 for(const f of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES(?,'Synthetic')").run(ws);
 for(const device of [devA,devB])
  sql.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,
   status,employee_authority_state)
   VALUES(?,?,'Synthetic','active','cloud')`).run(device,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of migrations.filter(x=>x>="0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 const appKey=await seal(context.encryptionKeyHex,ws,"amego",
   "SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings(
  workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
  changed_by_employee_id,changed_by_device_id,changed_at)
  VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:40:00.000Z')`)
  .run(ws,appKey,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 let beforeSend=null,loseReply=false,afterSend=null;
 class Statement {
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async run(){
   if(this.q.includes("UPDATE allowance_number_ledger")&&beforeSend){
    const cb=beforeSend;beforeSend=null;cb();
   }
   const result={meta:{changes:sql.prepare(this.q).run(...this.args).changes}};
   if(this.q.includes("UPDATE allowance_number_ledger")&&afterSend){
    const cb=afterSend;afterSend=null;cb();
   }
   if(this.q.includes("UPDATE allowance_number_ledger")&&loseReply){
    loseReply=false;throw new Error("lost response AFTER successful write");
   }
   return result;
  }
 }
 const db={prepare:q=>new Statement(q),async batch(parts){
  sql.exec("BEGIN IMMEDIATE");
  try{
   const result=parts.map(p=>({meta:{
    changes:sql.prepare(p.q).run(...p.args).changes}}));
   sql.exec("COMMIT");return result;
  }catch(e){sql.exec("ROLLBACK");throw e;}
 }};
 async function prepare(id){
  await create(db,input(id),context,authority(employee));
  sql.prepare(`UPDATE allowance_applications SET status='review',revision=2,
   manager_actor_id='manager',actor_id='manager',updated_by_device_id=?,
   updated_at='2026-10-19T07:58:00.000Z' WHERE application_id=?`)
  .run(devB,id);
  const official=fakeOfficial();
  await approve(db,approvalTarget(id),context,authority(),
   official.transport,clock);
  assert.equal(official.count,1);
  const num=await reserve(db,reserveTarget(id),context,authority(),clock);
  assert.equal(num.status,"reserved");
  return sendTarget(id,num);
 }
 return {sql,db,prepare,close:()=>sql.close(),
  beforeSend:f=>beforeSend=f,afterSend:f=>afterSend=f,
  lostReply:()=>loseReply=true};
}

const status=(sql,id)=>sql.prepare("SELECT status FROM allowance_number_ledger WHERE application_id=?").get(id)?.status;
function providerFor(target,options={}){
 const urls=[];
 const item={original_invoice_number:invoice,original_invoice_date:20261019,
  original_description:"FAKE ITEM",quantity:1,unit_price:50,
  amount:50,tax:0,tax_type:1};
 const full={allowance_number:target.allowanceNumber,allowance_date:20261019,
  invoice_type:"D0401",invoice_status:99,allowance_type:2,
  buyer_identifier:"0000000000",buyer_name:options.otherBuyer?"EXTERNAL":"FAKE BUYER",
  total_amount:50,tax_amount:0,cancel_date:0,wait:[],product_item:[item]};
 return {urls,send:async(url,opts)=>{
  urls.push(url);
  const form=new URLSearchParams(opts.body);
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+"SYNTHETIC_APP_KEY_DO_NOT_USE").digest("hex"));
  if(url.endsWith("/invoice_query"))return Response.json({code:0,data:[invoiceRecord]});
  if(url.endsWith("/g0401")){
   assert.deepEqual(JSON.parse(form.get("data"))[0].AllowanceNumber,target.allowanceNumber);
   if(options.lostPost)throw Error("response lost after official transaction");
   return Response.json({code:options.collision?4040161:0});
  }
  if(url.endsWith("/allowance_query")){
   assert.equal(JSON.parse(form.get("data")).allowance_number,target.allowanceNumber);
   if(options.unavailableReadback)return Response.json({code:71,data:null});
   return Response.json({code:0,data:full});
  }
  throw Error("unexpected official mutation");
 }};
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target);
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,1);
  assert.equal(p.urls.filter(x=>x.endsWith("/invoice_query")).length,1);
  assert.equal(p.urls.filter(x=>x.endsWith("/allowance_query")).length,1);
  assert.equal(result.issueKind,"accepted_unconfirmed");
  assert.equal(result.originalNumberReadback,"full_match_unattributed");
  assert.equal(result.recordedReadback,true);
  assert.equal(result.safeToAutoConfirm,false);
  assert.equal(result.safeToAutoRetry,false);
  assert.equal(result.canAllocateAnotherNumber,false);
  assert.equal(status(sql,app1),"sending");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1);
  const audit=sql.prepare("SELECT official_snapshot_hash FROM allowance_reconciliation_checks").get();
  assert.match(audit.official_snapshot_hash,/^[a-f0-9]{64}$/);
  assert.throws(()=>sql.prepare("DELETE FROM allowance_reconciliation_checks").run(),/APPEND_ONLY/);
  const repeat=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),repeat.send,clock),
   /ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(repeat.urls.length,0,"never a second provider request on repeated call");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target,{lostPost:true,unavailableReadback:true});
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(result.issueKind,"unknown_result");
  assert.equal(result.originalNumberReadback,"unconfirmed");
  assert.equal(result.recordedReadback,false);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,1);
  assert.equal(status(sql,app1),"sending");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0);
  const second=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),second.send,clock),
   /ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(second.urls.length,0);
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target,{collision:true,otherBuyer:true});
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(result.issueKind,"confirmed_number_collision");
  assert.equal(result.originalNumberReadback,"unconfirmed");
  assert.equal(result.recordedReadback,false);
  assert.equal(status(sql,app1),"sending","collision alone never renumbers or confirms");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,1);
 }finally{close();}
}
{
 const {sql,db,prepare,afterSend,close}=await fixture();
 try{
  const target=await prepare(app1);
  afterSend(()=>sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  const p=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),p.send,clock),
   /ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,0);
  assert.equal(status(sql,app1),"sending","post-CAS revocation is irreversibly readback-only");
 }finally{close();}
}
{
 const {sql,db,prepare,lostReply,close}=await fixture();
 try {
  const target=await prepare(app1);
  lostReply();
  const p=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),p.send,clock),
   /ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,0);
  assert.equal(status(sql,app1),"sending");
 }finally{close();}
}
console.log("CYInvoice C3c3b: one-shot coordinator, exact immutable readback audit, unknown/collision/revocation no-retry passed");
