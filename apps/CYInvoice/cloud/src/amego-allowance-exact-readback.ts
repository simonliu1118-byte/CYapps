// V3.2-C3c3a: signed READ-ONLY original-number g0401 reconciliation.
// Adapted from the Local B2c full-item match; unlike the old #440 summary
// reader, this checks buyer, each frozen item, exact amounts and official status.
// Still NOT attribution proof that our POST created this official record.
// This internal module cannot mark the Ledger confirmed, allocate or retry.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {fixed7} from "./amego-allowance-detail";

const URL="https://invoice-api.amego.tw/json/allowance_query";
const encoder=new TextEncoder();
type Data=Record<string,unknown>;
function record(v:unknown):Data {
 if(!v||typeof v!=="object"||Array.isArray(v))
  throw new Error("G0401_READBACK_INCOMPLETE");
 return v as Data;
}
function str(v:unknown):string {
 if(typeof v==="string")return v;
 if(typeof v==="number"&&Number.isFinite(v))return String(v);
 throw new Error("G0401_READBACK_INCOMPLETE");
}
function number(v:unknown):number {
 const s=str(v);
 if(!/^(0|[1-9][0-9]*)$/.test(s))
  throw new Error("G0401_READBACK_INCOMPLETE");
 const n=Number(s);
 if(!Number.isSafeInteger(n))throw new Error("G0401_READBACK_INCOMPLETE");
 return n;
}
function day(v:unknown):string{
 const s=str(v);
 if(!/^\d{8}$/.test(s))throw new Error("G0401_READBACK_DATE_INVALID");
 const y=+s.slice(0,4),m=+s.slice(4,6),d=+s.slice(6,8);
 const dt=new Date(Date.UTC(y,m-1,d));
 if(dt.getUTCFullYear()!==y||dt.getUTCMonth()+1!==m||dt.getUTCDate()!==d)
  throw new Error("G0401_READBACK_DATE_INVALID");
 return s;
}
function amount(v:unknown,signed=false):bigint{
 try{return fixed7(v,signed);}
 catch{throw new Error("G0401_READBACK_AMOUNT_INVALID");}
}
async function limited(response:Response):Promise<Data>{
 const rawLen=response.headers.get("content-length");
 if(rawLen!==null&&(!/^\d+$/.test(rawLen)||Number(rawLen)>262144))
  throw new Error("G0401_READBACK_TOO_LARGE");
 if(!response.body)throw new Error("G0401_READBACK_INCOMPLETE");
 const reader=response.body.getReader();let total=0;const chunks:Uint8Array[]=[];
 try{
  for(;;){
   const {done,value}=await reader.read();
   if(done)break;
   total+=value.length;
   if(total>262144)throw new Error("G0401_READBACK_TOO_LARGE");
   chunks.push(value);
  }
 }finally{reader.releaseLock();}
 const bytes=new Uint8Array(total);let pos=0;
 for(const chunk of chunks){bytes.set(chunk,pos);pos+=chunk.length;}
 try{return record(JSON.parse(new TextDecoder("utf-8",{fatal:true}).decode(bytes)));}
 catch{throw new Error("G0401_READBACK_INCOMPLETE");}
}
export interface ExactAllowanceReadbackRequest {
 sellerInvoice:string;appKey:string;allowanceNumber:string;
 allowanceDay:string;unnumberedTemplateJson:string;
}
export interface ExactAllowanceReadback {
 allowanceNumber:string;
 officialCheckedAtUtc:string;
 officialSnapshotHash:string;
 // A complete exact match, still not proof WHO created the number.
 kind:"full_frozen_match_unattributed";
 safeToAutoResend:false;
 canReallocateNumber:false;
}
function checkFrozen(t:Data,input:ExactAllowanceReadbackRequest):Data[] {
 if(t.AllowanceNumber!==""||number(t.AllowanceType)!==2||
   day(t.AllowanceDate)!==input.allowanceDay||
   typeof t.BuyerIdentifier!=="string"||!/^\d{8,10}$/.test(t.BuyerIdentifier)||
   typeof t.BuyerName!=="string"||!t.BuyerName.trim()||
   !Array.isArray(t.ProductItem)||t.ProductItem.length<1||
   t.ProductItem.length>50)
  throw new Error("G0401_READBACK_FROZEN_INVALID");
 return t.ProductItem.map(record);
}
// Readback must compare all immutable payload fields to the official
// original number; a mere matching number, summary amount or status=99
// may be an externally occupied number and is NEVER enough to confirm.
export async function queryFrozenAllowanceByOriginalNumber(
 input:ExactAllowanceReadbackRequest,transport:typeof fetch=fetch,
 clock:()=>Date=()=>new Date()
):Promise<ExactAllowanceReadback|{kind:"pending_official"}>{
 if(!/^\d{8}$/.test(input.sellerInvoice)||
   typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256||
   !/^A\d{8}(?:\d{3}|[A-Z][0-9A-Z]{2})$/.test(input.allowanceNumber)||
   day(input.allowanceDay)!==input.allowanceNumber.slice(1,9)||
   typeof input.unnumberedTemplateJson!=="string"||
   input.unnumberedTemplateJson.length<2||input.unnumberedTemplateJson.length>32768)
  throw new Error("G0401_READBACK_INPUT_INVALID");
 let frozen:Data;
 try{frozen=record(JSON.parse(input.unnumberedTemplateJson));}
 catch{throw new Error("G0401_READBACK_FROZEN_INVALID");}
 const source=checkFrozen(frozen,input);
 const requestedNet=amount(frozen.TotalAmount);
 const requestedTax=amount(frozen.TaxAmount);
 if(requestedNet+requestedTax<=0n)
  throw new Error("G0401_READBACK_FROZEN_INVALID");
 let sourceNet=0n,sourceTax=0n;
 for(const line of source){
  if(!/^[A-Z]{2}\d{8}$/.test(str(line.OriginalInvoiceNumber))||
   day(line.OriginalInvoiceDate).length!==8||
   typeof line.OriginalDescription!=="string"||
   !line.OriginalDescription.trim()||
   !["1","2","3"].includes(str(line.TaxType))||
   amount(line.Quantity)<=0n)
   throw new Error("G0401_READBACK_FROZEN_INVALID");
  sourceNet+=amount(line.Amount,true);sourceTax+=amount(line.Tax,true);
 }
 if(sourceNet!==requestedNet||sourceTax!==requestedTax)
  throw new Error("G0401_READBACK_FROZEN_TOTAL_INVALID");
 const now=clock();
 if(!(now instanceof Date)||!Number.isFinite(now.getTime()))
  throw new Error("G0401_READBACK_CLOCK_INVALID");
 const data=JSON.stringify({allowance_number:input.allowanceNumber});
 const timestamp=Math.floor(now.getTime()/1000);
 const form=new URLSearchParams({invoice:input.sellerInvoice,data,
  time:String(timestamp),sign:signOfficialInvoiceQuery(data,timestamp,input.appKey)});
 let response:Response;
 try{
  response=await transport(URL,{method:"POST",redirect:"manual",
   headers:{"content-type":"application/x-www-form-urlencoded",accept:"application/json"},
   body:form.toString(),signal:AbortSignal.timeout(8000)});
 }catch{throw new Error("G0401_READBACK_UNAVAILABLE");}
 if(response.status>=300&&response.status<400)throw new Error("G0401_READBACK_REDIRECT_REJECTED");
 if(!response.ok)throw new Error("G0401_READBACK_HTTP_FAILED");
 const root=await limited(response);
 if(number(root.code)!==0)
  throw new Error("G0401_READBACK_UNCONFIRMED");
 const official=record(root.data);
 if(str(official.allowance_number)===input.allowanceNumber &&
    [1,2,3,31,32].includes(number(official.invoice_status)))
  return {kind:"pending_official"};
 if(str(official.allowance_number)!==input.allowanceNumber||
   day(official.allowance_date)!==input.allowanceDay||
   !["D0401","B0401"].includes(str(official.invoice_type).trim().toUpperCase())||
   number(official.invoice_status)!==99||
   number(official.allowance_type)!==2||
   number(official.cancel_date)!==0||
   !Array.isArray(official.wait)||official.wait.length!==0)
  throw new Error("G0401_READBACK_OFFICIAL_NOT_FINAL");
 if(str(official.buyer_identifier)!==frozen.BuyerIdentifier||
   str(official.buyer_name)!==frozen.BuyerName||
   amount(official.total_amount)!==requestedNet||
   amount(official.tax_amount)!==requestedTax||
   !Array.isArray(official.product_item)||
   official.product_item.length!==source.length)
  throw new Error("G0401_READBACK_PAYLOAD_MISMATCH");
 let sumNet=0n,sumTax=0n;
 for(let i=0;i<source.length;i++){
  const expected=source[i],actual=record(official.product_item[i]);
  // AMEGO historical records may name the same original_description
  // field as description. Require the exact same non-empty value.
  const description=actual.original_description??actual.description;
  if(str(actual.original_invoice_number)!==str(expected.OriginalInvoiceNumber)||
   day(actual.original_invoice_date)!==day(expected.OriginalInvoiceDate)||
   str(description)!==expected.OriginalDescription||
   str(actual.tax_type)!==str(expected.TaxType)||
   amount(actual.quantity)!==amount(expected.Quantity)||
   amount(actual.unit_price,true)!==amount(expected.UnitPrice,true)||
   amount(actual.amount,true)!==amount(expected.Amount,true)||
   amount(actual.tax,true)!==amount(expected.Tax,true))
   throw new Error("G0401_READBACK_ITEM_MISMATCH");
  sumNet+=amount(actual.amount,true);
  sumTax+=amount(actual.tax,true);
 }
 if(sumNet!==requestedNet||sumTax!==requestedTax)
  throw new Error("G0401_READBACK_OFFICIAL_TOTAL_MISMATCH");
 return {allowanceNumber:input.allowanceNumber,
  officialCheckedAtUtc:now.toISOString(),
  officialSnapshotHash:await snapshotHash(official),
  kind:"full_frozen_match_unattributed",
  safeToAutoResend:false,canReallocateNumber:false};
}
async function snapshotHash(official:Data):Promise<string>{
 const digest=await crypto.subtle.digest("SHA-256",encoder.encode(JSON.stringify(official)));
 return Array.from(new Uint8Array(digest),v=>v.toString(16).padStart(2,"0")).join("");
}
