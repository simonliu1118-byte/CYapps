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
 requestedGross:50,reason:"Reason private",unnumberedTemplateJson:template});
const approvalTarget=id=>({workspaceId:ws,applicationId:id,
 deviceId:devB,expectedRevision:2});
const reserveTarget=id=>({...approvalTarget(id),expectedRevision:3});
const sendTarget=(id,number)=>({...reserveTarget(id),
 ledgerId:number.ledgerId,allowanceNumber:number.allowanceNumber});
const invoiceRecord={order_id:"SYNTHETIC_ORDER",invoice_number:invoice,status:99,cancel_date:0,
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
     "sHeq7t8G1wiQvhAuIM27").digest("hex"));
  return Response.json({code:0,data:[row]});
 }};
}
// Entirely synthetic transport: no Cloudflare, no AMEGO network access.
function fakeOneShotProvider(allowanceNumber,collision=false,expectedKey="sHeq7t8G1wiQvhAuIM27"){
 const calls=[];
 const product={original_invoice_number:collision?"CD87654321":invoice,original_invoice_date:20261019,
  original_description:"FAKE ITEM",quantity:1,unit_price:50,
  amount:50,tax:0,tax_type:1};
 const official={allowance_number:allowanceNumber,allowance_date:20261019,
  invoice_type:"D0401",invoice_status:99,allowance_type:2,
  buyer_identifier:collision?"87654321":"0000000000",buyer_name:"FAKE BUYER",
  total_amount:50,tax_amount:0,cancel_date:0,wait:[],product_item:[product]};
 return {calls,transport:async(url,options)=>{
  calls.push(url);
  assert.equal(options.method,"POST");
  assert.equal(options.redirect,"error");
  const form=new URLSearchParams(options.body);
  assert.equal(form.get("sign"),
   createHash("md5").update(form.get("data")+form.get("time")+
    expectedKey).digest("hex"));
  if(url.endsWith("/invoice_query"))
   return Response.json({code:0,data:[invoiceRecord]});
  if(url.endsWith("/g0401")){
   assert.equal(JSON.parse(form.get("data"))[0].AllowanceNumber,allowanceNumber);
   return Response.json({code:collision?4040161:0});
  }
  if(url.endsWith("/allowance_query")){
   assert.equal(JSON.parse(form.get("data")).allowance_number,allowanceNumber);
   return Response.json({code:0,data:official});
  }
  throw Error("Unexpected official URL");
 }};
}
async function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0022_allowance_unsent_withdrawal.sql");
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
 // Canonical schema19 is included above; all fixtures remain in memory.
 const appKey=await seal(context.encryptionKeyHex,ws,"amego",
   "SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings(
  workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
  changed_by_employee_id,changed_by_device_id,changed_at)
  VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:40:00.000Z')`)
  .run(ws,appKey,devB);
 // Canonical 0017/0018 were applied in the migration loop above.
 let beforeSend=null,loseReply=false,afterSend=null;
 const runD1=(query,args)=>{
  const total=()=>Number(sql.prepare("SELECT total_changes() AS n").get().n);
  const before=total();sql.prepare(query).run(...args);return {meta:{changes:total()-before}};
 };
 class Statement {
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async all(){return {results:sql.prepare(this.q).all(...this.args)};}
  async run(){
   if(this.q.includes("UPDATE allowance_number_ledger")&&beforeSend){
    const cb=beforeSend;beforeSend=null;cb();
   }
   const result=runD1(this.q,this.args);
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
   const result=parts.map(p=>(runD1(p.q,p.args)));
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
function config(db,user,allowanceFlag="true",schema="21"){
 return {DB:db,APP_ENV:"development",SCHEMA_VERSION:schema,
 ALLOWANCE_API_ENABLED:allowanceFlag,COMPANY_SETTINGS_KEY_HEX:context.encryptionKeyHex,
 CYID_ENABLED:"true",IDENTITY:{},
 IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_workspace",
 IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
 cyIdContext:{revalidate:async()=>user,verifiedEmail:async()=>"synthetic@example.invalid"}};
}
async function call(env,action,body={},token=rawDevA,context){
 const req=new Request("https://fake.invalid/v1/allowances/"+action,{
  method:"POST",headers:{"authorization":"Bearer "+token,
    "content-type":"application/json"},body:JSON.stringify({employeeNo:"1234",
    password:"NOT_A_REAL_PASSWORD",...body})});
 const res=await handle(req,env,context);
 assert.ok(res);
 return {status:res.status,body:await res.json()};
}
const f=await fixture();const {sql,db}=f;const savedFetch=globalThis.fetch;
try{
 sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(await hash(rawDevA),devA);
 sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(await hash(rawDevB),devB);
 const employee=config(db,principal("USER","employee"));const manager=config(db,principal("ADMIN","manager"));
 assert.equal((await call(config(db,principal("USER","employee"),"false"),"list")).status,503);
 assert.equal((await call(config(db,principal("USER","employee"),"true","20"),"list")).status,503);
 for(const action of ["preview","void-preview","review","reserve","send-once","reserve-after-collision","manual-review","attempt"])
  assert.equal((await call(manager,action,{applicationId:app1,expectedRevision:1})).status,410);
 assert.equal((await call(employee,"get",{applicationId:"bad"})).status,400);
 assert.equal((await call(employee,"list",{limit:51})).status,400);
 assert.equal((await call(employee,"list",{workspaceId:"ws_other"})).status,400);
 assert.equal((await call(employee,"list",{},"cydev_"+"c".repeat(64))).status,401);
 const created=await call(employee,"create",{...input(app1),workspaceId:undefined,deviceId:undefined});
 assert.equal(created.status,200);assert.equal(created.body.revision,1);
 assert.equal((await call(employee,"create",{...input(app1),workspaceId:undefined,deviceId:undefined})).status,409);
 const environmentFixture=await fixture();
 try {
 const environmentSql=environmentFixture.sql;
 for(const [device,raw] of [[devA,rawDevA],[devB,rawDevB]])
  environmentSql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(await hash(raw),device);
 const environmentEmployee=config(environmentFixture.db,principal("USER","employee"));
 const environmentManager=config(environmentFixture.db,principal("ADMIN","manager"));
 // APP_ENV is a deployment label; persisted business environment selects credentials.
 const prodId="application_production_environment";
 const prodCreate=await call(environmentEmployee,"create",{...input(prodId),environment:"production",workspaceId:undefined,deviceId:undefined});
 assert.equal(prodCreate.status,200,JSON.stringify(prodCreate));
 assert.equal(environmentSql.prepare("SELECT environment FROM allowance_applications WHERE application_id=?").get(prodId).environment,"production");
 const prodProvider=fakeOneShotProvider("P20261019001",false,"SYNTHETIC_APP_KEY_DO_NOT_USE");
 globalThis.fetch=prodProvider.transport;
 const prodDetail=await call(environmentManager,"detail",{applicationId:prodId,operation:"issue"},rawDevB);
 assert.equal(prodDetail.status,200,JSON.stringify(prodDetail));
 assert.ok(prodProvider.calls.some(x=>x.endsWith("/invoice_query")));
 const testOnProdId="application_test_on_production_worker";
 const testOnProd=await call({...environmentEmployee,APP_ENV:"production"},"create",{...input(testOnProdId),workspaceId:undefined,deviceId:undefined});
 assert.equal(testOnProd.status,200,JSON.stringify(testOnProd));
 assert.equal(environmentSql.prepare("SELECT environment FROM allowance_applications WHERE application_id=?").get(testOnProdId).environment,"test");

 } finally {environmentFixture.close();}

 assert.equal((await call(config(db,principal("USER","another")),"get",{applicationId:app1})).status,404);
 assert.equal((await call(employee,"detail",{applicationId:app1,operation:"issue"})).status,403);
 const provider=fakeOneShotProvider("A20261019001");globalThis.fetch=provider.transport;
 const detail=await call(manager,"detail",{applicationId:app1,operation:"issue"},rawDevB);
 assert.equal(detail.status,200);assert.ok(detail.body.rows.some(r=>r.label==="原發票商品"));
 assert.ok(detail.body.rows.some(r=>r.label==="本次折讓商品"));
 assert.ok(detail.body.rows.some(r=>r.label==="訂單編號"&&r.value==="SYNTHETIC_ORDER"));
 assert.ok(detail.body.rows.some(r=>r.label==="申請員工"&&r.value==="1234 Synthetic"));
 assert.ok(detail.body.rows.some(r=>r.label==="核准管理員"&&r.value==="尚未核准"));
 assert.deepEqual(sql.prepare("SELECT status,revision FROM allowance_applications WHERE application_id=?").get(app1),Object.assign(Object.create(null),{status:"open",revision:1}));
 assert.equal((await call(manager,"approve",{applicationId:app1,expectedRevision:1},rawDevB)).status,503);
 assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_approvals").get().n,0);
 manager.ALLOWANCE_G0401_SEND_ENABLED="true";
 const approved=await call(manager,"approve",{applicationId:app1,expectedRevision:1},rawDevB);
 assert.equal(approved.status,200,JSON.stringify(approved));assert.equal(approved.body.executionState,"completed");
 const approvedDetail=await call(manager,"detail",{applicationId:app1,operation:"issue"},rawDevB);
 assert.equal(approvedDetail.status,200);
 assert.ok(approvedDetail.body.rows.some(r=>r.label==="核准管理員"&&r.value==="1234 Synthetic"));
 assert.equal((await call({...employee,ALLOWANCE_G0401_SEND_ENABLED:"true"},"approve",{applicationId:app1,expectedRevision:1})).status,403);
 assert.equal(provider.calls.filter(x=>x.endsWith("/g0401")).length,1);
 assert.equal((await call(manager,"approve",{applicationId:app1,expectedRevision:1},rawDevB)).status,409);
 assert.equal(provider.calls.filter(x=>x.endsWith("/g0401")).length,1);
 const state=await call(employee,"status",{applicationId:app1});assert.equal(state.body.officiallyConfirmed,true);
 assert.ok(sql.prepare("SELECT authority_snapshot FROM allowance_execution_approvals WHERE application_id=?").get(app1).authority_snapshot);
 // Manager cancellation retains the immutable grant and reserved number.
 const cancelledId="application_cancel_unsent";
 await f.prepare(cancelledId);
 const manager22=config(db,principal("ADMIN","manager"),"true","22");
 assert.equal((await call(manager,"withdraw",{applicationId:cancelledId,expectedRevision:3},rawDevB)).status,503);
 const employee22=config(db,principal("USER","employee"),"true","22");
 assert.equal((await call(employee22,"withdraw",{applicationId:cancelledId,expectedRevision:3},rawDevB)).status,409);
 const cancelled=await call(config(db,principal("ADMIN","different_manager"),"true","22"),"withdraw",{applicationId:cancelledId,expectedRevision:3},rawDevB);
 assert.equal(cancelled.status,200,JSON.stringify(cancelled));
 assert.equal(sql.prepare("SELECT status FROM allowance_number_ledger WHERE application_id=?").get(cancelledId).status,"reserved");
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE application_id=?").get(cancelledId).n,1);
 assert.equal(sql.prepare("SELECT to_status FROM allowance_application_audit_events WHERE application_id=? ORDER BY revision DESC").get(cancelledId).to_status,"withdrawn");
 assert.equal((await call(manager22,"withdraw",{applicationId:cancelledId,expectedRevision:3},rawDevB)).status,409);
 assert.equal((await call(manager22,"withdraw",{applicationId:app1,expectedRevision:3},rawDevB)).status,409);
 const sendingId="application_cancel_sending";await f.prepare(sendingId);
 sql.prepare("UPDATE allowance_number_ledger SET status='sending' WHERE application_id=?").run(sendingId);
 assert.equal((await call(manager22,"withdraw",{applicationId:sendingId,expectedRevision:3},rawDevB)).status,409);
 assert.throws(()=>sql.prepare("UPDATE allowance_applications SET status='withdrawn',revision=4 WHERE application_id=?").run(sendingId),/TRANSITION_INVALID/);
 let voided=false,voidPosts=0;
 globalThis.fetch=async(url,options)=>{
  if(url.endsWith("/invoice_query"))return Response.json({code:0,data:[invoiceRecord]});
  if(url.endsWith("/allowance_status"))return Response.json({code:0,data:{"ALW-001":{type:voided?"D0501":"D0401",status:99}}});
  if(url.endsWith("/allowance_query"))return Response.json({code:0,data:{allowance_number:"ALW-001",invoice_type:voided?"D0501":"D0401",invoice_status:99,
   allowance_type:2,cancel_date:voided?20261022:0,buyer_identifier:"0000000000",buyer_name:"FAKE BUYER",total_amount:50,tax_amount:0,wait:[],
   product_item:[{original_invoice_number:invoice,original_invoice_date:20261019,original_description:"FAKE ITEM",quantity:1,unit_price:50,amount:50,tax:0,tax_type:1}]}});
  if(url.endsWith("/g0501")){voidPosts++;voided=true;return Response.json({code:0});}
  throw new Error("unexpected financial transport");
 };
 const voidId="void_route_synthetic_01";
 assert.equal((await call(employee,"void-create",{applicationId:voidId,environment:"test",allowanceNumber:"ALW-001",originalInvoiceNumber:invoice,reason:"SYNTHETIC RETURN"})).status,200);
 const vd=await call(manager,"detail",{applicationId:voidId,operation:"void"},rawDevB);
 assert.equal(vd.status,200);assert.ok(vd.body.rows.some(r=>r.label==="原折讓商品"));
 const body={applicationId:voidId,expectedRevision:1,requestHash:vd.body.requestHash,expectedOfficialSnapshotHash:vd.body.officialSnapshotHash};
 assert.equal((await call(manager,"void-approve",body,rawDevB)).status,503);
 manager.ALLOWANCE_G0501_SEND_ENABLED="true";
 const voidResult=await call(manager,"void-approve",body,rawDevB);
 assert.equal(voidResult.status,200);assert.equal(voidResult.body.executionState,"completed");assert.equal(voidPosts,1);
 assert.equal((await call(employee,"void-status",{applicationId:voidId})).body.officiallyConfirmed,true);
 assert.equal((await call(manager,"detail",{applicationId:voidId,operation:"void"},rawDevB)).status,200,"completed details remain readable");
 // Long histories are paged, never truncated to the first/last arbitrary slice.
 for(let i=0;i<205;i++)sql.prepare(`INSERT INTO allowance_execution_events(workspace_id,application_id,operation,stage,request_id,recorded_at)
  VALUES(?,?,'issue','synthetic','request_synthetic',?)`).run(ws,app1,time.toISOString());
 let cursor=0,total=0;for(;;){
  const page=await call(manager,"diagnostics",{applicationId:app1,operation:"issue",afterEventId:cursor},rawDevB);
  assert.equal(page.status,200);assert.ok(page.body.events.length<=100);total+=page.body.events.length;
  assert.ok(!JSON.stringify(page.body).includes("SYNTHETIC_APP_KEY_DO_NOT_USE"));
  if(!page.body.hasMore)break;assert.ok(page.body.nextEventId>cursor);cursor=page.body.nextEventId;
 }
 assert.ok(total>205);
 // Actual same-Device grant -> response -> background lifecycle. No live session retained.
 const same=await fixture();
 try {
  for(const [device,raw] of [[devA,rawDevA],[devB,rawDevB]])
   same.sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(await hash(raw),device);
  const user=config(same.db,principal("USER","employee"));
  const admin={...config(same.db,principal("ADMIN","manager")),ALLOWANCE_G0401_SEND_ENABLED:"true",
   IDENTITY_AUTHORITY:{invalidateCache:async()=>({workspaceId:"identity_workspace",applicationId:"CYINVOICE",available:true,invalidated:[]})}};
  const sameId="application_same_device_background";
  assert.equal((await call(user,"create",{...input(sameId),workspaceId:undefined,deviceId:undefined},rawDevA)).status,200);
  const slow=fakeOneShotProvider("A20261019001");let release;
  const gate=new Promise(resolve=>{release=resolve;});
  globalThis.fetch=async(url,options)=>{if(url.endsWith("/g0401"))await gate;return slow.transport(url,options);};
  const pending=[];
  const ack=await call(admin,"approve",{applicationId:sameId,expectedRevision:1},rawDevA,{waitUntil:p=>pending.push(p)});
  assert.equal(ack.status,200,JSON.stringify(ack));assert.equal(ack.body.executionState,"processing");
  assert.equal(same.sql.prepare("SELECT status FROM allowance_applications WHERE application_id=?").get(sameId).status,"approved");
  assert.equal(same.sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_approvals WHERE application_id=?").get(sameId).n,1);
  admin.cyIdContext.revalidate=async()=>{throw new Error("LIVE_SESSION_ALREADY_CLOSED");};
  release();await Promise.all(pending);
  assert.equal(slow.calls.filter(x=>x.endsWith("/g0401")).length,1);
  assert.equal(same.sql.prepare("SELECT execution_state FROM allowance_applications WHERE application_id=?").get(sameId).execution_state,"completed");
  // Pre-approval failure must keep open revision1, record the stage and classify a problem.
  const failId="application_preapproval_failure";
  assert.equal((await call(user,"create",{...input(failId),workspaceId:undefined,deviceId:undefined},rawDevA)).status,200);
  const failAdmin={...config(same.db,principal("ADMIN","manager")),ALLOWANCE_G0401_SEND_ENABLED:"true"};
  // Detail failures return safe diagnostics but do not save an approval failure,
  // alter execution state, create grants/ledger entries, or send a transaction.
  const detailId="application_detail_read_failure";
  assert.equal((await call(user,"create",{...input(detailId),workspaceId:undefined,deviceId:undefined},rawDevA)).status,200);
  const beforeDetail=same.sql.prepare("SELECT * FROM allowance_applications WHERE application_id=?").get(detailId);
  const beforeEvents=same.sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_events WHERE application_id=?").get(detailId).n;
  let detailCalls=0;
  globalThis.fetch=async()=>{detailCalls++;throw new TypeError("TLS handshake failed SECRET_RAW_PROVIDER_PAYLOAD");};
  const detailFailure=await call(failAdmin,"detail",{applicationId:detailId,operation:"issue"},rawDevA);
  assert.equal(detailFailure.status,409);assert.equal(detailFailure.body.error.stage,"detail_official_invoice_query");
  assert.equal(detailFailure.body.error.officialQuery.failureKind,"tls");
  assert.equal(detailFailure.body.error.officialQuery.responseReceived,false);
  assert.equal(detailCalls,1);assert.ok(!JSON.stringify(detailFailure).includes("SECRET_RAW_PROVIDER_PAYLOAD"));
  // A separate balance-check fetch must report its own phase.
  detailCalls=0;
  globalThis.fetch=async()=>{if(++detailCalls===1)return Response.json({code:0,data:[invoiceRecord]});throw new DOMException("SECRET_RAW_PROVIDER_PAYLOAD","TimeoutError");};
  const balanceFailure=await call(failAdmin,"detail",{applicationId:detailId,operation:"issue"},rawDevA);
  assert.equal(balanceFailure.body.error.stage,"detail_official_balance_check");
  assert.equal(balanceFailure.body.error.officialQuery.failureKind,"timeout");assert.equal(detailCalls,2);
  assert.deepEqual(same.sql.prepare("SELECT * FROM allowance_applications WHERE application_id=?").get(detailId),beforeDetail);
  assert.equal(same.sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_events WHERE application_id=?").get(detailId).n,beforeEvents);
  for(const table of ["allowance_execution_approvals","allowance_number_ledger"])
   assert.equal(same.sql.prepare(`SELECT COUNT(*) AS n FROM ${table} WHERE application_id=?`).get(detailId).n,0);
  globalThis.fetch=async()=>{throw new Error("SECRET_RAW_PROVIDER_PAYLOAD");};
  const rejected=await call(failAdmin,"approve",{applicationId:failId,expectedRevision:1},rawDevA);
  assert.equal(rejected.status,409);assert.equal(rejected.body.error.stage,"approval_official_check");
  const failedState=same.sql.prepare("SELECT status,revision,execution_state,last_error FROM allowance_applications WHERE application_id=?").get(failId);
  assert.equal(failedState.status,"open");assert.equal(failedState.revision,1);assert.equal(failedState.execution_state,"problem");
  assert.equal(same.sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_approvals WHERE application_id=?").get(failId).n,0);
  const events=same.sql.prepare("SELECT stage,error_code FROM allowance_execution_events WHERE application_id=? ORDER BY event_id").all(failId);
  assert.ok(events.some(x=>x.stage==="approval_failed"));assert.ok(!JSON.stringify(events).includes("SECRET_RAW_PROVIDER_PAYLOAD"));
 } finally {same.close();}
 console.log("Allowance HTTP ACL, view/cancel without mutation, one approval including send, default-off gates and complete paged diagnostics passed.");
}finally{globalThis.fetch=savedFetch;f.close();}
