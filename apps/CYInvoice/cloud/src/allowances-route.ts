// V3.2-C4: CYID-authorized Cloud allowance application HTTP entrypoints.
// Does not expose g0401/g0501; no automatic official mutation over HTTP.
// Requires explicit feature flag AND canonical schema >=18. All input PII
// stays in C3a AES-GCM frozen store; GET/LIST never return payload or keys.
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {createEncryptedCloudAllowance,loadEncryptedCloudAllowance}
 from "./allowance-frozen-store";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext}
 from "./allowance-frozen-store";
import {previewCloudAllowanceForManager} from "./allowance-manager-preview";
import {approveCloudAllowanceAfterOfficialProof} from "./allowance-manager-approval";
import {reserveApprovedCloudAllowanceNumber} from "./allowance-number-reservation";

interface Env extends CyIdEnv {
 DB:D1Database;SCHEMA_VERSION:string;APP_ENV:string;
 ALLOWANCE_API_ENABLED?:string;COMPANY_SETTINGS_KEY_HEX?:string;
}
const paths=new Set(["/v1/allowances/create","/v1/allowances/get",
 "/v1/allowances/list","/v1/allowances/review","/v1/allowances/withdraw",
 "/v1/allowances/preview","/v1/allowances/approve",
 "/v1/allowances/reserve","/v1/allowances/status"]);
const keys:Record<string,readonly string[]>={
 create:["employeeNo","password","applicationId","environment",
  "originalInvoiceNumber","allowanceDay","requestedGross","reason","unnumberedTemplateJson"],
 get:["employeeNo","password","applicationId"],
 list:["employeeNo","password","status","limit"],
 review:["employeeNo","password","applicationId","expectedRevision"],
 withdraw:["employeeNo","password","applicationId","expectedRevision"],
 preview:["employeeNo","password","applicationId","expectedRevision"],
 approve:["employeeNo","password","applicationId","expectedRevision"],
 reserve:["employeeNo","password","applicationId","expectedRevision"],
 status:["employeeNo","password","applicationId"]
};
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
 return {workspaceId:binding.workspaceId,employeeId:p.employeeId,
  workspaceRole:p.workspaceRole};
}
function auth(env:Env):AllowanceActorSource{
 return {revalidate:async()=>scope(
   (await env.cyIdContext?.revalidate())??null,env)};
}
async function read(req:Request,path:string):Promise<Record<string,unknown>|null>{
 const max=path==="create"?36864:4096;
 const header=req.headers.get("content-length");
 if(header!==null&&(!/^\d+$/.test(header)||Number(header)>max))return null;
 const text=await req.text();
 if(new TextEncoder().encode(text).byteLength>max)return null;
 let body:unknown;
 try{body=JSON.parse(text);}catch{return null;}
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
export async function handleAllowances(req:Request,env:Env):Promise<Response|null>{
 const tail=new URL(req.url).pathname;
 if(!tail.startsWith("/v1/allowances/"))return null;
 if(!paths.has(tail))return reply(404,{error:{code:"NOT_FOUND"}});
 if(req.method!=="POST")return reply(405,{error:{code:"METHOD_NOT_ALLOWED"}});
 if(!usesCyId(env))return reply(409,{error:{code:"CYID_REQUIRED"}});
 if(env.ALLOWANCE_API_ENABLED!=="true"||
   !/^\d+$/.test(env.SCHEMA_VERSION)||Number(env.SCHEMA_VERSION)<18)
  return reply(503,{error:{code:"ALLOWANCES_NOT_READY"}});
 if(!env.cyIdContext||!env.COMPANY_SETTINGS_KEY_HEX)
  return reply(503,{error:{code:"ALLOWANCES_NOT_READY"}});
 const action=tail.split("/").at(-1)!;
 const input=await read(req,action);
 if(!input)return reply(400,{error:{code:"INVALID_REQUEST"}});
 if(action!=="list"&&!id(input.applicationId))
  return reply(400,{error:{code:"INVALID_APPLICATION_ID"}});
 if(["review","withdraw","preview","approve","reserve"].includes(action)&&
    !rev(input.expectedRevision))
  return reply(400,{error:{code:"INVALID_REVISION"}});
 if(action==="list"&&
  ((input.status!==undefined&&!["open","review","approved","rejected","withdrawn"].includes(String(input.status)))||
   (input.limit!==undefined&&(!rev(input.limit)||(input.limit as number)>50))))
  return reply(400,{error:{code:"INVALID_LIST_FILTER"}});
 try{
  const binding=cyIdBinding(env),dev=await cyIdDevice(req,env);
  if(dev.workspace_id!==binding.workspaceId||
     !await checkDevice(env.DB,dev.device_id,binding.workspaceId))
    return reply(401,{error:{code:"DEVICE_INVALID"}});
  const authority=auth(env),actor=await authority.revalidate();
  if(!actor)return reply(403,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
  const target={workspaceId:binding.workspaceId,deviceId:dev.device_id,
   applicationId:input.applicationId as string,
   expectedRevision:input.expectedRevision as number};
  const environment=env.APP_ENV==="production"?"production":"test";
  const ctx:CloudAllowanceContext={
   encryptionKeyHex:env.COMPANY_SETTINGS_KEY_HEX,environment};
  if(action==="create"){
   if(!["production","test"].includes(String(input.environment))||
     input.environment!==environment||
     typeof input.originalInvoiceNumber!=="string"||
     typeof input.allowanceDay!=="string"||
     !Number.isSafeInteger(input.requestedGross)||
     typeof input.reason!=="string"||input.reason.length<1||
     input.reason.length>500||input.reason!==input.reason.trim()||
     /[\x00-\x1f\x7f]/.test(input.reason)||
     typeof input.unnumberedTemplateJson!=="string")
    return reply(400,{error:{code:"INVALID_REQUEST"}});
   const result=await createEncryptedCloudAllowance(env.DB,{
    workspaceId:binding.workspaceId,deviceId:dev.device_id,
    applicationId:target.applicationId,environment,
    originalInvoiceNumber:input.originalInvoiceNumber,
    allowanceDay:input.allowanceDay,
    requestedGross:input.requestedGross as number,
    reason:input.reason,
    unnumberedTemplateJson:input.unnumberedTemplateJson
   },ctx,authority);
   return reply(200,{applicationId:result.applicationId,
    status:result.status,revision:result.revision,officialActionPerformed:false});
  }
  if(action==="get"){
   // Authorization is checked BEFORE decrypting the frozen request. Same
   // Workspace alone never entitles another USER to inspect a colleague's
   // financial allowance; managers can inspect the queue.
   const visible=await env.DB.prepare(`SELECT application_id
    FROM allowance_applications WHERE workspace_id=?1
     AND application_id=?2 AND (?3=1 OR requester_actor_id=?4)
     LIMIT 1`)
    .bind(binding.workspaceId,target.applicationId,manager(actor)?1:0,
      actor.employeeId).first<{application_id:string}>();
   if(!visible)return reply(404,{error:{code:"NOT_FOUND"}});
   const result=await loadEncryptedCloudAllowance(env.DB,target,ctx,authority);
   const fresh=await authority.revalidate();
   if(!fresh||actor.employeeId!==fresh.employeeId||
      actor.workspaceRole!==fresh.workspaceRole||
      !await checkDevice(env.DB,dev.device_id,binding.workspaceId))
    return reply(409,{error:{code:"AUTHORITY_CHANGED"}});
   return reply(200,{application:{
    applicationId:result.applicationId,environment:result.environment,
    status:result.status,revision:result.revision,
    requestedGross:result.requestedGross,allowanceDay:result.allowanceDay,
    reason:result.reason
   },officialActionPerformed:false});
  }
  if(action==="list"){
   const rows=await env.DB.prepare(`SELECT application_id,status,revision,
    requested_gross,environment,updated_at
    FROM allowance_applications WHERE workspace_id=?1
    AND (?2 IS NULL OR status=?2)
    AND (?3=1 OR requester_actor_id=?4)
    ORDER BY updated_at DESC,application_id DESC LIMIT ?5`)
   .bind(binding.workspaceId,input.status??null,manager(actor)?1:0,
     actor.employeeId,input.limit??20).all<{
       application_id:string;status:string;revision:number;
       requested_gross:string;environment:string;updated_at:string
     }>();
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||
      fresh.workspaceRole!==actor.workspaceRole||
      !await checkDevice(env.DB,dev.device_id,binding.workspaceId))
    return reply(409,{error:{code:"AUTHORITY_CHANGED"}});
   return reply(200,{items:rows.results.map(x=>({
    applicationId:x.application_id,status:x.status,
    revision:x.revision,requestedGross:x.requested_gross,
    environment:x.environment,updatedAt:x.updated_at
   }))});
  }
  if(action==="status"){
   const row=await env.DB.prepare(`SELECT a.application_id,a.status,a.revision,
    a.requester_actor_id,l.allowance_number,l.status AS ledger_status
    FROM allowance_applications a LEFT JOIN allowance_number_ledger l
     ON l.application_id=a.application_id
    WHERE a.application_id=?1 AND a.workspace_id=?2
    ORDER BY l.attempt DESC LIMIT 1`)
   .bind(target.applicationId,binding.workspaceId).first<{
    application_id:string;status:string;revision:number;
    requester_actor_id:string;allowance_number:string|null;
    ledger_status:string|null
   }>();
   if(!row||(!manager(actor)&&row.requester_actor_id!==actor.employeeId))
    return reply(404,{error:{code:"NOT_FOUND"}});
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||
      !await checkDevice(env.DB,dev.device_id,binding.workspaceId))
    return reply(409,{error:{code:"AUTHORITY_CHANGED"}});
   return reply(200,{applicationId:row.application_id,
    status:row.status,revision:row.revision,
    allowanceNumber:row.allowance_number,ledgerStatus:row.ledger_status,
    officiallyConfirmed:false});
  }
  if(action==="withdraw"){
   // Applicant may withdraw ONLY while open, never after manager claims it.
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||
      fresh.workspaceRole!==actor.workspaceRole||
      !await checkDevice(env.DB,dev.device_id,binding.workspaceId))
    return reply(409,{error:{code:"AUTHORITY_CHANGED"}});
   const r=await env.DB.prepare(`UPDATE allowance_applications SET
    status='withdrawn',revision=revision+1,actor_id=?1,
    updated_by_device_id=?2,updated_at=?3
    WHERE workspace_id=?4 AND application_id=?5
     AND status='open' AND revision=?6
     AND requester_actor_id=?1 AND EXISTS (
       SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id
       WHERE d.device_id=?2 AND d.workspace_id=?4 AND d.status='active'
       AND d.employee_authority_state='cloud' AND w.status='active')`)
    .bind(actor.employeeId,dev.device_id,new Date().toISOString(),
     binding.workspaceId,target.applicationId,target.expectedRevision).run();
   return r.meta.changes===1
    ?reply(200,{applicationId:target.applicationId,status:"withdrawn",
      revision:target.expectedRevision+1})
    :reply(409,{error:{code:"APPLICATION_STATE_CHANGED"}});
  }
  if(!manager(actor))
   return reply(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
  if(action==="review"){
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||
      fresh.workspaceRole!==actor.workspaceRole)
    return reply(409,{error:{code:"AUTHORITY_CHANGED"}});
   const r=await env.DB.prepare(`UPDATE allowance_applications
    SET status='review',revision=revision+1,manager_actor_id=?1,actor_id=?1,
     updated_by_device_id=?2,updated_at=?3
    WHERE application_id=?4 AND workspace_id=?5
     AND status='open' AND revision=?6 AND EXISTS(
       SELECT 1 FROM allowance_frozen_requests f
       JOIN devices d ON d.device_id=?2 AND d.workspace_id=f.workspace_id
       JOIN workspaces w ON w.workspace_id=f.workspace_id
       WHERE f.application_id=?4 AND f.workspace_id=?5 AND
       f.request_hash=allowance_applications.request_hash
       AND d.status='active' AND d.employee_authority_state='cloud'
       AND w.status='active')`)
    .bind(actor.employeeId,dev.device_id,new Date().toISOString(),
     target.applicationId,binding.workspaceId,target.expectedRevision).run();
   return r.meta.changes===1
    ?reply(200,{applicationId:target.applicationId,status:"review",
      revision:target.expectedRevision+1,officialActionPerformed:false})
    :reply(409,{error:{code:"APPLICATION_STATE_CHANGED"}});
  }
  if(action==="preview"){
   const result=await previewCloudAllowanceForManager(
    env.DB,target,ctx,authority);
   return reply(200,{preview:result});
  }
  if(action==="approve"){
   const result=await approveCloudAllowanceAfterOfficialProof(
    env.DB,target,ctx,authority);
   return reply(200,{applicationId:result.applicationId,
    status:"approved",revision:result.approvedRevision,
    validUntilUtc:result.validUntilUtc,officialActionPerformed:false});
  }
  if(action==="reserve"){
   const result=await reserveApprovedCloudAllowanceNumber(
    env.DB,target,ctx,authority);
   return reply(200,{applicationId:result.applicationId,
    ledgerId:result.ledgerId,allowanceNumber:result.allowanceNumber,
    status:"reserved",maySendWithoutFreshOfficialCheck:false});
  }
  return reply(404,{error:{code:"NOT_FOUND"}});
 }catch(e){
  if(e instanceof CyIdError)
   return reply(e.status,{error:{code:e.code}});
  // Do not return API key, frozen content, official buyer data or SQL details.
  if(e instanceof Error&&/^(ALLOWANCE_|OFFICIAL_)/.test(e.message))
   return reply(409,{error:{code:"ALLOWANCE_OPERATION_UNCONFIRMED"}});
  return reply(503,{error:{code:"ALLOWANCE_RESULT_UNKNOWN"}});
 }
}
