// V3 DRAFT: conservative and read-only verification of one invoice's
// unnumbered g0401 lines against official invoice_query product_item.
// AMEGO invoice query detail_vat=1 prices are gross, while g0401 amounts
// are net. Never compare those directly or imply an approval on that path.
const SCALE=10000000n;
// Mirrors CYInvoice.Core.FixedDecimal.Multiply and RoundInt64: half away
// from zero. Only positive monetary source/claim values are accepted here.
function roundedDividePositive(numerator:bigint,denominator:bigint):bigint {
  return (numerator+denominator/2n)/denominator;
}
function lineAmount(quantity:bigint,unitPrice:bigint):bigint {
  return roundedDividePositive(quantity*unitPrice,SCALE);
}
function vatInWholeTwd(taxableNet:bigint):bigint {
  return roundedDividePositive(taxableNet*5n,100n*SCALE)*SCALE;
}
type Data=Record<string,unknown>;
function object(value:unknown):Data|null {
  return value!==null && typeof value==="object" && !Array.isArray(value)
    ? value as Data : null;
}
function decimal(value:unknown):bigint|null {
  if(typeof value!=="string" && typeof value!=="number")return null;
  if(typeof value==="number" && (!Number.isFinite(value) || !Number.isSafeInteger(Math.trunc(value))))
    return null;
  const s=String(value).trim();
  const m=/^(\d{1,12})(?:\.(\d{1,7}))?$/.exec(s);
  if(!m)return null;
  return BigInt(m[1])*SCALE+BigInt((m[2]||"").padEnd(7,"0"));
}
function integer(value:unknown):number|null {
  const v=typeof value==="number"?String(value):value;
  return typeof v==="string" && /^(0|[1-9]\d{0,2})$/.test(v)?Number(v):null;
}
function day(value:unknown):string|null {
  if(typeof value!=="string"&&typeof value!=="number")return null;
  const digits=String(value).replaceAll("-","");
  if(!/^\d{8}$/.test(digits))return null;
  const year=+digits.slice(0,4),month=+digits.slice(4,6),date=+digits.slice(6,8);
  const d=new Date(Date.UTC(year,month-1,date));
  return d.getUTCFullYear()===year&&d.getUTCMonth()===month-1&&d.getUTCDate()===date
    ? digits:null;
}
function exactName(value:unknown):string|null {
  return typeof value==="string" && value.length>=1 && value.length<=256 ? value:null;
}
export type AllowanceLineVerdict = {
  fullyMatched: boolean;
  reason: string; // safe status code, never a buyer name or raw product data
  verifiedLines: number;
};
export interface OfficialItemEvidence {
  invoiceNumber:string;
  invoiceDate:unknown;
  detailVat:unknown;
  invoiceTaxAmount:unknown;
  invoiceGross:number;
  status:number;
  cancelled:boolean;
  allowanceSummaryPresent:boolean;
  existingAllowanceCount:number;
  officialItems:unknown;
}
export function validateAllowanceOriginalItems(
  json:string, evidence:OfficialItemEvidence,
  requestedInvoice:string, requestedGross:number
):AllowanceLineVerdict {
  const failed=(reason:string):AllowanceLineVerdict=>({fullyMatched:false,reason,verifiedLines:0});
  if(evidence.invoiceNumber!==requestedInvoice||evidence.status!==99||
      evidence.cancelled||!evidence.allowanceSummaryPresent||
      evidence.existingAllowanceCount!==0)
    return failed("OFFICIAL_STATE_NOT_SAFE");
  // detailVat must be EXPLICITLY zero. For 1, official amounts are gross
  // and cannot be used as net g0401 allowance item proof.
  if(evidence.detailVat!==0 && evidence.detailVat!=="0")
    return failed("ORIGINAL_TAX_BASIS_NOT_VERIFIED");
  const originalDay=day(evidence.invoiceDate);
  if(!originalDay)return failed("OFFICIAL_DATE_MISSING");
  if(!Array.isArray(evidence.officialItems)||evidence.officialItems.length<1||
     evidence.officialItems.length>9999)
    return failed("OFFICIAL_ITEMS_MISSING");
  if(!Number.isSafeInteger(requestedGross)||requestedGross<1)
    return failed("REQUEST_GROSS_INVALID");
  let draft:Data|null=null;
  try{draft=object(JSON.parse(json));}catch{return failed("REQUEST_ITEMS_INVALID");}
  if(!draft||draft.AllowanceNumber!=="" || !Array.isArray(draft.ProductItem)||
      draft.ProductItem.length===0||draft.ProductItem.length>50)
    return failed("REQUEST_ITEMS_INVALID");
  const netClaim=decimal(draft.TotalAmount),taxClaim=decimal(draft.TaxAmount);
  const invoiceTax=decimal(evidence.invoiceTaxAmount);
  const originalGross=decimal(evidence.invoiceGross);
  if(netClaim===null||taxClaim===null||invoiceTax===null||originalGross===null)
    return failed("TOTALS_MISSING");
  const originals=new Map<string,{quantity:bigint;amount:bigint}>();
  let originalNet=0n;
  for(const raw of evidence.officialItems){
    const line=object(raw);
    if(!line)return failed("OFFICIAL_ITEM_INCOMPLETE");
    const name=exactName(line.description),taxType=integer(line.tax_type);
    const quantity=decimal(line.quantity),unitPrice=decimal(line.unit_price);
    const amount=decimal(line.amount);
    if(!name||taxType===null||taxType<1||taxType>3||
        quantity===null||quantity===0n||unitPrice===null||
        amount===null||amount===0n)
      return failed("OFFICIAL_ITEM_INCOMPLETE");
    if(lineAmount(quantity,unitPrice)!==amount)
      return failed("OFFICIAL_LINE_ARITHMETIC_MISMATCH");
    originalNet+=amount;
    const key=JSON.stringify([name,taxType,String(unitPrice)]);
    // Identical official rows lack a stable line ID: ambiguous partial
    // allocation cannot authorize a later refund.
    if(originals.has(key))return failed("OFFICIAL_DUPLICATE_ITEM");
    originals.set(key,{quantity,amount});
  }
  // An inconsistent official invoice is not proof of remaining balance.
  if(originalNet+invoiceTax!==originalGross)
    return failed("OFFICIAL_TOTAL_ARITHMETIC_MISMATCH");
  const claimed=new Map<string,{quantity:bigint;amount:bigint}>();
  let net=0n,tax=0n,taxableNet=0n;
  for(const raw of draft.ProductItem){
    const line=object(raw);
    if(!line)return failed("REQUEST_ITEM_INCOMPLETE");
    if(line.OriginalInvoiceNumber!==requestedInvoice ||
       day(line.OriginalInvoiceDate)!==originalDay)
      return failed("REQUEST_ORIGINAL_INVOICE_MISMATCH");
    const name=exactName(line.OriginalDescription),taxType=integer(line.TaxType);
    const quantity=decimal(line.Quantity),unitPrice=decimal(line.UnitPrice);
    const amount=decimal(line.Amount),itemTax=decimal(line.Tax);
    if(!name||taxType===null||taxType<1||taxType>3||quantity===null||
        quantity===0n||unitPrice===null||amount===null||amount===0n||
        itemTax===null||(taxType!==1&&itemTax!==0n))
      return failed("REQUEST_ITEM_INCOMPLETE");
    if(lineAmount(quantity,unitPrice)!==amount)
      return failed("REQUEST_LINE_ARITHMETIC_MISMATCH");
    // For the narrow automatic match use only explicit standard 5% VAT
    // with whole-TWD, half-away-from-zero rounding. Unsupported rounding
    // rules must stay manual rather than look like a positive proof.
    if(itemTax%SCALE!==0n || (taxType===1 &&
       itemTax!==vatInWholeTwd(amount)))
      return failed("REQUEST_TAX_ARITHMETIC_UNVERIFIED");
    if(taxType===1)taxableNet+=amount;
    const key=JSON.stringify([name,taxType,String(unitPrice)]);
    const official=originals.get(key);
    if(!official)return failed("REQUEST_ITEM_NOT_IN_ORIGINAL");
    const before=claimed.get(key)||{quantity:0n,amount:0n};
    const newQty=before.quantity+quantity,newAmt=before.amount+amount;
    if(newQty>official.quantity||newAmt>official.amount)
      return failed("REQUEST_EXCEEDS_ORIGINAL_ITEM");
    claimed.set(key,{quantity:newQty,amount:newAmt});
    net+=amount;tax+=itemTax;
  }
  if(tax!==vatInWholeTwd(taxableNet))
    return failed("REQUEST_TAX_ARITHMETIC_UNVERIFIED");
  if(net!==netClaim||tax!==taxClaim||net+tax!==BigInt(requestedGross)*SCALE)
    return failed("REQUEST_TOTALS_MISMATCH");
  if(tax>invoiceTax||BigInt(requestedGross)*SCALE>originalGross)
    return failed("REQUEST_EXCEEDS_INVOICE_BALANCE");
  return {fullyMatched:true,reason:"SOURCE_ITEMS_MATCHED_NO_PRIOR_ALLOWANCE",
    verifiedLines:draft.ProductItem.length};
}
