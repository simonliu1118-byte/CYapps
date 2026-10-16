// Draft-only internal owner for a cross-device allowance application.
// This creates both the exceptional Work Item and an immutable, encrypted,
// exactly bound application. NO HTTP route, no AMEGO calls, no number allocation.
import {encryptCompanyBusinessData,decryptCompanyBusinessData} from "./company-settings";
import type {ServerFrozenAllowanceRequest} from "./allowance-review-approval";
const encoder=new TextEncoder();

export interface ApplicantPrincipal {
  workspaceId: string;
  employeeId: string;
  workspaceRole: "SUPER_ADMIN"|"ADMIN"|"USER";
}
export interface ApplicantAuthority {
  revalidate(): Promise<ApplicantPrincipal | null>;
}
export interface NewFrozenAllowanceApplication {
  workspaceId: string;
  deviceId: string;
  workItemId: string; // durable operation ID reused for ambiguous response readback
  originalInvoiceNumber: string;
  allowanceDay: string; // yyyyMMdd
  requestedGrossAmount: number; // exact whole TWD
  reason: string;
  unnumberedTemplateJson: string;
}
export interface TrustedAllowanceRuntime {
  // Sourced from authenticated Worker environment, never from browser JSON.
  environment: "production"|"test";
  companyEncryptionKeyHex: string;
}
export interface FrozenAllowanceRecord extends ServerFrozenAllowanceRequest {
  applicantEmployeeId: string;
  applicantDeviceId: string;
  applicationReason: string;
  templateJson: string;
  companySettingsRevision: number;
}
interface ApplicationRow {
  work_item_id:string; workspace_id:string; seller_invoice:string;
  company_settings_revision:number; environment:"production"|"test";
  original_invoice_number:string; allowance_day:string; requested_gross_amount:number;
  application_reason:string; applicant_employee_id:string; applicant_device_id:string;
  template_sha256:string; template_ciphertext:string; reference_hash:string;
  work_reference_hash:string; work_environment:string; work_status:string;
  work_revision:number;
}
type Company = {seller_invoice:string;revision:number;amego_app_key_ciphertext:string|null};
const ID=/^[a-zA-Z0-9_-]{10,100}$/;
const DEVICE=/^dev_[0-9a-f-]{36}$/;
const INVOICE=/^[A-Z]{2}[0-9]{8}$/;
const DAY=/^[0-9]{8}$/;
function assertScope(workspace:string,device:string,id:string):void{
  if(!/^ws_[0-9a-zA-Z_-]{3,100}$/.test(workspace) || !DEVICE.test(device) || !ID.test(id))
    throw new Error("ALLOWANCE_APPLICATION_SCOPE_INVALID");
}
function assertDate(value:string):void{
  if(!DAY.test(value))throw new Error("ALLOWANCE_APPLICATION_DATE_INVALID");
  const y=Number(value.slice(0,4)),m=Number(value.slice(4,6)),d=Number(value.slice(6));
  const x=new Date(Date.UTC(y,m-1,d));
  if(x.getUTCFullYear()!==y||x.getUTCMonth()+1!==m||x.getUTCDate()!==d)
    throw new Error("ALLOWANCE_APPLICATION_DATE_INVALID");
}
function assertTemplate(json:string,orig:string,day:string):void{
  if(typeof json!=="string"||json.length<2||json.length>32768)
    throw new Error("ALLOWANCE_TEMPLATE_INVALID");
  let parsed:Record<string,unknown>;
  try {
    const p:unknown=JSON.parse(json);
    if(!p||typeof p!=="object"||Array.isArray(p))throw new Error();
    parsed=p as Record<string,unknown>;
  }catch{throw new Error("ALLOWANCE_TEMPLATE_INVALID");}
  if(parsed.AllowanceNumber!==""||parsed.AllowanceDate!==day||
      !Array.isArray(parsed.ProductItem)||parsed.ProductItem.length<1||
      parsed.ProductItem.length>50||
      !parsed.ProductItem.every((i:unknown)=>
        i!==null&&typeof i==="object"&&!Array.isArray(i)&&
        (i as Record<string,unknown>).OriginalInvoiceNumber===orig))
    throw new Error("ALLOWANCE_TEMPLATE_MISMATCH");
  // Monetary and tax proof is deliberately deferred to the manager's
  // actual official per-invoice/item verifier, NOT accepted from this JSON.
}
async function digest(s:string):Promise<string>{
  const buf=await crypto.subtle.digest("SHA-256",new Uint8Array(encoder.encode(s)));
  return Array.from(new Uint8Array(buf),b=>b.toString(16).padStart(2,"0")).join("");
}
function samePrincipal(a:ApplicantPrincipal|null,b:ApplicantPrincipal|null,ws:string):boolean{
  return !!a&&!!b&&a.workspaceId===ws&&b.workspaceId===ws&&
    !!a.employeeId&&a.employeeId===b.employeeId&&a.employeeId.length<=100&&
    ["SUPER_ADMIN","ADMIN","USER"].includes(a.workspaceRole)&&
    ["SUPER_ADMIN","ADMIN","USER"].includes(b.workspaceRole);
}
async function company(db:D1Database,workspace:string):Promise<Company>{
  const c=await db.prepare("SELECT seller_invoice,revision,amego_app_key_ciphertext "+
    "FROM company_settings WHERE workspace_id=?1").bind(workspace).first<Company>();
  if(!c||!c.amego_app_key_ciphertext)throw new Error("ALLOWANCE_COMPANY_NOT_CONFIGURED");
  return c;
}
function referenceHashParts(inp:NewFrozenAllowanceApplication,actor:string,
  seller:string,revision:number,env:string,templateHash:string){
  return JSON.stringify([inp.workItemId,inp.workspaceId,actor,seller,revision,env,
    inp.originalInvoiceNumber,inp.allowanceDay,inp.requestedGrossAmount,inp.reason,
    templateHash]);
}
export async function createFrozenAllowanceApplication(db:D1Database,
  input:NewFrozenAllowanceApplication,authority:ApplicantAuthority,
  runtime:TrustedAllowanceRuntime):Promise<{workItemId:string;referenceHash:string}>{
  assertScope(input.workspaceId,input.deviceId,input.workItemId);
  if(!INVOICE.test(input.originalInvoiceNumber)||
    !Number.isSafeInteger(input.requestedGrossAmount)||input.requestedGrossAmount<=0||
    typeof input.reason!=="string"||input.reason.trim().length<1||
    input.reason.length>500||/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(input.reason)||
    !["production","test"].includes(runtime.environment))
    throw new Error("ALLOWANCE_APPLICATION_INVALID");
  assertDate(input.allowanceDay);
  assertTemplate(input.unnumberedTemplateJson,input.originalInvoiceNumber,input.allowanceDay);
  const first=await authority.revalidate();
  if(!first||first.workspaceId!==input.workspaceId||!first.employeeId||
     first.employeeId.length>100||!["SUPER_ADMIN","ADMIN","USER"].includes(first.workspaceRole))
    throw new Error("ALLOWANCE_APPLICANT_AUTH_REQUIRED");
  const c=await company(db,input.workspaceId);
  const templateSha=await digest(input.unnumberedTemplateJson);
  const referenceHash=await digest(referenceHashParts(input,first.employeeId,
    c.seller_invoice,c.revision,runtime.environment,templateSha));
  const ciphertext=await encryptCompanyBusinessData(runtime.companyEncryptionKeyHex,
    input.workspaceId,"allowance-application/"+input.workItemId,input.unnumberedTemplateJson);
  const second=await authority.revalidate();
  if(!samePrincipal(first,second,input.workspaceId))
    throw new Error("ALLOWANCE_APPLICANT_AUTH_CHANGED");
  const at=new Date().toISOString();
  const firstStatement=db.prepare(
    "INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,updated_by_device_id,"+
    "environment,kind,reference_hash,actor_id,created_at,updated_at) "+
    "SELECT ?1,?2,?3,?3,?4,'ALLOWANCE',?5,?6,?7,?7 WHERE EXISTS "+
    "(SELECT 1 FROM devices d JOIN workspaces w ON w.workspace_id=d.workspace_id "+
    "WHERE d.device_id=?3 AND d.workspace_id=?2 AND d.status='active' "+
    "AND d.employee_authority_state='cloud' AND w.status='active') "+
    "AND EXISTS (SELECT 1 FROM company_settings c WHERE c.workspace_id=?2 "+
    "AND c.revision=?8 AND c.seller_invoice=?9 AND c.amego_app_key_ciphertext IS NOT NULL)"
  ).bind(input.workItemId,input.workspaceId,input.deviceId,runtime.environment,
    referenceHash,first.employeeId,at,c.revision,c.seller_invoice);
  const secondStatement=db.prepare(
    "INSERT INTO allowance_applications(work_item_id,workspace_id,seller_invoice,"+
    "company_settings_revision,environment,original_invoice_number,allowance_day,"+
    "requested_gross_amount,application_reason,applicant_employee_id,applicant_device_id,"+
    "template_sha256,template_ciphertext,reference_hash,created_at) "+
    "VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15)"
  ).bind(input.workItemId,input.workspaceId,c.seller_invoice,c.revision,runtime.environment,
    input.originalInvoiceNumber,input.allowanceDay,input.requestedGrossAmount,
    input.reason,first.employeeId,input.deviceId,templateSha,ciphertext,referenceHash,at);
  let results:D1Result[];
  try{results=await db.batch([firstStatement,secondStatement]);}
  catch{throw new Error("ALLOWANCE_APPLICATION_UNKNOWN_READBACK_REQUIRED");}
  if(results[0].meta.changes!==1||results[1].meta.changes!==1)
    throw new Error("ALLOWANCE_APPLICATION_REVOKED_OR_CONFLICT");
  return {workItemId:input.workItemId,referenceHash};
}
// Trusted internal approval callback owner, not a general applicant GET route.
// Fail closed on company rotation, Work Item mutation or encrypted corruption.
export async function loadFrozenAllowanceApplication(db:D1Database,
  workspaceId:string,workItemId:string,expectedRevision:number,keyHex:string):
  Promise<FrozenAllowanceRecord>{
  if(!ID.test(workItemId)||!Number.isSafeInteger(expectedRevision)||expectedRevision<1)
    throw new Error("ALLOWANCE_APPLICATION_LOOKUP_INVALID");
  const row=await db.prepare(
    "SELECT a.*,wi.reference_hash AS work_reference_hash,wi.environment AS work_environment,"+
    "wi.status AS work_status,wi.revision AS work_revision FROM allowance_applications a "+
    "JOIN work_items wi ON wi.work_item_id=a.work_item_id AND wi.workspace_id=a.workspace_id "+
    "WHERE a.workspace_id=?1 AND a.work_item_id=?2"
  ).bind(workspaceId,workItemId).first<ApplicationRow>();
  if(!row||row.work_status!=="review"||row.work_revision!==expectedRevision||
    row.work_reference_hash!==row.reference_hash||row.work_environment!==row.environment)
    throw new Error("ALLOWANCE_APPLICATION_NOT_REVIEWABLE");
  const c=await company(db,workspaceId);
  if(row.seller_invoice!==c.seller_invoice||row.company_settings_revision!==c.revision)
    throw new Error("ALLOWANCE_APPLICATION_COMPANY_CHANGED");
  const json=await decryptCompanyBusinessData(keyHex,workspaceId,
    "allowance-application/"+workItemId,row.template_ciphertext);
  assertTemplate(json,row.original_invoice_number,row.allowance_day);
  if(await digest(json)!==row.template_sha256)
    throw new Error("ALLOWANCE_APPLICATION_TEMPLATE_TAMPERED");
  const source:NewFrozenAllowanceApplication={workspaceId,workItemId,
    deviceId:row.applicant_device_id,originalInvoiceNumber:row.original_invoice_number,
    allowanceDay:row.allowance_day,requestedGrossAmount:row.requested_gross_amount,
    reason:row.application_reason,unnumberedTemplateJson:json};
  const expected=await digest(referenceHashParts(source,row.applicant_employee_id,
    row.seller_invoice,row.company_settings_revision,row.environment,row.template_sha256));
  if(expected!==row.reference_hash)throw new Error("ALLOWANCE_APPLICATION_REFERENCE_TAMPERED");
  return {referenceHash:expected,sellerInvoice:row.seller_invoice,
    originalInvoiceNumber:row.original_invoice_number,environment:row.environment,
    allowanceDay:row.allowance_day,requestedGrossAmount:row.requested_gross_amount,
    templateSha256:row.template_sha256,applicantEmployeeId:row.applicant_employee_id,
    applicantDeviceId:row.applicant_device_id,applicationReason:row.application_reason,
    templateJson:json,companySettingsRevision:row.company_settings_revision};
}

/** Applicant-only cancellation of an unapproved, never-numbered request.
 * Every guard (owner, CYID principal, device, revision, missing grant and
 * missing number) is checked in the ONE atomic D1 UPDATE. Never delete the
 * encrypted application or its original Work Item audit trail.
 */
export async function withdrawFrozenAllowanceApplication(db:D1Database,input:{
  workspaceId:string;deviceId:string;workItemId:string;
  expectedRevision:number;environment:"test"|"production";
},authority:ApplicantAuthority):Promise<{revision:number;withdrawnAt:string}>{
  assertScope(input.workspaceId,input.deviceId,input.workItemId);
  if(!Number.isSafeInteger(input.expectedRevision)||input.expectedRevision<1||
     !["test","production"].includes(input.environment))
    throw new Error("ALLOWANCE_WITHDRAW_INVALID");
  const first=await authority.revalidate();
  if(!first||first.workspaceId!==input.workspaceId||!first.employeeId||
     first.employeeId.length>100)
    throw new Error("ALLOWANCE_WITHDRAW_AUTH_REQUIRED");
  const second=await authority.revalidate();
  if(!samePrincipal(first,second,input.workspaceId))
    throw new Error("ALLOWANCE_WITHDRAW_AUTH_CHANGED");
  const at=new Date().toISOString();
  // Closing a reviewed item without a grant is safe; once a manager grants
  // authority, allocates a number or sends, withdrawal is permanently blocked.
  const changed=await db.prepare(`
    UPDATE work_items SET status='closed',revision=revision+1,
      actor_id=?1,updated_by_device_id=?2,updated_at=?3,
      withdrawn_by_actor_id=?1,withdrawn_at=?3
    WHERE work_item_id=?4 AND workspace_id=?5
      AND environment=?6 AND kind='ALLOWANCE'
      AND revision=?7 AND status IN ('open','review')
      AND withdrawn_at IS NULL
      -- Managers may have reviewed this Work Item, updating actor_id.
      -- Original applicant is verified against immutable application owner.
      AND EXISTS (
        SELECT 1 FROM allowance_applications a
        WHERE a.work_item_id=work_items.work_item_id
          AND a.workspace_id=work_items.workspace_id
          AND a.environment=work_items.environment
          AND a.reference_hash=work_items.reference_hash
          AND a.applicant_employee_id=?1)
      AND EXISTS (
        SELECT 1 FROM devices d JOIN workspaces w
          ON d.workspace_id=w.workspace_id
        WHERE d.device_id=?2 AND d.workspace_id=?5
          AND d.status='active' AND d.employee_authority_state='cloud'
          AND w.status='active')
      AND NOT EXISTS (
        SELECT 1 FROM allowance_execution_approvals g
        WHERE g.work_item_id=work_items.work_item_id
          AND g.workspace_id=work_items.workspace_id)
      AND NOT EXISTS (
        SELECT 1 FROM allowance_number_ledger l
        WHERE l.work_item_id=work_items.work_item_id
          AND l.workspace_id=work_items.workspace_id)
  `).bind(first.employeeId,input.deviceId,at,input.workItemId,
    input.workspaceId,input.environment,input.expectedRevision).run();
  if(changed.meta.changes!==1)
    throw new Error("ALLOWANCE_WITHDRAW_NOT_ALLOWED");
  return {revision:input.expectedRevision+1,withdrawnAt:at};
}
