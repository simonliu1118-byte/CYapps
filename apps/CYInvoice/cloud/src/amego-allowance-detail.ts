// V3.2-C3b2a: signed, read-only allowance_query detail.
// Adapted from #440's historical allowance_query source with the OFFICIAL
// per-original-invoice line amounts needed to avoid using invoice_query
// cross-invoice summary totals as this invoice's remaining balance.
// No retry, no state change, no g0401/g0501. Never expose raw buyer fields.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {fixed7,parseAllowanceFinancialJson} from "./allowance-decimal";
function validDay(s:string):boolean{
 if(!/^\d{8}$/.test(s))return false;
 const y=+s.slice(0,4),m=+s.slice(4,6),d=+s.slice(6,8);
 const dt=new Date(Date.UTC(y,m-1,d));
 return dt.getUTCFullYear()===y&&dt.getUTCMonth()+1===m&&dt.getUTCDate()===d;
}
function obj(x:unknown):Record<string,unknown>{
 if(!x||typeof x!=="object"||Array.isArray(x))
  throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
 return x as Record<string,unknown>;
}
function scalar(x:unknown):string {
 if(typeof x==="string")return x;
 if(typeof x==="number" && Number.isFinite(x))return String(x);
 throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
}
function natural(x:unknown):number{
 const s=scalar(x);
 if(!/^(0|[1-9][0-9]*)$/.test(s))
  throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
 const n=Number(s);
 if(!Number.isSafeInteger(n))throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
 return n;
}
export interface OfficialAllowanceLine{
 originalInvoiceNumber:string;
 originalInvoiceDate:string;
 amount:bigint;
 tax:bigint;
}
export interface OfficialAllowanceDetail {
 allowanceNumber:string;
 allowanceDay:string;
 invoiceType:string;
 status:number;
 cancelledAt:number;
 totalNet:bigint;
 totalTax:bigint;
 lines:OfficialAllowanceLine[];
 checkedAtUtc:string;
 snapshotHash:string;
}
async function readBounded(res:Response):Promise<string>{
 const cl=res.headers.get("content-length");
 if(cl!==null&&(!/^\d+$/.test(cl)||Number(cl)>262144))
  throw new Error("OFFICIAL_DETAIL_TOO_LARGE");
 if(!res.body)throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
 const reader=res.body.getReader(),chunks:Uint8Array[]=[];let count=0;
 try{
  for(;;){
   const {done,value}=await reader.read();if(done)break;
   count+=value.byteLength;
   if(count>262144)throw new Error("OFFICIAL_DETAIL_TOO_LARGE");
   chunks.push(value);
  }
 }finally{reader.releaseLock();}
 const all=new Uint8Array(count);let i=0;
 for(const b of chunks){all.set(b,i);i+=b.length;}
 return new TextDecoder("utf-8",{fatal:true}).decode(all);
}
const te=new TextEncoder();
export async function queryOfficialAllowanceDetail(
 sellerInvoice:string,appKey:string,number:string,
 transport:typeof fetch=fetch,clock:()=>Date=()=>new Date()
):Promise<OfficialAllowanceDetail>{
 if(!/^\d{8}$/.test(sellerInvoice)||
    typeof appKey!=="string"||appKey.length<8||appKey.length>256||
    !/^[A-Za-z0-9]{1,16}$/.test(number))
  throw new Error("OFFICIAL_DETAIL_INPUT_INVALID");
 const at=clock();
 if(!(at instanceof Date)||!Number.isFinite(at.getTime()))
  throw new Error("OFFICIAL_DETAIL_CLOCK_INVALID");
 const data=JSON.stringify({allowance_number:number});
 const time=Math.floor(at.getTime()/1000);
 const form=new URLSearchParams({invoice:sellerInvoice,data,time:String(time),
   sign:signOfficialInvoiceQuery(data,time,appKey)});
 let response:Response;
 try{
  response=await transport("https://invoice-api.amego.tw/json/allowance_query",{
   method:"POST",redirect:"manual",headers:{
    "content-type":"application/x-www-form-urlencoded",accept:"application/json"},
   body:form.toString(),signal:AbortSignal.timeout(8000)
  });
 }catch{throw new Error("OFFICIAL_DETAIL_UNAVAILABLE");}
 if(response.status>=300&&response.status<400)throw new Error("OFFICIAL_DETAIL_REDIRECT_REJECTED");
 if(!response.ok)throw new Error("OFFICIAL_DETAIL_HTTP_FAILED");
 let dataRoot:Record<string,unknown>;
 try{dataRoot=obj(parseAllowanceFinancialJson(await readBounded(response)));}
 catch(e){
  if(e instanceof Error&&e.message==="OFFICIAL_DETAIL_TOO_LARGE")throw e;
  throw new Error("OFFICIAL_DETAIL_INCOMPLETE");
 }
 if(natural(dataRoot.code)!==0)throw new Error("OFFICIAL_DETAIL_NOT_FOUND_OR_UNCONFIRMED");
 const row=obj(dataRoot.data);
 if(scalar(row.allowance_number)!==number)
  throw new Error("OFFICIAL_DETAIL_NUMBER_CHANGED");
 const type=scalar(row.invoice_type);
 if(!["D0401","B0401","B0101","B0102"].includes(type))
  throw new Error("OFFICIAL_DETAIL_TYPE_UNKNOWN");
 const day=scalar(row.allowance_date);
 if(!validDay(day))throw new Error("OFFICIAL_DETAIL_DATE_INVALID");
 const status=natural(row.invoice_status);
 const cancelledAt=natural(row.cancel_date);
 if(status!==99||natural(row.allowance_type)!==2||
   !Array.isArray(row.wait)||row.wait.length!==0)
  throw new Error("OFFICIAL_DETAIL_PENDING_OR_UNKNOWN");
 if(!Array.isArray(row.product_item)||row.product_item.length<1||
     row.product_item.length>250)
  throw new Error("OFFICIAL_DETAIL_ITEMS_INCOMPLETE");
 const lines:OfficialAllowanceLine[]=row.product_item.map(raw=>{
  const item=obj(raw),original=scalar(item.original_invoice_number);
  const invoiceDay=scalar(item.original_invoice_date);
  if(!/^[A-Z]{2}[0-9]{8}$/.test(original)||!validDay(invoiceDay)||
      !["1","2","3"].includes(scalar(item.tax_type)))
   throw new Error("OFFICIAL_DETAIL_ITEM_INVALID");
  const amount=fixed7(item.amount,true),tax=fixed7(item.tax,true);
  if(scalar(item.tax_type)!=="1"&&tax!==0n)
   throw new Error("OFFICIAL_DETAIL_ITEM_TAX_INVALID");
  return {originalInvoiceNumber:original,originalInvoiceDate:invoiceDay,amount,tax};
 });
 const net=fixed7(row.total_amount),tax=fixed7(row.tax_amount);
 if(lines.reduce((n,x)=>n+x.amount,0n)!==net||
    lines.reduce((n,x)=>n+x.tax,0n)!==tax ||
    net+tax<=0n)
  throw new Error("OFFICIAL_DETAIL_TOTAL_MISMATCH");
 const digest=await crypto.subtle.digest("SHA-256",
   te.encode(JSON.stringify(row)));
 return {allowanceNumber:number,allowanceDay:day,
   invoiceType:type,status,cancelledAt,totalNet:net,totalTax:tax,
   lines,checkedAtUtc:at.toISOString(),
   snapshotHash:Array.from(new Uint8Array(digest),
      x=>x.toString(16).padStart(2,"0")).join("")};
}
