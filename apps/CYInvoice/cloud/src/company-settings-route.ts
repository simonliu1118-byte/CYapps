// CYID company settings. Status/Update never disclose secret values;
// automatic sync uses the original active Device and current CYID app/workspace
// access. Changed secrets travel over HTTPS for immediate Windows DPAPI storage.
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,
  type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {getCompanySettingsStatus,changeCompanySettings,synchronizeCompanySettingsToDevice,
  type SecretChange} from "./company-settings";
interface Env extends CyIdEnv { DB:D1Database; COMPANY_SETTINGS_KEY_HEX?:string }
const paths=new Set(["/v1/company-settings/get","/v1/company-settings/update",
  "/v1/company-settings/sync"]);
const response=(status:number,body:Record<string,unknown>)=>Response.json(
  {ok:status<300,...body},{status,headers:{"cache-control":"no-store",
  "x-content-type-options":"nosniff"}});
function change(v:unknown):v is SecretChange{
  if(!v||typeof v!=="object"||Array.isArray(v))return false;
  const q=v as Record<string,unknown>,keys=Object.keys(q);
  if(q.action==="replace")
    return keys.length===2&&keys.includes("value")&&
      typeof q.value==="string"&&q.value.length>0&&q.value.length<=512&&
      !/[\u0000-\u001f\u007f]/u.test(q.value);
  return (q.action==="clear"||q.action==="keep")&&keys.length===1;
}
async function parse(request:Request,action:"get"|"update"|"sync"):Promise<Record<string,unknown>|null>{
  if(Number(request.headers.get("content-length")||"0")>4096)return null;
  const reader=request.body?.getReader();
  if(!reader)return null;
  const chunks:Uint8Array[]=[];let total=0;
  try{
    for(;;){const next=await reader.read();if(next.done)break;
      total+=next.value.byteLength;if(total>4096){await reader.cancel();return null;}chunks.push(next.value);}
  }finally{reader.releaseLock();}
  const bytes=new Uint8Array(total);let offset=0;
  for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.byteLength;}
  const raw=new TextDecoder().decode(bytes);
  if(raw.length>4096)return null;
  let data:unknown;
  try{data=JSON.parse(raw);}catch{return null;}
  if(!data||typeof data!=="object"||Array.isArray(data))return null;
  const obj=data as Record<string,unknown>;
  const keys=action==="sync" ? ["knownRevision"] : action!=="update" ? ["employeeNo","password"] :
    ["employeeNo","password","expectedRevision","sellerInvoice","amegoAppKey","moExcelPassword","companyName"];
  if(!Object.keys(obj).every(k=>keys.includes(k)))return null;
  if(action==="sync"&&(!Number.isSafeInteger(obj.knownRevision)||Number(obj.knownRevision)<0))return null;
  if(action==="update"&&(!Number.isSafeInteger(obj.expectedRevision)||
      typeof obj.sellerInvoice!=="string"||!change(obj.amegoAppKey)||!change(obj.moExcelPassword)))
    return null;
  return obj;
}
function currentPrincipal(env:Env,p:CyIdPrincipal|null){
  const b=cyIdBinding(env);
  if(!p||p.workspaceId!==b.identityWorkspaceId||!p.emailVerified||
    !p.employeeId||p.employeeId.length>100)return null;
  return {workspaceId:b.workspaceId,employeeId:p.employeeId,
    workspaceRole:p.workspaceRole,isWorkspaceSuperAdmin:p.isWorkspaceSuperAdmin,
    emailVerified:p.emailVerified};
}
function auth(env:Env){
  return {revalidate:async()=>currentPrincipal(env,
    (await env.cyIdContext?.revalidate())??null)};
}
export async function handleCompanySettings(request:Request,env:Env):Promise<Response|null>{
  const url=new URL(request.url).pathname;
  if(!paths.has(url))return null;
  if(!usesCyId(env))return response(409,{error:{code:"CYID_REQUIRED"}});
  if(request.method!=="POST")return response(405,{error:{code:"METHOD_NOT_ALLOWED"}});
  if(!url.endsWith("/sync")&&!env.cyIdContext)return response(401,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
  const action=url.endsWith("/update")?"update":
    url.endsWith("/sync")?"sync":"get";
  const input=await parse(request,action);
  if(!input)return response(400,{error:{code:"INVALID_REQUEST"}});
  try {
    const b=cyIdBinding(env);
    const d=await cyIdDevice(request,env);
    if(d.workspace_id!==b.workspaceId)return response(401,{error:{code:"DEVICE_INVALID"}});
    const proof=await env.DB.prepare(
      "SELECT employee_authority_state FROM devices WHERE device_id=?1 AND workspace_id=?2 AND status='active'"
    ).bind(d.device_id,d.workspace_id).first<{employee_authority_state:string}>();
    if(proof?.employee_authority_state!=="cloud")
      return response(401,{error:{code:"DEVICE_INVALID"}});
    if(action==="sync"){
      const authorize=async()=>{
        await cyIdDevice(request,env);
        if(!env.IDENTITY_AUTHORITY)throw new CyIdError(503,"IDENTITY_CONFIGURATION_INVALID");
        const access=await env.IDENTITY_AUTHORITY.invalidateCache({workspaceId:b.identityWorkspaceId,
          applicationId:b.applicationId,authorities:[]}) as Record<string,unknown>;
        if(!access||access.workspaceId!==b.identityWorkspaceId||access.applicationId!==b.applicationId||
          access.available!==true)throw new CyIdError(403,"ACCESS_DENIED");
      };
      const result=await synchronizeCompanySettingsToDevice(env.DB,b.workspaceId,d.device_id,
        input.knownRevision as number,env.COMPANY_SETTINGS_KEY_HEX??"",authorize);
      return response(200,result);
    }
    const actor=await auth(env).revalidate();
    if(!actor)return response(403,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
    if(action==="get"){
      const result=await getCompanySettingsStatus(env.DB,b.workspaceId,auth(env));
      const after=await auth(env).revalidate();
      if(!after||after.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(after.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      return response(200,{company:result});
    }
    if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
      return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
    const result=await changeCompanySettings(env.DB,{
      workspaceId:b.workspaceId,deviceId:d.device_id,
      expectedRevision:input.expectedRevision as number,
      sellerInvoice:input.sellerInvoice as string,
      companyName:input.companyName as string|undefined,
      amegoAppKey:input.amegoAppKey as SecretChange,
      moExcelPassword:input.moExcelPassword as SecretChange,
    },auth(env),env.COMPANY_SETTINGS_KEY_HEX!);
    return response(200,{company:result});
  }catch(error){
    if(error instanceof CyIdError)return response(error.status,{error:{code:error.code}});
    if(error instanceof Error){
      if(["COMPANY_SUPER_ADMIN_REQUIRED","COMPANY_STATUS_AUTH_REQUIRED","COMPANY_AUTHORITY_CHANGED"].includes(error.message))
        return response(403,{error:{code:"COMPANY_AUTH_REQUIRED"}});
      if(["COMPANY_SCOPE_INVALID","COMPANY_SETTINGS_INVALID"].includes(error.message))
        return response(400,{error:{code:"INVALID_COMPANY_SETTINGS"}});
      if(error.message==="COMPANY_PROVISION_INCOMPLETE")
        return response(409,{error:{code:"COMPANY_PROVISION_INCOMPLETE"}});
      if(error.message==="COMPANY_DEVICE_REVOKED")
        return response(401,{error:{code:"DEVICE_INVALID"}});
      if(error.message==="COMPANY_PROVISION_CHANGED")
        return response(409,{error:{code:"COMPANY_CONFLICT_READBACK_REQUIRED"}});
      if(["COMPANY_NO_CHANGES","COMPANY_SETTINGS_STALE_REVISION","COMPANY_SETTINGS_CONFLICT_OR_DEVICE_REVOKED"].includes(error.message))
        return response(409,{error:{code:"COMPANY_CONFLICT_READBACK_REQUIRED"}});
      if(error.message==="COMPANY_MUTATION_UNKNOWN_READBACK_REQUIRED")
        return response(503,{error:{code:"COMPANY_RESULT_UNKNOWN_READBACK_REQUIRED"}});
      if(/no such table: (company_settings|company_settings_audit_events)/i.test(error.message))
        return response(503,{error:{code:"COMPANY_SETTINGS_NOT_READY"}});
    }
    return response(503,{error:{code:"COMPANY_SETTINGS_UNAVAILABLE"}});
  }
}
