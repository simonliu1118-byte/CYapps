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
assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
console.log("CYID allowance void route: frozen source, atomic create, readback, manager list and scope fences passed");
sql.close();
