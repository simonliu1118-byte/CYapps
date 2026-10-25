import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {createEncryptedCloudAllowance,loadEncryptedCloudAllowance,type AllowanceActor,type AllowanceActorSource,type CloudAllowanceContext} from "./allowance-frozen-store";
import {approveCloudAllowanceAfterOfficialProof} from "./allowance-manager-approval";
import {executeApprovedCloudAllowance} from "./allowance-one-shot-coordinator";
import {executeCloudVoidOnce} from "./allowance-void-one-shot-coordinator";
import {createCloudAllowanceVoidApplication,readCloudAllowanceVoidApplication,loadCloudAllowanceVoidForManager} from "./allowance-void-applications";
import {previewCloudVoidForManager} from "./allowance-void-manager-preview";
import {approveCloudVoidAfterOfficialProof} from "./allowance-void-manager-approval";
import {loadAmegoCompanySettingsForGateway,amegoCredentialsForEnvironment} from "./company-settings";
import {reconcileOfficialAllowanceForOriginal} from "./allowance-official-proof";
import {queryOfficialInvoiceReadOnly} from "./amego-invoice-query";
interface Env extends CyIdEnv {DB:D1Database;SCHEMA_VERSION:string;APP_ENV:string;
 ALLOWANCE_API_ENABLED?:string;ALLOWANCE_G0401_SEND_ENABLED?:string;ALLOWANCE_G0501_SEND_ENABLED?:string;COMPANY_SETTINGS_KEY_HEX?:string;}
const keys:Record<string,readonly string[]>={
 create:["employeeNo","password","applicationId","environment","originalInvoiceNumber","allowanceDay","requestedGross","reason","unnumberedTemplateJson"],
 get:["employeeNo","password","applicationId"],list:["employeeNo","password","status","limit"],
 approve:["employeeNo","password","applicationId","expectedRevision"],
 status:["employeeNo","password","applicationId"],withdraw:["employeeNo","password","applicationId","expectedRevision"],
 "void-create":["employeeNo","password","applicationId","environment","allowanceNumber","originalInvoiceNumber","reason"],
 "void-status":["employeeNo","password","applicationId"],
 "void-approve":["employeeNo","password","applicationId","expectedRevision","requestHash","expectedOfficialSnapshotHash"],
 detail:["employeeNo","password","applicationId","operation"],
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
 return {authoritySnapshot:JSON.stringify({identityWorkspaceId:binding.identityWorkspaceId,applicationId:binding.applicationId,employeeId:p.employeeId,credentialVersion:p.credentialVersion,employeeRevision:p.employeeRevision,workspaceRole:p.workspaceRole,isIdentityAdmin:p.isIdentityAdmin,emailVerified:p.emailVerified}),workspaceId:binding.workspaceId,employeeId:p.employeeId,
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
export async function handleAllowances(req:Request,env:Env):Promise<Response|null>{
 const path=new URL(req.url).pathname;
 if(!path.startsWith("/v1/allowances/"))return null;
 const action=path.split("/").at(-1)!;
 if(!Object.hasOwn(keys,action))return reply(410,{error:{code:"ALLOWANCE_STEP_RETIRED"}});
 if(req.method!=="POST")return reply(405,{error:{code:"METHOD_NOT_ALLOWED"}});
 if(!usesCyId(env)||!env.cyIdContext)return reply(403,{error:{code:"CYID_REQUIRED"}});
 if(env.ALLOWANCE_API_ENABLED!=="true"||Number(env.SCHEMA_VERSION)<21||!env.COMPANY_SETTINGS_KEY_HEX)
  return reply(503,{error:{code:"ALLOWANCES_NOT_READY"}});
 const input=await read(req,action);
 if(!input||action!=="list"&&!id(input.applicationId))return reply(400,{error:{code:"INVALID_REQUEST"}});
 if(["approve","void-approve","withdraw"].includes(action)&&!rev(input.expectedRevision))
  return reply(400,{error:{code:"INVALID_REVISION"}});
 if((action==="approve"&&env.ALLOWANCE_G0401_SEND_ENABLED!=="true")||
    (action==="void-approve"&&env.ALLOWANCE_G0501_SEND_ENABLED!=="true"))
  return reply(503,{error:{code:"OFFICIAL_SEND_DISABLED"}});
 try{
  const binding=cyIdBinding(env),dev=await cyIdDevice(req,env),authority=auth(env),actor=await authority.revalidate();
  if(!actor||dev.workspace_id!==binding.workspaceId||!await checkDevice(env.DB,dev.device_id,binding.workspaceId))
   return reply(403,{error:{code:"AUTHORITY_CHANGED"}});
  const target={workspaceId:binding.workspaceId,applicationId:input.applicationId as string,
   deviceId:dev.device_id,expectedRevision:input.expectedRevision as number};
  let environment:unknown=input.environment;
  if(action!=="create"&&action!=="void-create"&&action!=="list"){
   const isVoid=action.startsWith("void-")||((action==="detail"||action==="diagnostics")&&input.operation==="void");
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
  const supplied=req.headers.get("x-request-id")??"";
  const requestId=/^[A-Za-z0-9_.:-]{1,128}$/.test(supplied)?supplied:"req-"+crypto.randomUUID();
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
   const state=await env.DB.prepare(`SELECT execution_state,last_error FROM allowance_void_requests WHERE application_id=?1 AND workspace_id=?2`).bind(target.applicationId,target.workspaceId).first<{execution_state:string;last_error:string}>();
   return reply(200,{...result,executionState:state?.execution_state,officiallyConfirmed:state?.execution_state==="completed",officialActionPerformed:false});
  }
  if(["get","status","withdraw"].includes(action)){
   const row=await env.DB.prepare(`SELECT a.*,l.allowance_number,l.status AS ledger_status FROM allowance_applications a
    LEFT JOIN allowance_number_ledger l ON l.application_id=a.application_id
    WHERE a.application_id=?1 AND a.workspace_id=?2 AND (?3=1 OR a.requester_actor_id=?4)
    ORDER BY l.attempt DESC LIMIT 1`).bind(target.applicationId,target.workspaceId,manager(actor)?1:0,actor.employeeId).first<Record<string,unknown>>();
   if(!row)return reply(404,{error:{code:"NOT_FOUND"}});
   if(action==="status")return reply(200,{applicationId:row.application_id,status:row.status,revision:row.revision,
    allowanceNumber:row.allowance_number,ledgerStatus:row.ledger_status,executionState:row.execution_state,
    officiallyConfirmed:row.execution_state==="completed"});
   if(action==="withdraw"){
    const fresh=await authority.revalidate();
    if(!fresh||fresh.employeeId!==actor.employeeId||fresh.workspaceRole!==actor.workspaceRole)throw new Error("AUTHORITY_CHANGED");
    if(row.status!=="open"&&Number(env.SCHEMA_VERSION)<22)return reply(503,{error:{code:"UNSENT_WITHDRAWAL_REQUIRES_SCHEMA22"}});
    const r=await env.DB.prepare(`UPDATE allowance_applications SET status='withdrawn',revision=revision+1,manager_actor_id=NULL,
     actor_id=?1,updated_by_device_id=?2,updated_at=?3 WHERE application_id=?4 AND workspace_id=?5
     AND EXISTS(SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id
      WHERE d.device_id=?2 AND d.workspace_id=?5 AND d.status='active' AND d.employee_authority_state='cloud' AND w.status='active')
     AND revision=?6 AND ((status='open' AND requester_actor_id=?1) OR
      (status IN ('review','approved') AND ?7=1))
     AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.application_id=?4
      AND l.status<>'reserved' AND NOT (l.status='occupied_external' AND EXISTS
   (SELECT 1 FROM allowance_number_rejections r WHERE r.ledger_id=l.ledger_id AND r.provider_code=l.collision_code)))`)
     .bind(actor.employeeId,dev.device_id,new Date().toISOString(),target.applicationId,target.workspaceId,target.expectedRevision,manager(fresh)?1:0).run();
    return reply(r.meta.changes===1?200:409,{applicationId:target.applicationId,status:"withdrawn",revision:target.expectedRevision+1});
   }
   const f=await loadEncryptedCloudAllowance(env.DB,target,ctx,authority);
   return reply(200,{application:{applicationId:f.applicationId,status:f.status,revision:f.revision,
    environment:f.environment,requestedGross:f.requestedGross,allowanceDay:f.allowanceDay,reason:f.reason},officialActionPerformed:false});
  }
  if(!manager(actor))return reply(403,{error:{code:"MANAGER_REQUIRED"}});
  if(action==="approve"){
   const grant=await approveCloudAllowanceAfterOfficialProof(env.DB,target,ctx,authority);
   const result=await executeApprovedCloudAllowance(env.DB,{...target,expectedRevision:grant.approvedRevision},ctx,authority,fetch,()=>new Date(),requestId);
   return reply(200,{status:"approved",revision:grant.approvedRevision,...result});
  }
  if(action==="void-approve"){
   if(typeof input.requestHash!=="string"||typeof input.expectedOfficialSnapshotHash!=="string")return reply(400,{error:{code:"INVALID_REQUEST"}});
   await approveCloudVoidAfterOfficialProof(env.DB,{...target,expectedRevision:1,requestHash:input.requestHash,
    expectedOfficialSnapshotHash:input.expectedOfficialSnapshotHash},ctx,authority);
   const result=await executeCloudVoidOnce(env.DB,{...target,expectedRevision:2},ctx,authority,fetch,()=>new Date(),true,requestId);
   return reply(200,{status:"approved",revision:2,...result});
  }
  if((action==="detail"||action==="diagnostics")&&input.operation!=="issue"&&input.operation!=="void")
   return reply(400,{error:{code:"INVALID_OPERATION"}});
  if(action==="detail"){
   const isVoid=input.operation==="void";
   const f=isVoid?await loadCloudAllowanceVoidForManager(env.DB,target,ctx,authority):await loadEncryptedCloudAllowance(env.DB,target,ctx,authority);
   const c=await loadAmegoCompanySettingsForGateway(env.DB,target.workspaceId,ctx.encryptionKeyHex);
   const invoice=await queryOfficialInvoiceReadOnly({...amegoCredentialsForEnvironment(c,ctx.environment),originalInvoiceNumber:f.originalInvoiceNumber});
   const rows:Array<{label:string;value:string}>=[];
   const add=(label:string,value:unknown)=>rows.push({label,value:value===undefined||value===null?"－":String(value)});
   add("原發票",invoice.invoiceNumber);add("原發票日期",invoice.invoiceDate);add("原發票含稅金額",invoice.totalAmount);add("原發票稅額",invoice.invoiceTaxAmount);
   const itemText=(raw:unknown)=>Array.isArray(raw)?raw.map((i:any)=>
    `${i.OriginalDescription??i.original_description??i.description??"－"}　數量 ${i.Quantity??i.quantity??"－"}　單價 ${i.UnitPrice??i.unit_price??"－"}　金額 ${i.Amount??i.amount??"－"}　稅 ${i.Tax??i.tax??"－"}`).join("\n"):"－";
   add("原發票商品",itemText(invoice.originalProductItems));
   add("既有折讓",invoice.allowanceHistory.map(x=>`${x.allowanceNumber??"－"}　金額 ${x.totalAmount??"－"}　稅 ${x.taxAmount??"－"}　狀態 ${x.uploadStatus??"－"}　作廢日 ${x.cancelDate??"－"}`).join("\n"));
   add("申請人",f.requesterActorId);add("申請原因",f.reason);
   let requestHash="",officialSnapshotHash="";
   if(isVoid){
    const v=f as Awaited<ReturnType<typeof loadCloudAllowanceVoidForManager>>;
    add("原折讓號",v.allowanceNumber);
    if(v.status==="open"){
     const preview=await previewCloudVoidForManager(env.DB,target,ctx,authority);
     requestHash=preview.requestHash;officialSnapshotHash=preview.officialSnapshotHash;
     const original=preview.originalAllowance as Record<string,unknown>|undefined;
     add("原折讓商品",itemText(original?.product_item));add("原折讓未稅額",original?.total_amount);add("原折讓稅額",original?.tax_amount);
     add("原折讓買受人",`${original?.buyer_name??"－"}／${original?.buyer_identifier??"－"}`);
    }else add("執行狀態","主管已核准，系統會依原號自動查回；無須再核准或送出");
   }else{
    const frozen=f as Awaited<ReturnType<typeof loadEncryptedCloudAllowance>>;
    const request=JSON.parse(frozen.unnumberedTemplateJson);
    add("買受人",`${request.BuyerName}／${request.BuyerIdentifier}`);
    add("本次折讓商品",itemText(request.ProductItem));add("本次含稅折讓",frozen.requestedGross);
    if(frozen.status==="open"||frozen.status==="review"){
     const proof=await reconcileOfficialAllowanceForOriginal(frozen,{...amegoCredentialsForEnvironment(c,ctx.environment)});
     const scaled=BigInt(proof.officialRemainingBeforeRequest);
     add("最新可折含稅額",`${scaled/10000000n}.${String(scaled%10000000n).padStart(7,"0")}`);
    }
   }
   const fresh=await authority.revalidate();
   if(!fresh||fresh.employeeId!==actor.employeeId||fresh.workspaceRole!==actor.workspaceRole)throw new Error("AUTHORITY_CHANGED");
   const withdrawable=!isVoid&&Number(env.SCHEMA_VERSION)>=22&&!!await env.DB.prepare(`SELECT application_id FROM allowance_applications
    WHERE application_id=?1 AND workspace_id=?2 AND status IN ('review','approved')
    AND NOT EXISTS(SELECT 1 FROM allowance_number_ledger l WHERE l.application_id=?1 AND l.status<>'reserved' AND NOT (l.status='occupied_external' AND EXISTS
   (SELECT 1 FROM allowance_number_rejections r WHERE r.ledger_id=l.ledger_id AND r.provider_code=l.collision_code)))`)
    .bind(target.applicationId,target.workspaceId).first();
   return reply(200,{applicationId:target.applicationId,status:f.status,revision:f.revision,operation:isVoid?"void":"issue",rows,requestHash,officialSnapshotHash,canWithdraw:withdrawable});
  }
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
  if(error instanceof CyIdError)return reply(error.status,{error:{code:error.code}});
  const code=error instanceof Error&&/^[A-Z0-9_]{3,100}$/.test(error.message)?error.message:"ALLOWANCE_RESULT_OR_STORAGE_UNKNOWN";
  return reply(code.includes("NOT_FOUND")?404:409,{error:{code}});
 }
}
