// V3 DRAFT: CYID-protected exceptional ALLOWANCE_VOID create/get/list.
// Official g0501 is deliberately NOT reachable from these endpoints.
// Only server-bound Workspace / environment / company settings are trusted.
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,
  type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {validOfficialAllowanceNumber} from "./amego-allowance-void";
import {managerAllowanceVoidAction,type VoidManagerAction} from "./allowance-void-manager";

interface Env extends CyIdEnv {
  DB:D1Database;
  ALLOWANCE_OPERATION_ENVIRONMENT?:string;
}
type Device={device_id:string;workspace_id:string;employee_authority_state:string};
type Actor={workspaceId:string;employeeId:string;workspaceRole:"SUPER_ADMIN"|"ADMIN"|"USER"};
const paths=new Set([
 "/v1/allowance-void-applications/create",
 "/v1/allowance-void-applications/get",
 "/v1/allowance-void-applications/list",
 "/v1/allowance-void-applications/approve",
 "/v1/allowance-void-applications/send-once",
 "/v1/allowance-void-applications/status-readback",
 "/v1/allowance-void-applications/close"
]);
const idPattern=/^[A-Za-z0-9_-]{10,100}$/;
const invoicePattern=/^[A-Z]{2}\d{8}$/;
const response=(status:number,data:Record<string,unknown>)=>Response.json(
  {ok:status<300,...data},{status,headers:{"cache-control":"no-store",
    "x-content-type-options":"nosniff"}});
function validUser(p:CyIdPrincipal|null,env:Env):p is CyIdPrincipal{
  return !!p && p.workspaceId===cyIdBinding(env).identityWorkspaceId &&
    p.emailVerified===true && p.employeeId.length>=1 && p.employeeId.length<=100 &&
    ["USER","ADMIN","SUPER_ADMIN"].includes(p.workspaceRole);
}
async function activeActor(env:Env):Promise<Actor|null>{
  const p=await env.cyIdContext?.revalidate()??null;
  if(!validUser(p,env))return null;
  return {workspaceId:cyIdBinding(env).workspaceId,employeeId:p.employeeId,
    workspaceRole:p.workspaceRole};
}
const manager=(a:Actor)=>a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN";
async function activeDevice(request:Request,env:Env):Promise<Device|null>{
  const token=await cyIdDevice(request,env);
  const d=await env.DB.prepare(
    "SELECT d.device_id,d.workspace_id,d.employee_authority_state "+
    "FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id "+
    "WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active' AND w.status='active'"
  ).bind(token.device_id,token.workspace_id).first<Device>();
  return d?.employee_authority_state==="cloud" ? d:null;
}
async function inputBody(request:Request):Promise<Record<string,unknown>|null>{
  if(Number(request.headers.get("content-length")||0)>8192)return null;
  const text=await request.text();
  if(text.length>8192)return null;
  try{
    const parsed:unknown=JSON.parse(text);
    return parsed&&typeof parsed==="object"&&!Array.isArray(parsed)
      ?parsed as Record<string,unknown>:null;
  }catch{return null;}
}
async function sha256(value:string){
  const h=await crypto.subtle.digest("SHA-256",new TextEncoder().encode(value));
  return Array.from(new Uint8Array(h),x=>x.toString(16).padStart(2,"0")).join("");
}
type Company={seller_invoice:string;revision:number};
type VoidRow={
 work_item_id:string;workspace_id:string;environment:string;
 allowance_number:string;original_invoice_number:string;reason:string;
 applicant_employee_id:string;seller_invoice:string;created_at:string;
 work_revision:number;work_status:string;reference_hash:string;
};
function present(row:VoidRow){
  return {itemId:row.work_item_id,environment:row.environment,
    allowanceNumber:row.allowance_number,
    originalInvoiceNumber:row.original_invoice_number,
    reason:row.reason,applicantEmployeeId:row.applicant_employee_id,
    sellerInvoice:row.seller_invoice,createdAt:row.created_at,
    revision:row.work_revision,status:row.work_status};
}
const select="SELECT r.work_item_id,r.workspace_id,r.environment,r.allowance_number,"+
  "r.original_invoice_number,r.reason,r.applicant_employee_id,r.seller_invoice,"+
  "r.created_at,r.reference_hash,wi.status AS work_status,wi.revision AS work_revision "+
  "FROM allowance_void_requests r JOIN work_items wi "+
  "ON wi.work_item_id=r.work_item_id AND wi.workspace_id=r.workspace_id "+
  "WHERE wi.kind='ALLOWANCE_VOID' AND wi.reference_hash=r.reference_hash "+
  "AND r.workspace_id=?1 AND r.environment=?2 ";

export async function handleAllowanceVoidApplications(
  request:Request,env:Env
):Promise<Response|null>{
  const path=new URL(request.url).pathname;
  if(!paths.has(path))return null;
  if(!usesCyId(env))return response(409,{error:{code:"CYID_REQUIRED"}});
  if(request.method!=="POST")return response(405,{error:{code:"METHOD_NOT_ALLOWED"}});
  if(!env.cyIdContext)return response(401,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
  const input=await inputBody(request);
  if(!input)return response(400,{error:{code:"INVALID_REQUEST"}});
  const action=path.slice(path.lastIndexOf("/")+1);
  const allowed=action==="create"?
    ["employeeNo","password","itemId","allowanceNumber","originalInvoiceNumber","reason"]:
    action==="get"?["employeeNo","password","itemId"]:
    action==="list"?["employeeNo","password","limit","cursorCreatedAt","cursorItemId"]:
    ["employeeNo","password","itemId","expectedRevision"];
  if(!Object.keys(input).every(k=>allowed.includes(k)))
    return response(400,{error:{code:"INVALID_REQUEST"}});
  const environment=env.ALLOWANCE_OPERATION_ENVIRONMENT;
  if(environment!=="test"&&environment!=="production")
    return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
  try{
    const binding=cyIdBinding(env);
    const d=await activeDevice(request,env);
    if(!d||d.workspace_id!==binding.workspaceId)
      return response(401,{error:{code:"DEVICE_INVALID"}});
    const a=await activeActor(env);
    if(!a)return response(403,{error:{code:"AUTHORITY_REQUIRED"}});
    if(["approve","send-once","status-readback","close"].includes(action)){
      if(!manager(a))return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(typeof input.itemId!=="string"||!idPattern.test(input.itemId)||
         !Number.isSafeInteger(input.expectedRevision)||
         Number(input.expectedRevision)<1)
        return response(400,{error:{code:"INVALID_REQUEST"}});
      if(!/^[0-9a-fA-F]{64}$/.test(
        (env as Env & {COMPANY_SETTINGS_KEY_HEX?:string}).COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      const authority={revalidate:async()=>{
        const latest=await activeActor(env);
        return latest?{workspaceId:latest.workspaceId,employeeId:latest.employeeId,
          role:latest.workspaceRole}:null;
      }};
      const result=await managerAllowanceVoidAction(action as VoidManagerAction,
        env.DB,{workspaceId:binding.workspaceId,workItemId:input.itemId,
          deviceId:d.device_id,expectedRevision:Number(input.expectedRevision),
          environment,encryptionKeyHex:(env as Env & {
            COMPANY_SETTINGS_KEY_HEX?:string}).COMPANY_SETTINGS_KEY_HEX!,
          employeeId:a.employeeId},authority);
      return response(action==="approve"?201:action==="send-once"?202:200,result);
    }
    if(action==="create"){
      if(typeof input.itemId!=="string"||!idPattern.test(input.itemId)||
         !validOfficialAllowanceNumber(input.allowanceNumber)||
         typeof input.originalInvoiceNumber!=="string"||
         !invoicePattern.test(input.originalInvoiceNumber)||
         typeof input.reason!=="string"||
         input.reason.trim()!==input.reason||
         input.reason.length<1||input.reason.length>500||
         /[\u0000-\u001f\u007f]/u.test(input.reason))
        return response(400,{error:{code:"INVALID_VOID_APPLICATION"}});
      const company=await env.DB.prepare(
        "SELECT seller_invoice,revision FROM company_settings "+
        "WHERE workspace_id=?1 AND amego_app_key_ciphertext IS NOT NULL"
      ).bind(binding.workspaceId).first<Company>();
      if(!company)
        return response(503,{error:{code:"COMPANY_SETTINGS_MISSING"}});
      const created=new Date().toISOString();
      const ref=await sha256(JSON.stringify([
        "ALLOWANCE_VOID_V1",binding.workspaceId,environment,company.seller_invoice,
        input.allowanceNumber,input.originalInvoiceNumber]));
      const again=await activeActor(env);
      if(!again||again.employeeId!==a.employeeId)
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      const stmts=[
        env.DB.prepare(
          "INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,"+
          "updated_by_device_id,environment,kind,reference_hash,status,revision,"+
          "actor_id,created_at,updated_at) "+
          "SELECT ?1,?2,?3,?3,?4,'ALLOWANCE_VOID',?5,'open',1,?6,?7,?7 "+
          "WHERE EXISTS(SELECT 1 FROM devices d JOIN workspaces w "+
          "ON w.workspace_id=d.workspace_id WHERE d.device_id=?3 "+
          "AND d.workspace_id=?2 AND d.status='active' "+
          "AND d.employee_authority_state='cloud' AND w.status='active')"
        ).bind(input.itemId,binding.workspaceId,d.device_id,environment,ref,
          a.employeeId,created),
        env.DB.prepare(
          "INSERT INTO allowance_void_requests(work_item_id,workspace_id,"+
          "seller_invoice,company_settings_revision,environment,allowance_number,"+
          "original_invoice_number,reason,applicant_employee_id,"+
          "applicant_device_id,reference_hash,created_at) "+
          "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12)"
        ).bind(input.itemId,binding.workspaceId,company.seller_invoice,
          company.revision,environment,input.allowanceNumber,
          input.originalInvoiceNumber,input.reason,a.employeeId,
          d.device_id,ref,created)
      ];
      const changed=await env.DB.batch(stmts);
      if(changed.length!==2||changed.some(v=>v.meta.changes!==1))
        return response(409,{error:{code:"APPLICATION_NOT_CREATED_READBACK_REQUIRED"}});
      const after=await activeActor(env);
      if(!after||after.employeeId!==a.employeeId)
        return response(409,{error:{code:"APPLICATION_RESULT_UNKNOWN_READBACK_REQUIRED"}});
      return response(201,{itemId:input.itemId,revision:1,status:"open"});
    }
    if(action==="get"){
      if(typeof input.itemId!=="string"||!idPattern.test(input.itemId))
        return response(400,{error:{code:"INVALID_REQUEST"}});
      const row=await env.DB.prepare(select+"AND r.work_item_id=?3 LIMIT 1")
        .bind(binding.workspaceId,environment,input.itemId).first<VoidRow>();
      if(!row)return response(404,{error:{code:"NOT_FOUND"}});
      if(!manager(a)&&row.applicant_employee_id!==a.employeeId)
        return response(404,{error:{code:"NOT_FOUND"}});
      const latest=await activeActor(env);
      if(!latest||latest.employeeId!==a.employeeId||
         (!manager(a)&&row.applicant_employee_id!==latest.employeeId))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      return response(200,{application:present(row)});
    }
    if(!manager(a))return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
    const limit=input.limit===undefined?20:Number(input.limit);
    if(!Number.isSafeInteger(limit)||limit<1||limit>50)
      return response(400,{error:{code:"INVALID_PAGINATION"}});
    const hasDate=input.cursorCreatedAt!==undefined;
    const hasId=input.cursorItemId!==undefined;
    if(hasDate!==hasId||(hasDate&&(
      typeof input.cursorCreatedAt!=="string"||Number.isNaN(Date.parse(input.cursorCreatedAt))||
      typeof input.cursorItemId!=="string"||!idPattern.test(input.cursorItemId))))
      return response(400,{error:{code:"INVALID_PAGINATION"}});
    const createdAt=hasDate?input.cursorCreatedAt:null;
    const itemId=hasId?input.cursorItemId:null;
    const page=await env.DB.prepare(
      select+"AND wi.status IN ('open','review') "+
      "AND (?3 IS NULL OR wi.created_at<?3 OR "+
      "(wi.created_at=?3 AND wi.work_item_id<?4)) "+
      "ORDER BY wi.created_at DESC,wi.work_item_id DESC LIMIT ?5"
    ).bind(binding.workspaceId,environment,createdAt,itemId,limit+1).all<VoidRow>();
    const latest=await activeActor(env);
    if(!latest||latest.employeeId!==a.employeeId||!manager(latest))
      return response(403,{error:{code:"AUTHORITY_CHANGED"}});
    const rows=page.results??[];
    const items=rows.slice(0,limit);
    const last=items.at(-1);
    return response(200,{applications:items.map(present),
      nextCursor:rows.length>limit&&last?
        {cursorCreatedAt:last.created_at,cursorItemId:last.work_item_id}:null});
  }catch(e){
    if(e instanceof CyIdError)return response(e.status,{error:{code:e.code}});
    if(e instanceof Error&&/^ALLOWANCE_VOID_/.test(e.message))
      return response(409,{error:{code:"ALLOWANCE_VOID_REQUIRES_RECHECK"}});
    if(e instanceof Error&&/UNIQUE constraint failed|ALLOWANCE_VOID_SOURCE_MISMATCH/i.test(e.message))
      return response(409,{error:{code:"DUPLICATE_OR_SOURCE_CHANGED"}});
    if(e instanceof Error&&/no such table: (work_items|company_settings|allowance_void_requests)/i.test(e.message))
      return response(503,{error:{code:"V3_SCHEMA_NOT_INSTALLED"}});
    return response(503,{error:{code:"ALLOWANCE_VOID_UNAVAILABLE"}});
  }
}
