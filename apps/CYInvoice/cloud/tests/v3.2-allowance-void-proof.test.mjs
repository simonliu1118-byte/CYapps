import assert from "node:assert/strict";
import {build} from "esbuild";
import {createHash} from "node:crypto";
const bundle=await build({entryPoints:["src/amego-allowance-void-proof.ts"],
 write:false,bundle:true,platform:"node",format:"esm",target:"node24"});
const {queryOfficialVoidTarget:read,freshVoidOfficialEvidence:fresh,
 sameVoidCompanyCard:sameCard}=await import("data:text/javascript;base64,"+
 Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const at=new Date("2026-10-21T06:00:00.000Z"),now=()=>at;
// Boundary checks for the shared predicates used before/after official IO.
// These DO NOT replace the independent actor/Device and D1 checks.
assert.equal(fresh(at.toISOString(),at),true);
assert.equal(fresh(new Date(at.valueOf()-60_000).toISOString(),at),true);
assert.equal(fresh(new Date(at.valueOf()-60_001).toISOString(),at),false);
assert.equal(fresh(new Date(at.valueOf()+1000).toISOString(),at),true);
assert.equal(fresh(new Date(at.valueOf()+1001).toISOString(),at),false);
assert.equal(fresh("not-a-date",at),false);
assert.equal(fresh(at.toISOString(),new Date(NaN)),false);
const card={revision:1,sellerInvoice:"12345678",amegoAppKey:"synthetic-key"};
assert.equal(sameCard(card,{...card}),true);
for(const delta of [{revision:2},{sellerInvoice:"87654321"},{amegoAppKey:"other-key"}])
 assert.equal(sameCard(card,{...card,...delta}),false);
const request={sellerInvoice:"12345678",appKey:"SYNTHETIC_ONLY_NO_REAL_SECRET",
 allowanceNumber:"ALW-001",originalInvoiceNumber:"AB12345678"};
const official={allowance_number:"ALW-001",invoice_type:"D0401",
 invoice_status:99,allowance_type:2,cancel_date:0,
 buyer_identifier:"0000000000",buyer_name:"Fake buyer",
 product_item:[{original_invoice_number:"AB12345678"}],wait:[]};
const status={code:0,data:{"ALW-001":{type:"D0401",status:99}}};
const calls=[];
function fake(query=official,state=status){
 return async (url,init)=>{
  calls.push(url);
  assert.equal(init.method,"POST");
  assert.equal(init.redirect,"error");
  assert.equal(init.headers["content-type"],"application/x-www-form-urlencoded");
  const form=new URLSearchParams(init.body);
  assert.equal(form.get("invoice"),request.sellerInvoice);
  assert.equal(form.get("time"),String(Math.floor(at.valueOf()/1000)));
  assert.equal(form.get("sign"),createHash("md5").update(form.get("data")+
    form.get("time")+request.appKey).digest("hex"));
  const expected=url.endsWith("/allowance_query")
   ?{allowance_number:request.allowanceNumber}
   :[{AllowanceNumber:request.allowanceNumber}];
  assert.deepEqual(JSON.parse(form.get("data")),expected);
  assert.ok(url.endsWith("/allowance_query")||url.endsWith("/allowance_status"));
  return Response.json(url.endsWith("/allowance_query")?{code:0,data:query}:state);
 };
}
const result=await read(request,fake(),now);
assert.deepEqual(calls,[
 "https://invoice-api.amego.tw/json/allowance_query",
 "https://invoice-api.amego.tw/json/allowance_status"]);
assert.equal(result.allowanceNumber,request.allowanceNumber);
assert.match(result.officialSnapshotHash,/^[a-f0-9]{64}$/);
assert.equal(result.eligibleForManagerPreview,true);
assert.equal(result.canApproveWithoutFullReview,false);
assert.equal(result.canSendWithoutDurableClaim,false);
assert.equal(result.proofOfOurVoidPost,false);
const negative=[
 ["wrong number",{...official,allowance_number:"OTHER"},status,"ORIGINAL_NOT_ELIGIBLE"],
 ["cancelled",{...official,invoice_type:"D0501"},status,"ORIGINAL_NOT_ELIGIBLE"],
 ["wrong buyer",{...official,buyer_identifier:""},status,"ORIGINAL_NOT_ELIGIBLE"],
 ["other invoice",{...official,product_item:[{original_invoice_number:"AB98765432"}]},status,
  "MULTIPLE_ORIGINALS_NOT_SUPPORTED"],
 ["multiple invoice",{...official,product_item:[
  {original_invoice_number:"AB12345678"},{original_invoice_number:"CD12345678"}]},status,
  "MULTIPLE_ORIGINALS_NOT_SUPPORTED"],
 ["not complete",{...official,invoice_status:91},status,"ORIGINAL_NOT_ELIGIBLE"],
 ["pending",{...official,wait:[{type:"D0501"}]},status,"ORIGINAL_NOT_ELIGIBLE"],
 ["status already voided",official,{code:0,data:{"ALW-001":{type:"D0501",status:99}}},
  "OFFICIAL_STATUS_CHANGED"],
 ["status not complete",official,{code:0,data:{"ALW-001":{type:"D0401",status:91}}},
  "OFFICIAL_STATUS_CHANGED"]
];
for(const [name,row,state,pattern] of negative){
 await assert.rejects(()=>read(request,fake(row,state),now),new RegExp("VOID_PROOF_"+pattern),name);
}
await assert.rejects(()=>read(request,async()=>{throw Error("network unavailable");},now),
 /VOID_PROOF_PROVIDER_UNAVAILABLE/);
await assert.rejects(()=>read(request,async()=>new Response("x".repeat(140000)),now),
 /VOID_PROOF_TOO_LARGE/);
await assert.rejects(()=>read({...request,allowanceNumber:"lower-case"},fake(),now),
 /VOID_PROOF_INPUT_INVALID/);
assert.ok(calls.every(x=>!x.endsWith("/g0501")),"No financial mutation was invoked");
console.log("V3.2 E signed read-only official g0501 preflight: original invoice, status, buyer, no retries or send grants OK");
