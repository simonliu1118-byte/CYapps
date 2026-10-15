import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {createHash} from "node:crypto";
import {build} from "esbuild";
const sql=new DatabaseSync(":memory:");
const ws="ws_synthetic",device="dev_11111111-1111-4111-8111-111111111111";
sql.exec("PRAGMA foreign_keys=ON;"+
 "CREATE TABLE workspaces(workspace_id TEXT PRIMARY KEY,status TEXT NOT NULL DEFAULT 'active');"+
 "CREATE TABLE devices(device_id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),"+
 "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud',token_hash TEXT);");
sql.prepare("INSERT INTO workspaces(workspace_id) VALUES(?)").run(ws);
sql.prepare("INSERT INTO devices(device_id,workspace_id) VALUES(?,?)").run(device,ws);
for(const name of ["0015_work_items.sql","0017_company_settings.sql",
 "0019_allowance_void_workflow.sql"])
 sql.exec(readFileSync("drafts/"+name,"utf8"));
sql.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,"+
 "revision,changed_by_employee_id,changed_by_device_id,changed_at) "+
 "VALUES (?,?,'synthetic_not_a_secret',1,?,?,?)")
 .run(ws,"12345678","cyid_manager",device,"2026-10-15");
const token="cydev_"+"a".repeat(64);
sql.prepare("UPDATE devices SET token_hash=?").run(
 createHash("sha256").update(token).digest("hex"));
class Statement{
 constructor(query){this.query=query;this.args=[];}
 bind(...args){this.args=args;return this;}
 async first(){return sql.prepare(this.query).get(...this.args)??null;}
 async all(){return{results:sql.prepare(this.query).all(...this.args)};}
 async run(){return{meta:{changes:sql.prepare(this.query).run(...this.args).changes}};}
}
const db={prepare:query=>new Statement(query),async batch(items){
 sql.exec("BEGIN IMMEDIATE");
 try{
   const results=items.map(item=>({meta:{changes:
     sql.prepare(item.query).run(...item.args).changes}}));
   sql.exec("COMMIT");return results;
 }catch(e){sql.exec("ROLLBACK");throw e;}
}};
const built=await build({entryPoints:["src/allowance-void-applications-route.ts"],
 bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {handleAllowanceVoidApplications:handle}=await import(
 "data:text/javascript;base64,"+Buffer.from(built.outputFiles[0].text).toString("base64"));
let actor={workspaceId:"identity_synthetic",employeeId:"cyid_employee",
 workspaceRole:"USER",emailVerified:true};
const env={DB:db,CYID_ENABLED:"true",
 IDENTITY:{fetch:async()=>Response.json({})},IDENTITY_APPLICATION_ID:"CYINVOICE",
 IDENTITY_WORKSPACE_ID:"identity_synthetic",
 IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
 ALLOWANCE_OPERATION_ENVIRONMENT:"test",
 cyIdContext:{revalidate:async()=>actor}};
async function call(action,payload,settings=env,bearer=token){
 const reply=await handle(new Request(
   "https://synthetic.cyinvoice/v1/allowance-void-applications/"+action,{
   method:"POST",headers:{authorization:"Bearer "+bearer,
     "content-type":"application/json"},body:JSON.stringify(payload)
 }),settings);
 return{status:reply.status,data:await reply.json()};
}
const input={employeeNo:"0001",password:"SYNTHETIC_PASS",
 itemId:"wi_allowance_void_first",allowanceNumber:"3821061800001",
 originalInvoiceNumber:"AB12345678",reason:"Synthetic user return"};
assert.equal((await call("create",input,{...env,cyIdContext:undefined})).status,401);
assert.equal((await call("create",{...input,workspaceId:"ws_attacker"})).status,400);
assert.equal((await call("create",{...input,environment:"production"})).status,400);
assert.equal((await call("create",input,{...env,ALLOWANCE_OPERATION_ENVIRONMENT:undefined})).status,503);
assert.equal((await call("create",input,env,"wrong")).status,401);
const made=await call("create",input);
assert.equal(made.status,201,JSON.stringify(made));
assert.equal(made.data.itemId,input.itemId);
assert.equal(sql.prepare("SELECT kind FROM work_items").get().kind,"ALLOWANCE_VOID");
assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_requests").get().n,1);
const row=sql.prepare("SELECT * FROM allowance_void_requests").get();
assert.equal(row.seller_invoice,"12345678");
assert.equal(row.applicant_employee_id,"cyid_employee");
assert.equal(row.environment,"test");
assert.equal(JSON.stringify(made.data).includes("synthetic_not_a_secret"),false);
assert.equal((await call("create",input)).status,409);
assert.equal((await call("get",{itemId:input.itemId})).status,200,
 "An unknown INSERT result can only be checked by original Work Item ID");
const copy={...input,itemId:"wi_allowance_void_duplicate",reason:"different"};
assert.equal((await call("create",copy)).status,409);
assert.equal(sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,1,
 "A failed second D1.batch may not leave an orphan Work Item");
actor={...actor,employeeId:"other_employee"};
assert.equal((await call("get",{itemId:input.itemId})).status,404);
assert.equal((await call("list",{limit:5})).status,403);
actor={...actor,workspaceRole:"ADMIN"};
assert.equal((await call("get",{itemId:input.itemId})).status,200);
assert.equal((await call("list",{limit:51})).status,400);
assert.equal((await call("list",{limit:20,workspaceId:"ws_wrong"})).status,400);
actor={...actor,employeeId:"cyid_employee",workspaceRole:"USER"};
const second={...input,itemId:"wi_allowance_void_second",
 allowanceNumber:"3821061800002"};
assert.equal((await call("create",second)).status,201);
actor={...actor,workspaceRole:"ADMIN"};
const page1=await call("list",{limit:1});
assert.equal(page1.status,200,JSON.stringify(page1));
assert.equal(page1.data.applications.length,1);
assert.notEqual(page1.data.nextCursor,null);
const page2=await call("list",{limit:1,...page1.data.nextCursor});
assert.equal(page2.status,200,JSON.stringify(page2));
assert.equal(page2.data.applications.length,1);
assert.equal(page2.data.nextCursor,null);
assert.notEqual(page1.data.applications[0].itemId,page2.data.applications[0].itemId);
// End-to-end synthetic CYID manager review + irreversible g0501 send CAS.
const companyBundle=await build({entryPoints:["src/company-settings.ts"],
  bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {encryptCompanyBusinessData:encryptSecret}=await import(
 "data:text/javascript;base64,"+
 Buffer.from(companyBundle.outputFiles[0].text).toString("base64"));
const privateKey="aa".repeat(32);
const amegoKey="SYNTHETIC_OFFICIAL_G0501_KEY";
const secret=await encryptSecret(privateKey,ws,"amego",amegoKey);
sql.prepare("UPDATE company_settings SET amego_app_key_ciphertext=?").run(secret);
env.COMPANY_SETTINGS_KEY_HEX=privateKey;
sql.prepare("UPDATE work_items SET status='review',revision=2 WHERE work_item_id=?")
 .run(input.itemId);
let remoteQueries=0,remoteSends=0,remoteStates=0,remoteLists=0;
let listPresent=true,officialType="D0401";
const originalFetch=globalThis.fetch;
globalThis.fetch=async(url,opts)=>{
 const form=new URLSearchParams(opts.body);
 const payload=JSON.parse(form.get("data"));
 assert.equal(form.get("invoice"),"12345678");
 assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+amegoKey).digest("hex"));
 if(url.endsWith("/allowance_query")){
   remoteQueries++;
   assert.deepEqual(payload,{allowance_number:input.allowanceNumber});
   return Response.json({code:0,data:{
     allowance_number:input.allowanceNumber,invoice_type:"D0401",
     invoice_status:99,allowance_type:2,cancel_date:0,
     allowance_date:"20261015",total_amount:"50",tax_amount:"3",
     product_item:[{original_invoice_number:input.originalInvoiceNumber}]
   }});
 }
 if(url.endsWith("/g0501")){
   remoteSends++;
   assert.deepEqual(payload,[{CancelAllowanceNumber:input.allowanceNumber}]);
   officialType="D0501";
   return Response.json({code:0,msg:""});
 }
 if(url.endsWith("/allowance_list")){
   remoteLists++;
   assert.ok(payload.date_select===1||payload.date_select===2);
   assert.equal(payload.page,1);
   assert.equal(payload.limit,20);
   return Response.json({code:0,page_now:1,
     page_total:listPresent?1:0,data_total:listPresent?1:0,
     data:listPresent?[{allowance_number:input.allowanceNumber,
       invoice_type:officialType,invoice_status:99,allowance_date:20261015}]:[]});
 }
 if(url.endsWith("/allowance_status")){
   remoteStates++;
   assert.deepEqual(payload,[{AllowanceNumber:input.allowanceNumber}]);
   return Response.json({code:0,data:{
     [input.allowanceNumber]:{type:officialType,status:99}
   }});
 }
 throw new Error("Unknown synthetic AMEGO endpoint");
};
const revision={employeeNo:"0001",password:"SYNTHETIC_PASS",
 itemId:input.itemId,expectedRevision:2};
try{
 actor={...actor,workspaceRole:"USER"};
 assert.equal((await call("approve",revision)).status,403);
 assert.equal(remoteQueries,0);
 actor={...actor,workspaceRole:"ADMIN"};
 assert.equal((await call("approve",{...revision,officialApproved:true})).status,400);
 assert.equal(remoteQueries,0);
 const grant=await call("approve",revision);
 assert.equal(grant.status,201,JSON.stringify(grant));
 assert.equal(grant.data.canIssueNow,false);
 assert.equal(remoteQueries,1);
 assert.equal((await call("reconcile",revision)).status,409,
   "Without a one-shot claim no reconciliation can be started");
 assert.equal(remoteLists,0);
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_approvals").get().n,1);
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_send_claims").get().n,0);
 const attempt=await call("send-once",revision);
 assert.equal(attempt.status,202,JSON.stringify(attempt));
 assert.equal(attempt.data.outcome,"accepted_unconfirmed");
 assert.equal(attempt.data.canAutoRetry,false);
 assert.equal(attempt.data.canAutoClose,false);
 assert.equal(remoteSends,1);
 assert.equal(remoteQueries,2,"Send must repeat official preflight");
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_send_claims").get().n,1);
 assert.equal((await call("send-once",revision)).status,409);
 assert.equal(remoteSends,1,"No second g0501 on repeated click or timeout");
 const inspected=await call("status-readback",revision);
 assert.equal(inspected.status,200,JSON.stringify(inspected));
 assert.equal(inspected.data.readback.officialCancelled,true);
 assert.equal(inspected.data.readback.canAutoClose,false);
 assert.equal(remoteStates,1);
 actor={...actor,workspaceRole:"USER"};
 assert.equal((await call("reconcile",revision)).status,403);
 actor={...actor,workspaceRole:"ADMIN"};
 assert.equal((await call("reconcile",{...revision,sellerInvoice:"87654321"})).status,400);
 assert.equal(remoteLists,0,"Untrusted fields and users never fetch official data");
 const reconciled=await call("reconcile",revision);
 assert.equal(reconciled.status,200,JSON.stringify(reconciled));
 assert.equal(reconciled.data.listReason,"OFFICIAL_STATUS_CHANGED_OR_PENDING");
 assert.equal(reconciled.data.readback.officialCancelled,true);
 assert.equal(reconciled.data.canAutoClose,false);
 assert.equal(reconciled.data.canAutoRetry,false);
 assert.equal(remoteLists,2,"Only two bounded official date dimensions");
 assert.equal(remoteStates,2,"Only known D1 Work Item gets selective official status");
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_official_checks").get().n,2);
 listPresent=false;
 const notSeen=await call("reconcile",revision);
 assert.equal(notSeen.status,200,JSON.stringify(notSeen));
 assert.equal(notSeen.data.listReason,"NOT_SEEN_NOT_PROOF");
 assert.equal(notSeen.data.readback,undefined);
 assert.equal(notSeen.data.canAutoRetry,false);
 assert.equal(remoteLists,4);
 assert.equal(remoteStates,2,"Missing list entry must not trigger a status call");
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_official_checks").get().n,2,
   "Only a specific official readback may append the existing Audit");
 listPresent=true;
 assert.equal(sql.prepare("SELECT status FROM work_items WHERE work_item_id=?")
   .get(input.itemId).status,"review","Scanning cannot close or alter a Work Item");
 const closed=await call("close",revision);
 assert.equal(closed.status,200,JSON.stringify(closed));
 assert.equal(closed.data.status,"closed");
 assert.equal(remoteStates,3);
 assert.equal(sql.prepare("SELECT status FROM work_items WHERE work_item_id=?")
   .get(input.itemId).status,"closed");
 assert.equal((await call("close",revision)).status,409);
 assert.equal(remoteStates,3,"Closed Work Item never starts another official request");
}finally{globalThis.fetch=originalFetch;}
assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
console.log("CYID allowance void: source, approval, one-shot g0501, official readback and verified close passed");
sql.close();
