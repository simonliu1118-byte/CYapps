import assert from "node:assert/strict";
import {build} from "esbuild";
import {createHash} from "node:crypto";
const bundle=await build({entryPoints:["src/amego-allowance-issue.ts"],
 bundle:true,platform:"node",format:"esm",target:"node24",write:false});
const {postNumberedAllowanceOnce:send}=await import("data:text/javascript;base64,"+
 Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const key="SYNTHETIC_G0401_KEY_DO_NOT_USE";
const payload=JSON.stringify({AllowanceNumber:"",AllowanceDate:"20261015",
 AllowanceType:2,BuyerIdentifier:"0000000000",BuyerName:"Synthetic consumer",
 ProductItem:[{OriginalInvoiceNumber:"AB12345678",OriginalInvoiceDate:20261015,
 OriginalDescription:"Service",Quantity:1,UnitPrice:50,Amount:50,Tax:3,TaxType:1}],
 TotalAmount:50,TaxAmount:3});
const req={sellerInvoice:"12345678",appKey:key,allowanceNumber:"A20261015001",
 allowanceDay:"20261015",unnumberedTemplateJson:payload};
const date=new Date("2026-10-15T02:00:00.000Z");
let calls=0;
function gateway(reply={code:0,msg:""}){
 return async(url,options)=>{
  calls++;
  assert.equal(url,"https://invoice-api.amego.tw/json/g0401");
  assert.equal(options.method,"POST");
  assert.equal(options.redirect,"manual");
  const form=new URLSearchParams(options.body);
  const data=JSON.parse(form.get("data"));
  assert.equal(data.length,1);
  assert.equal(data[0].AllowanceNumber,"A20261015001");
  assert.equal(data[0].AllowanceDate,"20261015");
  assert.equal(form.get("sign"),createHash("md5").update(form.get("data")+
    form.get("time")+key).digest("hex"));
  return Response.json(reply);
 };
}
assert.deepEqual(await send(req,gateway(),()=>date),
 {kind:"accepted_unconfirmed",code:0});
assert.equal(calls,1,"no implicit retry");
assert.deepEqual(await send(req,gateway({code:4040161}),()=>date),
 {kind:"confirmed_number_collision",code:4040161});
assert.deepEqual(await send(req,gateway({code:4040162}),()=>date),
 {kind:"confirmed_number_collision",code:4040162});
assert.deepEqual(await send(req,gateway({code:4000123}),()=>date),
 {kind:"definite_rejection",code:4000123});
assert.deepEqual(await send(req,gateway({code:"garbled"}),()=>date),
 {kind:"unknown_result"});
assert.deepEqual(await send(req,async()=>{throw Error("timeout")},()=>date),
 {kind:"unknown_result"});
assert.deepEqual(await send(req,async()=>new Response("broken"),()=>date),
 {kind:"unknown_result"});
assert.deepEqual(await send(req,async()=>new Response("a".repeat(45000)),()=>date),
 {kind:"unknown_result"});
const before=calls;
await assert.rejects(()=>send({...req,allowanceNumber:"A20261015999",
 allowanceDay:"20261014"},gateway(),()=>date),/REQUEST_INVALID/);
await assert.rejects(()=>send({...req,unnumberedTemplateJson:JSON.stringify({
 ...JSON.parse(payload),AllowanceNumber:"A20261015001"})},gateway(),()=>date),
 /FROZEN_TEMPLATE_INCOMPLETE/);
assert.equal(calls,before);
console.log("g0401 one-shot: signed form, definite collision vs unknown, no auto retries passed");
