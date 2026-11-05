import {allowanceView} from "./allowance-view";
import {encryptCompanyBusinessData} from "./company-settings";
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {createEncryptedCloudAllowance,loadEncryptedCloudAllowance,readAllowanceParticipant,allowanceParticipantText,type AllowanceActor,type AllowanceActorSource,type CloudAllowanceContext} from "./allowance-frozen-store";
import {approveCloudAllowanceAfterOfficialProof} from "./allowance-manager-approval";
import {approvedExecutionAuthority,recordExecutionEvent,saveExecutionResult,executeApprovedCloudAllowance} from "./allowance-one-shot-coordinator";
import {executeCloudVoidOnce} from "./allowance-void-one-shot-coordinator";
import {createCloudAllowanceVoidApplication,readCloudAllowanceVoidApplication,loadCloudAllowanceVoidForManager} from "./allowance-void-applications";
import {previewCloudVoidForManager} from "./allowance-void-manager-preview";
import {approveCloudVoidAfterOfficialProof} from "./allowance-void-manager-approval";
import {loadAmegoCompanySettingsForGateway,amegoCredentialsForEnvironment} from "./company-settings";
import {reconcileOfficialAllowanceForOriginal} from "./allowance-official-proof";
import {queryOfficialInvoiceReadOnly,OfficialInvoiceQueryError} from "./amego-invoice-query";
import {parseAllowanceFinancialJson} from "./allowance-decimal";
interface Env extends CyIdEnv {DB:D1Database;SCHEMA_VERSION:string;
 ALLOWANCE_API_ENABLED?:string;ALLOWANCE_G0401_SEND_ENABLED?:string;ALLOWANCE_G0501_SEND_ENABLED?:string;COMPANY_SETTINGS_KEY_HEX?:string;}
const keys:Record<string,readonly string[]>={
 create:["employeeNo","password","applicationId","environment","originalInvoiceNumber","allowanceDay","requestedGross","reason","unnumberedTemplateJson"],
 get:["employeeNo","password","applicationId"],list:["employeeNo","password","status","limit"],
 approve:["employeeNo","password","applicationId","expectedRevision"],
 status:["employeeNo","password","applicationId"],withdraw:["employeeNo","password","applicationId","expectedRevision","operation","reason"],
 "void-create":["employeeNo","password","applicationId","environment","allowanceNumber","originalInvoiceNumber","reason"],
 "void-status":["employeeNo","password","applicationId"],
 "void-approve":["employeeNo","password","applicationId","expectedRevision","requestHash","expectedOfficialSnapshotHash"],
 view:["applicationId","operation","allowanceNumber","originalInvoiceNumber","environment"],
 detail:["employeeNo","password","applicationId","operation","allowanceNumber","originalInvoiceNumber","environment"],
 diagnostics:["employeeNo","password","applicationId","operation","afterEventId"]};
const reply=(status:number,data:Record<string,unknown>)=>Response.json(
 {ok:status<300,...data},{status,headers:{
  "cache-control":"no-store","x-content-type-options":"nosniff"}});
const id=(v:unknown)=>typeof v==="string"&&/^[A-Za-z0-9_-]{8,100}$/.test(v);
const rev=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>0;
const manager=(actor:AllowanceActor)=>actor.workspaceRole==="ADMIN"||
 actor.workspaceRole==="SUPER_ADMIN";
function scope(p:CyIdPrincipal|null,env:Env):AllowanceActor|null {
 const binding=cyIdBinding(env);
 if(!p||p.workspaceId!==binding.identityWorkspaceId||
    !p.emailVerified||typeof p.employeeId!=="string"||
    p.employeeId.length<1||p.employeeId.length>100)return null;
 return {authoritySnapshot:JSON.stringify({identityWorkspaceId:binding.identityWorkspaceId,applicationId:binding.applicationId,employeeId:p.employeeId,employeeNo:p.employeeNo,displayName:p.displayName,credentialVersion:p.credentialVersion,employeeRevision:p.employeeRevision,workspaceRole:p.workspaceRole,isIdentityAdmin:p.isIdentityAdmin,emailVerified:p.emailVerified}),workspaceId:binding.workspaceId,employeeId:p.employeeId,
  workspaceRole:p.workspaceRole,employeeNo:p.employeeNo,displayName:p.displayName};
}
function auth(env:Env):AllowanceActorSource{
 return {revalidate:async()=>scope(
   (await env.cyIdContext?.revalidate())??null,env)};
}
async function read(req:Request,path:string):Promise<Record<string,unknown>|null>{
 const max=path==="create"?36864:4096;
 const header=req.headers.get("content-length");
 if(header!==null&&(!/^\d+$/.test(header)||Number(header)>max))return null;
 if(!req.body)return null;
 const reader=req.body.getReader(),chunks:Uint8Array[]=[];let length=0;
 try{
  for(;;){
   const {done,value}=await reader.read();if(done)break;
   length+=value.byteLength;
   if(length>max){await reader.cancel();return null;}
   chunks.push(value);
  }
 }catch{return null;}finally{reader.releaseLock();}
 const bytes=new Uint8Array(length);let offset=0;
 for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.byteLength;}
 let body:unknown;
 try{body=JSON.parse(new TextDecoder("utf-8",{fatal:true}).decode(bytes));}catch{return null;}
 if(!body||typeof body!=="object"||Array.isArray(body))return null;
 const v=body as Record<string,unknown>;
 return Object.keys(v).every(k=>keys[path].includes(k))?v:null;
}
async function checkDevice(db:D1Database,device:string,workspace:string){
 const r=await db.prepare(`SELECT d.device_id FROM devices d
 JOIN workspaces w ON w.workspace_id=d.workspace_id
 WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active'
 AND d.employee_authority_state='cloud' AND w.status='active' LIMIT 1`)
 .bind(device,workspace).first<{device_id:string}>();
 return !!r;
}
export async function handleAllowances(req:Request,env:Env,context?:ExecutionContext):Promise<Response|null>{
 const path=new URL(req.url).pathname;
 if(!path.startsWith("/v1/allowances/"))return null;
 const action=path.split("/").at(-1)!;
 if(!Object.hasOwn(keys,action))return reply(410,{error:{code:"ALLOWANCE_STEP_RETIRED"}});
 if(req.method!=="POST")return reply(405,{error:{code:"METHOD_NOT_ALLOWED"}});
 if(!usesCyId(env)||(action!=="view"&&!env.cyIdContext))return reply(403,{error:{code:"CYID_REQUIRED"}});
 if(env.ALLOWANCE_API_ENABLED!=="true"||Number(env.SCHEMA_VERSION)<21||!env.COMPANY_SETTINGS_KEY_HEX)
  return reply(503,{error:{code:"ALLOWANCES_NOT_READY"}});
 const input=await read(req,action);
 if(!input)return reply(400,{error:{code:"INVALID_REQUEST"}});
 const historyDetail=(action==="detail"||action==="view")&&input.applicationId===undefined;
 if(historyDetail?(input.operation!=="issue"||
   typeof input.allowanceNumber!=="string"||!/^[A-Z0-9]{12}$/.test(input.allowanceNumber)||
   typeof input.originalInvoiceNumber!=="string"||!/^[A-Z]{2}\d{8}$/.test(input.originalInvoiceNumber)||
   !["test","production"].includes(input.environment as string)):
   action!=="list"&&!id(input.applicationId))return reply(400,{error:{code:"INVALID_REQUEST"}});
 if(["approve","void-approve","withdraw"].includes(action)&&!rev(input.expectedRevision))
  return reply(400,{error:{code:"INVALID_REVISION"}});
 if((action==="approve"&&env.ALLOWANCE_G0401_SEND_ENABLED!=="true")||
    (action==="void-approve"&&env.ALLOWANCE_G0501_SEND_ENABLED!=="true"))
  return reply(503,{error:{code:"OFFICIAL_SEND_DISABLED"}});
 const supplied=req.headers.get("x-request-id")??"";
 const requestId=/^[A-Za-z0-9_.:-]{1,128}$/.test(supplied)?supplied:"req-"+crypto.randomUUID();
 let stage="request_authority_check";
 let reportStage:((stage:string,errorCode?:string)=>Promise<void>)|undefined;
 let failureTarget:{workspaceId:string;applicationId:string}|undefined;
 let diagnosticSaved=true;
 try{
  const binding=cyIdBinding(env),dev=await cyIdDevice(req,env),authority=auth(env),actor=action==="view"?null:await authority.revalidate();
  if((action!=="view"&&!actor)||dev.workspace_id!==binding.workspaceId||!await checkDevice(env.DB,dev.device_id,binding.workspaceId))
   return reply(403,{error:{code:"AUTHORITY_CHANGED"}});
  const target={workspaceId:binding.workspaceId,applicationId:input.applicationId as string,
   deviceId:dev.device_id,expectedRevision:input.expectedRevision as number};
  if(historyDetail){
   if(action!=="view"&&(!actor||!manager(actor)))return reply(403,{error:{code:"MANAGER_REQUIRED"}});
   const linked=await env.DB.prepare(`SELECT l.application_id FROM allowance_number_ledger l
    JOIN company_settings c ON c.workspace_id=l.workspace_id AND c.seller_invoice=l.seller_invoice
    WHERE l.seller_invoice=c.seller_invoice AND l.environment=?1 AND l.allowance_number=?2
     AND l.workspace_id=?3 AND l.status IN ('sending','confirmed') LIMIT 1`)
    .bind(input.environment,input.allowanceNumber,binding.workspaceId).first<{application_id:string}>();
   if(!linked)return reply(404,{error:{code:"NOT_FOUND"}});
   target.applicationId=linked.application_id;
  }
  let environment:unknown=input.environment;
  if(action!=="create"&&action!=="void-create"&&action!=="list"){
   const isVoid=action.startsWith("void-")||(["detail","view","diagnostics","withdraw"].includes(action)&&input.operation==="void");
   const table=isVoid?"allowance_void_requests":"allowance_applications";
   const source=await env.DB.prepare(`SELECT environment FROM ${table} WHERE workspace_id=?1 AND application_id=?2 LIMIT 1`)
    .bind(binding.workspaceId,target.applicationId).first<{environment:string}>();
   if(!source)return reply(404,{error:{code:"NOT_FOUND"}});
   environment=source.environment;
  }
  if(action!=="list"&&environment!=="test"&&environment!=="production")
   return reply(400,{error:{code:"INVALID_ENVIRONMENT"}});
  const ctx:CloudAllowanceContext={encryptionKeyHex:env.COMPANY_SETTINGS_KEY_HEX,
   environment:environment==="production"?"production":"test"};
  if(action==="view"||action==="detail"){
   if(Number(env.SCHEMA_VERSION)<23)return reply(503,{error:{code:"ALLOWANCE_DETAIL_UPDATE_REQUIRED"}});
   const operation=input.operation==="void"?"void":"issue";
   if(input.operation!=="void"&&input.operation!=="issue")return reply(400,{error:{code:"INVALID_OPERATION"}});
   if(action==="detail"&&(!actor||!manager(actor)))return reply(403,{error:{code:"MANAGER_REQUIRED"}});
   const view=await allowanceView(env.DB,target,ctx,operation,action==="view"?{deviceRead:true}:authority);
   if(historyDetail&&view.rows.find(row=>row.label==="原發票")?.value!==input.originalInvoiceNumber)return reply(404,{error:{code:"NOT_FOUND"}});
   if(!await checkDevice(env.DB,dev.device_id,binding.workspaceId))return reply(403,{error:{code:"DEVICE_INVALID"}});
   return reply(200,view);
  }
  if(!actor)return reply(403,{error:{code:"MANAGER_REQUIRED"}});
  if(action==="withdraw"){
   if(input.operation!=="issue"&&input.operation!=="void")return reply(400,{error:{code:"INVALID_OPERATION"}});
   if(Number(env.SCHEMA_VERSION)<23)return reply(503,{error:{code:"ALLOWANCE_RETURN_UPDATE_REQUIRED"}});
   if(!manager(actor))return reply(403,{error:{code:"MANAGER_REQUIRED"}});
   if(typeof input.reason!=="string"||!input.reason.trim()||input.reason.length>500||/[\x00-\x1f\x7f]/.test(input.reason))
    return reply(400,{error:{code:"RETURN_REASON_REQUIRED"}});
   const operation=input.operation==="void"?"void":"issue",at=new Date().toISOString();
   const ciphertext=await encryptCompanyBusinessData(ctx.encryptionKeyHex,target.workspaceId,"allowance-return/"+operation+"/"+target.applicationId,
    JSON.stringify({reason:input.reason.trim(),actorId:actor.employeeId,participant:actor.employeeNo&&actor.displayName?actor.employeeNo+" "+actor.displayName:"姓名未取得"}));
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||fresh.workspaceRole!==actor.workspaceRole)throw new Error("AUTHORITY_CHANGED");
   const sql=operation==="issue"?
    `UPDATE allowance_applications SET status='withdrawn',revision=revision+1,manager_actor_id=NULL,
      actor_id=?4,updated_by_device_id=?5,updated_at=?6,returned_at=?6,return_ciphertext=?7,execution_state='completed'
      WHERE application_id=?1 AND workspace_id=?2 AND revision=?3 AND status IN ('open','review') AND returned_at IS NULL
      AND NOT EXISTS(SELECT 1 FROM allowance_execution_approvals g WHERE g.application_id=?1)
      AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.application_id=?1)
      AND EXISTS(SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id WHERE d.device_id=?5 AND d.workspace_id=?2 AND d.status='active' AND w.status='active')`:
    `UPDATE allowance_void_requests SET returned_at=?6,return_ciphertext=?7,execution_state='completed'
      WHERE application_id=?1 AND workspace_id=?2 AND revision=?3 AND status='open' AND returned_at IS NULL
      AND NOT EXISTS(SELECT 1 FROM allowance_void_approvals g WHERE g.application_id=?1)
      AND EXISTS(SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id WHERE d.device_id=?5 AND d.workspace_id=?2 AND d.status='active' AND w.status='active')`;
   const result=await env.DB.prepare(sql).bind(target.applicationId,target.workspaceId,target.expectedRevision,actor.employeeId,dev.device_id,at,ciphertext).run();
   if(!Number.isSafeInteger(result.meta.changes)||result.meta.changes<=0)return reply(409,{error:{code:"RETURN_NOT_PENDING"}});
   try {await recordExecutionEvent(env.DB,target,operation,"returned",requestId,()=>new Date());}catch{console.warn("ALLOWANCE_RETURN_DIAGNOSTIC_FAILED");}
   return reply(200,{applicationId:target.applicationId,status:"withdrawn",revision:target.expectedRevision+(operation==="issue"?1:0)});
  }
  if(action==="create"){
   if(input.environment!==ctx.environment||typeof input.originalInvoiceNumber!=="string"||
    typeof input.allowanceDay!=="string"||!Number.isSafeInteger(input.requestedGross)||
    typeof input.reason!=="string"||!input.reason.trim()||input.reason.length>500||/[\x00-\x1f]/.test(input.reason)||typeof input.unnumberedTemplateJson!=="string")
    return reply(400,{error:{code:"INVALID_REQUEST"}});
   const result=await createEncryptedCloudAllowance(env.DB,{...target,environment:ctx.environment,
    originalInvoiceNumber:input.originalInvoiceNumber,allowanceDay:input.allowanceDay,
    requestedGross:input.requestedGross as number,reason:input.reason,unnumberedTemplateJson:input.unnumberedTemplateJson},ctx,authority);
   return reply(200,{...result,officialActionPerformed:false});
  }
  if(action==="void-create"){
   if(input.environment!==ctx.environment||typeof input.allowanceNumber!=="string"||typeof input.originalInvoiceNumber!=="string"||typeof input.reason!=="string")
    return reply(400,{error:{code:"INVALID_REQUEST"}});
   const result=await createCloudAllowanceVoidApplication(env.DB,{...target,environment:ctx.environment,
    allowanceNumber:input.allowanceNumber,originalInvoiceNumber:input.originalInvoiceNumber,reason:input.reason},ctx,authority);
   return reply(200,{...result,officiallyConfirmed:false,officialActionPerformed:false});
  }
  if(action==="list"){
   if(input.limit!==undefined&&(!rev(input.limit)||(input.limit as number)>50))return reply(400,{error:{code:"INVALID_LIST_FILTER"}});
   if(input.status!==undefined&&!["open","review","approved","rejected","withdrawn"].includes(String(input.status)))return reply(400,{error:{code:"INVALID_LIST_FILTER"}});
   const rows=await env.DB.prepare(`SELECT application_id,status,revision,requested_gross,environment,updated_at,execution_state
    FROM allowance_applications WHERE workspace_id=?1 AND (?2 IS NULL OR status=?2) AND (?3=1 OR requester_actor_id=?4)
    ORDER BY updated_at DESC,application_id DESC LIMIT ?5`).bind(binding.workspaceId,input.status??null,manager(actor)?1:0,actor.employeeId,input.limit??20).all();
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||fresh.workspaceRole!==actor.workspaceRole)throw new Error("AUTHORITY_CHANGED");
   return reply(200,{items:rows.results.map((r:any)=>({applicationId:r.application_id,status:r.status,revision:r.revision,
    requestedGross:r.requested_gross,environment:r.environment,updatedAt:r.updated_at,executionState:r.execution_state}))});
  }
  if(action==="void-status"){
   const result=await readCloudAllowanceVoidApplication(env.DB,target,authority);
   const state=await env.DB.prepare(`SELECT execution_state,last_error${Number(env.SCHEMA_VERSION)>=23?",returned_at":""} FROM allowance_void_requests WHERE application_id=?1 AND workspace_id=?2`).bind(target.applicationId,target.workspaceId).first<{execution_state:string;last_error:string;returned_at?:string}>();
   return reply(200,{...result,...(state?.returned_at?{status:"withdrawn"}:{}),executionState:state?.execution_state,officiallyConfirmed:!state?.returned_at&&result.status==="approved"&&state?.execution_state==="completed",officialActionPerformed:false});
  }
  if(["get","status"].includes(action)){
   const row=await env.DB.prepare(`SELECT a.*,l.allowance_number,l.status AS ledger_status FROM allowance_applications a
    LEFT JOIN allowance_number_ledger l ON l.application_id=a.application_id
    WHERE a.application_id=?1 AND a.workspace_id=?2 AND (?3=1 OR a.requester_actor_id=?4)
    ORDER BY l.attempt DESC LIMIT 1`).bind(target.applicationId,target.workspaceId,manager(actor)?1:0,actor.employeeId).first<Record<string,unknown>>();
   if(!row)return reply(404,{error:{code:"NOT_FOUND"}});
   if(action==="status")return reply(200,{applicationId:row.application_id,status:row.status,revision:row.revision,
    allowanceNumber:row.allowance_number,ledgerStatus:row.ledger_status,executionState:row.execution_state,
    officiallyConfirmed:row.status==="approved"&&row.execution_state==="completed"});
   const f=await loadEncryptedCloudAllowance(env.DB,target,ctx,authority);
   return reply(200,{application:{applicationId:f.applicationId,status:f.status,revision:f.revision,
    environment:f.environment,requestedGross:f.requestedGross,allowanceDay:f.allowanceDay,reason:f.reason},officialActionPerformed:false});
  }
  if(!manager(actor))return reply(403,{error:{code:"MANAGER_REQUIRED"}});
  if(action==="approve"){
   failureTarget=target;
   reportStage=async(next,errorCode)=>{
    stage=next;
    try {await recordExecutionEvent(env.DB,target,"issue",next,requestId,()=>new Date(),{errorCode});}
    catch {diagnosticSaved=false;}
   };
   await reportStage("approval_started");
   const grant=await approveCloudAllowanceAfterOfficialProof(env.DB,target,ctx,authority,fetch,()=>new Date(),reportStage);
   const executionAuthority=context?approvedExecutionAuthority(env,target.workspaceId,grant.authoritySnapshot??null,
    grant.managerActorId,{applicationId:target.applicationId,operation:"issue"}):authority;
   const work=executeApprovedCloudAllowance(env.DB,{...target,expectedRevision:grant.approvedRevision},ctx,executionAuthority,fetch,()=>new Date(),requestId);
   if(context){
    // The existing execution owner continues after the durable decision. Runtime sync resumes interruptions.
    context.waitUntil(work);
    return reply(200,{applicationId:target.applicationId,status:"approved",revision:grant.approvedRevision,
     executionState:"processing",diagnosticSaved});
   }
   const result=await work;
   return reply(200,{status:"approved",revision:grant.approvedRevision,...result,diagnosticSaved});
  }
  if(action==="void-approve"){
   if(Number(env.SCHEMA_VERSION)>=23&&await env.DB.prepare("SELECT application_id FROM allowance_void_requests WHERE application_id=?1 AND workspace_id=?2 AND returned_at IS NOT NULL").bind(target.applicationId,target.workspaceId).first())
    return reply(409,{error:{code:"RETURN_NOT_PENDING"}});
   failureTarget=target;
   reportStage=async(next,errorCode)=>{
    stage=next;
    try {await recordExecutionEvent(env.DB,target,"void",next,requestId,()=>new Date(),{errorCode});}
    catch {diagnosticSaved=false;}
   };
   await reportStage("void_approval_official_check");
   const preview=await previewCloudVoidForManager(env.DB,target,ctx,authority);
   await approveCloudVoidAfterOfficialProof(env.DB,{...target,expectedRevision:1,requestHash:preview.requestHash,
    expectedOfficialSnapshotHash:preview.officialSnapshotHash},ctx,authority);
   await reportStage("void_approval_saved");
   const saved=await env.DB.prepare("SELECT manager_actor_id,authority_snapshot FROM allowance_void_approvals WHERE application_id=?1 AND workspace_id=?2").bind(target.applicationId,target.workspaceId)
    .first<{manager_actor_id:string;authority_snapshot:string|null}>();
   if(!saved)throw new Error("VOID_APPROVAL_UNCONFIRMED_READBACK_ONLY");
   const executionAuthority=context?approvedExecutionAuthority(env,target.workspaceId,saved.authority_snapshot,saved.manager_actor_id,{applicationId:target.applicationId,operation:"void"}):authority;
   const work=executeCloudVoidOnce(env.DB,{...target,expectedRevision:2},ctx,executionAuthority,fetch,()=>new Date(),true,requestId);
   if(context){context.waitUntil(work);return reply(200,{applicationId:target.applicationId,status:"approved",revision:2,executionState:"processing"});}
   return reply(200,{status:"approved",revision:2,...await work});
  }
  if((action==="detail"||action==="diagnostics")&&input.operation!=="issue"&&input.operation!=="void")
   return reply(400,{error:{code:"INVALID_OPERATION"}});
  if(action==="diagnostics"){
   const operation=input.operation==="void"?"void":"issue",table=operation==="issue"?"allowance_applications":"allowance_void_requests";
   const state=await env.DB.prepare(`SELECT execution_state,first_error,last_error,request_id FROM ${table} WHERE application_id=?1 AND workspace_id=?2`).bind(target.applicationId,target.workspaceId).first();
   if(!state)return reply(404,{error:{code:"NOT_FOUND"}});
   const after=input.afterEventId??0;
   if(!Number.isSafeInteger(after)||(after as number)<0)return reply(400,{error:{code:"INVALID_DIAGNOSTIC_CURSOR"}});
   const events=await env.DB.prepare(`SELECT event_id,stage,attempt,provider_code,http_status,response_received,request_id,error_code,recorded_at
    FROM allowance_execution_events WHERE workspace_id=?1 AND application_id=?2 AND operation=?3 AND event_id>?4 ORDER BY event_id LIMIT 100`).bind(target.workspaceId,target.applicationId,operation,after).all<{event_id:number}>();
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||fresh.workspaceRole!==actor.workspaceRole)throw new Error("AUTHORITY_CHANGED");
   return reply(200,{applicationId:target.applicationId,state,events:events.results,
    nextEventId:events.results.at(-1)?.event_id??after,hasMore:events.results.length===100});
  }
  return reply(404,{error:{code:"NOT_FOUND"}});
 }catch(error){
  const officialQuery=error instanceof OfficialInvoiceQueryError?error.diagnostic:undefined;
  const code=error instanceof CyIdError?error.code:
   error instanceof Error&&/^[A-Z0-9_]{3,100}$/.test(error.message)?error.message:"ALLOWANCE_RESULT_OR_STORAGE_UNKNOWN";
  const failedStage=stage;
  let causeCode="";
  if(officialQuery)causeCode="OFFICIAL_QUERY_"+officialQuery.failureKind.toUpperCase();
  if(reportStage&&failureTarget){
   if(!officialQuery&&error instanceof Error&&error.cause instanceof Error){
    // Only schema-defined symbolic errors, never raw SQL or bound values.
    const known=error.cause.message.match(/\b(?:ALLOWANCE|COMPANY|DEVICE|WORKSPACE)_[A-Z0-9_]{3,90}\b/);
    causeCode=known?.[0]??"D1_BATCH_FAILED";
   }
   await reportStage("approval_failed",code);
   if(causeCode)await reportStage("approval_storage_rejected",causeCode);
   try {await saveExecutionResult(env.DB,failureTarget,action==="void-approve"?"void":"issue",{applicationId:failureTarget.applicationId,
    executionState:"problem",errorCode:code},requestId,()=>new Date());}catch{diagnosticSaved=false;}
  }
  return reply(error instanceof CyIdError?error.status:code.includes("NOT_FOUND")?404:409,{error:{code,stage:failedStage,requestId,
   diagnosticSaved:action==="detail"?undefined:diagnosticSaved,causeCode,officialQuery}});
 }
}
