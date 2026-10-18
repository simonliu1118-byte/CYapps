// CYInvoice V3.1-B2: non-allowance Work Item reports and manual review.
// No official AMEGO operation or invoice-status mutation.
// Both the schema16 and feature flag gates are OFF in the existing Worker.
import {cyIdDevice,cyIdBinding,usesCyId,CyIdError,type CyIdEnv} from "./cyid";
import {getWorkItem,listWorkItems,createWorkItem,reportUploadIssue,
  transitionWorkItem,uploadIssueCodes,uploadResolutionCodes} from "./work-items";
interface Env extends CyIdEnv { DB:D1Database; SCHEMA_VERSION:string; WORK_ITEMS_ENABLED?:string }
const paths=new Set(["/v1/work-items/get","/v1/work-items/list",
  "/v1/work-items/create","/v1/work-items/report-upload-issue",
  "/v1/work-items/transition"]);
const response=(status:number,body:Record<string,unknown>)=>Response.json(
  {ok:status<300,...body},{status,headers:{"cache-control":"no-store","x-content-type-options":"nosniff"}});
const validId=(x:unknown)=>typeof x==="string"&&/^[A-Za-z0-9_-]{10,100}$/.test(x);
const validHash=(x:unknown)=>typeof x==="string"&&/^[a-f0-9]{64}$/.test(x);
const validEnvironment=(x:unknown)=>x==="production"||x==="test";
const allowedFields:{[path:string]:readonly string[]}={
  "/v1/work-items/get":["itemId","employeeNo","password"],
  "/v1/work-items/list":["employeeNo","password","kind","status","limit","cursor"],
  "/v1/work-items/create":["itemId","environment","kind","referenceHash","employeeNo","password"],
  "/v1/work-items/report-upload-issue":["itemId","environment","referenceHash",
    "issueCode","employeeNo","password"],
  "/v1/work-items/transition":["itemId","expectedRevision","from","to","resolutionCode",
    "employeeNo","password"],
};
async function bodyObject(req:Request):Promise<Record<string,unknown>|null>{
  // App ingress has already checked the REAL payload length before CYID Login.
  const header=req.headers.get("content-length");
  if(header!==null&&(!/^\d+$/.test(header)||Number(header)>4096))return null;
  const txt=await req.text();
  if(new TextEncoder().encode(txt).length>4096)return null;
  try {const data:unknown=JSON.parse(txt);return data&&typeof data==="object"&&!Array.isArray(data)
    ?data as Record<string,unknown>:null;}catch{return null}
}
export async function handleWorkItems(req:Request,env:Env):Promise<Response|null>{
  const path=new URL(req.url).pathname;
  if(!paths.has(path))return null;
  if(!usesCyId(env))return response(409,{error:{code:"CYID_REQUIRED"}});
  if(env.WORK_ITEMS_ENABLED!=="true"||!/^\d+$/.test(env.SCHEMA_VERSION)||
     Number(env.SCHEMA_VERSION)<16)
    return response(503,{error:{code:"WORK_ITEMS_NOT_READY"}});
  if(req.method!=="POST")return response(405,{error:{code:"METHOD_NOT_ALLOWED"}});
  if(!env.cyIdContext)return response(401,{error:{code:"EMPLOYEE_AUTH_REQUIRED"}});
  const b=await bodyObject(req);
  if(!b)return response(400,{error:{code:"INVALID_REQUEST"}});
  const fields=allowedFields[path];
  if(!Object.keys(b).every(k=>fields.includes(k)))
    return response(400,{error:{code:"INVALID_REQUEST"}});
  if(path!=="/v1/work-items/list"&&!validId(b.itemId))
    return response(400,{error:{code:"INVALID_WORK_ITEM"}});
  if(path.endsWith("/list")&&(
     (b.kind!==undefined&&!["VOID","UPLOAD_ISSUE"].includes(String(b.kind)))||
     (b.status!==undefined&&!["open","review","closed"].includes(String(b.status)))||
     (b.limit!==undefined&&(!Number.isSafeInteger(b.limit)||
       (b.limit as number)<1||(b.limit as number)>50))))
    return response(400,{error:{code:"INVALID_LIST_FILTER"}});
  let cursor:{createdAt:string;itemId:string}|null=null;
  if(b.cursor!==undefined){
    if(!b.cursor||typeof b.cursor!=="object"||Array.isArray(b.cursor))
      return response(400,{error:{code:"INVALID_LIST_CURSOR"}});
    const c=b.cursor as Record<string,unknown>;
    if(Object.keys(c).length!==2||!Object.keys(c).every(k=>k==="createdAt"||k==="itemId")||
       typeof c.createdAt!=="string"||
       !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$/.test(c.createdAt)||
       !validId(c.itemId))
      return response(400,{error:{code:"INVALID_LIST_CURSOR"}});
    cursor={createdAt:c.createdAt,itemId:c.itemId as string};
  }
  try{
    const binding=cyIdBinding(env);
    const device=await cyIdDevice(req,env);
    const state=await env.DB.prepare(
      "SELECT employee_authority_state FROM devices WHERE device_id=?1 AND workspace_id=?2 AND status='active'"
    ).bind(device.device_id,device.workspace_id).first<{employee_authority_state:string}>();
    if(device.workspace_id!==binding.workspaceId||state?.employee_authority_state!=="cloud")
      return response(401,{error:{code:"DEVICE_INVALID"}});
    const actor=await env.cyIdContext.revalidate();
    const manager=actor.workspaceRole==="SUPER_ADMIN"||actor.workspaceRole==="ADMIN";
    if(path==="/v1/work-items/create"||path==="/v1/work-items/report-upload-issue"||
       path==="/v1/work-items/transition"){
      if(path==="/v1/work-items/transition"&&!manager)
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(path==="/v1/work-items/create"&&b.kind!=="VOID")
        return response(b.kind==="ALLOWANCE"||b.kind==="ALLOWANCE_VOID"?409:400,
          {error:{code:b.kind==="ALLOWANCE"||b.kind==="ALLOWANCE_VOID"
            ?"ALLOWANCE_RESERVED_FOR_3_2":"INVALID_WORK_ITEM"}});
      if((path==="/v1/work-items/create"||path==="/v1/work-items/report-upload-issue")&&
         (!validEnvironment(b.environment)||!validHash(b.referenceHash)))
        return response(400,{error:{code:"INVALID_WORK_ITEM"}});
      if(path==="/v1/work-items/report-upload-issue"&&
         !uploadIssueCodes.includes(b.issueCode as typeof uploadIssueCodes[number]))
        return response(400,{error:{code:"INVALID_UPLOAD_ISSUE"}});
      if(path==="/v1/work-items/transition"&&
         (!Number.isSafeInteger(b.expectedRevision)||(b.expectedRevision as number)<1||
          !((b.from==="open"&&b.to==="review")||(b.from==="review"&&b.to==="closed"))||
          (b.resolutionCode!==undefined&&
           !uploadResolutionCodes.includes(b.resolutionCode as typeof uploadResolutionCodes[number]))))
        return response(400,{error:{code:"INVALID_TRANSITION"}});
      const recent=await env.cyIdContext.revalidate();
      if(recent.employeeId!==actor.employeeId||recent.workspaceRole!==actor.workspaceRole)
        return response(409,{error:{code:"AUTHORITY_CHANGED"}});
      await cyIdDevice(req,env);
      // Every D1 write independently fences active Device, Workspace and CYID cloud state.
      let changed:boolean;
      try{
        if(path==="/v1/work-items/create")
          changed=await createWorkItem(env.DB,{
            itemId:b.itemId as string,workspaceId:binding.workspaceId,
            deviceId:device.device_id,actorId:recent.employeeId,kind:"VOID",
            environment:b.environment as "production"|"test",referenceHash:b.referenceHash as string
          });
        else if(path==="/v1/work-items/report-upload-issue")
          changed=await reportUploadIssue(env.DB,{
            itemId:b.itemId as string,workspaceId:binding.workspaceId,
            deviceId:device.device_id,actorId:recent.employeeId,
            environment:b.environment as "production"|"test",
            referenceHash:b.referenceHash as string,
            issueCode:b.issueCode as typeof uploadIssueCodes[number]
          });
        else
          changed=await transitionWorkItem(env.DB,{
            itemId:b.itemId as string,workspaceId:binding.workspaceId,
            deviceId:device.device_id,actorId:recent.employeeId,
            expectedRevision:b.expectedRevision as number,
            from:b.from as "open"|"review",to:b.to as "review"|"closed",
            resolutionCode:b.resolutionCode as typeof uploadResolutionCodes[number]|undefined
          });
      }catch(error){
        if(error instanceof Error&&/UNIQUE constraint failed/i.test(error.message))
          return response(409,{error:{code:"WORK_ITEM_CONFLICT"}});
        if(error instanceof Error&&/^INVALID_WORK_ITEM/.test(error.message))
          return response(400,{error:{code:"INVALID_REQUEST"}});
        throw error;
      }
      if(!changed)return response(409,{error:{code:"WORK_ITEM_CONFLICT"}});
      return response(200,{itemId:b.itemId,recorded:true,
        officialVerified:false,providerActionPerformed:false});
    }
    if(!manager)
      return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
    const result=path.endsWith("/get")
      ?await getWorkItem(env.DB,binding.workspaceId,b.itemId as string)
      :await listWorkItems(env.DB,binding.workspaceId,{
        limit:b.limit===undefined?20:b.limit as number,
        kind:b.kind as "VOID"|"UPLOAD_ISSUE"|undefined,
        status:b.status as "open"|"review"|"closed"|undefined,
        beforeCreatedAt:cursor?.createdAt,beforeItemId:cursor?.itemId
      });
    const after=await env.cyIdContext.revalidate();
    if(after.employeeId!==actor.employeeId||
       !["SUPER_ADMIN","ADMIN"].includes(after.workspaceRole))
      return response(409,{error:{code:"AUTHORITY_CHANGED"}});
    await cyIdDevice(req,env); // revoked while query was in flight
    return path.endsWith("/get")
      ?(result?response(200,{item:result}):response(404,{error:{code:"NOT_FOUND"}}))
      :response(200,result as Record<string,unknown>);
  }catch(error){
    if(error instanceof CyIdError)
      return response(error.status,{error:{code:error.code}});
    if(error instanceof Error&&/no such table: work_items/i.test(error.message))
      return response(503,{error:{code:"WORK_ITEMS_NOT_READY"}});
    // Cloud/CYID result unknown: never send partial sensitive manager data.
    return response(503,{error:{code:path==="/v1/work-items/get"||path==="/v1/work-items/list"
      ?"WORK_ITEM_READ_UNAVAILABLE":"WORK_ITEM_RESULT_UNKNOWN"}});
  }
}
