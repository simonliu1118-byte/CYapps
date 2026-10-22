import assert from "node:assert/strict";
import {readFileSync,readdirSync,mkdtempSync,copyFileSync,rmSync} from "node:fs";
import {tmpdir} from "node:os";
import {join,dirname} from "node:path";
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
const {executeApprovedCloudAllowance:execute,approvedExecutionAuthority,resumeApprovedCloudAllowances}=await bundle("allowance-one-shot-coordinator.ts");
const {approveCloudAllowanceAfterOfficialProof:approve}=
 await bundle("allowance-manager-approval.ts");
const {reserveApprovedCloudAllowanceNumber:reserve,
 reserveCloudAllowanceAfterExternalCollision:reserveCollision}=
 await bundle("allowance-number-reservation.ts");
const {createEncryptedCloudAllowance:create}=await bundle("allowance-frozen-store.ts");
const {encryptCompanyBusinessData:seal}=await bundle("company-settings.ts");

const ws="ws_c3c2",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const context={encryptionKeyHex:"aa".repeat(32),environment:"test"};
const employee={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const cached={identityWorkspaceId:"identity_synthetic",applicationId:"CYINVOICE",employeeId:"manager",credentialVersion:1,employeeRevision:1,workspaceRole:"ADMIN",isIdentityAdmin:false,emailVerified:true};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN",authoritySnapshot:JSON.stringify(cached)};
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
async function fixture(schema19=false,filename=":memory:",through21=true,through22=true){
 const sql=new DatabaseSync(filename);
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
 for(const f of migrations.filter(x=>x>="0014"&&x<"0019"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 for(const file of migrations.filter(x=>x>="0019"&&(through21||x<"0021")&&(through22||x<"0022")))sql.exec(readFileSync("migrations/"+file,"utf8"));
 const appKey=await seal(context.encryptionKeyHex,ws,"amego",
   "SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings(
  workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
  changed_by_employee_id,changed_by_device_id,changed_at)
  VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:40:00.000Z')`)
  .run(ws,appKey,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 let beforeSend=null,loseReply=false,afterSend=null,beforeBatch=null,lostBatch=false;
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
  if(beforeBatch){const f=beforeBatch;beforeBatch=null;f();}
  sql.exec("BEGIN IMMEDIATE");
  let result;
  try{
   result=parts.map(p=>({meta:{
    changes:sql.prepare(p.q).run(...p.args).changes}}));
   sql.exec("COMMIT");
  }catch(e){sql.exec("ROLLBACK");throw e;}
  if(lostBatch){lostBatch=false;throw new Error("lost batch reply AFTER commit");}
  return result;
 }};
 async function prepare(id){
  await create(db,input(id),context,authority(employee));
  const official=fakeOfficial();
  await approve(db,{...approvalTarget(id),expectedRevision:1},context,authority(through21?manager:{...manager,authoritySnapshot:undefined}),
   official.transport,clock);
  assert.equal(official.count,1);
  const num=await reserve(db,reserveTarget(id),context,authority(through21?manager:{...manager,authoritySnapshot:undefined}),clock);
  assert.equal(num.status,"reserved");
  return sendTarget(id,num);
 }
 return {sql,db,prepare,close:()=>sql.close(),
  beforeSend:f=>beforeSend=f,afterSend:f=>afterSend=f,
  lostReply:()=>loseReply=true,beforeBatch:f=>beforeBatch=f,lostBatch:()=>lostBatch=true};
}


const {createCloudAllowanceVoidApplication:createVoid}=await bundle("allowance-void-applications.ts");
const {previewCloudVoidForManager:previewVoid}=await bundle("allowance-void-manager-preview.ts");
const {approveCloudVoidAfterOfficialProof:approveVoid}=await bundle("allowance-void-manager-approval.ts");
const {executeCloudVoidOnce:executeVoid}=await bundle("allowance-void-one-shot-coordinator.ts");
const {allowanceNumberFromSequence:format}=await bundle("allowance-number-reservation.ts");
function provider(options={}){
 let posts=0,queries=0;
 const transport=async(url,opts)=>{
  const data=JSON.parse(new URLSearchParams(opts.body).get("data"));
  if(url.endsWith("/invoice_query"))return Response.json({code:0,data:[invoiceRecord]});
  if(url.endsWith("/g0401")){
   posts++;
   if(posts<=(options.collisions??0))return Response.json({code:4040161});
   if(options.unknown)throw new Error("synthetic lost response");
   return Response.json({code:0});
  }
  if(url.endsWith("/allowance_query")){
   queries++;
   if(options.unavailable)return Response.json({code:71,data:null});
   const number=data.allowance_number;
   return Response.json({code:0,data:{allowance_number:number,allowance_date:20261019,
    invoice_type:"D0401",invoice_status:options.pending?31:99,allowance_type:2,
    buyer_identifier:options.mismatch?"87654321":"0000000000",buyer_name:"FAKE BUYER",total_amount:50,tax_amount:0,cancel_date:0,wait:[],
    product_item:[{original_invoice_number:invoice,original_invoice_date:20261019,original_description:"FAKE ITEM",quantity:1,unit_price:50,amount:50,tax:0,tax_type:1}]}});
  }
  throw new Error("unexpected provider operation");
 };
 return {transport,get posts(){return posts;},get queries(){return queries;}};
}
for(const [sequence,suffix] of [[1,"001"],[999,"999"],[1000,"A00"],[34695,"ZZZ"]])assert.equal(format("20261019",sequence),"A20261019"+suffix);
assert.throws(()=>format("20261019",34696));
// Exact official content closes automatically, including restart after timeout.
for(const mode of ["success","unknown","pending","mismatch"]){
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider({unknown:mode==="unknown",pending:mode==="pending",mismatch:mode==="mismatch"});
 const result=await execute(f.db,t,context,authority(),api.transport,clock);
 assert.equal(result.executionState,mode==="pending"?"processing":mode==="mismatch"?"problem":"completed");
 assert.equal(api.posts,1);
 const resumed=provider();await execute(f.db,t,context,authority(),resumed.transport,clock);
 assert.equal(resumed.posts,0,"already sent/confirmed never POSTs after restart");
 assert.equal(f.sql.prepare("SELECT status FROM allowance_number_ledger WHERE application_id=? ORDER BY attempt DESC").get(app1).status,"confirmed");
 f.close();
}
{
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider({collisions:25});
 const result=await execute(f.db,t,context,authority(),api.transport,clock);
 assert.equal(result.executionState,"completed");assert.equal(api.posts,26,"no artificial 20-collision limit");
 assert.equal(f.sql.prepare("SELECT COUNT(*) AS n FROM allowance_number_rejections").get().n,25);
 assert.equal(f.sql.prepare("SELECT COUNT(*) AS n FROM allowance_reconciliation_checks WHERE evidence_kind='external_collision'").get().n,0,"no fabricated official collision snapshots");
 f.close();
}
{
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider({unknown:true,unavailable:true});
 const result=await execute(f.db,t,context,authority(),api.transport,clock);assert.equal(result.executionState,"problem");
 assert.equal(f.sql.prepare("SELECT COUNT(*) AS n FROM allowance_number_ledger").get().n,1);
 const next=provider({unavailable:true});await execute(f.db,t,context,authority(),next.transport,clock);assert.equal(next.posts,0);
 assert.ok(f.sql.prepare("SELECT first_error,last_error FROM allowance_applications WHERE application_id=?").get(app1).first_error);
 f.close();
}
{
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider();
 const revoked=await execute(f.db,t,context,{revalidate:async()=>null},api.transport,clock);
 assert.equal(revoked.executionState,"problem");assert.equal(api.posts,0);f.close();
}
{
 const directory=mkdtempSync(join(tmpdir(),"allowance-auto-restart-"));const path=join(directory,"local.sqlite");
 try{
  const f=await fixture(true,path);const t=await f.prepare(app1);const api=provider({unknown:true,unavailable:true});
  await execute(f.db,t,context,authority(),api.transport,clock);f.close();
  const reopened=new DatabaseSync(path);
  const stmt=q=>({args:[],bind(...args){this.args=args;return this;},async first(){return reopened.prepare(q).get(...this.args)??null;},async all(){return{results:reopened.prepare(q).all(...this.args)};},async run(){return{meta:{changes:reopened.prepare(q).run(...this.args).changes}};}});
  const db={prepare:stmt,async batch(parts){reopened.exec("BEGIN");try{const rows=[];for(const part of parts)rows.push(await part.run());reopened.exec("COMMIT");return rows;}catch(e){reopened.exec("ROLLBACK");throw e;}}};
  const next=provider();await execute(db,t,context,authority(),next.transport,clock);assert.equal(next.posts,0);
  assert.equal(reopened.prepare("SELECT execution_state FROM allowance_applications WHERE application_id=?").get(app1).execution_state,"completed");reopened.close();
 }finally{rmSync(directory,{recursive:true,force:true});}
}
const voidNumber="ALW-001",voidId="void_synthetic_01";
function voidProvider(options={}){
 let posts=0,voided=!!options.alreadyVoided;
 const transport=async(url,opts)=>{
  if(url.endsWith("/allowance_status"))return Response.json({code:0,data:{[voidNumber]:{type:voided?"D0501":"D0401",status:99}}});
  if(url.endsWith("/allowance_query"))return Response.json({code:0,data:{allowance_number:voidNumber,invoice_type:voided?"D0501":"D0401",invoice_status:99,
   allowance_type:2,cancel_date:voided?20261019:0,buyer_identifier:"0000000000",buyer_name:"FAKE BUYER",wait:[],
   total_amount:50,tax_amount:0,product_item:[{original_invoice_number:invoice,original_invoice_date:20261019,original_description:"FAKE ITEM",quantity:1,unit_price:50,amount:50,tax:0,tax_type:1}]}});
  if(url.endsWith("/g0501")){posts++;if(options.unknown)throw new Error("lost reply");if(!options.keepOpen)voided=true;return Response.json({code:options.waiting?4050141:0});}
  throw new Error("unexpected void request");
 };return{transport,get posts(){return posts;}};
}
async function preparedVoid(f,api){
 const t={workspaceId:ws,applicationId:voidId,deviceId:devB,expectedRevision:2};
 await createVoid(f.db,{...t,deviceId:devA,environment:"test",allowanceNumber:voidNumber,originalInvoiceNumber:invoice,reason:"SYNTHETIC RETURN"},context,authority(employee));
 const proof=await previewVoid(f.db,t,context,authority(),api.transport,clock);
 await approveVoid(f.db,{...t,expectedRevision:1,requestHash:proof.requestHash,expectedOfficialSnapshotHash:proof.officialSnapshotHash},context,authority(),api.transport,clock);
 return t;
}
for(const mode of ["normal","already","unknown","pending"]){
 const f=await fixture(true),api=voidProvider({alreadyVoided:mode==="already",unknown:mode==="unknown",keepOpen:mode==="pending",waiting:mode==="pending"});
 const t=await preparedVoid(f,api);const result=await executeVoid(f.db,t,context,authority(),api.transport,clock,true);
 assert.equal(result.executionState,mode==="pending"?"processing":mode==="unknown"?"problem":"completed");
 assert.equal(api.posts,mode==="already"?0:1);
 f.close();
}
{
 const f=await fixture(true),api=voidProvider({unknown:true,keepOpen:true});const t=await preparedVoid(f,api);
 let offset=0;const later=()=>new Date(time.valueOf()+offset);
 for(let n=1;n<=3;n++){await executeVoid(f.db,t,context,authority(),api.transport,later,true);assert.equal(api.posts,n);offset+=61_000;}
 const exhausted=await executeVoid(f.db,t,context,authority(),api.transport,later,true);
 assert.equal(api.posts,3);assert.equal(exhausted.errorCode,"VOID_RETRY_LIMIT_REACHED");
 assert.equal(f.sql.prepare("SELECT COUNT(*) AS n FROM allowance_void_send_attempts").get().n,3);f.close();
}
{
 const f=await fixture(true),api=voidProvider({keepOpen:true,waiting:true});const t=await preparedVoid(f,api);
 await executeVoid(f.db,t,context,authority(),api.transport,clock,true);
 await executeVoid(f.db,t,context,authority(),api.transport,()=>new Date(time.valueOf()+120_000),true);
 assert.equal(api.posts,1,"official processing never blindly reposts");f.close();
}
// One ordinary approval/execution has a bounded live-authority IO budget.
{
 const f=await fixture();await create(f.db,input(app1),context,authority(employee));
 let checks=0;const counted={revalidate:async()=>{checks++;return manager;}};
 const grant=await approve(f.db,{...approvalTarget(app1),expectedRevision:1},context,counted,fakeOfficial().transport,clock);
 assert.ok(checks<=5);const api=provider();
 const result=await execute(f.db,{...reserveTarget(app1),expectedRevision:grant.approvedRevision},context,counted,api.transport,clock);
 assert.equal(result.executionState,"completed");assert.equal(api.posts,1);
 assert.ok(checks<=16,"same immutable request is not repeatedly loaded/revalidated between internal stages");f.close();
}
// Recovery rechecks the original business approval's revocation fingerprint.
{
 const env={CYID_ENABLED:"true",IDENTITY:{fetch(){}},IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_synthetic",IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
  IDENTITY_AUTHORITY:{async invalidateCache(){return{workspaceId:"identity_synthetic",applicationId:"CYINVOICE",available:true,invalidated:[]};}}};
 const auth=approvedExecutionAuthority(env,ws,JSON.stringify(cached),"manager",{applicationId:app1,operation:"issue"});assert.equal((await auth.revalidate()).employeeId,"manager");
 const f=await fixture();
 await assert.rejects(()=>create(f.db,input(app2),context,auth),/LIVE_SESSION_REQUIRED/);
 await assert.rejects(()=>approve(f.db,approvalTarget(app1),context,auth),/LIVE_SESSION_REQUIRED/);
 await assert.rejects(()=>execute(f.db,reserveTarget(app2),context,auth),()=>true);
 f.close();
 env.IDENTITY_AUTHORITY.invalidateCache=async()=>({workspaceId:"identity_synthetic",applicationId:"CYINVOICE",available:true,invalidated:["manager"]});
 await assert.rejects(auth.revalidate(),/REVOKED/);
 await assert.rejects(approvedExecutionAuthority(env,ws,null,"manager",{applicationId:app1,operation:"issue"}).revalidate(),/UNVERIFIABLE/);
}
// The existing runtime sync actually drives recovery without another manager
// password, continuation button, service queue, or scheduled Worker.
{
 const f=await fixture(true);await f.prepare(app1);const api=provider();
 const env={DB:f.db,APP_ENV:"test",SCHEMA_VERSION:"21",API_VERSION:"1",COMPANY_SETTINGS_KEY_HEX:context.encryptionKeyHex,
  ALLOWANCE_API_ENABLED:"true",ALLOWANCE_G0401_SEND_ENABLED:"true",ALLOWANCE_G0501_SEND_ENABLED:"false",
  CYID_ENABLED:"true",IDENTITY:{async fetch(){return Response.json({identity:"ready"});}},
  IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_synthetic",IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
  IDENTITY_AUTHORITY:{async invalidateCache(){return{workspaceId:"identity_synthetic",applicationId:"CYINVOICE",available:true,invalidated:[]};}}};
 const token="cydev_"+"a".repeat(64),digest=createHash("sha256").update(token).digest("hex");
 f.sql.prepare("UPDATE devices SET token_hash=? WHERE device_id=?").run(digest,devA);
 const {handleRuntimeSync}=await bundle("runtime-sync.ts");
 const originalFetch=globalThis.fetch;globalThis.fetch=api.transport;let background;
 try{
  const request=()=>new Request("https://fake.invalid/v1/runtime/sync",{method:"POST",headers:{authorization:"Bearer "+token,"content-type":"application/json"},body:JSON.stringify({synchronize:true,authorities:[],allowanceIds:[app1]})});
  await handleRuntimeSync(request(),env,{waitUntil(p){background=p;}});await background;
  assert.equal(api.posts,1);
  const reply=await handleRuntimeSync(request(),env,{waitUntil(p){background=p;}});await background;
  assert.equal((await reply.json()).allowances[0].execution_state,"completed");assert.equal(api.posts,1);
  // Page another device's history without hiding completed known operations.
  for(let n=0;n<60;n++)await create(f.db,input("runtime_page_"+String(n).padStart(3,"0")),context,authority(employee),clock);
  let issueCursor="",voidCursor="";const seen=new Set();
  for(let page=0;page<4;page++){
   const req=new Request("https://fake.invalid/v1/runtime/sync",{method:"POST",headers:{authorization:"Bearer "+token,"content-type":"application/json"},body:JSON.stringify({synchronize:true,authorities:[],allowanceIds:[app1],allowanceIssueCursor:issueCursor,allowanceVoidCursor:voidCursor})});
   const response=await handleRuntimeSync(req,env,{waitUntil(p){background=p;}});await background;
   const data=await response.json();assert.equal(response.status,200);assert.ok(data.allowances.length<=150);
   for(const row of data.allowances)seen.add(row.application_id);
   issueCursor=data.allowanceCursors.issue;voidCursor=data.allowanceCursors.void;
   if(!issueCursor)break;
  }
  assert.equal(seen.size,61);assert.equal(api.posts,1);
 }finally{globalThis.fetch=originalFetch;f.close();}
}
{
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider();
 f.lostReply();const held=await execute(f.db,t,context,authority(),api.transport,clock);
 assert.equal(held.executionState,"problem");assert.equal(api.posts,0,"lost CAS acknowledgement cannot POST");
 await execute(f.db,t,context,authority(),api.transport,clock);assert.equal(api.posts,0,"restart only reads the original number");f.close();
}
{
 const f=await fixture(true);const t=await f.prepare(app1);const api=provider();
 f.afterSend(()=>f.sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
 await execute(f.db,t,context,authority(),api.transport,clock);assert.equal(api.posts,0,"revocation after claim suppresses POST");f.close();
}
{
 const f=await fixture(true),api=voidProvider({keepOpen:true});const t=await preparedVoid(f,api);
 f.sql.prepare(`INSERT INTO allowance_void_send_claims(application_id,workspace_id,environment,seller_invoice,allowance_fingerprint,request_hash,official_snapshot_hash,manager_actor_id,manager_device_id,claimed_at)
  SELECT r.application_id,r.workspace_id,r.environment,r.seller_invoice,r.allowance_fingerprint,r.request_hash,a.official_snapshot_hash,a.manager_actor_id,a.manager_device_id,a.approved_at
  FROM allowance_void_requests r JOIN allowance_void_approvals a ON a.application_id=r.application_id WHERE r.application_id=?`).run(voidId);
 const legacy=await executeVoid(f.db,t,context,authority(),api.transport,clock,true);
 assert.equal(legacy.errorCode,"VOID_LEGACY_CLAIM_READBACK_ONLY");assert.equal(api.posts,0);
 const official=voidProvider({alreadyVoided:true});
 assert.equal((await executeVoid(f.db,t,context,authority(),official.transport,clock,true)).executionState,"completed");assert.equal(official.posts,0);f.close();
}
{
 const directory=mkdtempSync(join(tmpdir(),"schema20-forward21-")),path=join(directory,"schema20.sqlite"),backup=join(directory,"backup.sqlite");
 try{
  const f=await fixture(true,path,false);const t=await f.prepare(app1);
  f.sql.prepare("UPDATE allowance_number_ledger SET status='sending' WHERE application_id=?").run(app1);
  const before=f.sql.prepare("SELECT * FROM allowance_number_ledger").all();copyFileSync(path,backup);
  const migration=readFileSync("migrations/0021_allowance_automatic_execution.sql","utf8");
  assert.throws(()=>{f.sql.exec("BEGIN");try{f.sql.exec(migration+";SELECT * FROM nonexistent_injected_failure;");f.sql.exec("COMMIT");}catch(e){f.sql.exec("ROLLBACK");throw e;}},/no such table/);
  assert.ok(!f.sql.prepare("PRAGMA table_info(allowance_applications)").all().some(r=>r.name==="execution_state"));
  f.sql.exec(migration);assert.deepEqual(f.sql.prepare("SELECT * FROM allowance_number_ledger").all(),before);
  assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
  assert.equal(f.sql.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  assert.equal(f.sql.prepare("SELECT authority_snapshot FROM allowance_execution_approvals").get().authority_snapshot,null,"old approval authority is never fabricated");
  assert.equal(f.sql.prepare("SELECT status FROM allowance_number_ledger").get().status,"sending");
  f.close();const restored=new DatabaseSync(backup,{readOnly:true});
  assert.deepEqual(restored.prepare("SELECT * FROM allowance_number_ledger").all(),before);
  assert.ok(!restored.prepare("PRAGMA table_info(allowance_applications)").all().some(r=>r.name==="execution_state"));restored.close();
 }finally{rmSync(directory,{recursive:true,force:true});}
}
{
 const f=await fixture(true,":memory:",true,false);const target=await f.prepare(app1);
 const tables=["allowance_applications","allowance_execution_approvals","allowance_number_ledger","allowance_application_audit_events"];
 const snapshot=()=>tables.map(name=>f.sql.prepare("SELECT * FROM "+name).all());
 const before=snapshot(),oldTrigger=f.sql.prepare("SELECT sql FROM sqlite_master WHERE name='allowance_application_transition_guard'").get().sql;
 const migration=readFileSync("migrations/0022_allowance_unsent_withdrawal.sql","utf8");
 assert.throws(()=>{f.sql.exec("BEGIN");try{f.sql.exec(migration+";SELECT * FROM injected_missing;");f.sql.exec("COMMIT");}catch(e){f.sql.exec("ROLLBACK");throw e;}},/no such table/);
 assert.equal(f.sql.prepare("SELECT sql FROM sqlite_master WHERE name='allowance_application_transition_guard'").get().sql,oldTrigger);
 assert.deepEqual(snapshot(),before);
 f.sql.exec(migration);assert.deepEqual(snapshot(),before);
 // Withdrawal winning the race prevents a stale executor from posting.
 f.beforeSend(()=>f.sql.prepare("UPDATE allowance_applications SET status='withdrawn',manager_actor_id=NULL,actor_id='other_current_manager',revision=4 WHERE application_id=?").run(app1));
 const api=provider();await execute(f.db,target,context,authority(),api.transport,clock);
 assert.equal(api.posts,0);
 assert.equal(f.sql.prepare("SELECT status FROM allowance_applications WHERE application_id=?").get(app1).status,"withdrawn");
 assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
 assert.equal(f.sql.prepare("PRAGMA integrity_check").get().integrity_check,"ok");f.close();
}
console.log("Cloud single-approval automatic execution, 25 collisions, unknown/restart recovery and bounded void retry passed.");
