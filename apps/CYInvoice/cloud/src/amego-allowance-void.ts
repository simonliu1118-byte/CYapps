// V3.2-E: internal AMEGO g0501 one-shot transport ONLY.
// Caller must first persist an approved ALLOWANCE_VOID Work Item and win
// the D1 send-once CAS. NEVER retry a mutation after any ambiguous result.
// Historical official allowance IDs are not necessarily CYInvoice A-date IDs.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";

export interface OneShotAllowanceVoidInput {
  sellerInvoice:string;
  appKey:string;
  allowanceNumber:string;
}
export type OneShotAllowanceVoidOutcome =
  | {kind:"accepted_unconfirmed";code:0}
  | {kind:"definite_rejection_manual";code:number}
  | {kind:"unknown_result"};

export function validOfficialAllowanceNumber(value:unknown):value is string {
  return typeof value==="string" && value.length>=1 && value.length<=16 &&
    value.trim()===value && !/[\u0000-\u001f\u007f]/u.test(value);
}

async function boundedReply(res:Response):Promise<string>{
  const length=res.headers.get("content-length");
  if(length!==null && (!/^\d+$/.test(length)||Number(length)>32768))
    throw new Error("G0501_REPLY_UNCONFIRMED");
  if(!res.body)throw new Error("G0501_REPLY_UNCONFIRMED");
  const reader=res.body.getReader();
  const chunks:Uint8Array[]=[];let total=0;
  try{
    for(;;){
      const {done,value}=await reader.read();
      if(done)break;
      total+=value.byteLength;
      if(total>32768)throw new Error("G0501_REPLY_UNCONFIRMED");
      chunks.push(value);
    }
  }finally{reader.releaseLock();}
  const bytes=new Uint8Array(total);let off=0;
  for(const c of chunks){bytes.set(c,off);off+=c.byteLength;}
  return new TextDecoder("utf-8",{fatal:true}).decode(bytes);
}

export async function postAllowanceVoidOnce(
  input:OneShotAllowanceVoidInput,transport:typeof fetch=fetch,
  now:()=>Date=()=>new Date()
):Promise<OneShotAllowanceVoidOutcome>{
  if(!input||!/^\d{8}$/.test(input.sellerInvoice)||
    !validOfficialAllowanceNumber(input.allowanceNumber)||
    typeof input.appKey!=="string"||input.appKey.length<8||
    input.appKey.length>256)
    throw new Error("G0501_INPUT_INVALID");
  const clock=now();
  if(!(clock instanceof Date)||!Number.isFinite(clock.valueOf()))
    throw new Error("G0501_CLOCK_INVALID");
  const data=JSON.stringify([{CancelAllowanceNumber:input.allowanceNumber}]);
  const time=Math.floor(clock.valueOf()/1000);
  const body=new URLSearchParams({invoice:input.sellerInvoice,data,
    time:String(time),sign:signOfficialInvoiceQuery(data,time,input.appKey)});
  let response:Response;
  try{
    response=await transport("https://invoice-api.amego.tw/json/g0501",{
      method:"POST",redirect:"error",signal:AbortSignal.timeout(8000),
      headers:{"content-type":"application/x-www-form-urlencoded",
        accept:"application/json"},body:body.toString()
    });
  }catch{return{kind:"unknown_result"};}
  if(!response.ok)return{kind:"unknown_result"};
  let parsed:unknown;
  try{parsed=JSON.parse(await boundedReply(response));}
  catch{return{kind:"unknown_result"};}
  if(!parsed||typeof parsed!=="object"||Array.isArray(parsed))
    return{kind:"unknown_result"};
  const code=(parsed as {code?:unknown}).code;
  const v=typeof code==="number"&&Number.isSafeInteger(code)?code:
    typeof code==="string"&&/^(0|[1-9]\d{0,9})$/.test(code)?Number(code):null;
  if(v===null||!Number.isSafeInteger(v)||v<0)return{kind:"unknown_result"};
  if(v===0)return{kind:"accepted_unconfirmed",code:0};
  return{kind:"definite_rejection_manual",code:v};
}
