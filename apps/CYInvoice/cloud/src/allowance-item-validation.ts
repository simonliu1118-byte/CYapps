// Gross-only allowance gate. Employees choose and edit g0401 line items;
// official capacity is invoice gross minus completed, effective allowances.
// Unknown official history is NOT zero. No per-product balance estimation.
const SCALE=10000000n;
type Obj=Record<string,unknown>;
const obj=(v:unknown):Obj|null=>v!==null&&typeof v==="object"&&
  !Array.isArray(v)?v as Obj:null;
function dec(value:unknown,signed=false):bigint|null {
  if(typeof value!=="string"&&typeof value!=="number")return null;
  if(typeof value==="number"&&(!Number.isFinite(value)||
     !Number.isSafeInteger(Math.trunc(value))))return null;
  const m=/^(-?)(0|[1-9][0-9]{0,11})(?:\.([0-9]{1,7}))?$/.exec(String(value));
  if(!m||(!signed&&m[1]==="-"))return null;
  const valueScaled=BigInt(m[2])*SCALE+
    BigInt((m[3]??"").padEnd(7,"0"));
  return m[1]==="-"?-valueScaled:valueScaled;
}
function date(v:unknown):string|null {
  if(typeof v!=="string"&&typeof v!=="number")return null;
  const s=String(v).replaceAll("-","");
  if(!/^[0-9]{8}$/.test(s))return null;
  const y=+s.slice(0,4),m=+s.slice(4,6),d=+s.slice(6,8);
  const dt=new Date(Date.UTC(y,m-1,d));
  return dt.getUTCFullYear()===y&&dt.getUTCMonth()+1===m&&
    dt.getUTCDate()===d?s:null;
}
function lineGross(q:bigint,unit:bigint):bigint {
  const product=q*unit;
  return product>=0n?(product+SCALE/2n)/SCALE:
    -((-product+SCALE/2n)/SCALE);
}
export interface OfficialItemEvidence {
  invoiceNumber:string;invoiceDate:unknown;detailVat:unknown;
  invoiceTaxAmount:unknown;invoiceGross:number;status:number;
  cancelled:boolean;allowanceSummaryPresent:boolean;
  existingAllowanceCount:number;officialItems:unknown;
  allowanceHistory?:unknown;
}
export interface AllowanceLineVerdict {
  fullyMatched:boolean;reason:string;verifiedLines:number;
}
// invoice_query allowance[] amounts are for this invoice, not a separate
// cross-invoice list. This is a single official snapshot and must be fresh.
export function remainingOfficialGross(x:OfficialItemEvidence):bigint|null {
  if(!x.allowanceSummaryPresent||!Number.isSafeInteger(x.invoiceGross)||
     x.invoiceGross<=0||!Number.isSafeInteger(x.existingAllowanceCount)||
     x.existingAllowanceCount<0)return null;
  const history=x.allowanceHistory??(x.existingAllowanceCount===0?[]:null);
  if(!Array.isArray(history)||history.length!==x.existingAllowanceCount||
     history.length>50)return null;
  let taken=0n;const known=new Set<string>();
  for(const raw of history){
    const h=obj(raw);
    if(!h||h.metadataComplete!==true||
       typeof h.allowanceNumber!=="string"||
       !/^[A-Za-z0-9]{1,16}$/.test(h.allowanceNumber)||
       known.has(h.allowanceNumber)||!date(h.allowanceDate))return null;
    known.add(h.allowanceNumber);
    if(h.uploadStatus!==99)return null;
    if(h.cancelDate!=="0"){
      if(!date(h.cancelDate))return null;
      continue; // Confirmed void: this allowance no longer consumes capacity.
    }
    const net=dec(h.totalAmount),tax=dec(h.taxAmount);
    if(net===null||tax===null)return null;
    taken+=net+tax;
  }
  const total=BigInt(x.invoiceGross)*SCALE;
  return taken<=total?total-taken:null;
}
export function validateAllowanceOriginalItems(
  json:string,x:OfficialItemEvidence,invoice:string,gross:number
):AllowanceLineVerdict {
  const no=(reason:string):AllowanceLineVerdict=>({
    fullyMatched:false,reason,verifiedLines:0});
  if(x.invoiceNumber!==invoice||x.status!==99||x.cancelled)
    return no("OFFICIAL_STATE_NOT_SAFE");
  const remaining=remainingOfficialGross(x);
  if(remaining===null)return no("OFFICIAL_HISTORY_UNVERIFIED");
  if(!Number.isSafeInteger(gross)||gross<=0)return no("REQUEST_GROSS_INVALID");
  if(BigInt(gross)*SCALE>remaining)return no("REQUEST_EXCEEDS_REMAINING");
  const origDay=date(x.invoiceDate);
  if(!origDay)return no("OFFICIAL_DATE_MISSING");
  let data:Obj|null=null;
  try{data=obj(JSON.parse(json));}catch{return no("REQUEST_ITEMS_INVALID");}
  if(!data||data.AllowanceNumber!==""||
     !Array.isArray(data.ProductItem)||data.ProductItem.length<1||
     data.ProductItem.length>50)return no("REQUEST_ITEMS_INVALID");
  const total=dec(data.TotalAmount,true),tax=dec(data.TaxAmount,true);
  if(total===null||tax===null||total<=0n||tax<0n)
    return no("REQUEST_TOTALS_MISMATCH");
  let netSum=0n,taxSum=0n;
  for(const p of data.ProductItem){
    const line=obj(p);
    if(!line)return no("REQUEST_ITEM_INCOMPLETE");
    if(line.OriginalInvoiceNumber!==invoice||
       date(line.OriginalInvoiceDate)!==origDay)
      return no("REQUEST_ORIGINAL_INVOICE_MISMATCH");
    if(typeof line.OriginalDescription!=="string"||
       line.OriginalDescription.length<1||
       line.OriginalDescription.length>256||
       !["1","2","3"].includes(String(line.TaxType)))
      return no("REQUEST_ITEM_INCOMPLETE");
    const qty=dec(line.Quantity),price=dec(line.UnitPrice,true),
      amount=dec(line.Amount,true),itemTax=dec(line.Tax,true);
    if(qty===null||qty<=0n||price===null||
       amount===null||itemTax===null)
      return no("REQUEST_ITEM_INCOMPLETE");
    if(lineGross(qty,price)!==amount)
      return no("REQUEST_LINE_ARITHMETIC_MISMATCH");
    if(String(line.TaxType)!=="1"&&itemTax!==0n)
      return no("REQUEST_ITEM_INCOMPLETE");
    netSum+=amount;taxSum+=itemTax;
  }
  if(total!==netSum||tax!==taxSum||
     total+tax!==BigInt(gross)*SCALE)
    return no("REQUEST_TOTALS_MISMATCH");
  return {fullyMatched:true,reason:"REQUEST_TOTALS_WITHIN_REMAINING",
    verifiedLines:data.ProductItem.length};
}
