// V3 DRAFT: protected CYID allowance application transport.
// No official AMEGO call, approval, allowance numbering or execution route.
// Worker app.ts must wrap these endpoints in app-scoped withCyIdSession.
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {createFrozenAllowanceApplication} from "./allowance-applications";

interface Env extends CyIdEnv {
  DB:D1Database;
  COMPANY_SETTINGS_KEY_HEX?:string; // secret, not public Wrangler vars
  ALLOWANCE_OPERATION_ENVIRONMENT?:string; // 'test' or 'production' from deploy gate
}
const paths=new Set(["/v1/allowance-applications/create","/v1/allowance-applications/get",
  "/v1/allowance-applications/list"]);
const itemPattern=/^[A-Za-z0-9_-]{10,100}$/;
const response=(status:number,body:Record<string,unknown>)=>Response.json(
  {ok:status<300,...body},{status,headers:{"cache-control":"no-store",
  "x-content-type-options":"nosniff"}});
type Device={device_id:string,workspace_id:string,employee_authority_state:string};
type Metadata={
 work_item_id:string,workspace_id:string,environment:string,work_status:string,work_revision:number,
 original_invoice_number:string,allowance_day:string,requested_gross_amount:number,
 application_reason:string,applicant_employee_id:string,seller_invoice:string,created_at:string
};
type Action = "create"|"get"|"list";
function validBody(body:unknown,action:Action):body is Record<string,unknown>{
  if(!body||typeof body!=="object"||Array.isArray(body))return false;
  const val=body as Record<string,unknown>;
  const allowed=action==="create"
    ? new Set(["employeeNo","password","itemId","originalInvoiceNumber","allowanceDay",
      "requestedGrossAmount","reason","unnumberedTemplateJson"])
    : action==="get" ? new Set(["employeeNo","password","itemId"])
    : new Set(["employeeNo","password","limit","cursorCreatedAt","cursorItemId"]);
  if(!Object.keys(val).every(k=>allowed.has(k)))return false;
  if(action==="list"){
    const hasCreated=val.cursorCreatedAt!==undefined,hasId=val.cursorItemId!==undefined;
    return hasCreated===hasId && (!hasCreated ||
      (typeof val.cursorCreatedAt==="string" && !Number.isNaN(Date.parse(val.cursorCreatedAt)) &&
        typeof val.cursorItemId==="string" && itemPattern.test(val.cursorItemId)));
  }
  return typeof val.itemId==="string"&&itemPattern.test(val.itemId);
}
async function body(request:Request,action:Action){
  if(Number(request.headers.get("content-length")||"0")>45000)return null;
  const text=await request.text();
  if(text.length>45000)return null;
  try {const json:unknown=JSON.parse(text);return validBody(json,action)?json:null;}
  catch{return null;}
}
function principal(env:Env,person:CyIdPrincipal):boolean{
  const b=cyIdBinding(env);
  return person.workspaceId===b.identityWorkspaceId && person.emailVerified===true
    && !!person.employeeId && person.employeeId.length<=100
    && ["SUPER_ADMIN","ADMIN","USER"].includes(person.workspaceRole);
}
function authority(env:Env){
  return {revalidate:async()=>{
    const p=await env.cyIdContext?.revalidate();
    if(!p||!principal(env,p))return null;
    // CYID's principal.workspaceId is IDENTITY Workspace, not Business D1.
    // Only binding, never browser JSON, maps it to CYInvoice Business Workspace.
    return {workspaceId:cyIdBinding(env).workspaceId,
      employeeId:p.employeeId,workspaceRole:p.workspaceRole};
  }};
}
async function dev(request:Request,env:Env):Promise<Device|null>{
  const v=await cyIdDevice(request,env);
  const d=await env.DB.prepare("SELECT device_id,workspace_id,employee_authority_state "+
    "FROM devices WHERE device_id=?1 AND workspace_id=?2 AND status='active'")
    .bind(v.device_id,v.workspace_id).first<Device>();
  return d && d.employee_authority_state==="cloud" ? d : null;
}
export async function handleAllowanceApplications(request:Request,env:Env):Promise<Response|null>{
  const path=new URL(request.url).pathname;
  if(!paths.has(path))return null;
  if(!usesCyId(env))return response(409,{error:{code:"CYID_REQUIRED"}});
  if(request.method!=="POST")return response(405,{error:{code:"METHOD_NOT_ALLOWED"}});
  // Defense in depth; direct dispatch without withCyIdSession cannot enter.
  if(!env.cyIdContext)return response(401,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
  const action=path.endsWith("/create")?"create":path.endsWith("/list")?"list":"get";
  const input=await body(request,action);
  if(!input)return response(400,{error:{code:"INVALID_REQUEST"}});
  try {
    const d=await dev(request,env);
    if(!d || d.workspace_id!==cyIdBinding(env).workspaceId)
      return response(401,{error:{code:"DEVICE_INVALID"}});
    const actor=await authority(env).revalidate();
    if(!actor)return response(403,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
    if(action==="list"){
      if(actor.workspaceRole==="USER")return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(env.ALLOWANCE_OPERATION_ENVIRONMENT!=="test" &&
         env.ALLOWANCE_OPERATION_ENVIRONMENT!=="production")
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      const limit=input.limit===undefined?20:Number(input.limit);
      if(!Number.isSafeInteger(limit)||limit<1||limit>50)
        return response(400,{error:{code:"INVALID_PAGINATION"}});
      const cursorCreated=input.cursorCreatedAt===undefined?null:input.cursorCreatedAt as string;
      const cursorId=input.cursorItemId===undefined?null:input.cursorItemId as string;
      // Stable cursor: creation UTC timestamp + ID, not offset. No enumeration
      // outside the bound business Workspace and runtime environment.
      const page=await env.DB.prepare(
        "SELECT a.work_item_id,a.workspace_id,a.environment,a.original_invoice_number,"+
        "a.allowance_day,a.requested_gross_amount,a.application_reason,"+
        "a.applicant_employee_id,a.seller_invoice,a.created_at,"+
        "wi.status AS work_status,wi.revision AS work_revision "+
        "FROM allowance_applications a JOIN work_items wi "+
        "ON wi.work_item_id=a.work_item_id AND wi.workspace_id=a.workspace_id "+
        "WHERE a.workspace_id=?1 AND a.environment=?2 AND wi.kind='ALLOWANCE' "+
        "AND wi.status IN ('open','review') AND wi.reference_hash=a.reference_hash "+
        "AND (?3 IS NULL OR wi.created_at<?3 OR (wi.created_at=?3 AND wi.work_item_id<?4)) "+
        "ORDER BY wi.created_at DESC, wi.work_item_id DESC LIMIT ?5")
        .bind(d.workspace_id,env.ALLOWANCE_OPERATION_ENVIRONMENT,cursorCreated,cursorId,limit+1)
        .all<Metadata>();
      const again=await authority(env).revalidate();
      if(!again||again.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(again.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      const items=page.results??[];
      const visible=items.slice(0,limit);
      const last=visible.at(-1);
      return response(200,{applications:visible.map(item=>({
        itemId:item.work_item_id,environment:item.environment,status:item.work_status,
        revision:item.work_revision,originalInvoiceNumber:item.original_invoice_number,
        allowanceDay:item.allowance_day,requestedGrossAmount:item.requested_gross_amount,
        reason:item.application_reason,applicantEmployeeId:item.applicant_employee_id,
        sellerInvoice:item.seller_invoice,createdAt:item.created_at
      })),nextCursor:items.length>limit&&last ? {
        cursorCreatedAt:last.created_at,cursorItemId:last.work_item_id
      }:null});
    }
    if(action==="get"){
      const row=await env.DB.prepare(
        "SELECT a.work_item_id,a.workspace_id,a.environment,a.original_invoice_number,"+
        "a.allowance_day,a.requested_gross_amount,a.application_reason,"+
        "a.applicant_employee_id,a.seller_invoice,a.created_at,"+
        "wi.status AS work_status,wi.revision AS work_revision "+
        "FROM allowance_applications a JOIN work_items wi "+
        "ON wi.work_item_id=a.work_item_id AND wi.workspace_id=a.workspace_id "+
        "WHERE a.workspace_id=?1 AND a.work_item_id=?2 AND wi.kind='ALLOWANCE' "+
        "AND wi.reference_hash=a.reference_hash")
        .bind(d.workspace_id,input.itemId).first<Metadata>();
      if(!row || (actor.workspaceRole==="USER" && row.applicant_employee_id!==actor.employeeId))
        return response(404,{error:{code:"NOT_FOUND"}});
      // Don't return stale metadata after an actor's authority was revoked.
      const again=await authority(env).revalidate();
      if(!again || again.employeeId!==actor.employeeId ||
         (again.workspaceRole==="USER" && row.applicant_employee_id!==again.employeeId))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      return response(200,{application:{
        itemId:row.work_item_id,environment:row.environment,status:row.work_status,
        revision:row.work_revision,originalInvoiceNumber:row.original_invoice_number,
        allowanceDay:row.allowance_day,requestedGrossAmount:row.requested_gross_amount,
        reason:row.application_reason,applicantEmployeeId:row.applicant_employee_id,
        sellerInvoice:row.seller_invoice,createdAt:row.created_at
      }});
    }
    if(env.ALLOWANCE_OPERATION_ENVIRONMENT!=="test"&&
       env.ALLOWANCE_OPERATION_ENVIRONMENT!=="production")
      return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
    if(typeof env.COMPANY_SETTINGS_KEY_HEX!=="string"||
       !/^[0-9a-f]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX))
      return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
    if(typeof input.originalInvoiceNumber!=="string"||
       typeof input.allowanceDay!=="string"||
       typeof input.reason!=="string"||
       typeof input.unnumberedTemplateJson!=="string"||
       !Number.isSafeInteger(input.requestedGrossAmount))
      return response(400,{error:{code:"INVALID_APPLICATION"}});
    const created=await createFrozenAllowanceApplication(env.DB,{
      workspaceId:d.workspace_id,deviceId:d.device_id,workItemId:input.itemId as string,
      originalInvoiceNumber:input.originalInvoiceNumber,
      allowanceDay:input.allowanceDay,
      requestedGrossAmount:input.requestedGrossAmount as number,
      reason:input.reason,
      unnumberedTemplateJson:input.unnumberedTemplateJson
    },authority(env),{environment:env.ALLOWANCE_OPERATION_ENVIRONMENT,
      companyEncryptionKeyHex:env.COMPANY_SETTINGS_KEY_HEX});
    // Return no full invoice content, company key, password, ciphertext or payload.
    return response(201,{itemId:created.workItemId,status:"open",revision:1});
  } catch(error) {
    if(error instanceof CyIdError)
      return response(error.status,{error:{code:error.code}});
    if(error instanceof Error){
      if(/^ALLOWANCE_(APPLICATION_INVALID|APPLICATION_DATE_INVALID|APPLICATION_SCOPE_INVALID|TEMPLATE_INVALID|TEMPLATE_MISMATCH)$/.test(error.message))
        return response(400,{error:{code:"INVALID_APPLICATION"}});
      if(/ALLOWANCE_(APPLICANT_AUTH_REQUIRED|APPLICANT_AUTH_CHANGED)/.test(error.message))
        return response(403,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
      if(/ALLOWANCE_(COMPANY_NOT_CONFIGURED|APPLICATION_COMPANY_CHANGED)/.test(error.message)||
         /no such table: (allowance_applications|company_settings|work_items)/i.test(error.message))
        return response(503,{error:{code:"ALLOWANCE_NOT_READY"}});
      if(/UNIQUE constraint failed/i.test(error.message))
        return response(409,{error:{code:"APPLICATION_CONFLICT_READBACK_REQUIRED"}});
      if(error.message==="ALLOWANCE_APPLICATION_UNKNOWN_READBACK_REQUIRED"||
         error.message==="ALLOWANCE_APPLICATION_REVOKED_OR_CONFLICT")
        return response(409,{error:{code:"APPLICATION_RESULT_UNKNOWN_READBACK_REQUIRED"}});
    }
    // Never echo user content, SQL, credentials, or AMEGO data in public errors.
    return response(503,{error:{code:"APPLICATION_RESULT_UNKNOWN"}});
  }
}
