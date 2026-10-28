import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {build} from "esbuild";
const built=await build({entryPoints:["src/amego-invoice-query.ts"],write:false,bundle:true,
 platform:"node",format:"esm",target:"node24"});
const {signOfficialInvoiceQuery:sign,queryOfficialInvoiceReadOnly:query}=
 await import("data:text/javascript;base64,"+
 Buffer.from(built.outputFiles[0].text).toString("base64"));
const key="SYNTHETIC_PRIVATE_APP_KEY_DO_NOT_USE";
const dt=new Date("2026-10-15T02:00:00.000Z");
const millis=Math.floor(dt.valueOf()/1000);
for(const data of ["","hello","光貿 UTF-8","a".repeat(120),
  JSON.stringify({type:"invoice",invoice_number:"AB12345678"})]){
  assert.equal(sign(data,millis,key),
    createHash("md5").update(data+String(millis)+key).digest("hex"),
    "AMEGO MD5 signing must exactly follow the wire algorithm");
}
const expected=JSON.stringify({type:"invoice",invoice_number:"AB12345678"});
let calls=0;
const response={code:0,msg:"",data:[{invoice_number:"AB12345678",status:99,
 cancel_date:0,total_amount:"100",allowance:[],product_item:[{description:"Synthetic"}]}]};
function transport(value=response){
 return async (url,opts)=>{
   calls++;
   assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
   assert.equal(opts.method,"POST");
   assert.equal(opts.redirect,"error");
   assert.equal(opts.headers["content-type"],"application/x-www-form-urlencoded");
   const form=new URLSearchParams(opts.body);
   assert.equal(form.get("invoice"),"12345678");
   assert.equal(form.get("data"),expected);
   assert.equal(form.get("time"),String(millis));
   assert.equal(form.get("sign"),
     createHash("md5").update(expected+String(millis)+key).digest("hex"));
   return Response.json(value);
 };
}
const request={sellerInvoice:"12345678",appKey:key,originalInvoiceNumber:"AB12345678"};
const snapshot=await query(request,transport(),()=>dt);
assert.equal(snapshot.uploadStatus,99);
assert.equal(snapshot.allowanceSummaryPresent,true);
assert.equal(snapshot.existingAllowanceCount,0);
assert.deepEqual(snapshot.allowanceHistory,[]);
assert.equal(snapshot.safeToInferRemainingBalance,false);
assert.equal(snapshot.totalAmount,100);
assert.equal(snapshot.productItemsPresent,true);
assert.equal(snapshot.canApprove,false);
assert.equal(snapshot.originalItemsAndRequestFullyVerified,false);
assert.match(snapshot.snapshotSha256,/^[a-f0-9]{64}$/);
assert.equal(calls,1,"Read only means exactly one signed POST, never retry");
const noAllowance=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:"100"
}]}),()=>dt);
assert.equal(noAllowance.allowanceSummaryPresent,false,
 "Missing allowance[] must not silently become proof of no prior allowance");
const pending=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",invoice_status:99,cancel_date:0,total_amount:100,
 allowance:[],wait:[{invoice_type:"C0501",invoice_status:1}]
}]}),()=>dt);
assert.equal(pending.voidPending,true,"Official C0501 wait[] is a pending void");
assert.equal(pending.hasUnsettledMutation,true);
const pendingAllowance=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:100,
 allowance:[],wait:[{invoice_type:"D0401",invoice_status:1}]
}]}),()=>dt);
assert.equal(pendingAllowance.hasUnsettledMutation,true,
 "Unresolved D0401 cannot be mistaken for zero past allowances");
assert.equal(pendingAllowance.canApprove,false);
assert.equal(pending.canApprove,false);
const existing=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:100,
 allowance:[{allowance_number:"A20261015001"}]}]}),()=>dt);
assert.equal(existing.existingAllowanceCount,1);
assert.equal(existing.canApprove,false);
assert.deepEqual(existing.allowanceHistory,[{
  allowanceNumber:"A20261015001",allowanceDate:null,totalAmount:null,
  taxAmount:null,uploadStatus:null,cancelDate:null,metadataComplete:false
}],"Partial official allowance summary is NOT zero or a verified balance");
assert.equal(existing.safeToInferRemainingBalance,false);
const history=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:200,
 allowance:[{allowance_number:"A20261015001",allowance_date:20261015,
   total_amount:"50",tax_amount:"3",invoice_status:99,cancel_date:0},
  {allowance_number:"3821061800001",allowance_date:20261015,
   total_amount:"15",tax_amount:"1",invoice_status:91,cancel_date:20261016}]
}]}),()=>dt);
assert.equal(history.existingAllowanceCount,2);
assert.deepEqual(history.allowanceHistory.map(x=>x.metadataComplete),[true,true]);
assert.equal(history.allowanceHistory[1].cancelDate,"20261016");
assert.equal(history.safeToInferRemainingBalance,false,
  "Even fully populated summary amounts may include other original invoices");
const ambiguousHistory=await query(request,transport({code:0,data:[{
 invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:200,
 allowance:[{allowance_number:"A20261015001",allowance_date:"20261345",
  total_amount:"-50",invoice_status:"not-known",cancel_date:null}]
}]}),()=>dt);
assert.equal(ambiguousHistory.allowanceHistory[0].metadataComplete,false);
assert.equal(ambiguousHistory.allowanceHistory[0].totalAmount,null);
assert.equal(ambiguousHistory.allowanceHistory[0].cancelDate,null);
for(const [scenario,body,code] of [
 ["wrong invoice",{code:0,data:[{...response.data[0],invoice_number:"XY12345678"}]},
  /INVOICE_MISMATCH/],
 ["two matches",{code:0,data:[response.data[0],response.data[0]]},/AMBIGUOUS/],
 ["failed API",{code:403,data:response.data},/REJECTED/],
 ["bad amount",{code:0,data:[{...response.data[0],total_amount:"not-a-number"}]},/INCOMPLETE/],
 ["missing status",{code:0,data:[{...response.data[0],status:undefined}]},/INCOMPLETE/],
 ])await assert.rejects(()=>query(request,transport(body),()=>dt),code,scenario);
const before=calls;
await assert.rejects(()=>query({...request,appKey:""},transport(),()=>dt),/INPUT_INVALID/);
assert.equal(calls,before,"Invalid credentials must not start network I/O");
await assert.rejects(()=>query(request,async()=>{throw Error("network fail")},()=>dt),
 /UNAVAILABLE/);
await assert.rejects(()=>query(request,async()=>new Response("a".repeat(300000)),()=>dt),
 /RESPONSE_TOO_LARGE/);
console.log("AMEGO official read-only query: signed form, strict response, no retry, no implicit approval passed");

// Capture only fixed classifications, including causes; never serialize the raw
// runtime error even when it contains credentials, SQL or provider payloads.
for(const [error,kind,name,code] of [
 [new DOMException("SYNTHETIC_SECRET", "TimeoutError"),"timeout","TimeoutError",null],
 [new DOMException("SYNTHETIC_SECRET", "AbortError"),"aborted","AbortError",null],
 [new TypeError("fetch failed",{cause:Object.assign(new Error("SYNTHETIC_SECRET"),{code:"ENOTFOUND"})}),"dns","TypeError","ENOTFOUND"],
 [new TypeError("TLS handshake failed SYNTHETIC_SECRET"),"tls","TypeError",null],
 [new TypeError("Unexpected redirect SYNTHETIC_SECRET"),"redirect","TypeError",null],
 [Object.assign(new Error("SYNTHETIC_SECRET"),{code:"ECONNRESET"}),"connection","Error","ECONNRESET"],
 [Object.assign(new Error("SYNTHETIC_SECRET"),{code:"SYNTHETIC_SECRET",name:"SYNTHETIC_SECRET"}),"unknown","Unknown",null]
]){
 let attempts=0;
 await assert.rejects(()=>query(request,async()=>{attempts++;throw error;},()=>dt),caught=>{
  assert.equal(caught.message,"OFFICIAL_QUERY_UNAVAILABLE");
  assert.equal(caught.diagnostic.failureKind,kind);assert.equal(caught.diagnostic.errorName,name);
  assert.equal(caught.diagnostic.transportCode,code);assert.equal(caught.diagnostic.phase,"request");
  assert.equal(caught.diagnostic.responseReceived,false);assert.equal(caught.diagnostic.httpStatus,null);
  assert.ok(caught.diagnostic.elapsedMs>=0);assert.equal(caught.cause,undefined);
  assert.ok(!JSON.stringify(caught).includes("SYNTHETIC_SECRET"));return true;
 });
 assert.equal(attempts,1,"diagnostics must not retry official calls");
}
for(const [response,expectedCode,phase,kind,status,providerCode] of [
 [new Response("SYNTHETIC_SECRET",{status:503}),"OFFICIAL_QUERY_HTTP_FAILED","request","http",503,null],
 [new Response("SYNTHETIC_SECRET"),"OFFICIAL_QUERY_INVALID_RESPONSE","response_parse","response",200,null],
 [Response.json({code:403,msg:"SYNTHETIC_SECRET"}),"OFFICIAL_QUERY_REJECTED","response_validation","response",200,403],
 [new Response(new ReadableStream({start(c){c.error(new DOMException("SYNTHETIC_SECRET","TimeoutError"));}})),
  "OFFICIAL_QUERY_INVALID_RESPONSE","response_read","timeout",200,null]
])await assert.rejects(()=>query(request,async()=>response,()=>dt),error=>{
 assert.equal(error.message,expectedCode);assert.equal(error.diagnostic.phase,phase);
 assert.equal(error.diagnostic.failureKind,kind);assert.equal(error.diagnostic.httpStatus,status);
 assert.equal(error.diagnostic.responseReceived,true);assert.equal(error.diagnostic.providerCode,providerCode);
 assert.ok(!JSON.stringify(error).includes("SYNTHETIC_SECRET"));return true;
});
console.log("Official query phase, duration, timeout/DNS/TLS/redirect/connection/unknown and redaction passed");
