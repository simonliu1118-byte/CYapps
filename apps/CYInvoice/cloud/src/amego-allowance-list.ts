// V3 Draft official allowance_list page. Read-only, whitelisted metadata.
// This is a coarse anomaly locator, NOT reconciliation evidence for an
// automatic D1 transition. Compare the two supported date dimensions before
// selectively calling allowance_query for an existing exception Work Item.
import {signOfficialInvoiceQuery} from "./amego-invoice-query";
import {validOfficialAllowanceNumber} from "./amego-allowance-void";

export type OfficialAllowanceDateFilter=1|2;
export interface AllowanceListPageInput{
  sellerInvoice:string;appKey:string;
  dateSelect:OfficialAllowanceDateFilter;
  dateStart:string;dateEnd:string;page:number;limit:number;
}
export interface OfficialAllowanceListItem{
  number:string;type:string;status:number;allowanceDate:string|null;
}
export interface OfficialAllowanceListPage{
  dateSelect:OfficialAllowanceDateFilter;
  page:number;pageTotal:number;dataTotal:number;
  items:OfficialAllowanceListItem[];
  checkedAtUtc:string;
  snapshotSha256:string;
  canAutoConfirm:false;
}
function natural(x:unknown):number{
  const str=typeof x==="number"&&Number.isSafeInteger(x)?String(x):x;
  if(typeof str!=="string"||!/^(0|[1-9][0-9]{0,8})$/.test(str))
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  const n=Number(str);
  if(!Number.isSafeInteger(n))throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  return n;
}
function object(x:unknown):Record<string,unknown>{
  if(!x||typeof x!=="object"||Array.isArray(x))
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  return x as Record<string,unknown>;
}
function compactDate(input:unknown):string|null{
  if(input===undefined||input===null||input==="")return null;
  if(typeof input!=="string"&&typeof input!=="number")
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  const s=String(input);
  if(!/^\d{8}$/.test(s))throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  return s;
}
function validDate(s:string):boolean{
  if(!/^\d{8}$/.test(s))return false;
  const yyyy=Number(s.slice(0,4)),mm=Number(s.slice(4,6)),dd=Number(s.slice(6,8));
  const d=new Date(Date.UTC(yyyy,mm-1,dd));
  return d.getUTCFullYear()===yyyy&&d.getUTCMonth()===mm-1&&d.getUTCDate()===dd;
}
async function readBounded(res:Response):Promise<string>{
  const len=res.headers.get("content-length");
  if(len!==null&&(!/^\d+$/.test(len)||Number(len)>524288))
    throw new Error("ALLOWANCE_LIST_TOO_LARGE");
  const reader=res.body?.getReader();
  if(!reader)throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  const chunks:Uint8Array[]=[];let lenRead=0;
  try{
    while(true){
      const {value,done}=await reader.read();
      if(done)break;
      lenRead+=value.byteLength;
      if(lenRead>524288)throw new Error("ALLOWANCE_LIST_TOO_LARGE");
      chunks.push(value);
    }
  }finally{reader.releaseLock();}
  const bytes=new Uint8Array(lenRead);let at=0;
  for(const c of chunks){bytes.set(c,at);at+=c.byteLength;}
  return new TextDecoder("utf-8",{fatal:true}).decode(bytes);
}
export async function fetchOfficialAllowancePage(
  input:AllowanceListPageInput,transport:typeof fetch=fetch,
  now:()=>Date=()=>new Date()
):Promise<OfficialAllowanceListPage>{
  if(!input||!/^\d{8}$/.test(input.sellerInvoice)||
    typeof input.appKey!=="string"||input.appKey.length<8||input.appKey.length>256||
    (input.dateSelect!==1&&input.dateSelect!==2)||
    !validDate(input.dateStart)||!validDate(input.dateEnd)||
    input.dateStart>input.dateEnd||
    !Number.isSafeInteger(input.page)||input.page<1||input.page>10000||
    !Number.isSafeInteger(input.limit)||input.limit<20||input.limit>500)
    throw new Error("ALLOWANCE_LIST_INPUT_INVALID");
  const clock=now();
  if(!(clock instanceof Date)||!Number.isFinite(clock.getTime()))
    throw new Error("ALLOWANCE_LIST_CLOCK_INVALID");
  const data=JSON.stringify({date_select:input.dateSelect,date_start:input.dateStart,
    date_end:input.dateEnd,limit:input.limit,page:input.page});
  const timestamp=Math.floor(clock.getTime()/1000);
  const body=new URLSearchParams({invoice:input.sellerInvoice,data,
    time:String(timestamp),sign:signOfficialInvoiceQuery(data,timestamp,input.appKey)});
  let res:Response;
  try{
    res=await transport("https://invoice-api.amego.tw/json/allowance_list",{
      method:"POST",redirect:"error",signal:AbortSignal.timeout(8000),
      headers:{"content-type":"application/x-www-form-urlencoded",accept:"application/json"},
      body:body.toString()
    });
  }catch{throw new Error("ALLOWANCE_LIST_UNAVAILABLE");}
  if(!res.ok)throw new Error("ALLOWANCE_LIST_HTTP_UNCONFIRMED");
  let root:Record<string,unknown>;
  try{root=object(JSON.parse(await readBounded(res)));}
  catch(e){
    if(e instanceof Error&&e.message==="ALLOWANCE_LIST_TOO_LARGE")throw e;
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  }
  if(natural(root.code)!==0)throw new Error("ALLOWANCE_LIST_OFFICIAL_REJECTED");
  const pageTotal=natural(root.page_total),pageNow=natural(root.page_now);
  const dataTotal=natural(root.data_total);
  if(pageNow!==input.page||pageTotal<input.page&&!(pageTotal===0&&
    input.page===1&&dataTotal===0))
    throw new Error("ALLOWANCE_LIST_PAGINATION_CHANGED");
  if(!Array.isArray(root.data)||root.data.length>input.limit)
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  const ids=new Set<string>();
  const items:OfficialAllowanceListItem[]=root.data.map(raw=>{
    const row=object(raw);
    if(!validOfficialAllowanceNumber(row.allowance_number)||
      ids.has(row.allowance_number))
      throw new Error("ALLOWANCE_LIST_DUPLICATE_OR_BAD_NUMBER");
    ids.add(row.allowance_number);
    const kind=row.invoice_type;
    if(typeof kind!=="string"||!/^[A-Z0-9_]{1,16}$/.test(kind))
      throw new Error("ALLOWANCE_LIST_INCOMPLETE");
    const status=natural(row.invoice_status);
    const allowanceDate=compactDate(row.allowance_date);
    if(allowanceDate!==null&&!validDate(allowanceDate))
      throw new Error("ALLOWANCE_LIST_INCOMPLETE");
    return {number:row.allowance_number,type:kind,status,allowanceDate};
  });
  if(dataTotal===0&&items.length!==0)
    throw new Error("ALLOWANCE_LIST_INCOMPLETE");
  const rawHash=await crypto.subtle.digest("SHA-256",
    new TextEncoder().encode(JSON.stringify(root.data)));
  const snapshotSha256=Array.from(new Uint8Array(rawHash),
    n=>n.toString(16).padStart(2,"0")).join("");
  return {dateSelect:input.dateSelect,page:input.page,pageTotal,dataTotal,items,
    checkedAtUtc:clock.toISOString(),snapshotSha256,canAutoConfirm:false};
}

export interface ExceptionWatch {
  allowanceNumber:string;
  workItemId:string;
}
export type AllowanceListHint={
  allowanceNumber:string;workItemId:string;
  observedTypes:string[];needsSelectiveQuery:boolean;
  reason:"NOT_SEEN_NOT_PROOF"|"OFFICIAL_STATUS_CHANGED_OR_PENDING"|"CONFLICTING_LIST_RESULTS";
};
// Accept only known exceptional Work Items. Do not synchronize all ordinary
// invoices or grant success from list membership / absence.
export function selectAllowanceExceptionHints(
  watches:ExceptionWatch[],pages:OfficialAllowanceListPage[]
):AllowanceListHint[]{
  const numbers=new Set<string>();
  for(const w of watches){
    if(!validOfficialAllowanceNumber(w.allowanceNumber)||
      !/^[a-zA-Z0-9_-]{10,100}$/.test(w.workItemId)||
      numbers.has(w.allowanceNumber))
      throw new Error("ALLOWANCE_WATCH_INVALID");
    numbers.add(w.allowanceNumber);
  }
  if(pages.some(p=>p.canAutoConfirm!==false))
    throw new Error("ALLOWANCE_WATCH_UNTRUSTED_PAGE");
  return watches.map(w=>{
    const rows=pages.flatMap(p=>p.items).filter(i=>i.number===w.allowanceNumber);
    const versions=new Set(rows.map(x=>x.type+":"+x.status));
    const types=[...new Set(rows.map(x=>x.type))].sort();
    const reason=versions.size>1?"CONFLICTING_LIST_RESULTS":
      rows.length===0?"NOT_SEEN_NOT_PROOF":"OFFICIAL_STATUS_CHANGED_OR_PENDING";
    return {allowanceNumber:w.allowanceNumber,workItemId:w.workItemId,
      observedTypes:types,needsSelectiveQuery:rows.length>0,reason};
  });
}
