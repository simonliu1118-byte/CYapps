import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {build} from "esbuild";
const buildResult=await build({entryPoints:["src/amego-allowance-query.ts"],
  bundle:true,format:"esm",platform:"node",target:"node24",write:false});
const {queryOfficialAllowanceReadOnly:query}=await import("data:text/javascript;base64,"+
  Buffer.from(buildResult.outputFiles[0].text).toString("base64"));
const appKey="SYNTHETIC_READBACK_KEY_DO_NOT_USE";
const date=new Date("2026-10-15T02:00:00.000Z");
const input={sellerInvoice:"12345678",appKey,allowanceNumber:"A20261015001",
  originalInvoiceNumber:"AB12345678"};
const official={code:0,data:{allowance_number:"A20261015001",invoice_type:"D0401",
 invoice_status:99,allowance_date:"20261015",allowance_type:2,
 cancel_date:0,total_amount:"50",tax_amount:"3",
 product_item:[{original_invoice_number:"AB12345678",
   original_invoice_date:20261015,description:"Synthetic",
   quantity:"1",unit_price:"50",tax_type:1,amount:"50",tax:"3"}]}};
let requests=0;
function transport(result=official){
 return async (url,options)=>{
  requests++;
  assert.equal(url,"https://invoice-api.amego.tw/json/allowance_query");
  assert.equal(options.method,"POST");
  assert.equal(options.redirect,"error");
  const form=new URLSearchParams(options.body);
  const data='{"allowance_number":"A20261015001"}';
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(form.get("data"),data);
  assert.equal(form.get("sign"),
    createHash("md5").update(data+form.get("time")+appKey).digest("hex"));
  return Response.json(result);
 };
}
const success=await query(input,transport(),()=>date);
assert.equal(requests,1);
assert.equal(success.possibleMatch,true);
assert.equal(success.safeToAutoResend,false);
assert.equal(success.safeToAutoConfirm,false);
assert.deepEqual(success.originalInvoices,["AB12345678"]);
assert.match(success.officialSnapshotSha256,/^[0-9a-f]{64}$/);
assert.deepEqual(success.originalAllocation,{
  lineTotalsVerified:true,originalNet:"50",originalTax:"3",
  originalGross:"53",reason:"ALLOCATED_LINES_MATCH_HEADER",
  safeToInferRemainingBalance:false
},"One original invoice: exact official line sums, not spendable balance");
// An allowance may span multiple original invoices. Never apply its entire
// header to this invoice; sum only its own item rows.
const multi=await query(input,transport({code:0,data:{
  ...official.data,total_amount:"75.0000000",tax_amount:"4",
  product_item:[
    {...official.data.product_item[0],amount:"20",unit_price:"20",tax:"1"},
    {...official.data.product_item[0],original_invoice_number:"XY12345678",
      amount:"25",unit_price:"25",tax:"1"},
    {...official.data.product_item[0],amount:"30",unit_price:"30",tax:"2"}
  ]
}}),()=>date);
assert.equal(multi.possibleMatch,false);
assert.deepEqual(multi.originalAllocation,{
  lineTotalsVerified:true,originalNet:"50",originalTax:"3",
  originalGross:"53",reason:"ALLOCATED_LINES_MATCH_HEADER",
  safeToInferRemainingBalance:false
},"Only the AB original-invoice allocation is exposed");
// The exact 7-place decimal fields must use bigint, not binary floats.
const fractions=await query(input,transport({code:0,data:{
  ...official.data,total_amount:"0.6000000",tax_amount:"0",
  product_item:[
    {...official.data.product_item[0],unit_price:"0.1000001",
      amount:"0.1000001",tax:"0",tax_type:3},
    {...official.data.product_item[0],original_invoice_number:"XY12345678",
      unit_price:"0.4999999",amount:"0.4999999",tax:"0",tax_type:3}
  ]
}}),()=>date);
assert.equal(fractions.originalAllocation.originalGross,"0.1000001");
assert.equal(fractions.originalAllocation.safeToInferRemainingBalance,false);
const roundedFraction=await query(input,transport({code:0,data:{
  ...official.data,total_amount:"0.3333333",tax_amount:"0",
  product_item:[{...official.data.product_item[0],tax_type:3,
    quantity:"3",unit_price:"0.1111111",amount:"0.3333333",tax:"0"}]
}}),()=>date);
assert.equal(roundedFraction.originalAllocation.lineTotalsVerified,true,
  "7-decimal quantity * unit price is compared without Number");
assert.equal(roundedFraction.originalAllocation.originalGross,"0.3333333");
const roundedMismatch=await query(input,transport({code:0,data:{
  ...official.data,total_amount:"0.3333334",tax_amount:"0",
  product_item:[{...official.data.product_item[0],tax_type:3,
    quantity:"3",unit_price:"0.1111111",amount:"0.3333334",tax:"0"}]
}}),()=>date);
assert.equal(roundedMismatch.originalAllocation.reason,"ITEM_AMOUNTS_UNVERIFIED");
assert.equal(roundedMismatch.originalAllocation.originalGross,null);
// Header sum matching is not enough to prove an individual historical
// allowance quantity, and neither case grants an actual remaining balance.
for(const [label,row,expectedReason] of [
  ["missing line tax",{...official.data,product_item:[{
    ...official.data.product_item[0],tax:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["missing quantity",{...official.data,product_item:[{
    ...official.data.product_item[0],quantity:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["zero quantity",{...official.data,product_item:[{
    ...official.data.product_item[0],quantity:0}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["missing unit price",{...official.data,product_item:[{
    ...official.data.product_item[0],unit_price:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["mismatched line quantity",{...official.data,product_item:[{
    ...official.data.product_item[0],quantity:2}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["mismatched unit price",{...official.data,product_item:[{
    ...official.data.product_item[0],unit_price:"49.9999999"}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["invalid price precision",{...official.data,product_item:[{
    ...official.data.product_item[0],unit_price:"50.00000001"}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["missing original date",{...official.data,product_item:[{
    ...official.data.product_item[0],original_invoice_date:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["missing description",{...official.data,product_item:[{
    ...official.data.product_item[0],description:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["missing tax type",{...official.data,product_item:[{
    ...official.data.product_item[0],tax_type:undefined}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["non-taxable taxed",{...official.data,product_item:[{
    ...official.data.product_item[0],tax_type:3}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["fractional tax",{...official.data,product_item:[{
    ...official.data.product_item[0],tax:"2.5"}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["line mismatch",{...official.data,product_item:[{
    ...official.data.product_item[0],amount:"40"}]},"ITEM_AMOUNTS_UNVERIFIED"],
  ["other original",{...official.data,product_item:[{
    ...official.data.product_item[0],original_invoice_number:"XY12345678"}]},"ORIGINAL_INVOICE_NOT_IN_ITEMS"],
  ["pending upload",{...official.data,invoice_status:91},"OFFICIAL_STATUS_NOT_EFFECTIVE"],
  ["cancelled",{...official.data,cancel_date:20261016},"OFFICIAL_STATUS_NOT_EFFECTIVE"],
]){
  const readback=await query(input,transport({code:0,data:row}),()=>date);
  assert.equal(readback.originalAllocation.lineTotalsVerified,false,label);
  assert.equal(readback.originalAllocation.originalGross,null,label);
  assert.equal(readback.originalAllocation.reason,expectedReason,label);
  assert.equal(readback.originalAllocation.safeToInferRemainingBalance,false,label);
}

assert.equal((await query(input,transport({code:0,data:{
  ...official.data,invoice_status:91}}),()=>date)).possibleMatch,false);
assert.equal((await query(input,transport({code:0,data:{
  ...official.data,cancel_date:20261015}}),()=>date)).possibleMatch,false);
assert.equal((await query(input,transport({code:0,data:{
  ...official.data,product_item:[
    {original_invoice_number:"AB12345678"},
    {original_invoice_number:"XY12345678"}]}}),()=>date)).possibleMatch,false,
  "A multi-invoice allowance is NOT proof of this single-invoice request");
for(const [response,expected] of [
 [{code:51,data:null},/UNCONFIRMED/],
 [{code:0,data:{...official.data,allowance_number:"A20261015002"}},/NUMBER_MISMATCH/],
 [{code:0,data:{...official.data,product_item:[]}},/ITEMS_UNCONFIRMED/],
 [{code:0,data:{...official.data,tax_amount:"oops"}},/AMOUNT_INVALID/],
 [{code:0,data:{...official.data,allowance_date:"20261014"}},/DATE_MISMATCH/],
 ])await assert.rejects(()=>query(input,transport(response),()=>date),expected);
const historicInput={...input,allowanceNumber:"3821061800001"};
const historic=await query(historicInput,async(url,opts)=>{
  requests++;
  assert.equal(JSON.parse(new URLSearchParams(opts.body).get("data")).allowance_number,
    historicInput.allowanceNumber);
  return Response.json({code:0,data:{...official.data,allowance_number:"3821061800001"}});
},()=>date);
assert.equal(historic.possibleMatch,true,
 "Old official allowance numbers have no encoded date and must remain queryable");
const before=requests;
await assert.rejects(()=>query({...input,appKey:""},transport(),()=>date),/INPUT_INVALID/);
assert.equal(before,requests);
await assert.rejects(()=>query(input,async()=>{throw Error("network")},()=>date),/UNAVAILABLE/);
await assert.rejects(()=>query(input,async()=>new Response("a".repeat(300000)),()=>date),
 /RESPONSE_TOO_LARGE/);
console.log("Official allowance readback: number, source, seven-decimal line quantity/price math, status, no resend/auto-confirm passed");
