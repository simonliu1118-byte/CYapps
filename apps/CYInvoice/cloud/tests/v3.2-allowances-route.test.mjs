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
const {handleAllowances:handle}=await bundle("allowances-route.ts");
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
  async all(){return {results:sql.prepare(this.q).all(...this.args)};}
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

const rawDevA="cydev_"+"a".repeat(64),rawDevB="cydev_"+"b".repeat(64);
const hash=async(v)=>Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256",
 new TextEncoder().encode(v))),b=>b.toString(16).padStart(2,"0")).join("");
const principal=(role,id)=>({workspaceId:"identity_workspace",employeeId:id,
 employeeNo:"1234",displayName:"Synthetic",workspaceRole:role,
 isIdentityAdmin:false,emailVerified:true,
 isWorkspaceSuperAdmin:role==="SUPER_ADMIN",credentialVersion:1,employeeRevision:1});
function config(db,user,allowanceFlag="true",schema="18"){
 return {DB:db,APP_ENV:"development",SCHEMA_VERSION:schema,
 ALLOWANCE_API_ENABLED:allowanceFlag,COMPANY_SETTINGS_KEY_HEX:context.encryptionKeyHex,
 CYID_ENABLED:"true",IDENTITY:{},
 IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_workspace",
 IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
 cyIdContext:{revalidate:async()=>user,verifiedEmail:async()=>"synthetic@example.invalid"}};
}
async function call(env,action,body={},token=rawDevA){
 const req=new Request("https://fake.invalid/v1/allowances/"+action,{
  method:"POST",headers:{"authorization":"Bearer "+token,
    "content-type":"application/json"},body:JSON.stringify({employeeNo:"1234",
    password:"NOT_A_REAL_PASSWORD",...body})});
 const res=await handle(req,env);
 assert.ok(res);
 return {status:res.status,body:await res.json()};
}
{
 const {sql,db,close}=await fixture();
 try{
  sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?")
   .run(await hash(rawDevA),devA);
  sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?")
   .run(await hash(rawDevB),devB);
  const employee=config(db,principal("USER","employee"));
  const manager=config(db,principal("ADMIN","manager"));
  assert.equal((await call(config(db,principal("USER","employee"),"false"),"list")).status,503);
  assert.equal((await call(config(db,principal("USER","employee"),"true","16"),"list")).status,503);
  assert.equal((await call(employee,"list",{},"cydev_"+"0".repeat(64))).status,401);
  assert.equal((await call(employee,"create",{applicationId:app1,
   environment:"test",originalInvoiceNumber:invoice,allowanceDay:"20261019",
   requestedGross:50,unnumberedTemplateJson:template,untrustedClaim:true})).status,400);
  const created=await call(employee,"create",{applicationId:app1,
   environment:"test",originalInvoiceNumber:invoice,allowanceDay:"20261019",
   requestedGross:50,unnumberedTemplateJson:template});
  assert.equal(created.status,200);
  assert.equal(created.body.status,"open");
  assert.equal(created.body.officialActionPerformed,false);
  const got=await call(employee,"get",{applicationId:app1});
  assert.equal(got.status,200);
  assert.equal(got.body.application.status,"open");
  assert.ok(!JSON.stringify(got.body).includes("unnumberedTemplateJson"));
  assert.ok(!JSON.stringify(got.body).includes("NOT_A_REAL_APP"));
  const employeeList=await call(employee,"list");
  assert.equal(employeeList.body.items.length,1);
  assert.equal((await call(employee,"review",{applicationId:app1,
   expectedRevision:1})).status,403);
  const reviewed=await call(manager,"review",{applicationId:app1,
   expectedRevision:1},rawDevB);
  assert.equal(reviewed.status,200);
  assert.equal(reviewed.body.revision,2);
  assert.equal((await call(manager,"review",{applicationId:app1,
   expectedRevision:1},rawDevB)).status,409);
  assert.equal((await call(employee,"withdraw",{applicationId:app1,
   expectedRevision:2})).status,409);
  const p=fakeOfficial();
  const old=globalThis.fetch;
  try{
   globalThis.fetch=p.transport;
   const preview=await call(manager,"preview",{applicationId:app1,
    expectedRevision:2},rawDevB);
   assert.equal(preview.status,200);
   assert.equal(preview.body.preview.canApprove,false);
   const approved=await call(manager,"approve",{applicationId:app1,
    expectedRevision:2},rawDevB);
   assert.equal(approved.status,200);
   assert.equal(approved.body.status,"approved");
   assert.equal(approved.body.officialActionPerformed,false);
   const reserved=await call(manager,"reserve",{applicationId:app1,
    expectedRevision:3},rawDevB);
   assert.equal(reserved.status,200);
   assert.equal(reserved.body.status,"reserved");
   assert.equal(reserved.body.maySendWithoutFreshOfficialCheck,false);
   assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,1);
   assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0);
  } finally {globalThis.fetch=old;}
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
console.log("CYInvoice C4: opt-in schema18 CYID Device scoped allowance create, manager CAS, preview, approve, reserve and no public financial send passed");
