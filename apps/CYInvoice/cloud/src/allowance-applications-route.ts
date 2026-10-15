// V3 DRAFT: protected CYID allowance application transport.
// No official AMEGO call, approval, allowance numbering or execution route.
// Worker app.ts must wrap these endpoints in app-scoped withCyIdSession.
import {cyIdBinding,cyIdDevice,usesCyId,CyIdError,type CyIdEnv,type CyIdPrincipal} from "./cyid";
import {createFrozenAllowanceApplication,loadFrozenAllowanceApplication} from "./allowance-applications";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialInvoiceReadOnly} from "./amego-invoice-query";
import {queryOfficialAllowanceReadOnly} from "./amego-allowance-query";
import {approveAllowanceAfterOfficialReview} from "./allowance-review-approval";
import {reserveCloudAllowanceNumber} from "./allowance-number-ledger";
import {validateAllowanceOriginalItems} from "./allowance-item-validation";

interface Env extends CyIdEnv {
  DB:D1Database;
  COMPANY_SETTINGS_KEY_HEX?:string; // secret, not public Wrangler vars
  ALLOWANCE_OPERATION_ENVIRONMENT?:string; // 'test' or 'production' from deploy gate
}
const paths=new Set(["/v1/allowance-applications/create","/v1/allowance-applications/get",
  "/v1/allowance-applications/list","/v1/allowance-applications/review-preview",
  "/v1/allowance-applications/official-inspection",
  "/v1/allowance-applications/number-readback",
  "/v1/allowance-applications/approve",
  "/v1/allowance-applications/reserve","/v1/allowance-applications/number-status"]);
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
type Action = "create"|"get"|"list"|"review-preview"|"official-inspection"|"number-readback"|"approve"|"reserve"|"number-status";
function validBody(body:unknown,action:Action):body is Record<string,unknown>{
  if(!body||typeof body!=="object"||Array.isArray(body))return false;
  const val=body as Record<string,unknown>;
  const allowed=action==="create"
    ? new Set(["employeeNo","password","itemId","originalInvoiceNumber","allowanceDay",
      "requestedGrossAmount","reason","unnumberedTemplateJson"])
    : action==="get" ? new Set(["employeeNo","password","itemId"])
    : (action==="review-preview"||action==="official-inspection"||
       action==="number-readback"||action==="approve"||
       action==="reserve"||action==="number-status")
      ? new Set(["employeeNo","password","itemId","expectedRevision"])
    : new Set(["employeeNo","password","limit","cursorCreatedAt","cursorItemId"]);
  if(!Object.keys(val).every(k=>allowed.has(k)))return false;
  if(action==="list"){
    const hasCreated=val.cursorCreatedAt!==undefined,hasId=val.cursorItemId!==undefined;
    return hasCreated===hasId && (!hasCreated ||
      (typeof val.cursorCreatedAt==="string" && !Number.isNaN(Date.parse(val.cursorCreatedAt)) &&
        typeof val.cursorItemId==="string" && itemPattern.test(val.cursorItemId)));
  }
  return typeof val.itemId==="string"&&itemPattern.test(val.itemId) &&
    (!["review-preview","official-inspection","number-readback","approve","reserve","number-status"].includes(action) ||
      (Number.isSafeInteger(val.expectedRevision) && Number(val.expectedRevision)>=1));
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
  const action=path.endsWith("/create")?"create":path.endsWith("/list")?"list":
    path.endsWith("/review-preview")?"review-preview":
    path.endsWith("/official-inspection")?"official-inspection":
    path.endsWith("/number-readback")?"number-readback":
    path.endsWith("/approve")?"approve":
    path.endsWith("/reserve")?"reserve":
    path.endsWith("/number-status")?"number-status":"get";
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
    if(action==="number-status"){
      if(!["ADMIN","SUPER_ADMIN"].includes(actor.workspaceRole))
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      const result=await env.DB.prepare(
        "SELECT l.allowance_number,l.status,l.attempt,l.request_sha256,"+
        "a.template_sha256,a.seller_invoice,a.environment,"+
        "wi.revision,wi.status AS work_status "+
        "FROM allowance_number_ledger l "+
        "JOIN allowance_applications a ON a.work_item_id=l.work_item_id "+
        "AND a.workspace_id=l.workspace_id "+
        "JOIN work_items wi ON wi.work_item_id=a.work_item_id AND wi.workspace_id=a.workspace_id "+
        "WHERE l.workspace_id=?1 AND l.work_item_id=?2 AND wi.revision=?3 "+
        "AND wi.kind='ALLOWANCE' AND wi.reference_hash=a.reference_hash "+
        "ORDER BY l.attempt DESC LIMIT 1"
      ).bind(d.workspace_id,input.itemId,input.expectedRevision).first<{
        allowance_number:string;status:string;attempt:number;
        request_sha256:string;template_sha256:string;seller_invoice:string;
        environment:string;revision:number;work_status:string
      }>();
      if(!result)return response(404,{error:{code:"NUMBER_NOT_FOUND"}});
      if(result.request_sha256!==result.template_sha256 ||
         result.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT ||
         !["review","closed"].includes(result.work_status))
        return response(409,{error:{code:"NUMBER_SOURCE_CHANGED"}});
      const again=await authority(env).revalidate();
      if(!again||again.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(again.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      return response(200,{number:{
        itemId:input.itemId,revision:result.revision,
        allowanceNumber:result.allowance_number,attempt:result.attempt,
        status:result.status,
        // "sending" is ambiguous, even if the local HTTP response was lost.
        mustReadbackBeforeAnyNewAttempt:result.status==="sending",
        canAutoIssue:false
      }});
    }
    if(action==="reserve"){
      if(!["ADMIN","SUPER_ADMIN"].includes(actor.workspaceRole))
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(!["test","production"].includes(env.ALLOWANCE_OPERATION_ENVIRONMENT??""))
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      const itemId=input.itemId as string, rev=input.expectedRevision as number;
      const frozen=await loadFrozenAllowanceApplication(env.DB,d.workspace_id,
        itemId,rev,env.COMPANY_SETTINGS_KEY_HEX!);
      const company=await loadAmegoCompanySettingsForGateway(env.DB,
        d.workspace_id,env.COMPANY_SETTINGS_KEY_HEX!);
      if(frozen.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT||
         frozen.sellerInvoice!==company.sellerInvoice||
         frozen.companySettingsRevision!==company.revision)
        return response(409,{error:{code:"ALLOWANCE_COMPANY_CHANGED"}});
      const again=await authority(env).revalidate();
      if(!again||again.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(again.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      // SQL fences a fresh, append-only approval for the same frozen request,
      // active manager Device, Work Item revision and Workspace.
      const allocated=await reserveCloudAllowanceNumber(env.DB,{
        workspaceId:d.workspace_id,workItemId:itemId,
        deviceId:d.device_id,expectedRevision:rev,
        sellerInvoice:frozen.sellerInvoice,environment:frozen.environment,
        allowanceDay:frozen.allowanceDay,requestSha256:frozen.templateSha256
      });
      const after=await authority(env).revalidate();
      if(!after||after.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(after.workspaceRole))
        return response(409,{error:{code:"NUMBER_RESULT_UNKNOWN_READBACK_REQUIRED"}});
      return response(201,{number:{
        itemId,revision:rev,allowanceNumber:allocated.allowanceNumber,
        attempt:allocated.attempt,status:allocated.status,
        canAutoIssue:false,requiresFinalPreflight:true
      }});
    }
    if(action==="approve"){
      if(!["ADMIN","SUPER_ADMIN"].includes(actor.workspaceRole))
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(!["test","production"].includes(env.ALLOWANCE_OPERATION_ENVIRONMENT??""))
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      const target={workspaceId:d.workspace_id,workItemId:input.itemId as string,
        deviceId:d.device_id,expectedRevision:input.expectedRevision as number};
      // All three callbacks are Worker-owned. No browser-provided official
      // evidence, reviewer ID, seller Invoice or per-item "approved" claim.
      const approved=await approveAllowanceAfterOfficialReview(env.DB,target,{
        revalidateManager:async()=>{
          const p=await authority(env).revalidate();
          return p?{workspaceId:p.workspaceId,employeeId:p.employeeId,
            role:p.workspaceRole}:null;
        },
        loadFrozenApplication:async t=>{
          if(t.workspaceId!==d.workspace_id||t.deviceId!==d.device_id||
             t.workItemId!==target.workItemId||t.expectedRevision!==target.expectedRevision)
            throw new Error("ALLOWANCE_APPROVAL_SCOPE_CHANGED");
          const frozen=await loadFrozenAllowanceApplication(env.DB,d.workspace_id,
            t.workItemId,t.expectedRevision,env.COMPANY_SETTINGS_KEY_HEX!);
          if(frozen.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT)
            throw new Error("ALLOWANCE_APPROVAL_ENVIRONMENT_CHANGED");
          return frozen;
        },
        queryAndVerifyOfficialInvoice:async frozen=>{
          const application=await loadFrozenAllowanceApplication(env.DB,d.workspace_id,
            target.workItemId,target.expectedRevision,env.COMPANY_SETTINGS_KEY_HEX!);
          if(application.referenceHash!==frozen.referenceHash||
             application.templateSha256!==frozen.templateSha256)
            throw new Error("ALLOWANCE_APPROVAL_SOURCE_CHANGED");
          const company=await loadAmegoCompanySettingsForGateway(env.DB,
            d.workspace_id,env.COMPANY_SETTINGS_KEY_HEX!);
          if(company.sellerInvoice!==frozen.sellerInvoice||
             company.revision!==application.companySettingsRevision)
            throw new Error("ALLOWANCE_APPROVAL_COMPANY_CHANGED");
          const official=await queryOfficialInvoiceReadOnly({
            sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
            originalInvoiceNumber:frozen.originalInvoiceNumber
          });
          const cancelled=official.cancelDate!==0||official.voidPending||
            official.hasUnsettledMutation;
          const check=validateAllowanceOriginalItems(application.templateJson,{
            invoiceNumber:official.invoiceNumber,invoiceDate:official.invoiceDate,
            detailVat:official.detailVat,invoiceTaxAmount:official.invoiceTaxAmount,
            invoiceGross:official.totalAmount,status:official.uploadStatus,
            cancelled,allowanceSummaryPresent:official.allowanceSummaryPresent,
            existingAllowanceCount:official.existingAllowanceCount,
            officialItems:official.originalProductItems
          },application.originalInvoiceNumber,application.requestedGrossAmount);
          // A positive result is only possible in the narrow, unambiguous
          // net-price, no-prior-allowance, exact 5% VAT subset.
          if(!check.fullyMatched)
            throw new Error("ALLOWANCE_APPROVAL_OFFICIAL_ITEM_UNVERIFIED");
          return {originalInvoiceNumber:official.invoiceNumber,
            sellerInvoice:company.sellerInvoice,environment:frozen.environment,
            checkedAtUtc:official.checkedAtUtc,
            officialSnapshotSha256:official.snapshotSha256,
            invoiceUploadStatus:official.uploadStatus,
            invoiceCancelled:cancelled,
            allowanceSummaryPresent:official.allowanceSummaryPresent,
            existingAllowanceCount:official.existingAllowanceCount,
            originalInvoiceGrossAmount:official.totalAmount,
            originalItemsAndRequestFullyVerified:true};
        }
      });
      // A reviewer grants authorization; it is NOT a number reservation or
      // a g0401 attempt. Clients cannot infer that any AMEGO mutation occurred.
      return response(201,{approval:{
        approvalId:approved.approvalId,itemId:approved.workItemId,
        revision:approved.approvedRevision,approvedByEmployeeId:approved.approvedByEmployeeId,
        approvedAt:approved.approvedAt,validUntilUtc:approved.validUntilUtc,
        canIssueNow:false,requiresFinalPreflight:true
      }});
    }
    if(action==="number-readback"){
      if(actor.workspaceRole==="USER")
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(env.ALLOWANCE_OPERATION_ENVIRONMENT!=="test"&&
         env.ALLOWANCE_OPERATION_ENVIRONMENT!=="production")
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      const itemId=input.itemId as string;
      const expectedRevision=input.expectedRevision as number;
      // The number MUST originate from D1, never from caller JSON. This also
      // supports a closed, already-confirmed Work Item without reopening it.
      const ledger=await env.DB.prepare(
        "SELECT l.ledger_id,l.allowance_number,l.status,l.environment,"+
        "l.seller_invoice,l.allowance_day,l.request_sha256,"+
        "a.original_invoice_number,a.requested_gross_amount,"+
        "a.company_settings_revision,a.template_sha256,"+
        "wi.revision AS work_revision,wi.environment AS work_environment "+
        "FROM allowance_number_ledger l "+
        "JOIN work_items wi ON wi.work_item_id=l.work_item_id AND wi.workspace_id=l.workspace_id "+
        "JOIN allowance_applications a ON a.work_item_id=wi.work_item_id "+
        "AND a.workspace_id=wi.workspace_id "+
        "WHERE l.workspace_id=?1 AND l.work_item_id=?2 "+
        "AND wi.kind='ALLOWANCE' AND wi.revision=?3 "+
        "AND wi.status IN ('review','closed') AND wi.reference_hash=a.reference_hash "+
        "ORDER BY l.attempt DESC LIMIT 1"
      ).bind(d.workspace_id,itemId,expectedRevision).first<{
        allowance_number:string,status:string,environment:string,
        seller_invoice:string,allowance_day:string,request_sha256:string,
        original_invoice_number:string,requested_gross_amount:number,
        company_settings_revision:number,template_sha256:string,
        work_revision:number,work_environment:string
      }>();
      if(!ledger)return response(404,{error:{code:"NUMBER_NOT_FOUND"}});
      if(!["sending","confirmed"].includes(ledger.status))
        return response(409,{error:{code:"NUMBER_NOT_SUBMITTED_READBACK_UNAVAILABLE"}});
      if(ledger.request_sha256!==ledger.template_sha256 ||
          ledger.environment!==ledger.work_environment||
          ledger.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT)
        return response(409,{error:{code:"NUMBER_SOURCE_CHANGED"}});
      const company=await loadAmegoCompanySettingsForGateway(env.DB,
        d.workspace_id,env.COMPANY_SETTINGS_KEY_HEX!);
      if(company.sellerInvoice!==ledger.seller_invoice ||
         company.revision!==ledger.company_settings_revision)
        return response(409,{error:{code:"NUMBER_COMPANY_CHANGED"}});
      const before=await authority(env).revalidate();
      if(!before||before.employeeId!==actor.employeeId||
        !["ADMIN","SUPER_ADMIN"].includes(before.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      const official=await queryOfficialAllowanceReadOnly({
        sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
        allowanceNumber:ledger.allowance_number,
        originalInvoiceNumber:ledger.original_invoice_number
      });
      const after=await authority(env).revalidate();
      const currentDevice=await dev(request,env);
      if(!after||after.employeeId!==actor.employeeId||
         !["ADMIN","SUPER_ADMIN"].includes(after.workspaceRole) ||
         currentDevice?.device_id!==d.device_id)
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      const scaled=(value:string)=>{
        const [a,b=""]=value.split(".");
        return BigInt(a)*10000000n+BigInt(b.padEnd(7,"0")||"0");
      };
      const grossMatches=scaled(official.totalAmount)+scaled(official.taxAmount)===
        BigInt(ledger.requested_gross_amount)*10000000n;
      return response(200,{readback:{
        itemId,expectedRevision,allowanceNumber:ledger.allowance_number,
        ledgerStatus:ledger.status,checkedAtUtc:official.checkedAtUtc,
        officialSnapshotSha256:official.officialSnapshotSha256,
        officialInvoiceType:official.invoiceType,
        officialUploadStatus:official.uploadStatus,
        officialCancelled:official.cancelDate!==0,
        exactOriginalInvoiceMatch:official.possibleMatch,
        grossMatchesFrozenRequest:grossMatches,
        possibleMatch:official.possibleMatch&&grossMatches,
        safeToAutoResend:false,safeToAutoConfirm:false,
        requiresManualReconciliation:true
      }});
    }
    if(action==="official-inspection"){
      if(actor.workspaceRole==="USER")
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(env.ALLOWANCE_OPERATION_ENVIRONMENT!=="test" &&
         env.ALLOWANCE_OPERATION_ENVIRONMENT!=="production")
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      const itemId=input.itemId as string;
      const rev=input.expectedRevision as number;
      // This is only a LIVE READ-ONLY status inspection. The official response
      // is NOT an approval grant, and no g0401 is sent from this route.
      const frozen=await loadFrozenAllowanceApplication(env.DB,d.workspace_id,itemId,
        rev,env.COMPANY_SETTINGS_KEY_HEX!);
      if(frozen.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT)
        return response(409,{error:{code:"ALLOWANCE_ENVIRONMENT_CHANGED"}});
      const company=await loadAmegoCompanySettingsForGateway(env.DB,
        d.workspace_id,env.COMPANY_SETTINGS_KEY_HEX!);
      if(company.sellerInvoice!==frozen.sellerInvoice ||
          company.revision!==frozen.companySettingsRevision)
        return response(409,{error:{code:"ALLOWANCE_COMPANY_CHANGED"}});
      // Revalidate before transferring the request to AMEGO; never contact
      // the provider after a manager has lost authority.
      const beforeRemote=await authority(env).revalidate();
      if(!beforeRemote||beforeRemote.employeeId!==actor.employeeId||
          !["ADMIN","SUPER_ADMIN"].includes(beforeRemote.workspaceRole))
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      const snapshot=await queryOfficialInvoiceReadOnly({
        sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
        originalInvoiceNumber:frozen.originalInvoiceNumber
      });
      const after=await authority(env).revalidate();
      const liveDevice=await dev(request,env);
      if(!after||after.employeeId!==actor.employeeId||
          !["ADMIN","SUPER_ADMIN"].includes(after.workspaceRole) ||
          liveDevice?.device_id!==d.device_id)
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      // Verify with exact 7-decimal arithmetic. Even a matched line item
      // does NOT authorize g0401: a new final preflight and approval grant
      // are still required after complete official historical reconciliation.
      const itemCheck=validateAllowanceOriginalItems(
        frozen.templateJson,{
          invoiceNumber:snapshot.invoiceNumber,invoiceDate:snapshot.invoiceDate,
          detailVat:snapshot.detailVat,invoiceTaxAmount:snapshot.invoiceTaxAmount,
          invoiceGross:snapshot.totalAmount,status:snapshot.uploadStatus,
          cancelled:snapshot.cancelDate!==0||snapshot.voidPending||
            snapshot.hasUnsettledMutation,
          allowanceSummaryPresent:snapshot.allowanceSummaryPresent,
          existingAllowanceCount:snapshot.existingAllowanceCount,
          officialItems:snapshot.originalProductItems
        },frozen.originalInvoiceNumber,frozen.requestedGrossAmount);
      // Minimal status only: no key, buyer PII or raw official line items.
      const flags={
        uploadComplete:snapshot.uploadStatus===99,
        notCancelled:snapshot.cancelDate===0 && !snapshot.voidPending,
        allowanceSummaryExplicit:snapshot.allowanceSummaryPresent,
        zeroExistingAllowances:snapshot.allowanceSummaryPresent &&
          snapshot.existingAllowanceCount===0,
        invoiceGrossEnough:snapshot.totalAmount>=frozen.requestedGrossAmount,
        officialProductsPresent:snapshot.productItemsPresent
      };
      return response(200,{inspection:{
        itemId,revision:rev,originalInvoiceNumber:frozen.originalInvoiceNumber,
        checkedAtUtc:snapshot.checkedAtUtc,
        officialSnapshotSha256:snapshot.snapshotSha256,
        officialUploadStatus:snapshot.uploadStatus,
        cancelled:snapshot.cancelDate!==0||snapshot.voidPending,
        officialInvoiceGross:snapshot.totalAmount,
        requestedGrossAmount:frozen.requestedGrossAmount,
        existingAllowanceCount:snapshot.existingAllowanceCount,
        officialPendingMutation:snapshot.hasUnsettledMutation,
        itemCheck,
        ...flags,
        requiresPerItemVerification:true,canApprove:false,
        status:"OFFICIAL_READ_ONLY_NOT_APPROVED"
      }});
    }
    if(action==="review-preview"){
      if(actor.workspaceRole==="USER")
        return response(403,{error:{code:"MANAGER_AUTH_REQUIRED"}});
      if(env.ALLOWANCE_OPERATION_ENVIRONMENT!=="test" &&
         env.ALLOWANCE_OPERATION_ENVIRONMENT!=="production")
        return response(503,{error:{code:"ALLOWANCE_ENVIRONMENT_NOT_CONFIGURED"}});
      if(!/^[a-f0-9]{64}$/i.test(env.COMPANY_SETTINGS_KEY_HEX??""))
        return response(503,{error:{code:"COMPANY_ENCRYPTION_NOT_CONFIGURED"}});
      // Only the immutable, source-verified application can be shown for
      // manager review. The official AMEGO invoice is NOT consulted here.
      const frozen=await loadFrozenAllowanceApplication(env.DB,d.workspace_id,
        input.itemId as string,input.expectedRevision as number,
        env.COMPANY_SETTINGS_KEY_HEX!);
      if(frozen.environment!==env.ALLOWANCE_OPERATION_ENVIRONMENT)
        return response(409,{error:{code:"ALLOWANCE_ENVIRONMENT_CHANGED"}});
      const template:unknown=JSON.parse(frozen.templateJson);
      if(!template||typeof template!=="object"||Array.isArray(template))
        throw new Error("ALLOWANCE_TEMPLATE_INVALID");
      const products=(template as {ProductItem?:unknown}).ProductItem;
      if(!Array.isArray(products)||products.length<1||products.length>50)
        throw new Error("ALLOWANCE_TEMPLATE_INVALID");
      // Whitelist only the information a manager needs to compare with the
      // official invoice. Never return an arbitrary applicant JSON document,
      // buyer personal contact details, ciphertext or any company credentials.
      const items=products.map((p:unknown)=>{
        if(!p||typeof p!=="object"||Array.isArray(p))
          throw new Error("ALLOWANCE_TEMPLATE_INVALID");
        const i=p as Record<string,unknown>;
        const description=typeof i.OriginalDescription==="string"
          ? i.OriginalDescription.slice(0,300):"";
        const decimal=(v:unknown)=>typeof v==="number"&&Number.isFinite(v)?v:
          typeof v==="string"&&/^-?\d+(?:\.\d{1,7})?$/.test(v)?v:null;
        return {originalInvoiceNumber:frozen.originalInvoiceNumber,
          description,quantity:decimal(i.Quantity),unitPrice:decimal(i.UnitPrice),
          amount:decimal(i.Amount),tax:decimal(i.Tax),
          taxType:typeof i.TaxType==="number"&&Number.isSafeInteger(i.TaxType)?i.TaxType:null};
      });
      const again=await authority(env).revalidate();
      const latestDevice=await dev(request,env);
      if(!again||again.employeeId!==actor.employeeId||
          !["ADMIN","SUPER_ADMIN"].includes(again.workspaceRole) ||
          latestDevice?.device_id!==d.device_id)
        return response(403,{error:{code:"AUTHORITY_CHANGED"}});
      return response(200,{review:{
        itemId:input.itemId,revision:input.expectedRevision,
        originalInvoiceNumber:frozen.originalInvoiceNumber,
        allowanceDay:frozen.allowanceDay,sellerInvoice:frozen.sellerInvoice,
        environment:frozen.environment,requestedGrossAmount:frozen.requestedGrossAmount,
        reason:frozen.applicationReason,applicantEmployeeId:frozen.applicantEmployeeId,
        templateSha256:frozen.templateSha256,items,
        officialVerification:"NOT_PERFORMED",canApprove:false
      }});
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
      if(/ALLOWANCE_(APPLICATION_NOT_REVIEWABLE|APPLICATION_COMPANY_CHANGED|APPLICATION_REFERENCE_TAMPERED|APPLICATION_TEMPLATE_TAMPERED)/.test(error.message))
        return response(409,{error:{code:"ALLOWANCE_REVIEW_REQUIRES_REFRESH"}});
      if(/^ALLOWANCE_(NUMBER_|SEQUENCE_|ALLOCATION_|DATE_|RESERVATION_|WORK_ITEM_|PRIOR_SEND_)/.test(error.message))
        return response(409,{error:{code:"NUMBER_RESULT_UNKNOWN_READBACK_REQUIRED"}});
      if(/^ALLOWANCE_APPROVAL_/.test(error.message))
        return response(409,{error:{code:"ALLOWANCE_APPROVAL_NOT_AUTHORIZED"}});
      if(/^ALLOWANCE_QUERY_/.test(error.message))
        return response(503,{error:{code:"ALLOWANCE_READBACK_UNCONFIRMED"}});
      if(/^OFFICIAL_QUERY_/.test(error.message))
        return response(503,{error:{code:"OFFICIAL_QUERY_UNCONFIRMED"}});
      if(/ALLOWANCE_(COMPANY_NOT_CONFIGURED)/.test(error.message)||
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
