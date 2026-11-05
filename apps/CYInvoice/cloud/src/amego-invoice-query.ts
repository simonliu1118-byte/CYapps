// CYInvoice V3 DRAFT. Official AMEGO /json/invoice_query READ-ONLY adapter.
// The documented AMEGO wire protocol requires MD5(data + time + AppKey);
// this MD5 is for interoperability only, NOT password storage or generic MAC.
// No issue, cancellation, allowance approval, number reservation, or retry.
// These are unverified *summary* fields from invoice_query allowance[].
// In particular, total_amount may span multiple original invoices. Never
// infer the original-invoice remaining allowance balance from these rows.
export interface OfficialAllowanceHistorySummary {
  allowanceNumber:string|null;
  allowanceDate:string|null;
  totalAmount:string|null;
  taxAmount:string|null;
  uploadStatus:number|null;
  cancelDate:string|null;
  metadataComplete:boolean;
}
export interface OfficialInvoiceSnapshot {
  invoiceNumber: string;
  orderId: string;
  uploadStatus: number;
  voidPending: boolean;
  hasUnsettledMutation: boolean;
  cancelDate: number;
  totalAmount: number;
  // Internal-only evidence for strict g0401 comparison. Never forward raw items.
  invoiceDate: unknown;
  detailVat: unknown;
  invoiceTaxAmount: unknown;
  originalProductItems: unknown;
  allowanceSummaryPresent: boolean;
  existingAllowanceCount: number;
  allowanceHistory: OfficialAllowanceHistorySummary[];
  safeToInferRemainingBalance: false;
  productItemsPresent: boolean;
  checkedAtUtc: string;
  snapshotSha256: string;
  // Never asserted until the separate official item and remaining balance
  // reconciliation has been completed and independently audited.
  originalItemsAndRequestFullyVerified: false;
  canApprove: false;
}
export interface OfficialInvoiceQueryInput {
  sellerInvoice: string;
  appKey: string;
  originalInvoiceNumber: string;
}
const endpoint="https://invoice-api.amego.tw/json/invoice_query";
const encoder=new TextEncoder();
const digits=/^[0-9]{8}$/;
const invoicePattern=/^[A-Z]{2}[0-9]{8}$/;
export interface OfficialQueryDiagnostic {
  endpoint:"invoice_query";
  phase:"input_validation"|"request"|"response_read"|"response_parse"|"response_validation";
  failureKind:string;
  errorName:string;
  transportCode:string|null;
  elapsedMs:number;
  responseReceived:boolean;
  httpStatus:number|null;
  providerCode:number|null;
}
export class OfficialInvoiceQueryError extends Error {
  constructor(code:string,readonly diagnostic:OfficialQueryDiagnostic){super(code);}
}
// Inspect only bounded exception chains; export classifications, never messages,
// stack, URLs, request bodies, credentials or the original Error/cause object.
function transportFailure(error:unknown):Pick<OfficialQueryDiagnostic,"failureKind"|"errorName"|"transportCode">{
  let current=error;
  let errorName="Unknown",transportCode:string|null=null,failureKind="unknown";
  for(let depth=0;depth<3&&current instanceof Error;depth++){
    if(depth===0&&["Error","TypeError","TimeoutError","AbortError","SyntaxError"].includes(current.name))errorName=current.name;
    const code=(current as Error&{code?:unknown}).code;
    if(typeof code==="string"&&/^(?:ENOTFOUND|EAI_AGAIN|ECONNREFUSED|ECONNRESET|ETIMEDOUT|UND_ERR_CONNECT_TIMEOUT|CERT_HAS_EXPIRED|DEPTH_ZERO_SELF_SIGNED_CERT|ERR_TLS_CERT_ALTNAME_INVALID|UNABLE_TO_VERIFY_LEAF_SIGNATURE)$/.test(code))transportCode=code;
    const message=current.message.slice(0,2048);
    if(current.name==="TimeoutError"||code==="ETIMEDOUT"||code==="UND_ERR_CONNECT_TIMEOUT")failureKind="timeout";
    else if(failureKind==="unknown"&&current.name==="AbortError")failureKind="aborted";
    else if(failureKind==="unknown"&&(/^(ENOTFOUND|EAI_AGAIN)$/.test(String(code))||/DNS lookup failed/i.test(message)))failureKind="dns";
    else if(failureKind==="unknown"&&(/^(CERT_HAS_EXPIRED|DEPTH_ZERO_SELF_SIGNED_CERT|ERR_TLS_CERT_ALTNAME_INVALID|UNABLE_TO_VERIFY_LEAF_SIGNATURE)$/.test(String(code))||/TLS handshake|certificate verify failed/i.test(message)))failureKind="tls";
    else if(failureKind==="unknown"&&/redirect.*(?:not allowed|mode.*error)|unexpected redirect/i.test(message))failureKind="redirect";
    else if(failureKind==="unknown"&&/^(ECONNREFUSED|ECONNRESET)$/.test(String(code)))failureKind="connection";
    current=current.cause;
  }
  return {failureKind,errorName,transportCode};
}
function md5Hex(value:string):string {
  const data=encoder.encode(value);
  const size=Math.ceil((data.length+9)/64)*64;
  const padded=new Uint8Array(size);
  padded.set(data);padded[data.length]=0x80;
  const view=new DataView(padded.buffer);
  const bits=BigInt(data.length)*8n;
  view.setUint32(size-8,Number(bits&0xffffffffn),true);
  view.setUint32(size-4,Number((bits>>32n)&0xffffffffn),true);
  const shift=[7,12,17,22,5,9,14,20,4,11,16,23,6,10,15,21];
  const k=Array.from({length:64},(_,i)=>Math.floor(Math.abs(Math.sin(i+1))*0x100000000)>>>0);
  let h0=0x67452301,h1=0xefcdab89,h2=0x98badcfe,h3=0x10325476;
  for(let offset=0;offset<size;offset+=64){
    const w=Array.from({length:16},(_,j)=>view.getUint32(offset+4*j,true));
    let a=h0,b=h1,c=h2,d=h3;
    for(let i=0;i<64;i++){
      let f=0,g=0,s=0;
      if(i<16){f=(b&c)|(~b&d);g=i;s=shift[i%4];}
      else if(i<32){f=(d&b)|(~d&c);g=(5*i+1)%16;s=shift[4+i%4];}
      else if(i<48){f=b^c^d;g=(3*i+5)%16;s=shift[8+i%4];}
      else{f=c^(b|~d);g=(7*i)%16;s=shift[12+i%4];}
      const sum=(a+f+k[i]+w[g])>>>0;
      const rotated=(sum<<s)|(sum>>>(32-s));
      const next=(b+rotated)>>>0;
      a=d;d=c;c=b;b=next;
    }
    h0=(h0+a)>>>0;h1=(h1+b)>>>0;h2=(h2+c)>>>0;h3=(h3+d)>>>0;
  }
  return [h0,h1,h2,h3].map(n=>Array.from({length:4},(_,i)=>((n>>>(8*i))&255)
    .toString(16).padStart(2,"0")).join("")).join("");
}
export function signOfficialInvoiceQuery(data:string,time:number,appKey:string):string{
  if(!Number.isSafeInteger(time)||time<=0||typeof appKey!=="string"||
      appKey.length<8||appKey.length>256)
    throw new Error("OFFICIAL_QUERY_CONFIG_INVALID");
  return md5Hex(data+String(time)+appKey);
}
function obj(v:unknown):Record<string,unknown> {
  if(!v||typeof v!=="object"||Array.isArray(v))
    throw new Error("OFFICIAL_QUERY_INVALID_RESPONSE");
  return v as Record<string,unknown>;
}
function str(v:unknown):string|null {
  return typeof v==="string"?v:typeof v==="number"&&Number.isFinite(v)?String(v):null;
}
function number(v:unknown):number|null {
  const s=str(v);
  if(!s||!/^[0-9]+(?:\.0+)?$/.test(s))return null;
  const n=Number(s);
  return Number.isSafeInteger(n)&&n>=0?n:null;
}
// The official invoice_query summary does not necessarily say which original
// invoice each allowance amount belongs to. Missing/malformed optional fields
// become NULL, never numeric zero or "not cancelled".
function historyAmount(x:unknown):string|null {
  if(typeof x!=="string"&&typeof x!=="number")return null;
  const s=String(x);
  return /^(?:0|[1-9][0-9]{0,11})(?:\.[0-9]{1,7})?$/.test(s)?s:null;
}
function historyDate(x:unknown,allowZero=false):string|null {
  if(typeof x!=="string"&&typeof x!=="number")return null;
  const s=String(x);
  if(allowZero&&s==="0")return "0";
  if(!/^[0-9]{8}$/.test(s))return null;
  const y=Number(s.slice(0,4)),m=Number(s.slice(4,6)),d=Number(s.slice(6));
  const date=new Date(Date.UTC(y,m-1,d));
  return date.getUTCFullYear()===y&&date.getUTCMonth()+1===m&&
    date.getUTCDate()===d?s:null;
}
export function summarizeOfficialAllowanceHistory(value:unknown):
  OfficialAllowanceHistorySummary[] {
  if(!Array.isArray(value)||value.length>50)
    throw new Error("OFFICIAL_ALLOWANCE_HISTORY_UNVERIFIED");
  return value.map(raw=>{
    if(!raw||typeof raw!=="object"||Array.isArray(raw))
      return {allowanceNumber:null,allowanceDate:null,totalAmount:null,
        taxAmount:null,uploadStatus:null,cancelDate:null,metadataComplete:false};
    const row=raw as Record<string,unknown>;
    const id=typeof row.allowance_number==="string" &&
      /^[a-zA-Z0-9]{1,16}$/.test(row.allowance_number)?
      row.allowance_number:null;
    const allowanceDate=historyDate(row.allowance_date);
    const totalAmount=historyAmount(row.total_amount);
    const taxAmount=historyAmount(row.tax_amount);
    const status=number(row.invoice_status);
    const cancelDate=historyDate(row.cancel_date,true);
    return {allowanceNumber:id,allowanceDate,totalAmount,taxAmount,
      uploadStatus:status,cancelDate,
      metadataComplete:id!==null&&allowanceDate!==null&&
        totalAmount!==null&&taxAmount!==null&&status!==null&&cancelDate!==null};
  });
}

async function limitedText(res:Response):Promise<string>{
  const length=Number(res.headers.get("content-length"));
  if(length>262144)throw new Error("OFFICIAL_QUERY_RESPONSE_TOO_LARGE");
  const reader=res.body?.getReader();
  if(!reader)throw new Error("OFFICIAL_QUERY_INVALID_RESPONSE");
  const chunks:Uint8Array[]=[];
  let count=0;
  try {
    while(true){
      const {done,value}=await reader.read();
      if(done)break;
      count+=value.byteLength;
      if(count>262144)throw new Error("OFFICIAL_QUERY_RESPONSE_TOO_LARGE");
      chunks.push(value);
    }
  } finally {reader.releaseLock();}
  const joined=new Uint8Array(count);let at=0;
  for(const v of chunks){joined.set(v,at);at+=v.length;}
  return new TextDecoder("utf-8",{fatal:true}).decode(joined);
}
export async function queryOfficialInvoiceReadOnly(
  input:OfficialInvoiceQueryInput,
  transport:typeof fetch=fetch,
  now:()=>Date=()=>new Date(),
):Promise<OfficialInvoiceSnapshot>{
  const started=performance.now();
  const diagnostic:OfficialQueryDiagnostic={endpoint:"invoice_query",phase:"input_validation",
    failureKind:"unknown",errorName:"Unknown",transportCode:null,elapsedMs:0,responseReceived:false,httpStatus:null,providerCode:null};
  try{return await queryOfficialInvoice(input,transport,now,diagnostic);}
  catch(error){
    const code=error instanceof Error&&/^OFFICIAL_[A-Z0-9_]{3,90}$/.test(error.message)?error.message:
      diagnostic.phase==="request"?"OFFICIAL_QUERY_UNAVAILABLE":"OFFICIAL_QUERY_INVALID_RESPONSE";
    if(diagnostic.phase!=="request"&&diagnostic.errorName==="Unknown"){
      const failure=transportFailure(error);diagnostic.errorName=failure.errorName;
      if(failure.failureKind!=="unknown")diagnostic.failureKind=failure.failureKind;
      if(failure.transportCode)diagnostic.transportCode=failure.transportCode;
    }
    diagnostic.elapsedMs=Math.max(0,Math.round(performance.now()-started));
    throw new OfficialInvoiceQueryError(code,diagnostic);
  }
}
async function queryOfficialInvoice(
  input:OfficialInvoiceQueryInput,
  transport:typeof fetch,
  now:()=>Date,
  diagnostic:OfficialQueryDiagnostic,
):Promise<OfficialInvoiceSnapshot>{
  if(!digits.test(input.sellerInvoice)||!invoicePattern.test(input.originalInvoiceNumber)||
     typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256)
    throw new Error("OFFICIAL_QUERY_INPUT_INVALID");
  const clock=now();
  if(!(clock instanceof Date)||!Number.isFinite(clock.valueOf()))
    throw new Error("OFFICIAL_QUERY_CLOCK_INVALID");
  const data=JSON.stringify({type:"invoice",invoice_number:input.originalInvoiceNumber});
  const time=Math.floor(clock.getTime()/1000);
  const body=new URLSearchParams({invoice:input.sellerInvoice,data,time:String(time),
    sign:signOfficialInvoiceQuery(data,time,input.appKey)});
  let response:Response;
  diagnostic.phase="request";
  try {
    response=await transport(endpoint,{method:"POST",
      headers:{"content-type":"application/x-www-form-urlencoded",
        "accept":"application/json"},body:body.toString(),
      signal:AbortSignal.timeout(8000),redirect:"manual"});
  }catch(error){Object.assign(diagnostic,transportFailure(error));throw new Error("OFFICIAL_QUERY_UNAVAILABLE");}
  diagnostic.responseReceived=true;diagnostic.httpStatus=response.status;
  if(response.status>=300&&response.status<400){
    diagnostic.failureKind="redirect";
    throw new Error("OFFICIAL_QUERY_REDIRECT_REJECTED");
  }
  diagnostic.failureKind="http";
  if(!response.ok)throw new Error("OFFICIAL_QUERY_HTTP_FAILED");
  diagnostic.failureKind="response";
  let root:Record<string,unknown>;
  try{
    diagnostic.phase="response_read";
    const text=await limitedText(response);
    diagnostic.phase="response_parse";
    root=obj(JSON.parse(text));
  }
  catch(e){
    const failure=transportFailure(e);
    Object.assign(diagnostic,failure,{failureKind:failure.failureKind==="unknown"?"response":failure.failureKind});
    if(e instanceof Error && e.message==="OFFICIAL_QUERY_RESPONSE_TOO_LARGE")throw e;
    throw new Error("OFFICIAL_QUERY_INVALID_RESPONSE");
  }
  diagnostic.phase="response_validation";
  diagnostic.providerCode=number(root.code);
  if(diagnostic.providerCode!==null&&diagnostic.providerCode>9999999)diagnostic.providerCode=null;
  if(number(root.code)!==0)throw new Error("OFFICIAL_QUERY_REJECTED");
  const results=Array.isArray(root.data)?root.data:[root.data];
  if(results.length!==1)throw new Error("OFFICIAL_QUERY_AMBIGUOUS");
  const record=obj(results[0]);
  const invoiceNumber=str(record.invoice_number);
  if(invoiceNumber!==input.originalInvoiceNumber)
    throw new Error("OFFICIAL_QUERY_INVOICE_MISMATCH");
  const status=number(record.status??record.invoice_status);
  const cancel=number(record.cancel_date);
  const gross=number(record.total_amount);
  // AMEGO's invoice_query may expose pending cancellation under wait[].
  // A pending C0501 is NOT proof the invoice remains usable.
  const pendingFromWait=Array.isArray(record.wait)&&record.wait.some(value=>{
    if(!value||typeof value!=="object"||Array.isArray(value))return true;
    const item=value as Record<string,unknown>;
    const kind=str(item.invoice_type??item.type);
    return !kind || kind.toUpperCase()==="C0501";
  });
  const voidPending=pendingFromWait||
    record.void_pending===true||record.void_pending===1||
    record.void_pending==="1";
  // An unresolved D0401 or any other pending mutation also blocks
  // safe remaining-balance inference, even when allowance[] is empty.
  const hasUnsettledMutation=Array.isArray(record.wait)
    ? record.wait.length>0
    : record.wait!==undefined && record.wait!==null;
  const allowanceSummaryPresent=Array.isArray(record.allowance);
  if(status===null||cancel===null||gross===null ||
     (!allowanceSummaryPresent && record.allowance!==undefined && record.allowance!==null))
    throw new Error("OFFICIAL_QUERY_INCOMPLETE");
  // This parsing supplies only a manual-review summary. It cannot prove
  // tax basis, actual allocated amount, or any available refund balance.
  const allowanceHistory=allowanceSummaryPresent?
    summarizeOfficialAllowanceHistory(record.allowance):[];
  const productItemsPresent=Array.isArray(record.product_item);
  const digest=await crypto.subtle.digest("SHA-256",
    new Uint8Array(encoder.encode(JSON.stringify(record))));
  const snapshotSha256=Array.from(new Uint8Array(digest),
    n=>n.toString(16).padStart(2,"0")).join("");
  return {invoiceNumber,orderId:typeof record.order_id==="string"?record.order_id:typeof record.OrderId==="string"?record.OrderId:"",uploadStatus:status,voidPending,hasUnsettledMutation,cancelDate:cancel,
    totalAmount:gross,
    invoiceDate:record.date??record.invoice_date??null,
    detailVat:record.detail_vat??null,
    invoiceTaxAmount:record.tax_amount??null,
    originalProductItems:record.product_item??null,
    allowanceSummaryPresent,
    existingAllowanceCount:allowanceHistory.length,allowanceHistory,
    safeToInferRemainingBalance:false,
    productItemsPresent,checkedAtUtc:clock.toISOString(),snapshotSha256,
    originalItemsAndRequestFullyVerified:false,canApprove:false};
}
