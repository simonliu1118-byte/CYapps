// Canonical seven-place decimal boundary. JSON numeric tokens must be read
// from their source, never recovered from an already-rounded JS Number.
export const allowanceScale=10000000n;
export const decimalPattern=/^-?(0|[1-9][0-9]{0,11})(?:\.([0-9]{1,7}))?$/;
export function fixed7(x:unknown,signed=false):bigint {
 if(typeof x!=="string"&&(typeof x!=="number"||!Number.isFinite(x)||!Number.isSafeInteger(x)))
  throw new Error("OFFICIAL_DETAIL_AMOUNT_INVALID");
 const text=String(x),match=decimalPattern.exec(text);
 if(!match||(!signed&&text.startsWith("-")))throw new Error("OFFICIAL_DETAIL_AMOUNT_INVALID");
 const value=BigInt(match[1])*allowanceScale+BigInt((match[2]??"").padEnd(7,"0"));
 return text.startsWith("-")?-value:value;
}
const financialFields=new Set(["Quantity","UnitPrice","GrossUnitPrice","Amount","Tax","TotalAmount","TaxAmount",
 "quantity","unit_price","amount","tax","total_amount","tax_amount"]);
function token(context?:{source?:string}):string {
 if(typeof context?.source!=="string")throw new Error("ALLOWANCE_JSON_SOURCE_UNAVAILABLE");
 return context.source;
}
export function parseAllowanceFinancialJson(text:string):unknown {
 return JSON.parse(text,(key:string,value:unknown,context?:{source?:string})=>{
  if(typeof value!=="number"||!financialFields.has(key))return value;
  // Keep the raw token even when Number rounded it to an integer. fixed7 at
  // the existing validation owner still enforces range, sign and precision.
  return token(context);
 });
}
export function numberedAllowanceJson(templateJson:string,allowanceNumber:string):string {
 const json=JSON as typeof JSON&{rawJSON?:(text:string)=>unknown};
 if(typeof json.rawJSON!=="function")throw new Error("ALLOWANCE_JSON_RUNTIME_UNAVAILABLE");
 // Preserve every numeric wire token (including its JSON number type) while
 // adding only the approved number. Frozen bytes/hash/storage are untouched.
 const template=JSON.parse(templateJson,(_key:string,value:unknown,context?:{source?:string})=>
  typeof value==="number"?json.rawJSON!(token(context)):value);
 template.AllowanceNumber=allowanceNumber;
 return JSON.stringify([template]);
}
