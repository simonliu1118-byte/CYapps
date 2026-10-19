// V3.2-C3b2a internal OFFICIAL evidence calculation only.
// The signed invoice_query allowance summary is a discovery index, NOT proof
// of amounts by original invoice. Each listed number MUST be resolved using
// signed allowance_query and its per-line original_invoice_number/amount/tax.
// No public route, manager approval grant, ledger, g0401 or g0501.
import type {StoredCloudAllowance} from "./allowance-frozen-store";
import {queryOfficialInvoiceReadOnly} from "./amego-invoice-query";
import {queryOfficialAllowanceDetail,fixed7,allowanceScale}
 from "./amego-allowance-detail";

const encoder=new TextEncoder();
const day=(v:unknown):string|null=>{
 if(typeof v!=="string"&&typeof v!=="number")return null;
 const s=String(v).replaceAll("-","");
 if(!/^\d{8}$/.test(s))return null;
 const y=+s.slice(0,4),m=+s.slice(4,6),d=+s.slice(6,8);
 const dt=new Date(Date.UTC(y,m-1,d));
 return dt.getUTCFullYear()===y&&dt.getUTCMonth()+1===m&&
    dt.getUTCDate()===d?s:null;
};
const fail=(reason:string):never=>{throw new Error("ALLOWANCE_OFFICIAL_PROOF_"+reason);};
function checkedAt(iso:string,now:Date):void{
 const then=Date.parse(iso),ms=now.getTime();
 if(!Number.isFinite(then)||!Number.isFinite(ms)||
   then>ms+1000||ms-then>60_000)fail("STALE");
}
function obj(x:unknown):Record<string,unknown>{
 if(!x||typeof x!=="object"||Array.isArray(x))fail("TEMPLATE_INVALID");
 return x as Record<string,unknown>;
}
function proveFrozenItems(f:StoredCloudAllowance,originDay:string):void{
 let request:Record<string,unknown>;
 try{request=obj(JSON.parse(f.unnumberedTemplateJson));}
 catch{fail("TEMPLATE_INVALID");}
 if(request.AllowanceNumber!==""||String(request.AllowanceType)!=="2"||
     day(request.AllowanceDate)!==f.allowanceDay||
     !Array.isArray(request.ProductItem)||request.ProductItem.length<1||
     request.ProductItem.length>50)fail("TEMPLATE_INVALID");
 let net=0n,tax=0n;
 for(const raw of request.ProductItem){
  const line=obj(raw);
  if(line.OriginalInvoiceNumber!==f.originalInvoiceNumber||
     day(line.OriginalInvoiceDate)!==originDay||
     typeof line.OriginalDescription!=="string"||
     line.OriginalDescription.length<1||line.OriginalDescription.length>256||
     !["1","2","3"].includes(String(line.TaxType)))
   fail("SOURCE_ITEM_INVALID");
  let quantity:bigint,amount:bigint,itemTax:bigint;
  try{
   quantity=fixed7(line.Quantity);
   fixed7(line.UnitPrice,true);
   amount=fixed7(line.Amount,true);
   itemTax=fixed7(line.Tax,true);
  }catch{fail("SOURCE_AMOUNT_INVALID");}
  if(quantity<=0n||(String(line.TaxType)!=="1"&&itemTax!==0n))
   fail("SOURCE_ITEM_INVALID");
  net+=amount;tax+=itemTax;
 }
 let total=0n,allTax=0n;
 try{total=fixed7(request.TotalAmount,true);allTax=fixed7(request.TaxAmount,true);}
 catch{fail("TOTAL_INVALID");}
 if(net!==total||tax!==allTax||total<=0n||allTax<0n||
    total+allTax!==BigInt(f.requestedGross)*allowanceScale)
   fail("TOTAL_MISMATCH");
}
export interface OfficialPerInvoiceProof {
 applicationId:string;
 originalInvoiceNumber:string;
 officialInvoiceGross:string;
 officialEffectiveAlreadyAllowed:string;
 officialRemainingBeforeRequest:string;
 requestedGross:string;
 officialCheckedAtUtc:string;
 officialEvidenceHash:string;
 verifiedExistingAllowanceNumbers:number;
 // This is evidence, NOT a manager's decision or authorization.
 approvalRecorded:false;
 numberReserved:false;
}
export async function reconcileOfficialAllowanceForOriginal(
 frozen:Readonly<StoredCloudAllowance>,
 credentials:Readonly<{sellerInvoice:string;appKey:string}>,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<OfficialPerInvoiceProof>{
 if(credentials.sellerInvoice!==frozen.sellerInvoice||
     !/^[A-Z]{2}\d{8}$/.test(frozen.originalInvoiceNumber)||
     !Number.isSafeInteger(frozen.requestedGross)||frozen.requestedGross<=0)
   fail("INPUT_INVALID");
 const snapshot=await queryOfficialInvoiceReadOnly({
  sellerInvoice:credentials.sellerInvoice,appKey:credentials.appKey,
  originalInvoiceNumber:frozen.originalInvoiceNumber
 },transport,clock);
 checkedAt(snapshot.checkedAtUtc,clock());
 if(snapshot.uploadStatus!==99||snapshot.cancelDate!==0||
    snapshot.voidPending||snapshot.hasUnsettledMutation||
    !snapshot.allowanceSummaryPresent||!snapshot.productItemsPresent||
    snapshot.existingAllowanceCount>16)
   fail("INVOICE_INCOMPLETE_OR_PENDING");
 const originalDay=day(snapshot.invoiceDate);
 if(!originalDay||!Number.isSafeInteger(snapshot.totalAmount)||
    snapshot.totalAmount<=0)fail("INVOICE_DATE_OR_AMOUNT_INVALID");
 proveFrozenItems(frozen,originalDay);
 let effective=0n;
 const snapshots=[snapshot.snapshotSha256];
 const seen=new Set<string>();
 for(const history of snapshot.allowanceHistory){
  if(!history.metadataComplete||!history.allowanceNumber||
    !history.allowanceDate||history.totalAmount===null||
    history.taxAmount===null||history.cancelDate===null||
    seen.has(history.allowanceNumber))
    fail("ALLOWANCE_SUMMARY_INCOMPLETE");
  seen.add(history.allowanceNumber);
  const row=await queryOfficialAllowanceDetail(
   credentials.sellerInvoice,credentials.appKey,history.allowanceNumber,
   transport,clock);
  checkedAt(row.checkedAtUtc,clock());
  if(row.allowanceDay!==history.allowanceDate)
   fail("ALLOWANCE_DATE_MISMATCH");
  let summaryNet:bigint,summaryTax:bigint;
  try{
   summaryNet=fixed7(history.totalAmount);
   summaryTax=fixed7(history.taxAmount);
  }catch{fail("ALLOWANCE_SUMMARY_AMOUNT_INVALID");}
  if(summaryNet!==row.totalNet||summaryTax!==row.totalTax)
   fail("ALLOWANCE_SUMMARY_DETAIL_MISMATCH");
  if(history.cancelDate==="0" ? row.cancelledAt!==0 :
      row.cancelledAt===0)fail("ALLOWANCE_VOID_MISMATCH");
  // Only a fully uploaded, not-pending official allowance can be counted.
  // A confirmed, finished void releases its contribution; unknown is blocked.
  if(history.uploadStatus!==99||row.status!==99)
   fail("ALLOWANCE_STATUS_UNKNOWN");
  const matching=row.lines.filter(x=>
    x.originalInvoiceNumber===frozen.originalInvoiceNumber);
  if(matching.length===0)fail("ALLOWANCE_NOT_FOR_THIS_INVOICE");
  if(matching.some(x=>x.originalInvoiceDate!==originalDay))
   fail("ALLOWANCE_ORIGINAL_DATE_MISMATCH");
  const contributed=matching.reduce((n,x)=>n+x.amount+x.tax,0n);
  if(contributed<0n)fail("ALLOWANCE_NEGATIVE_CONTRIBUTION");
  if(row.cancelledAt===0)effective+=contributed;
  snapshots.push(row.snapshotHash);
 }
 const gross=BigInt(snapshot.totalAmount)*allowanceScale;
 const requested=BigInt(frozen.requestedGross)*allowanceScale;
 if(effective>gross||requested>gross-effective)
  fail("EXCEEDS_OFFICIAL_REMAINING");
 const digest=await crypto.subtle.digest("SHA-256",new Uint8Array(
  encoder.encode(JSON.stringify([frozen.applicationId,frozen.requestHash,
    frozen.originalInvoiceNumber,snapshots,effective.toString(),
    requested.toString()]))));
 const hash=Array.from(new Uint8Array(digest),v=>v.toString(16).padStart(2,"0")).join("");
 return {applicationId:frozen.applicationId,
   originalInvoiceNumber:frozen.originalInvoiceNumber,
   officialInvoiceGross:gross.toString(),officialEffectiveAlreadyAllowed:effective.toString(),
   officialRemainingBeforeRequest:(gross-effective).toString(),
   requestedGross:requested.toString(),officialCheckedAtUtc:clock().toISOString(),
   officialEvidenceHash:hash,verifiedExistingAllowanceNumbers:seen.size,
   approvalRecorded:false,numberReserved:false};
}
