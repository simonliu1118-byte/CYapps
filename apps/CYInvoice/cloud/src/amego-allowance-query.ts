// Draft-only AMEGO allowance_query readback. It is READ ONLY and NEVER
// authorizes a retry, number reassignment, or D1 "confirmed" transition.
// A matching number is not sufficient proof that OUR g0401 was accepted:
// an external system may already have occupied that same number.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {validOfficialAllowanceNumber} from "./amego-allowance-void";
const url="https://invoice-api.amego.tw/json/allowance_query";
const te=new TextEncoder();
const numberPattern=/^A[0-9]{8}(?:[0-9]{3}|[A-Z][0-9A-Z]{2})$/;
// One official allowance_query can contain lines for several original invoices.
// This checks ONLY its item-to-header arithmetic. A matching subtotal is NOT
// proof that all historical allowances were found, and never grants balance.
export interface OfficialAllowanceOriginalAllocation {
  lineTotalsVerified:boolean;
  originalNet:string|null;
  originalTax:string|null;
  originalGross:string|null;
  reason:"ALLOCATED_LINES_MATCH_HEADER"|"ITEM_AMOUNTS_UNVERIFIED"|
    "HEADER_TOTALS_MISMATCH"|"ORIGINAL_INVOICE_NOT_IN_ITEMS"|
    "OFFICIAL_STATUS_NOT_EFFECTIVE";
  safeToInferRemainingBalance:false;
}
export interface OfficialAllowanceReadback {
  number:string; invoiceType:string; uploadStatus:number;
  allowanceType:number; cancelDate:number;
  allowanceDay:string; totalAmount:string; taxAmount:string;
  originalInvoices:string[];
  originalAllocation:OfficialAllowanceOriginalAllocation;
  checkedAtUtc:string; officialSnapshotSha256:string;
  possibleMatch:boolean;
  safeToAutoResend:false;
  safeToAutoConfirm:false;
}
export interface OfficialAllowanceReadbackRequest {
  sellerInvoice:string; appKey:string; allowanceNumber:string;
  originalInvoiceNumber:string;
}
function field(x:unknown):Record<string,unknown>{
  if(!x||typeof x!=="object"||Array.isArray(x))
    throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
  return x as Record<string,unknown>;
}
function scalar(x:unknown):string{
  if(typeof x==="string")return x;
  if(typeof x==="number"&&Number.isFinite(x))return String(x);
  throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
}
function nat(x:unknown):number{
  const v=scalar(x);
  if(!/^(0|[1-9][0-9]*)$/.test(v))throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
  const n=Number(v);
  if(!Number.isSafeInteger(n))throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
  return n;
}
function amount(x:unknown):string{
  const v=scalar(x);
  if(!/^(?:0|[1-9][0-9]{0,11})(?:\.[0-9]{1,7})?$/.test(v))
    throw new Error("ALLOWANCE_QUERY_AMOUNT_INVALID");
  return v;
}
const AMOUNT_SCALE=10_000_000n;
function scaledAmount(raw:unknown):bigint|null{
  if(typeof raw!=="string"&&typeof raw!=="number")return null;
  if(typeof raw==="number"&&(!Number.isFinite(raw)||
     Math.abs(raw)>Number.MAX_SAFE_INTEGER))return null;
  const match=/^(0|[1-9][0-9]{0,11})(?:\.([0-9]{1,7}))?$/.exec(String(raw));
  return match?BigInt(match[1])*AMOUNT_SCALE+
    BigInt((match[2]??"").padEnd(7,"0")):null;
}
function formatScaled(value:bigint):string{
  const integer=value/AMOUNT_SCALE;
  const fraction=(value%AMOUNT_SCALE).toString().padStart(7,"0").replace(/0+$/,"");
  return fraction?integer+"."+fraction:integer.toString();
}
// No numeric rounding and no per-line allocation guess. Incomplete fields,
// mismatched document totals and pending/cancelled official records fail closed.
function originalAllocation(
  items:unknown[], originalInvoice:string, netHeader:string,taxHeader:string,
  officiallyEffective:boolean
):OfficialAllowanceOriginalAllocation{
  const unknown=(reason:OfficialAllowanceOriginalAllocation["reason"]):
    OfficialAllowanceOriginalAllocation=>({
      lineTotalsVerified:false,originalNet:null,originalTax:null,
      originalGross:null,reason,safeToInferRemainingBalance:false
    });
  let lineNet=0n,lineTax=0n,invoiceNet=0n,invoiceTax=0n,found=false;
  for(const raw of items){
    const row=raw as Record<string,unknown>;
    const net=scaledAmount(row.amount),tax=scaledAmount(row.tax);
    if(net===null||tax===null||net<=0n)
      return unknown("ITEM_AMOUNTS_UNVERIFIED");
    lineNet+=net;lineTax+=tax;
    if(row.original_invoice_number===originalInvoice){
      found=true;invoiceNet+=net;invoiceTax+=tax;
    }
  }
  if(lineNet!==scaledAmount(netHeader)||lineTax!==scaledAmount(taxHeader))
    return unknown("HEADER_TOTALS_MISMATCH");
  if(!found)return unknown("ORIGINAL_INVOICE_NOT_IN_ITEMS");
  if(!officiallyEffective)return unknown("OFFICIAL_STATUS_NOT_EFFECTIVE");
  return {lineTotalsVerified:true,originalNet:formatScaled(invoiceNet),
    originalTax:formatScaled(invoiceTax),
    originalGross:formatScaled(invoiceNet+invoiceTax),
    reason:"ALLOCATED_LINES_MATCH_HEADER",safeToInferRemainingBalance:false};
}

async function readLimited(res:Response):Promise<string>{
  const n=Number(res.headers.get("content-length"));
  if(n>262144)throw new Error("ALLOWANCE_QUERY_RESPONSE_TOO_LARGE");
  const reader=res.body?.getReader();
  if(!reader)throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
  let sum=0;const buffers:Uint8Array[]=[];
  try {
    while(true){
      const {done,value}=await reader.read();
      if(done)break;
      sum+=value.byteLength;
      if(sum>262144)throw new Error("ALLOWANCE_QUERY_RESPONSE_TOO_LARGE");
      buffers.push(value);
    }
  }finally{reader.releaseLock();}
  const all=new Uint8Array(sum);let offset=0;
  for(const bytes of buffers){all.set(bytes,offset);offset+=bytes.length;}
  return new TextDecoder("utf-8",{fatal:true}).decode(all);
}
export async function queryOfficialAllowanceReadOnly(
  input:OfficialAllowanceReadbackRequest,
  transport:typeof fetch=fetch,now:()=>Date=()=>new Date()
):Promise<OfficialAllowanceReadback>{
  if(!/^[0-9]{8}$/.test(input.sellerInvoice)||
     !validOfficialAllowanceNumber(input.allowanceNumber)||
     !/^[A-Z]{2}[0-9]{8}$/.test(input.originalInvoiceNumber)||
     typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256)
    throw new Error("ALLOWANCE_QUERY_INPUT_INVALID");
  const checked=now();
  if(!(checked instanceof Date)||!Number.isFinite(checked.valueOf()))
    throw new Error("ALLOWANCE_QUERY_CLOCK_INVALID");
  const data=JSON.stringify({allowance_number:input.allowanceNumber});
  const timestamp=Math.floor(checked.valueOf()/1000);
  const form=new URLSearchParams({invoice:input.sellerInvoice,data,time:String(timestamp),
    sign:signOfficialInvoiceQuery(data,timestamp,input.appKey)});
  let res:Response;
  try {
    res=await transport(url,{method:"POST",redirect:"error",
      headers:{"content-type":"application/x-www-form-urlencoded","accept":"application/json"},
      body:form.toString(),signal:AbortSignal.timeout(8000)});
  }catch {throw new Error("ALLOWANCE_QUERY_UNAVAILABLE");}
  if(!res.ok)throw new Error("ALLOWANCE_QUERY_HTTP_FAILED");
  let root:Record<string,unknown>;
  try {root=field(JSON.parse(await readLimited(res)));}
  catch(e){
    if(e instanceof Error && e.message==="ALLOWANCE_QUERY_RESPONSE_TOO_LARGE")throw e;
    throw new Error("ALLOWANCE_QUERY_RESPONSE_INVALID");
  }
  // Code 51 / not found is NOT proof that a timed-out g0401 never reached AMEGO.
  if(nat(root.code)!==0)throw new Error("ALLOWANCE_QUERY_UNCONFIRMED");
  const row=field(root.data);
  const number=scalar(row.allowance_number);
  if(number!==input.allowanceNumber)throw new Error("ALLOWANCE_QUERY_NUMBER_MISMATCH");
  const invoiceType=scalar(row.invoice_type).trim().toUpperCase();
  const uploadStatus=nat(row.invoice_status);
  const cancelDate=nat(row.cancel_date);
  const allowanceType=nat(row.allowance_type);
  const allowanceDay=scalar(row.allowance_date);
  const totalAmount=amount(row.total_amount);
  const taxAmount=amount(row.tax_amount);
  if(!/^[0-9]{8}$/.test(allowanceDay)||
     (numberPattern.test(input.allowanceNumber) &&
      allowanceDay!==input.allowanceNumber.slice(1,9)))
    throw new Error("ALLOWANCE_QUERY_DATE_MISMATCH");
  // The historic AMEGO allowance numbers used by the website don't encode
  // YYYYMMDD. Verify their date against the official field, not the ID.
  if(!Array.isArray(row.product_item)||row.product_item.length===0||
    row.product_item.length>1000)
    throw new Error("ALLOWANCE_QUERY_ITEMS_UNCONFIRMED");
  const originals:string[]=[];
  for(const raw of row.product_item){
    const item=field(raw);
    const original=scalar(item.original_invoice_number);
    if(!/^[A-Z]{2}[0-9]{8}$/.test(original))
      throw new Error("ALLOWANCE_QUERY_ORIGINAL_INVOICE_INVALID");
    originals.push(original);
  }
  const effective=["D0401","B0401","B0101","B0102"].includes(invoiceType) &&
    uploadStatus===99 && cancelDate===0 && allowanceType===2;
  const allocation=originalAllocation(row.product_item,input.originalInvoiceNumber,
    totalAmount,taxAmount,effective);
  const digest=await crypto.subtle.digest("SHA-256",
    new Uint8Array(te.encode(JSON.stringify(row))));
  const officialSnapshotSha256=Array.from(new Uint8Array(digest),v=>v.toString(16).padStart(2,"0")).join("");
  // A query from the same seller's AppKey still cannot establish WHICH
  // actor sent g0401, so never directly advance the local ledger from here.
  const possibleMatch=["D0401","B0401","B0101","B0102"].includes(invoiceType) &&
    uploadStatus===99 && cancelDate===0 && allowanceType===2 &&
    originals.every(x=>x===input.originalInvoiceNumber);
  return {number,invoiceType,uploadStatus,allowanceType,cancelDate,allowanceDay,
    totalAmount,taxAmount,originalInvoices:[...new Set(originals)],
    originalAllocation:allocation,
    checkedAtUtc:checked.toISOString(),officialSnapshotSha256,possibleMatch,
    safeToAutoResend:false,safeToAutoConfirm:false};
}
