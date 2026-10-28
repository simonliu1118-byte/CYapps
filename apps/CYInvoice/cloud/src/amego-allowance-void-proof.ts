// V3.2-E internal OFFICIAL read-only g0501 preflight.
// Provider-signed allowance_query + allowance_status are evidence of the
// original D0401, not transaction-origin proof of any g0501 POST.
// No public route, approval mutation, retry or financial POST.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {readOfficialAllowanceVoidStatus} from "./amego-allowance-status";

export interface VoidProofInput {
 sellerInvoice:string;appKey:string;allowanceNumber:string;originalInvoiceNumber:string;
}
export interface OfficialVoidProof {
 allowanceNumber:string;originalInvoiceNumber:string;
 checkedAtUtc:string;officialSnapshotHash:string;
 officialStatus:"D0401"|"D0501";
 detail?:unknown;eligibleForManagerPreview:true;
 canApproveWithoutFullReview:false;
 canSendWithoutDurableClaim:false;proofOfOurVoidPost:false;
}
// Shared g0501 invariants, not an authorization or a new source of truth.
// All callers must still perform their own post-IO CYID and D1 rechecks.
export function sameVoidCompanyCard(
 first:{revision:number;sellerInvoice:string;amegoAppKey:string},
 last:{revision:number;sellerInvoice:string;amegoAppKey:string}
):boolean{
 return first.revision===last.revision&&
  first.sellerInvoice===last.sellerInvoice&&
  first.amegoAppKey===last.amegoAppKey;
}
export function freshVoidOfficialEvidence(
 checkedAtUtc:string,now:Date
):boolean{
 const checked=Date.parse(checkedAtUtc);
 return now instanceof Date&&Number.isFinite(now.valueOf())&&
  Number.isFinite(checked)&&checked<=now.valueOf()+1000&&
  now.valueOf()-checked<=60_000;
}
const QUERY="https://invoice-api.amego.tw/json/allowance_query";
function dict(v:unknown):Record<string,unknown>{
 if(!v||typeof v!=="object"||Array.isArray(v))
  throw new Error("VOID_PROOF_INCOMPLETE");
 return v as Record<string,unknown>;
}
function str(v:unknown):string{
 if(typeof v==="string")return v;
 if(typeof v==="number"&&Number.isFinite(v))return String(v);
 throw new Error("VOID_PROOF_INCOMPLETE");
}
function integer(v:unknown):number{
 const s=str(v);
 if(!/^(0|[1-9]\d{0,8})$/.test(s))throw new Error("VOID_PROOF_INCOMPLETE");
 return Number(s);
}
async function bounded(r:Response):Promise<Record<string,unknown>>{
 const limit=131072;
 const header=r.headers.get("content-length");
 if(header!==null&&(!/^\d+$/.test(header)||Number(header)>limit))
  throw new Error("VOID_PROOF_TOO_LARGE");
 if(!r.body)throw new Error("VOID_PROOF_INCOMPLETE");
 const reader=r.body.getReader(),parts:Uint8Array[]=[];let n=0;
 try{
  for(;;){
   const {done,value}=await reader.read();
   if(done)break;
   n+=value.byteLength;
   if(n>limit)throw new Error("VOID_PROOF_TOO_LARGE");
   parts.push(value);
  }
 }finally{reader.releaseLock();}
 const buf=new Uint8Array(n);let at=0;
 for(const p of parts){buf.set(p,at);at+=p.length;}
 try{return dict(JSON.parse(new TextDecoder("utf-8",{fatal:true}).decode(buf)));}
 catch{throw new Error("VOID_PROOF_INCOMPLETE");}
}
export async function queryOfficialVoidTarget(
 input:VoidProofInput,transport:typeof fetch=fetch,now:()=>Date=()=>new Date()
):Promise<OfficialVoidProof>{
 if(!input||!/^\d{8}$/.test(input.sellerInvoice)||
  !/^[A-Z0-9-]{1,16}$/.test(input.allowanceNumber)||
  !/^[A-Z]{2}\d{8}$/.test(input.originalInvoiceNumber)||
  typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256)
  throw new Error("VOID_PROOF_INPUT_INVALID");
 const date=now();
 if(!(date instanceof Date)||!Number.isFinite(date.valueOf()))
  throw new Error("VOID_PROOF_CLOCK_INVALID");
 const data=JSON.stringify({allowance_number:input.allowanceNumber});
 const stamp=Math.floor(date.valueOf()/1000);
 const form=new URLSearchParams({invoice:input.sellerInvoice,data,
  time:String(stamp),sign:signOfficialInvoiceQuery(data,stamp,input.appKey)});
 let response:Response;
 try{
  response=await transport(QUERY,{method:"POST",redirect:"manual",
   signal:AbortSignal.timeout(8000),headers:{
    "content-type":"application/x-www-form-urlencoded",accept:"application/json"},
   body:form.toString()});
 }catch{throw new Error("VOID_PROOF_PROVIDER_UNAVAILABLE");}
 if(response.status>=300&&response.status<400)throw new Error("VOID_PROOF_REDIRECT_REJECTED");
 if(!response.ok)throw new Error("VOID_PROOF_PROVIDER_UNCONFIRMED");
 const body=await bounded(response);
 if(integer(body.code)!==0)throw new Error("VOID_PROOF_PROVIDER_UNCONFIRMED");
 const row=dict(body.data);
 if(str(row.allowance_number)!==input.allowanceNumber||
  !["D0401","D0501"].includes(str(row.invoice_type).toUpperCase())||
  integer(row.invoice_status)!==99||
  integer(row.allowance_type)!==2||
  typeof row.buyer_identifier!=="string"||
  !/^\d{8,10}$/.test(row.buyer_identifier)||
  typeof row.buyer_name!=="string"||!row.buyer_name.trim()||
  (row.wait!==undefined&&(!Array.isArray(row.wait)||row.wait.length!==0))||
  !Array.isArray(row.product_item)||row.product_item.length<1||
  row.product_item.length>50)
  throw new Error("VOID_PROOF_ORIGINAL_NOT_ELIGIBLE");
 for(const entry of row.product_item){
  const line=dict(entry);
  if(str(line.original_invoice_number)!==input.originalInvoiceNumber)
   throw new Error("VOID_PROOF_MULTIPLE_ORIGINALS_NOT_SUPPORTED");
 }
 // State is re-read from independent provider endpoint; neither query is
 // sufficient by itself. No response claims authorization to POST g0501.
 const status=await readOfficialAllowanceVoidStatus({
  sellerInvoice:input.sellerInvoice,appKey:input.appKey,
  allowanceNumber:input.allowanceNumber},transport,now);
 if(!status.officialCancelled&&(!status.definitiveOpen||status.type!=="D0401"||
  status.uploadStatus!==99||integer(row.cancel_date)!==0||str(row.invoice_type).toUpperCase()!=="D0401"))
  throw new Error("VOID_PROOF_OFFICIAL_STATUS_CHANGED");
 const digest=await crypto.subtle.digest("SHA-256",new TextEncoder().encode(
  JSON.stringify({provider:"AMEGO",originalInvoice:input.originalInvoiceNumber,
    allowanceNumber:input.allowanceNumber,record:row,
    statusHash:status.statusEvidenceSha256})));
 const officialSnapshotHash=Array.from(new Uint8Array(digest),
  n=>n.toString(16).padStart(2,"0")).join("");
 return {allowanceNumber:input.allowanceNumber,
  originalInvoiceNumber:input.originalInvoiceNumber,
  checkedAtUtc:date.toISOString(),officialSnapshotHash,
  detail:row,officialStatus:status.officialCancelled?"D0501":"D0401",eligibleForManagerPreview:true,
  canApproveWithoutFullReview:false,
  canSendWithoutDurableClaim:false,proofOfOurVoidPost:false};
}
