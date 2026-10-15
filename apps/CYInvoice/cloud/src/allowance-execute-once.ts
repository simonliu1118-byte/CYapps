// V3 Draft-only single-attempt g0401 coordinator.
// MUST be called only with a CYID-authenticated manager + validated Device.
// No public endpoint may pass company credentials or allowance number here.
import {loadFrozenAllowanceApplication} from "./allowance-applications";
import {loadAmegoCompanySettingsForGateway} from "./company-settings";
import {queryOfficialInvoiceReadOnly} from "./amego-invoice-query";
import {validateAllowanceOriginalItems} from "./allowance-item-validation";
import {markCloudAllowanceSending,markCloudAllowanceExternalCollision,
  type AllowanceReservationRequest,type AllowanceNumberReservation} from "./allowance-number-ledger";
import {postNumberedAllowanceOnce} from "./amego-allowance-issue";
export interface ExecuteAllowanceTarget{
 workspaceId:string;workItemId:string;deviceId:string;expectedRevision:number;
 environment:"test"|"production";encryptionKeyHex:string;
}
export interface ExecutionManagerAuthority{
 revalidate():Promise<{workspaceId:string;employeeId:string;
  role:"SUPER_ADMIN"|"ADMIN"|"USER"}|null>;
}
export type FirstSendResult={
  allowanceNumber:string;
  result:"ACCEPTED_PENDING_OFFICIAL"|"DEFINITE_NUMBER_COLLISION"|
    "DEFINITE_REJECTION_MANUAL"|"UNKNOWN_REQUIRES_ORIGINAL_NUMBER_READBACK";
  officialCode?:number;
  safeToAutoResend:false;
  safeToAutoConfirm:false;
};
interface StoredNumber{
 ledger_id:string;work_item_id:string;allowance_number:string;
 sequence:number;attempt:number;status:string;
 request_sha256:string;seller_invoice:string;
 environment:"test"|"production";allowance_day:string;
}
interface Approval{
 official_snapshot_sha256:string;valid_until_utc:string;
 template_sha256:string;
}
const isManager=(p:Awaited<ReturnType<ExecutionManagerAuthority["revalidate"]>>,
  scope:string)=>!!p&&p.workspaceId===scope&&
  ["ADMIN","SUPER_ADMIN"].includes(p.role)&&
  typeof p.employeeId==="string"&&p.employeeId.length>0;
export async function executeApprovedAllowanceOnce(
 db:D1Database,t:ExecuteAllowanceTarget,authority:ExecutionManagerAuthority
):Promise<FirstSendResult>{
 if(!/^ws_[A-Za-z0-9_-]{3,100}$/.test(t.workspaceId)||
    !/^[A-Za-z0-9_-]{10,100}$/.test(t.workItemId)||
    !/^dev_[0-9a-f-]{36}$/.test(t.deviceId)||
    !Number.isSafeInteger(t.expectedRevision)||t.expectedRevision<1)
   throw new Error("G0401_EXECUTION_INVALID");
 const first=await authority.revalidate();
 if(!isManager(first,t.workspaceId))
   throw new Error("G0401_MANAGER_AUTH_REQUIRED");
 const frozen=await loadFrozenAllowanceApplication(db,t.workspaceId,
   t.workItemId,t.expectedRevision,t.encryptionKeyHex);
 const company=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,
   t.encryptionKeyHex);
 if(frozen.environment!==t.environment||company.sellerInvoice!==frozen.sellerInvoice||
    company.revision!==frozen.companySettingsRevision)
   throw new Error("G0401_COMPANY_CHANGED");
 const row=await db.prepare(
   "SELECT ledger_id,work_item_id,allowance_number,sequence,attempt,status,"+
   "request_sha256,seller_invoice,environment,allowance_day "+
   "FROM allowance_number_ledger WHERE workspace_id=?1 AND work_item_id=?2 "+
   "ORDER BY attempt DESC LIMIT 1"
 ).bind(t.workspaceId,t.workItemId).first<StoredNumber>();
 if(!row||row.status!=="reserved"||row.request_sha256!==frozen.templateSha256||
    row.seller_invoice!==frozen.sellerInvoice||row.environment!==frozen.environment||
    row.allowance_day!==frozen.allowanceDay)
   throw new Error("G0401_ORIGINAL_NUMBER_NOT_RESERVED");
 // Strictly check an unnumbered, complete payload BEFORE marking sending.
 let draft:Record<string,unknown>;
 try{
   const value:unknown=JSON.parse(frozen.templateJson);
   if(!value||typeof value!=="object"||Array.isArray(value))
     throw new Error();
   draft=value as Record<string,unknown>;
 }catch{throw new Error("G0401_FROZEN_TEMPLATE_INVALID");}
 if(draft.AllowanceNumber!==""||draft.AllowanceType!==2||
    draft.AllowanceDate!==frozen.allowanceDay||
    typeof draft.BuyerIdentifier!=="string"||draft.BuyerIdentifier.length<8||
    typeof draft.BuyerName!=="string"||!draft.BuyerName.trim()||
    !Array.isArray(draft.ProductItem)||draft.ProductItem.length<1)
   throw new Error("G0401_FROZEN_TEMPLATE_INVALID");
 const approve=await db.prepare(
   "SELECT official_snapshot_sha256,valid_until_utc,template_sha256 "+
   "FROM allowance_execution_approvals WHERE workspace_id=?1 AND work_item_id=?2 "+
   "AND approved_revision=?3 AND seller_invoice=?4 AND environment=?5 "+
   "AND allowance_day=?6 AND template_sha256=?7 AND valid_until_utc>?8 "+
   "ORDER BY approved_at DESC LIMIT 1"
 ).bind(t.workspaceId,t.workItemId,t.expectedRevision,frozen.sellerInvoice,
   frozen.environment,frozen.allowanceDay,frozen.templateSha256,
   new Date().toISOString()).first<Approval>();
 if(!approve)throw new Error("G0401_FRESH_APPROVAL_REQUIRED");
 const original=await queryOfficialInvoiceReadOnly({
   sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
   originalInvoiceNumber:frozen.originalInvoiceNumber
 });
 const cancelled=original.cancelDate!==0||original.voidPending||
   original.hasUnsettledMutation;
 const check=validateAllowanceOriginalItems(frozen.templateJson,{
   invoiceNumber:original.invoiceNumber,invoiceDate:original.invoiceDate,
   detailVat:original.detailVat,invoiceTaxAmount:original.invoiceTaxAmount,
   invoiceGross:original.totalAmount,status:original.uploadStatus,cancelled,
   allowanceSummaryPresent:original.allowanceSummaryPresent,
   existingAllowanceCount:original.existingAllowanceCount,
   officialItems:original.originalProductItems
 },frozen.originalInvoiceNumber,frozen.requestedGrossAmount);
 // Approval's original official document must still be identical.
 if(!check.fullyMatched||original.snapshotSha256!==approve.official_snapshot_sha256)
   throw new Error("G0401_FINAL_PREFLIGHT_CHANGED");
 const next=await authority.revalidate();
 if(!isManager(next,t.workspaceId)||next!.employeeId!==first!.employeeId)
   throw new Error("G0401_MANAGER_AUTH_CHANGED");
 // Re-check company version after network delay; it is a pure server D1
 // read and never trusts client provided seller/AppKey.
 const current=await loadAmegoCompanySettingsForGateway(db,t.workspaceId,
   t.encryptionKeyHex);
 if(current.revision!==frozen.companySettingsRevision||
    current.amegoAppKey!==company.amegoAppKey)
   throw new Error("G0401_COMPANY_CHANGED");
 const reservation:AllowanceNumberReservation={
   ledgerId:row.ledger_id,workItemId:t.workItemId,
   allowanceNumber:row.allowance_number,sequence:row.sequence,
   attempt:row.attempt,status:"reserved",requestSha256:row.request_sha256
 };
 const request:AllowanceReservationRequest={
   workspaceId:t.workspaceId,workItemId:t.workItemId,deviceId:t.deviceId,
   expectedRevision:t.expectedRevision,sellerInvoice:frozen.sellerInvoice,
   environment:frozen.environment,allowanceDay:frozen.allowanceDay,
   requestSha256:frozen.templateSha256
 };
 // If losing this CAS, NO mutating HTTP request is made.
 const claimed=await markCloudAllowanceSending(db,request,reservation);
 if(!claimed)throw new Error("G0401_SEND_ALREADY_CLAIMED_OR_REVOKED");
 // After a successful CAS, even a crash BEFORE fetch is uncertain; the
 // ledger stays "sending" and requires readback by the ORIGINAL number.
 let outcome:Awaited<ReturnType<typeof postNumberedAllowanceOnce>>;
 try {
   outcome=await postNumberedAllowanceOnce({
     sellerInvoice:company.sellerInvoice,appKey:company.amegoAppKey,
     allowanceNumber:row.allowance_number,
     allowanceDay:frozen.allowanceDay,
     unnumberedTemplateJson:frozen.templateJson
   });
 }catch{
   outcome={kind:"unknown_result"};
 }
 const status=(result:FirstSendResult["result"],officialCode?:number):FirstSendResult=>({
   allowanceNumber:row.allowance_number,result,
   ...(officialCode===undefined?{}:{officialCode}),
   safeToAutoResend:false,safeToAutoConfirm:false
 });
 if(outcome.kind==="confirmed_number_collision"){
   try{
     const marked=await markCloudAllowanceExternalCollision(db,request,reservation,outcome.code);
     if(!marked)return status("UNKNOWN_REQUIRES_ORIGINAL_NUMBER_READBACK");
   }catch{return status("UNKNOWN_REQUIRES_ORIGINAL_NUMBER_READBACK");}
   return status("DEFINITE_NUMBER_COLLISION",outcome.code);
 }
 if(outcome.kind==="accepted_unconfirmed")
   return status("ACCEPTED_PENDING_OFFICIAL",0);
 if(outcome.kind==="definite_rejection")
   return status("DEFINITE_REJECTION_MANUAL",outcome.code);
 return status("UNKNOWN_REQUIRES_ORIGINAL_NUMBER_READBACK");
}
