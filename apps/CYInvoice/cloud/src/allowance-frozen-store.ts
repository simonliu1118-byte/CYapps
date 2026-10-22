// V3.2-C3a: INTERNAL ONLY. No Worker route, AMEGO mutation, or D1 deployment.
// Derived from PR #440 allowance-applications.ts; NOT its legacy work_items kind.
// Canonical schema16 + reviewed migrations 0017/0018 (deployment separately gated).
// Caller must be a trusted withCyIdSession/active-Device handler; never pass
// an untrusted caller-defined Principal, company key or official success bit.
import {encryptCompanyBusinessData,decryptCompanyBusinessData} from "./company-settings";

export interface AllowanceActor {
  authoritySnapshot?:string;
  workspaceId:string;
  employeeId:string;
  workspaceRole:"SUPER_ADMIN"|"ADMIN"|"USER";
}
export interface AllowanceActorSource {
  revalidate():Promise<AllowanceActor|null>;
}
export interface NewCloudAllowance {
  workspaceId:string;applicationId:string;deviceId:string;
  environment:"production"|"test";
  originalInvoiceNumber:string;allowanceDay:string;
  requestedGross:number;
  // Present on all new HTTP requests; optional only for isolated legacy source tests.
  reason?:string;
  unnumberedTemplateJson:string;
}
export interface CloudAllowanceContext {
  // Worker-injected, never read from a client request body.
  encryptionKeyHex:string;
  environment:"production"|"test";
}
export interface StoredCloudAllowance {
  applicationId:string;workspaceId:string;environment:"production"|"test";
  status:"open"|"review"|"approved"|"rejected"|"withdrawn";
  revision:number;requesterActorId:string;
  sellerInvoice:string;companyRevision:number;
  originalInvoiceNumber:string;allowanceDay:string;
  requestedGross:number;requestHash:string;
  reason:string;
  unnumberedTemplateJson:string;
}
interface CompanyRow {
  seller_invoice:string;revision:number;amego_app_key_ciphertext:string|null;
}
interface FrozenRow {
  application_id:string;workspace_id:string;environment:"production"|"test";
  status:StoredCloudAllowance["status"];revision:number;
  requester_actor_id:string;opened_by_device_id:string;
  request_hash:string;frozen_hash:string;reference_hash:string;
  requested_gross:string;template_ciphertext:string;
  original_invoice_fingerprint:string;
  seller_invoice:string;company_settings_revision:number;
  allowance_day:string;company_seller:string;company_revision:number;
}
const te=new TextEncoder();
const ID=/^[A-Za-z0-9_-]{8,100}$/;
const WS=/^ws_[A-Za-z0-9_-]{3,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const SHA=/^[0-9a-f]{64}$/;
const INVOICE=/^[A-Z]{2}[0-9]{8}$/;

function validDate(day:string):boolean {
  if(typeof day!=="string"||!/^\d{8}$/.test(day))return false;
  const y=Number(day.slice(0,4)),m=Number(day.slice(4,6)),d=Number(day.slice(6,8));
  const dt=new Date(Date.UTC(y,m-1,d));
  return dt.getUTCFullYear()===y&&dt.getUTCMonth()+1===m&&dt.getUTCDate()===d;
}
function checkScope(workspace:string,id:string,device:string):void {
  if(typeof workspace!=="string"||!WS.test(workspace)||
      typeof id!=="string"||!ID.test(id)||
      typeof device!=="string"||!DEVICE.test(device))
    throw new Error("ALLOWANCE_SCOPE_INVALID");
}
function checkTemplate(input:NewCloudAllowance):void {
  const json=input.unnumberedTemplateJson;
  if(typeof json!=="string"||json.length<2||json.length>32768)
    throw new Error("ALLOWANCE_TEMPLATE_INVALID");
  let doc:Record<string,unknown>;
  try {
    const v:unknown=JSON.parse(json);
    if(!v||typeof v!=="object"||Array.isArray(v))throw new Error();
    doc=v as Record<string,unknown>;
  }catch{throw new Error("ALLOWANCE_TEMPLATE_INVALID");}
  if(doc.AllowanceNumber!==""||doc.AllowanceType!==2||
    doc.AllowanceDate!==input.allowanceDay||
    !Array.isArray(doc.ProductItem)||doc.ProductItem.length<1||
    doc.ProductItem.length>50||!doc.ProductItem.every((v:unknown)=>{
      if(!v||typeof v!=="object"||Array.isArray(v))return false;
      const item=v as Record<string,unknown>;
      return item.OriginalInvoiceNumber===input.originalInvoiceNumber;
    }))throw new Error("ALLOWANCE_TEMPLATE_SOURCE_MISMATCH");
  // This is only frozen-template binding, NOT an AMEGO official amount/item
  // verification. C3b/H must verify those immediately before approval/send.
}
function checkInput(input:NewCloudAllowance,ctx:CloudAllowanceContext):void {
  checkScope(input.workspaceId,input.applicationId,input.deviceId);
  if(!["test","production"].includes(input.environment)||
      ctx.environment!==input.environment||
      !INVOICE.test(input.originalInvoiceNumber)||
      !validDate(input.allowanceDay)||
      !Number.isSafeInteger(input.requestedGross)||input.requestedGross<=0)
    throw new Error("ALLOWANCE_REQUEST_INVALID");
  if(input.reason!==undefined&&(
      typeof input.reason!=="string"||input.reason.length>500||
      input.reason!==input.reason.trim()||/[\x00-\x1f\x7f]/.test(input.reason)))
    throw new Error("ALLOWANCE_REASON_INVALID");
  checkTemplate(input);
}
function sameActor(a:AllowanceActor|null,b:AllowanceActor|null,workspace:string):boolean{
  return !!a&&!!b&&a.workspaceId===workspace&&b.workspaceId===workspace&&
    a.employeeId.length>0&&a.employeeId.length<=100&&
    a.employeeId===b.employeeId&&a.workspaceRole===b.workspaceRole&&
    ["USER","ADMIN","SUPER_ADMIN"].includes(a.workspaceRole);
}
function validated(a:AllowanceActor|null,ws:string):a is AllowanceActor {
  return !!a&&a.workspaceId===ws&&
    typeof a.employeeId==="string"&&a.employeeId.length>0&&
    a.employeeId.length<=100&&["USER","ADMIN","SUPER_ADMIN"].includes(a.workspaceRole);
}
// Domain-separate the HMAC key from the existing AES-GCM key. The original
// invoice fingerprint must be unguessable even though invoice IDs have only
// a small possible space; never hash a raw invoice number without a secret.
async function keyedHash(keyHex:string,purpose:string,fields:unknown[]):Promise<string>{
  if(typeof keyHex!=="string"||!/^[0-9a-f]{64}$/i.test(keyHex))
    throw new Error("COMPANY_ENCRYPTION_NOT_CONFIGURED");
  const bytes=Uint8Array.from(keyHex.match(/.{2}/g)!,v=>parseInt(v,16));
  const root=await crypto.subtle.importKey("raw",bytes,"HKDF",false,["deriveKey"]);
  const salt=te.encode("CYInvoice/allowance/v1/hmac-key");
  const info=te.encode(purpose);
  const hmacKey=await crypto.subtle.deriveKey(
    {name:"HKDF",hash:"SHA-256",salt,info},root,
    {name:"HMAC",hash:"SHA-256",length:256},false,["sign"]);
  const digest=await crypto.subtle.sign("HMAC",hmacKey,
    te.encode(JSON.stringify(fields)));
  return Array.from(new Uint8Array(digest),n=>n.toString(16).padStart(2,"0")).join("");
}
async function hashes(input:NewCloudAllowance,ctx:CloudAllowanceContext,seller:string) {
  const common=[input.workspaceId,seller,input.environment,input.originalInvoiceNumber];
  return {
    requestHash:await keyedHash(ctx.encryptionKeyHex,"request",[
      ...common,input.applicationId,input.allowanceDay,
      input.requestedGross,input.reason??"",input.unnumberedTemplateJson]),
    referenceHash:await keyedHash(ctx.encryptionKeyHex,"reference",common),
    originalFingerprint:await keyedHash(ctx.encryptionKeyHex,"invoice-serialize",common)
  };
}
const field=(id:string,hash:string)=>"allowance-frozen/"+id+"/"+hash;
async function configuredCompany(db:D1Database,workspace:string):Promise<CompanyRow>{
  const c=await db.prepare(`SELECT seller_invoice,revision,amego_app_key_ciphertext
    FROM company_settings WHERE workspace_id=?1`).bind(workspace).first<CompanyRow>();
  if(!c||!/^\d{8}$/.test(c.seller_invoice)||!Number.isSafeInteger(c.revision)||
     c.revision<=0||!c.amego_app_key_ciphertext)
    throw new Error("ALLOWANCE_COMPANY_NOT_CONFIGURED");
  return c;
}
async function activeDevice(db:D1Database,workspace:string,device:string):Promise<boolean>{
  const result=await db.prepare(`SELECT d.device_id FROM devices d
    JOIN workspaces w ON w.workspace_id=d.workspace_id
    WHERE d.device_id=?1 AND d.workspace_id=?2 AND d.status='active'
      AND d.employee_authority_state='cloud' AND w.status='active'
    LIMIT 1`).bind(device,workspace).first<{device_id:string}>();
  return !!result;
}
export async function createEncryptedCloudAllowance(db:D1Database,
 input:NewCloudAllowance,ctx:CloudAllowanceContext,authority:AllowanceActorSource
):Promise<{applicationId:string;status:"open";revision:1}>{
  checkInput(input,ctx);
  const first=await authority.revalidate();
  if(!validated(first,input.workspaceId))
    throw new Error("ALLOWANCE_ACTOR_NOT_AUTHORIZED");
  if(!await activeDevice(db,input.workspaceId,input.deviceId))
    throw new Error("ALLOWANCE_DEVICE_INACTIVE");
  const company=await configuredCompany(db,input.workspaceId);
  const values=await hashes(input,ctx,company.seller_invoice);
  const ciphertext=await encryptCompanyBusinessData(ctx.encryptionKeyHex,
    input.workspaceId,field(input.applicationId,values.requestHash),
    JSON.stringify({v:1,reason:input.reason??"",templateJson:input.unnumberedTemplateJson}));
  const second=await authority.revalidate();
  if(!sameActor(first,second,input.workspaceId))
    throw new Error("ALLOWANCE_ACTOR_CHANGED");
  const at=new Date().toISOString();
  // D1.batch is one transaction. Insert #1 is fenced by active Device and
  // Workspace, company revision and seller; #2 has the C2 immutable source
  // trigger. Failure cannot leave an unencrypted / half-frozen application.
  const insertApp=db.prepare(`INSERT INTO allowance_applications
    (application_id,workspace_id,opened_by_device_id,updated_by_device_id,
     environment,reference_hash,request_hash,requested_gross,
     requester_actor_id,actor_id,created_at,updated_at)
    SELECT ?1,?2,?3,?3,?4,?5,?6,?7,?8,?8,?9,?9
    FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id
    JOIN company_settings c ON c.workspace_id=w.workspace_id
    WHERE d.device_id=?3 AND d.workspace_id=?2 AND d.status='active'
      AND d.employee_authority_state='cloud' AND w.status='active'
      AND c.revision=?10 AND c.seller_invoice=?11
      AND c.amego_app_key_ciphertext IS NOT NULL`)
    .bind(input.applicationId,input.workspaceId,input.deviceId,input.environment,
      values.referenceHash,values.requestHash,String(input.requestedGross),
      first.employeeId,at,company.revision,company.seller_invoice);
  const insertFrozen=db.prepare(`INSERT INTO allowance_frozen_requests
    (application_id,workspace_id,seller_invoice,environment,
     company_settings_revision,request_hash,original_invoice_fingerprint,
     allowance_day,template_ciphertext,encrypted_by_device_id,frozen_at)
    VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11)`)
    .bind(input.applicationId,input.workspaceId,company.seller_invoice,
      input.environment,company.revision,values.requestHash,
      values.originalFingerprint,input.allowanceDay,ciphertext,input.deviceId,at);
  const result=await db.batch([insertApp,insertFrozen]);
  if(result.length!==2||result.some(r=>r.meta.changes!==1))
    throw new Error("ALLOWANCE_ATOMIC_CREATE_NOT_CONFIRMED");
  // If authority disappeared during D1 I/O, surface an uncertain result.
  // Client must use the original ID for readback; never allocate another ID.
  const last=await authority.revalidate();
  if(!sameActor(first,last,input.workspaceId)||
      !await activeDevice(db,input.workspaceId,input.deviceId))
    throw new Error("ALLOWANCE_CREATE_READBACK_REQUIRED");
  return {applicationId:input.applicationId,status:"open",revision:1};
}
export async function loadEncryptedCloudAllowance(db:D1Database,
 target:{workspaceId:string;applicationId:string;deviceId:string},
 ctx:CloudAllowanceContext,authority:AllowanceActorSource
):Promise<StoredCloudAllowance>{
  checkScope(target.workspaceId,target.applicationId,target.deviceId);
  const first=await authority.revalidate();
  if(!validated(first,target.workspaceId)||
      !await activeDevice(db,target.workspaceId,target.deviceId))
    throw new Error("ALLOWANCE_READ_AUTH_REQUIRED");
  const row=await db.prepare(`SELECT a.application_id,a.workspace_id,a.environment,a.status,
      a.revision,a.requester_actor_id,a.opened_by_device_id,
      a.request_hash,a.reference_hash,a.requested_gross,
      f.request_hash AS frozen_hash,f.template_ciphertext,
      f.original_invoice_fingerprint,f.seller_invoice,
      f.company_settings_revision,f.allowance_day,
      c.seller_invoice AS company_seller,c.revision AS company_revision
    FROM allowance_applications a
    JOIN allowance_frozen_requests f ON f.application_id=a.application_id
      AND f.workspace_id=a.workspace_id AND f.environment=a.environment
    JOIN company_settings c ON c.workspace_id=a.workspace_id
    WHERE a.application_id=?1 AND a.workspace_id=?2 LIMIT 1`)
   .bind(target.applicationId,target.workspaceId).first<FrozenRow>();
  if(!row||ctx.environment!==row.environment||!SHA.test(row.request_hash)||
      row.request_hash!==row.frozen_hash||
      row.company_seller!==row.seller_invoice||
      row.company_revision!==row.company_settings_revision)
    throw new Error("ALLOWANCE_FROZEN_UNAVAILABLE");
  if(first.workspaceRole==="USER"&&
      (first.employeeId!==row.requester_actor_id||
       !["open","review"].includes(row.status)))
    throw new Error("ALLOWANCE_READ_AUTH_REQUIRED");
  const plaintext=await decryptCompanyBusinessData(ctx.encryptionKeyHex,
    target.workspaceId,field(target.applicationId,row.request_hash),
    row.template_ciphertext);
  let parsed:Record<string,unknown>,reason:string,templateJson:string;
  try {
    // The AES-GCM ciphertext holds both business reason and AMEGO request
    // in one authenticated envelope; AMEGO still receives ONLY templateJson.
    const document:unknown=JSON.parse(plaintext);
    if(!document||typeof document!=="object"||Array.isArray(document))
      throw new Error();
    const envelope=document as Record<string,unknown>;
    if(Object.keys(envelope).sort().join(",")!=="reason,templateJson,v"||
       envelope.v!==1||typeof envelope.reason!=="string"||
       typeof envelope.templateJson!=="string")throw new Error();
    reason=envelope.reason;
    templateJson=envelope.templateJson;
    const data:unknown=JSON.parse(templateJson);
    if(!data||typeof data!=="object"||Array.isArray(data))throw new Error();
    parsed=data as Record<string,unknown>;
  }catch{throw new Error("ALLOWANCE_FROZEN_INVALID");}
  const original=Array.isArray(parsed.ProductItem)&&parsed.ProductItem.length?
    (parsed.ProductItem[0] as Record<string,unknown>)?.OriginalInvoiceNumber:null;
  if(typeof original!=="string"||!INVOICE.test(original))
    throw new Error("ALLOWANCE_FROZEN_INVALID");
  const request:NewCloudAllowance={
    workspaceId:row.workspace_id,applicationId:row.application_id,
    deviceId:row.opened_by_device_id,environment:row.environment,
    originalInvoiceNumber:original,allowanceDay:row.allowance_day,
    requestedGross:Number(row.requested_gross),reason,unnumberedTemplateJson:templateJson
  };
  checkInput(request,ctx);
  const values=await hashes(request,ctx,row.seller_invoice);
  if(values.requestHash!==row.request_hash||
      values.referenceHash!==row.reference_hash||
      values.originalFingerprint!==row.original_invoice_fingerprint)
    throw new Error("ALLOWANCE_FROZEN_BINDING_MISMATCH");
  const second=await authority.revalidate();
  if(!sameActor(first,second,target.workspaceId)||
      !await activeDevice(db,target.workspaceId,target.deviceId))
    throw new Error("ALLOWANCE_READ_AUTH_CHANGED");
  const company=await configuredCompany(db,target.workspaceId);
  if(company.revision!==row.company_revision||
      company.seller_invoice!==row.seller_invoice)
    throw new Error("ALLOWANCE_COMPANY_CHANGED");
  return {applicationId:row.application_id,workspaceId:row.workspace_id,
    environment:row.environment,status:row.status,revision:row.revision,
    requesterActorId:row.requester_actor_id,sellerInvoice:row.seller_invoice,
    companyRevision:row.company_revision,
    originalInvoiceNumber:original,allowanceDay:row.allowance_day,
    requestedGross:request.requestedGross,requestHash:row.request_hash,
    reason,unnumberedTemplateJson:templateJson};
}
