// V3.2-E: official allowance_status is READ ONLY. D0501/99 can be a
// provider-side cancellation signal, NOT proof of this app's own g0501 attempt.
// No request, number allocation or D1 transition is ever initiated here.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {validOfficialAllowanceNumber} from "./amego-allowance-void";

export interface OfficialAllowanceVoidStatus {
  allowanceNumber:string;
  type:string;
  uploadStatus:number;
  officialCancelled:boolean;
  definitiveOpen:boolean;
  checkedAtUtc:string;
  statusEvidenceSha256:string;
  safeToAutoResend:false;
  safeToAutoClose:false;
}
export interface OfficialAllowanceStatusInput {
  sellerInvoice:string;
  appKey:string;
  allowanceNumber:string;
}
function dict(x:unknown):Record<string,unknown>{
  if(!x||typeof x!=="object"||Array.isArray(x))
    throw new Error("ALLOWANCE_STATUS_INCOMPLETE");
  return x as Record<string,unknown>;
}
function value(x:unknown):string{
  if(typeof x==="string")return x;
  if(typeof x==="number"&&Number.isFinite(x))return String(x);
  throw new Error("ALLOWANCE_STATUS_INCOMPLETE");
}
function statusNumber(x:unknown):number{
  const s=value(x);
  if(!/^(0|[1-9]\d{0,5})$/.test(s))throw new Error("ALLOWANCE_STATUS_INCOMPLETE");
  return Number(s);
}
async function readBounded(res:Response):Promise<string>{
  const header=res.headers.get("content-length");
  if(header!==null&&(!/^\d+$/.test(header)||Number(header)>65536))
    throw new Error("ALLOWANCE_STATUS_RESPONSE_TOO_LARGE");
  const reader=res.body?.getReader();
  if(!reader)throw new Error("ALLOWANCE_STATUS_INCOMPLETE");
  const chunks:Uint8Array[]=[];let size=0;
  try{
    while(true){
      const {value:bytes,done}=await reader.read();
      if(done)break;
      size+=bytes.byteLength;
      if(size>65536)throw new Error("ALLOWANCE_STATUS_RESPONSE_TOO_LARGE");
      chunks.push(bytes);
    }
  }finally{reader.releaseLock();}
  const buf=new Uint8Array(size);let off=0;
  for(const bytes of chunks){buf.set(bytes,off);off+=bytes.byteLength;}
  return new TextDecoder("utf-8",{fatal:true}).decode(buf);
}
export async function readOfficialAllowanceVoidStatus(
  input:OfficialAllowanceStatusInput,
  transport:typeof fetch=fetch,now:()=>Date=()=>new Date()
):Promise<OfficialAllowanceVoidStatus>{
  if(!input||!/^\d{8}$/.test(input.sellerInvoice)||
     !validOfficialAllowanceNumber(input.allowanceNumber)||
     typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256)
    throw new Error("ALLOWANCE_STATUS_INPUT_INVALID");
  const clock=now();
  if(!(clock instanceof Date)||!Number.isFinite(clock.valueOf()))
    throw new Error("ALLOWANCE_STATUS_CLOCK_INVALID");
  const data=JSON.stringify([{AllowanceNumber:input.allowanceNumber}]);
  const timestamp=Math.floor(clock.valueOf()/1000);
  const form=new URLSearchParams({invoice:input.sellerInvoice,data,
    time:String(timestamp),sign:signOfficialInvoiceQuery(data,timestamp,input.appKey)});
  let response:Response;
  try{
    response=await transport("https://invoice-api.amego.tw/json/allowance_status",{
      method:"POST",redirect:"manual",signal:AbortSignal.timeout(8000),
      headers:{"content-type":"application/x-www-form-urlencoded",accept:"application/json"},
      body:form.toString()
    });
  }catch{throw new Error("ALLOWANCE_STATUS_UNAVAILABLE");}
  if(response.status>=300&&response.status<400)throw new Error("ALLOWANCE_STATUS_REDIRECT_REJECTED");
  if(!response.ok)throw new Error("ALLOWANCE_STATUS_HTTP_UNCONFIRMED");
  let root:Record<string,unknown>;
  try{root=dict(JSON.parse(await readBounded(response)));}
  catch(e){
    if(e instanceof Error&&e.message==="ALLOWANCE_STATUS_RESPONSE_TOO_LARGE")throw e;
    throw new Error("ALLOWANCE_STATUS_INCOMPLETE");
  }
  if(statusNumber(root.code)!==0)throw new Error("ALLOWANCE_STATUS_UNCONFIRMED");
  const raw=root.data;
  let row:Record<string,unknown>;
  if(Array.isArray(raw)){
    if(raw.length!==1)throw new Error("ALLOWANCE_STATUS_AMBIGUOUS");
    row=dict(raw[0]);
    if(value(row.allowance_number)!==input.allowanceNumber)
      throw new Error("ALLOWANCE_STATUS_WRONG_NUMBER");
  }else{
    const all=dict(raw);
    const keys=Object.keys(all);
    if(keys.length!==1||keys[0]!==input.allowanceNumber)
      throw new Error("ALLOWANCE_STATUS_WRONG_NUMBER");
    row=dict(all[input.allowanceNumber]);
  }
  const typ=value(row.type??row.invoice_type).trim().toUpperCase();
  const status=statusNumber(row.status??row.invoice_status);
  // NOT_FOUND, unknown types or statuses MUST NOT look like a completed
  // cancellation. The caller must keep a pending Work Item for manual check.
  const cancelled=typ==="D0501"&&status===99;
  const open=typ==="D0401"&&status===99;
  const digest=await crypto.subtle.digest("SHA-256",
    new TextEncoder().encode(JSON.stringify(row)));
  const statusEvidenceSha256=Array.from(new Uint8Array(digest),
    x=>x.toString(16).padStart(2,"0")).join("");
  return{
    allowanceNumber:input.allowanceNumber,type:typ,uploadStatus:status,
    officialCancelled:cancelled,definitiveOpen:open,
    checkedAtUtc:clock.toISOString(),statusEvidenceSha256,
    safeToAutoResend:false,safeToAutoClose:false
  };
}
