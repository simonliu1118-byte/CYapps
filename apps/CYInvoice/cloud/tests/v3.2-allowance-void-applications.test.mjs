import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync,readdirSync,mkdtempSync,rmSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {build} from "esbuild";

// Source-only synthetic schema20 test: no Cloudflare, CYID or AMEGO network.
const bundle=await build({entryPoints:["src/allowance-void-applications.ts"],
 bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {createCloudAllowanceVoidApplication:create,
 readCloudAllowanceVoidApplication:read,
 loadCloudAllowanceVoidForManager:inspect}=await import("data:text/javascript;base64,"+
 Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const companyBundle=await build({entryPoints:["src/company-settings.ts"],
 bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {encryptCompanyBusinessData:seal}=await import("data:text/javascript;base64,"+
 Buffer.from(companyBundle.outputFiles[0].text).toString("base64"));
const previewBundle=await build({entryPoints:["src/allowance-void-manager-preview.ts"],
 bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {previewCloudVoidForManager:preview}=await import("data:text/javascript;base64,"+
 Buffer.from(previewBundle.outputFiles[0].text).toString("base64"));
const approveBundle=await build({entryPoints:["src/allowance-void-manager-approval.ts"],
 bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {approveCloudVoidAfterOfficialProof:approve}=await import("data:text/javascript;base64,"+
 Buffer.from(approveBundle.outputFiles[0].text).toString("base64"));
const directory=mkdtempSync(join(tmpdir(),"cyinvoice-void-restart-"));
const databasePath=join(directory,"schema20.sqlite");
let sql=new DatabaseSync(databasePath);
const ws="ws_void_test",otherWs="ws_elsewhere";
const employee="dev_11111111-1111-4111-8111-111111111111";
const managerDevice="dev_22222222-2222-4222-8222-222222222222";
const ctx={encryptionKeyHex:"ab".repeat(32),environment:"test"};
const applicant={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const supervisor={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const authority=(a=applicant)=>({revalidate:async()=>a});
const request={workspaceId:ws,deviceId:employee,applicationId:"void_application_first",
 environment:"test",allowanceNumber:"ALW-001",originalInvoiceNumber:"AB12345678",
 reason:"Synthetic review request"};
let throwAfterWrite=false,loseClaimAcknowledgement=false,stopImmediatelyAfterClaim=false,revokeBeforeClaim=false;
class Statement{
 constructor(q){this.q=q;this.args=[];}
 bind(...args){this.args=args;return this;}
 async first(){return sql.prepare(this.q).get(...this.args)??null;}
 async run(){
  if(revokeBeforeClaim&&this.q.includes("INSERT INTO allowance_void_send_claims")){
   revokeBeforeClaim=false;
   active(managerDevice,"revoked");
  }
  const result=sql.prepare(this.q).run(...this.args);
  if(stopImmediatelyAfterClaim&&this.q.includes("INSERT INTO allowance_void_send_claims")){
   stopImmediatelyAfterClaim=false;
   active(managerDevice,"revoked");
  }
  if(loseClaimAcknowledgement&&this.q.includes("INSERT INTO allowance_void_send_claims")){
   loseClaimAcknowledgement=false;
   throw Error("simulated D1 claim ACK lost AFTER durable write");
  }
  if(throwAfterWrite&&this.q.includes("INSERT INTO allowance_void_requests")){
   throwAfterWrite=false;
   throw Error("simulated lost D1 response AFTER write");
  }
  return {meta:{changes:result.changes}};
 }
}
const db={prepare:q=>new Statement(q),async batch(parts){
 sql.exec("BEGIN IMMEDIATE");
 try{
  const results=parts.map(p=>({meta:{changes:sql.prepare(p.q).run(...p.args).changes}}));
  sql.exec("COMMIT");
  return results;
 }catch(e){sql.exec("ROLLBACK");throw e;}
}};
const active=(device,state)=>sql.prepare("UPDATE devices SET status=? WHERE device_id=?").run(state,device);
async function rejects(code,run){await assert.rejects(run,new RegExp(code));}
function assertReadonlyReconciliation(result){
 assert.equal(result.requiresManualReview,true);
 assert.equal(result.safeToAutoResend,false);
 assert.equal(result.safeToAutoClose,false);
 assert.equal(result.thisPostAttributed,false);
}
try{
 sql.exec("PRAGMA foreign_keys=ON");
 const names=readdirSync("migrations").filter(s=>/^\d{4}_.*\.sql$/.test(s)).sort();
 assert.equal(names.at(-1),"0021_allowance_automatic_execution.sql");
 for(const file of names.filter(s=>s<"0014"))
  sql.exec(readFileSync("migrations/"+file,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES(?,'Fake')").run(ws);
 for(const dev of [employee,managerDevice])
  sql.prepare("INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state) "+
  "VALUES(?,?,'Fake','active','cloud')").run(dev,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const file of names.filter(s=>s>="0014"))
  sql.exec(readFileSync("migrations/"+file,"utf8"));
 const syntheticAppKey="SYNTHETIC_ONLY_NO_REAL_SECRET";
 const encryptedAppKey=await seal(ctx.encryptionKeyHex,ws,"amego",syntheticAppKey);
 sql.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,"+
  "revision,changed_by_employee_id,changed_by_device_id,changed_at) "+
  "VALUES(?,'12345678',?,1,'manager',?,"+
  "'2026-10-21T08:00:00.000Z')").run(ws,encryptedAppKey,managerDevice);
 await rejects("VOID_APPLICATION_INVALID",()=>create(db,{...request,allowanceNumber:"alw-001"},ctx,authority()));
 await rejects("VOID_APPLICATION_INVALID",()=>create(db,{...request,reason:"contains\nbreak"},ctx,authority()));
 await rejects("VOID_APPLICATION_INVALID",()=>create(db,{...request,originalInvoiceNumber:"BAD"},ctx,authority()));
 await rejects("VOID_APPLICATION_AUTH_REQUIRED",()=>create(db,{...request,workspaceId:otherWs},ctx,authority()));
 await rejects("VOID_APPLICATION_AUTH_REQUIRED",()=>create(db,request,ctx,authority(null)));
 await rejects("VOID_APPLICATION_AUTH_REQUIRED",()=>create(db,request,ctx,authority({...applicant,workspaceId:otherWs})));
 let authCalls=0;
 await rejects("VOID_APPLICATION_AUTH_CHANGED",()=>create(db,request,ctx,{
  revalidate:async()=>++authCalls===1?applicant:null}));
 const count=()=>sql.prepare("SELECT COUNT(*) n FROM allowance_void_requests").get().n;
 assert.equal(count(),0,"revoked actor must not write");
 active(employee,"revoked");
 await rejects("VOID_APPLICATION_AUTH_REQUIRED",()=>create(db,request,ctx,authority()));
 active(employee,"active");
 const result=await create(db,request,ctx,authority());
 assert.deepEqual(result,{applicationId:request.applicationId,status:"open",
  revision:1,claimed:false,readbackOnly:false});
 const row=sql.prepare("SELECT allowance_fingerprint,request_hash,frozen_ciphertext "+
  "FROM allowance_void_requests WHERE application_id=?").get(request.applicationId);
 assert.match(row.allowance_fingerprint,/^[0-9a-f]{64}$/);
 assert.match(row.request_hash,/^[0-9a-f]{64}$/);
 assert.match(row.frozen_ciphertext,/^v1:[0-9a-f]{24}:[0-9a-f]+$/);
 assert.ok(!JSON.stringify(row).includes("ALW-001"));
 assert.ok(!JSON.stringify(row).includes("AB12345678"));
 assert.ok(!JSON.stringify(row).includes(request.reason));
 assert.deepEqual(await read(db,{workspaceId:ws,applicationId:request.applicationId,
  deviceId:employee},authority()),result);
 await rejects("VOID_APPLICATION_NOT_FOUND",()=>read(db,{workspaceId:ws,
  applicationId:request.applicationId,deviceId:managerDevice},
  authority({...applicant,employeeId:"other"})));
 assert.deepEqual(await read(db,{workspaceId:ws,applicationId:request.applicationId,
  deviceId:managerDevice},authority(supervisor)),result);
 const target={workspaceId:ws,applicationId:request.applicationId,deviceId:managerDevice};
 await rejects("VOID_PREVIEW_MANAGER_REQUIRED",()=>inspect(db,
  {...target,deviceId:employee},ctx,authority()));
 const frozen=await inspect(db,target,ctx,authority(supervisor));
 assert.equal(frozen.allowanceNumber,request.allowanceNumber);
 assert.equal(frozen.originalInvoiceNumber,request.originalInvoiceNumber);
 assert.equal(frozen.reason,request.reason);
 assert.equal(frozen.companyRevision,1);
 assert.equal(frozen.requestHash,row.request_hash);
 await rejects("VOID_PREVIEW_FROZEN_UNAVAILABLE",()=>inspect(db,target,
  {...ctx,encryptionKeyHex:"cd".repeat(32)},authority(supervisor)));
 let freshManager=0;
 await rejects("VOID_PREVIEW_AUTH_CHANGED",()=>inspect(db,target,ctx,{
  revalidate:async()=>++freshManager===1?supervisor:null}));
 active(managerDevice,"revoked");
 await rejects("VOID_PREVIEW_MANAGER_REQUIRED",()=>inspect(db,target,
  ctx,authority(supervisor)));
 active(managerDevice,"active");
 const officialRecord={allowance_number:"ALW-001",invoice_type:"D0401",
  invoice_status:99,allowance_type:2,cancel_date:0,
  buyer_identifier:"0000000000",buyer_name:"Synthetic buyer",wait:[],
  product_item:[{original_invoice_number:"AB12345678"}]};
 const time=new Date("2026-10-21T09:00:00.000Z");
 let providerReads=0;
 const official=async(url,options)=>{
  providerReads++;
  assert.equal(options.method,"POST");
  return Response.json(url.endsWith("/allowance_query")
   ?{code:0,data:officialRecord}
   :{code:0,data:{"ALW-001":{type:"D0401",status:99}}});
 };
 const previewResult=await preview(db,target,ctx,authority(supervisor),official,()=>time);
 assert.equal(providerReads,2);
 assert.equal(previewResult.applicationId,request.applicationId);
 assert.equal(previewResult.requestHash,row.request_hash);
 assert.equal(previewResult.mayPostG0501,false);
 assert.equal(previewResult.canApproveWithoutFurtherReview,false);
 assert.match(previewResult.officialSnapshotHash,/^[a-f0-9]{64}$/);
 let revokedOnStatus=0;
 const revokeDuringProof=async(url,options)=>{
  const response=await official(url,options);
  if(++revokedOnStatus===2)active(managerDevice,"revoked");
  return response;
 };
 await rejects("VOID_PREVIEW_MANAGER_REQUIRED",()=>preview(db,target,ctx,
  authority(supervisor),revokeDuringProof,()=>time));
 active(managerDevice,"active");
 const approval={...target,expectedRevision:1,requestHash:row.request_hash,
  expectedOfficialSnapshotHash:previewResult.officialSnapshotHash};
 await rejects("VOID_APPROVAL_STALE",()=>approve(db,{
  ...approval,expectedOfficialSnapshotHash:"d".repeat(64)},
  ctx,authority(supervisor),official,()=>time));
 await rejects("VOID_APPROVAL_MANAGER_REQUIRED",()=>approve(db,approval,ctx,
  authority(applicant),official,()=>time));
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_approvals").get().n,0);
 // D1.batch must rollback BOTH statements on an append-only audit failure.
 sql.exec("CREATE TRIGGER synthetic_grant_audit_failure BEFORE INSERT ON allowance_void_audit "+
  "WHEN NEW.kind='manager_approved' BEGIN SELECT RAISE(ABORT,'SYNTHETIC_AUDIT_FAILED'); END;");
 await rejects("VOID_APPROVAL_UNCONFIRMED_READBACK_ONLY",()=>approve(db,approval,
  ctx,authority(supervisor),official,()=>time));
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_approvals").get().n,0);
 assert.equal(sql.prepare("SELECT status FROM allowance_void_requests WHERE application_id=?")
  .get(request.applicationId).status,"open");
 sql.exec("DROP TRIGGER synthetic_grant_audit_failure");
 const approved=await approve(db,approval,ctx,authority(supervisor),official,()=>time);
 assert.equal(approved.approvedRevision,2);
 assert.equal(approved.mayPostG0501,false);
 assert.equal(approved.requiresFreshOfficialCheck,true);
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_approvals").get().n,1);
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_send_claims").get().n,0);
 assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_void_audit").get().n,2);
 console.log("Cloud void encrypted application, manager preview/approval, original-ID ACL and immutable history passed.");
}finally{sql.close();rmSync(directory,{recursive:true,force:true});}
