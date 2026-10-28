// DRAFT V3. The ONLY transport permitted to submit a previously approved,
// durably numbered g0401 request. Caller must first win the atomic D1
// "reserved -> sending" fence; a lost reply is permanently ambiguous.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {parseAllowanceFinancialJson,numberedAllowanceJson} from "./allowance-decimal";
const URL="https://invoice-api.amego.tw/json/g0401";
const TE=new TextEncoder();
type ObjectValue=Record<string,unknown>;
export interface OneShotAllowanceIssue {
  sellerInvoice:string;
  appKey:string;
  allowanceNumber:string;
  allowanceDay:string;
  unnumberedTemplateJson:string;
}
export type OneShotAllowanceOutcome =
  | {kind:"accepted_unconfirmed";code:0}
  | {kind:"confirmed_number_collision";code:4040161|4040162}
  | {kind:"definite_rejection";code:number}
  | {kind:"unknown_result"};
function record(v:unknown):ObjectValue{
  if(!v||typeof v!=="object"||Array.isArray(v))
    throw new Error("G0401_REQUEST_INVALID");
  return v as ObjectValue;
}
async function limited(response:Response):Promise<string>{
  if(Number(response.headers.get("content-length"))>32768)
    throw new Error("G0401_RESPONSE_UNCONFIRMED");
  const reader=response.body?.getReader();
  if(!reader)throw new Error("G0401_RESPONSE_UNCONFIRMED");
  let n=0;const blocks:Uint8Array[]=[];
  try{
    while(true){
      const {value,done}=await reader.read();
      if(done)break;
      n+=value.byteLength;
      if(n>32768)throw new Error("G0401_RESPONSE_UNCONFIRMED");
      blocks.push(value);
    }
  }finally{reader.releaseLock();}
  const bytes=new Uint8Array(n);
  let at=0;for(const b of blocks){bytes.set(b,at);at+=b.length;}
  return new TextDecoder("utf-8",{fatal:true}).decode(bytes);
}
// Called ONCE after the D1 send fence. Never retries / issues a new number.
export async function postNumberedAllowanceOnce(
  input:OneShotAllowanceIssue,transport:typeof fetch=fetch,now:()=>Date=()=>new Date(),observe?:(status?:number)=>Promise<void>
):Promise<OneShotAllowanceOutcome>{
  if(!/^[0-9]{8}$/.test(input.sellerInvoice)||
    !/^A[0-9]{8}(?:[0-9]{3}|[A-Z][0-9A-Z]{2})$/.test(input.allowanceNumber)||
    input.allowanceNumber.slice(1,9)!==input.allowanceDay||
    !/^[0-9]{8}$/.test(input.allowanceDay)||
    typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256||
    typeof input.unnumberedTemplateJson!=="string"||
    input.unnumberedTemplateJson.length<2||input.unnumberedTemplateJson.length>32768)
    throw new Error("G0401_REQUEST_INVALID");
  const template=record(parseAllowanceFinancialJson(input.unnumberedTemplateJson));
  if(template.AllowanceNumber!==""||template.AllowanceDate!==input.allowanceDay||
    template.AllowanceType!==2||!Array.isArray(template.ProductItem)||
    template.ProductItem.length<1||template.ProductItem.length>50||
    typeof template.BuyerIdentifier!=="string"||template.BuyerIdentifier.length<8||
    typeof template.BuyerName!=="string"||!template.BuyerName.trim())
    throw new Error("G0401_FROZEN_TEMPLATE_INCOMPLETE");
  const date=now();
  if(!(date instanceof Date)||!Number.isFinite(date.valueOf()))
    throw new Error("G0401_CLOCK_INVALID");
  const data=numberedAllowanceJson(input.unnumberedTemplateJson,input.allowanceNumber);
  const timestamp=Math.floor(date.valueOf()/1000);
  const form=new URLSearchParams({invoice:input.sellerInvoice,data,
    time:String(timestamp),sign:signOfficialInvoiceQuery(data,timestamp,input.appKey)});
  let response:Response;
  // Fail-closed: a transport exception may have occurred AFTER the provider
  // accepted the request. Never classify it as a safe definite rejection.
  try{
    response=await transport(URL,{method:"POST",redirect:"manual",
      headers:{"content-type":"application/x-www-form-urlencoded",
        "accept":"application/json"},body:form.toString(),
      signal:AbortSignal.timeout(8000)});
  }catch{return{kind:"unknown_result"};}
  await observe?.(response.status);
  // Workers supports manual, not error. Never forward a signed POST to a
  // Location target or turn a redirect into rejection/collision/retry authority.
  if(response.status>=300&&response.status<400)return{kind:"unknown_result"};
  if(!response.ok)return{kind:"unknown_result"};
  let root:ObjectValue;
  try{
    const value:unknown=JSON.parse(await limited(response));
    root=record(value);
  }catch{return{kind:"unknown_result"};}
  const code=root.code;
  const parsed=typeof code==="number"&&Number.isSafeInteger(code)?code:
    typeof code==="string"&&/^(0|[1-9][0-9]{0,9})$/.test(code)?Number(code):null;
  if(parsed===null||!Number.isSafeInteger(parsed)||parsed<0)
    return{kind:"unknown_result"};
  if(parsed===0)return{kind:"accepted_unconfirmed",code:0};
  if(parsed===4040161||parsed===4040162)
    return{kind:"confirmed_number_collision",code:parsed};
  // Other errors need explicit manual handling. A definite response does
  // NOT authorize the next number or auto retry.
  return{kind:"definite_rejection",code:parsed};
}
