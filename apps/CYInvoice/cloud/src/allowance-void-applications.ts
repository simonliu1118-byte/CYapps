// V3.2 D/E internal CYID Cloud g0501 application owner, draft schema20 only.
// Trusted Worker injects current CYID authority and Device; no public HTTP,
// manager approval, mutation, retries, offline send or deployed D1.
import {encryptCompanyBusinessData} from "./company-settings";
import type {AllowanceActor,AllowanceActorSource,CloudAllowanceContext} from "./allowance-frozen-store";
import {validOfficialAllowanceNumber} from "./amego-allowance-void";

export interface NewCloudAllowanceVoid {
 workspaceId:string;applicationId:string;deviceId:string;
 environment:"test"|"production";allowanceNumber:string;
 originalInvoiceNumber:string;reason:string;
}
export interface CloudVoidApplicationStatus {
 applicationId:string;status:"open"|"approved";revision:1|2;
 claimed:boolean;readbackOnly:boolean;
}
interface Company {seller_invoice:string;revision:number;amego_app_key_ciphertext:string|null;}
interface Stored {application_id:string;requester_actor_id:string;
 status:"open"|"approved";revision:number;claimed:number;}
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const DEV=/^dev_[0-9a-f-]{36}$/;
const INVOICE=/^[A-Z]{2}[0-9]{8}$/;
const NUMBER=/^[A-Z0-9-]{1,16}$/;
const encoder=new TextEncoder();
function validActor(a:AllowanceActor|null,ws:string):a is AllowanceActor{
 return !!a&&a.workspaceId===ws&&typeof a.employeeId==="string"&&
  a.employeeId.length>=1&&a.employeeId.length<=100&&
  ["USER","ADMIN","SUPER_ADMIN"].includes(a.workspaceRole);
}
function sameActor(a:AllowanceActor|null,b:AllowanceActor|null,ws:string):boolean{
 return validActor(a,ws)&&validActor(b,ws)&&
  a.employeeId===b.employeeId&&a.workspaceRole===b.workspaceRole;
}
function isManager(a:AllowanceActor):boolean{
 return a.workspaceRole==="ADMIN"||a.workspaceRole==="SUPER_ADMIN";
}
async function activeDevice(db:D1Database,ws:string,dev:string):Promise<boolean>{
 return !!await db.prepare("SELECT d.device_id FROM devices d "+
 "JOIN workspaces w ON w.workspace_id=d.workspace_id "+
 "WHERE w.workspace_id=?1 AND w.status='active' AND d.device_id=?2 "+
 "AND d.status='active' AND d.employee_authority_state='cloud' LIMIT 1")
 .bind(ws,dev).first<{device_id:string}>();
}
function validateTarget(ws:string,app:string,dev:string):void{
 if(!WS.test(ws)||!ID.test(app)||!DEV.test(dev))
  throw new Error("VOID_APPLICATION_SCOPE_INVALID");
}
function validateRequest(i:NewCloudAllowanceVoid,ctx:CloudAllowanceContext):void{
 if(!i)throw new Error("VOID_APPLICATION_INVALID");
 validateTarget(i.workspaceId,i.applicationId,i.deviceId);
 if(ctx.environment!==i.environment||
  (i.environment!=="test"&&i.environment!=="production")||
  !validOfficialAllowanceNumber(i.allowanceNumber)||
  !NUMBER.test(i.allowanceNumber)||!INVOICE.test(i.originalInvoiceNumber)||
  typeof i.reason!=="string"||i.reason.trim()!==i.reason||
  i.reason.length<1||i.reason.length>500||/[\u0000-\u001f\u007f]/.test(i.reason))
  throw new Error("VOID_APPLICATION_INVALID");
}
async function fingerprint(keyHex:string,purpose:string,fields:unknown[]):Promise<string>{
 if(typeof keyHex!=="string"||!/^[0-9a-f]{64}$/i.test(keyHex))
  throw new Error("VOID_APPLICATION_ENCRYPTION_REQUIRED");
 const raw=Uint8Array.from(keyHex.match(/.{2}/g)!,x=>parseInt(x,16));
 const source=await crypto.subtle.importKey("raw",raw,"HKDF",false,["deriveKey"]);
 const key=await crypto.subtle.deriveKey({name:"HKDF",hash:"SHA-256",
  salt:encoder.encode("CYInvoice/allowance-void/v1/hmac"),
  info:encoder.encode(purpose)},source,
  {name:"HMAC",hash:"SHA-256",length:256},false,["sign"]);
 const mac=await crypto.subtle.sign("HMAC",key,encoder.encode(JSON.stringify(fields)));
 return Array.from(new Uint8Array(mac),n=>n.toString(16).padStart(2,"0")).join("");
}
async function company(db:D1Database,ws:string):Promise<Company>{
 const row=await db.prepare("SELECT seller_invoice,revision,amego_app_key_ciphertext "+
 "FROM company_settings WHERE workspace_id=?1").bind(ws).first<Company>();
 if(!row||!/^\d{8}$/.test(row.seller_invoice)||
  !Number.isSafeInteger(row.revision)||row.revision<=0||
  !row.amego_app_key_ciphertext)
  throw new Error("VOID_APPLICATION_COMPANY_NOT_CONFIGURED");
 return row;
}
export async function createCloudAllowanceVoidApplication(
 db:D1Database,i:NewCloudAllowanceVoid,ctx:CloudAllowanceContext,
 authority:AllowanceActorSource
):Promise<CloudVoidApplicationStatus>{
 validateRequest(i,ctx);
 const first=await authority.revalidate();
 if(!validActor(first,i.workspaceId)||!await activeDevice(db,i.workspaceId,i.deviceId))
  throw new Error("VOID_APPLICATION_AUTH_REQUIRED");
 const c=await company(db,i.workspaceId);
 const identity=[i.workspaceId,c.seller_invoice,i.environment,i.allowanceNumber];
 const numberHash=await fingerprint(ctx.encryptionKeyHex,"number",identity);
 const requestHash=await fingerprint(ctx.encryptionKeyHex,"request",[
  ...identity,i.applicationId,i.originalInvoiceNumber,i.reason]);
 const ciphertext=await encryptCompanyBusinessData(ctx.encryptionKeyHex,i.workspaceId,
  "allowance-void/"+i.applicationId+"/"+requestHash,
  JSON.stringify({v:1,allowanceNumber:i.allowanceNumber,
   originalInvoiceNumber:i.originalInvoiceNumber,reason:i.reason}));
 const second=await authority.revalidate();
 if(!sameActor(first,second,i.workspaceId)||
  !await activeDevice(db,i.workspaceId,i.deviceId))
  throw new Error("VOID_APPLICATION_AUTH_CHANGED");
 const updated=await company(db,i.workspaceId);
 if(updated.revision!==c.revision||updated.seller_invoice!==c.seller_invoice||
  updated.amego_app_key_ciphertext!==c.amego_app_key_ciphertext)
  throw new Error("VOID_APPLICATION_COMPANY_CHANGED");
 const now=new Date().toISOString();
 try{
  const result=await db.prepare("INSERT INTO allowance_void_requests "+
   "(application_id,workspace_id,opened_by_device_id,environment,seller_invoice,"+
   "company_revision,allowance_fingerprint,request_hash,frozen_ciphertext,"+
   "requester_actor_id,created_at,updated_at) "+
   "SELECT ?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?11 "+
   "FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id "+
   "JOIN company_settings c ON c.workspace_id=w.workspace_id "+
   "WHERE w.workspace_id=?2 AND w.status='active' "+
   "AND d.device_id=?3 AND d.status='active' "+
   "AND d.employee_authority_state='cloud' "+
   "AND c.seller_invoice=?5 AND c.revision=?6 "+
   "AND c.amego_app_key_ciphertext=?12")
   .bind(i.applicationId,i.workspaceId,i.deviceId,i.environment,c.seller_invoice,
     c.revision,numberHash,requestHash,ciphertext,first.employeeId,now,
     c.amego_app_key_ciphertext).run();
  if(result.meta.changes!==1)throw new Error("VOID_APPLICATION_STALE");
 }catch{
  // Lost D1 response or duplicate number is original-ID readback only.
  throw new Error("VOID_APPLICATION_UNCONFIRMED_READBACK_OR_INVESTIGATE");
 }
 return {applicationId:i.applicationId,status:"open",revision:1,
  claimed:false,readbackOnly:false};
}
export async function readCloudAllowanceVoidApplication(
 db:D1Database,t:{workspaceId:string;applicationId:string;deviceId:string},
 authority:AllowanceActorSource
):Promise<CloudVoidApplicationStatus>{
 validateTarget(t.workspaceId,t.applicationId,t.deviceId);
 const first=await authority.revalidate();
 if(!validActor(first,t.workspaceId)||!await activeDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_APPLICATION_AUTH_REQUIRED");
 const row=await db.prepare("SELECT r.application_id,r.requester_actor_id,"+
 "r.status,r.revision,EXISTS(SELECT 1 FROM allowance_void_send_claims c "+
 "WHERE c.application_id=r.application_id AND c.workspace_id=r.workspace_id) AS claimed "+
 "FROM allowance_void_requests r WHERE r.application_id=?1 "+
 "AND r.workspace_id=?2 LIMIT 1")
 .bind(t.applicationId,t.workspaceId).first<Stored>();
 if(!row||(row.requester_actor_id!==first.employeeId&&!isManager(first)))
  throw new Error("VOID_APPLICATION_NOT_FOUND");
 const second=await authority.revalidate();
 if(!sameActor(first,second,t.workspaceId)||
  !await activeDevice(db,t.workspaceId,t.deviceId))
  throw new Error("VOID_APPLICATION_AUTH_CHANGED");
 if((row.status!=="open"&&row.status!=="approved")||
   (row.revision!==1&&row.revision!==2))
  throw new Error("VOID_APPLICATION_CORRUPT");
 return {applicationId:row.application_id,status:row.status,revision:row.revision,
  claimed:row.claimed===1,readbackOnly:row.claimed===1};
}
