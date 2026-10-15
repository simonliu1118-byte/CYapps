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
 product_item:[{original_invoice_number:"AB12345678",amount:"50",tax:"3"}]}};
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
const before=requests;
await assert.rejects(()=>query({...input,appKey:""},transport(),()=>date),/INPUT_INVALID/);
assert.equal(before,requests);
await assert.rejects(()=>query(input,async()=>{throw Error("network")},()=>date),/UNAVAILABLE/);
await assert.rejects(()=>query(input,async()=>new Response("a".repeat(300000)),()=>date),
 /RESPONSE_TOO_LARGE/);
console.log("Official allowance readback: number, source, status, signature, no resend/auto-confirm passed");
