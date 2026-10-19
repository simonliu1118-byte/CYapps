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
const {claimCloudAllowanceOneShotSending:claim}=await bundle("allowance-sending-claim.ts");
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
 assert.equal(migrations.at(-1),"0016_non_allowance_work_items.sql");
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
 sql.exec(readFileSync("drafts/0017_allowance_applications.sql","utf8"));
 sql.exec(readFileSync("drafts/0018_allowance_frozen_ledger.sql","utf8"));
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
    changes:sql.prepare(p.sql).run(...p.args).changes}}));
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
const status=(sql,id)=>sql.prepare(`SELECT status FROM allowance_number_ledger
 WHERE application_id=?`).get(id)?.status;
{
 const {sql,db,prepare,close}=await fixture();
 try {
  const target=await prepare(app1);
  const official=fakeOfficial();
  const result=await claim(db,target,context,authority(),official.transport,clock);
  assert.equal(official.count,1);
  assert.equal(result.status,"sending");
  assert.equal(result.allowanceNumber,target.allowanceNumber);
  assert.equal(result.claimedExactlyOnce,true);
  assert.equal(result.providerPostPerformed,false);
  assert.equal(result.mayPostG0401FromThisResponse,false);
  assert.equal(status(sql,app1),"sending");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,1);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0);
  const second=fakeOfficial();
  await assert.rejects(()=>claim(db,target,context,authority(),
   second.transport,clock),/ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(second.count,0,"second attempt must not query and cannot post");
  assert.equal(status(sql,app1),"sending");
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  for(const bad of [employee,null,{...manager,workspaceRole:"USER"},
   {...manager,workspaceId:"ws_elsewhere"}]){
   const official=fakeOfficial();
   await assert.rejects(()=>claim(db,target,context,authority(bad),
    official.transport,clock),/ALLOWANCE_SENDING_MANAGER_REQUIRED/);
   assert.equal(official.count,0);
  }
  sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB);
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial().transport,clock),/ALLOWANCE_SENDING_MANAGER_REQUIRED/);
  assert.equal(status(sql,app1),"reserved");
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial({...invoiceRecord,total_amount:101}).transport,clock),
    /ALLOWANCE_SENDING_OFFICIAL_CHANGED_OR_GRANT_EXPIRED/,
    "official source changed since manager approval");
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial({...invoiceRecord,allowance:undefined}).transport,clock),
    /ALLOWANCE_OFFICIAL_PROOF_INVOICE_INCOMPLETE_OR_PENDING/);
  assert.equal(status(sql,app1),"reserved");
  const later=()=>new Date("2026-10-19T08:07:00.000Z");
  await assert.rejects(()=>claim(db,target,context,authority(),
    fakeOfficial().transport,later),
    /ALLOWANCE_SENDING_OFFICIAL_CHANGED_OR_GRANT_EXPIRED/);
  assert.equal(status(sql,app1),"reserved");
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  let roles=0;
  const changing={revalidate:async()=>++roles>=4?employee:manager};
  const official=fakeOfficial();
  await assert.rejects(()=>claim(db,target,context,changing,
   official.transport,clock),/ALLOWANCE_SENDING_AUTH_CHANGED/);
  assert.equal(official.count,1);
  assert.equal(status(sql,app1),"reserved");
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const official=fakeOfficial(invoiceRecord,()=>{
   sql.prepare("UPDATE company_settings SET revision=revision+1").run();
  });
  await assert.rejects(()=>claim(db,target,context,authority(),
   official.transport,clock),/ALLOWANCE_SENDING_COMPANY_CHANGED/);
  assert.equal(status(sql,app1),"reserved");
 }finally{close();}
}
{
 const {sql,db,prepare,beforeSend,close}=await fixture();
 try{
  const first=await prepare(app1),second=await prepare(app2);
  beforeSend(()=>sql.prepare(`UPDATE allowance_number_ledger
    SET status='sending' WHERE ledger_id=? AND status='reserved'`)
    .run(second.ledgerId));
  await assert.rejects(()=>claim(db,first,context,authority(),
    fakeOfficial().transport,clock),/ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(status(sql,app1),"reserved");
  assert.equal(status(sql,app2),"sending");
 }finally{close();}
}
{
 const {sql,db,prepare,beforeSend,close}=await fixture();
 try{
  const target=await prepare(app1);
  beforeSend(()=>sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial().transport,clock),/ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(status(sql,app1),"reserved");
 }finally{close();}
}
{
 const {sql,db,prepare,lostReply,close}=await fixture();
 try{
  const target=await prepare(app1);
  lostReply();
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial().transport,clock),/ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(status(sql,app1),"sending","lost response still consumes only CAS");
  const repeat=fakeOfficial();
  await assert.rejects(()=>claim(db,target,context,authority(),
   repeat.transport,clock),/ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(repeat.count,0);
 }finally{close();}
}
{
 const {sql,db,prepare,afterSend,close}=await fixture();
 try{
  const target=await prepare(app1);
  afterSend(()=>sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  await assert.rejects(()=>claim(db,target,context,authority(),
   fakeOfficial().transport,clock),/ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(status(sql,app1),"sending");
 }finally{close();}
}
console.log("CYInvoice C3c2: real TS+schema16/drafts17-18 one-shot CAS, official recheck, revoked CYID/Device, competing sending, lost acknowledgement, and no provider mutation passed");
