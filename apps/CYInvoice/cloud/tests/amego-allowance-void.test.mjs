import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {basename} from "node:path";
import {build} from "esbuild";
const bundle=await build({entryPoints:[
 "src/amego-allowance-void.ts","src/amego-allowance-status.ts"
],outdir:"dist",write:false,bundle:true,platform:"node",format:"esm",target:"node24"});
const loaded=await Promise.all(bundle.outputFiles.map(async item=>({
  name:basename(item.path),
  module:await import("data:text/javascript;base64,"+
    Buffer.from(item.text).toString("base64"))
})));
const voidModule=loaded.find(x=>x.name==="amego-allowance-void.js").module;
const statusModule=loaded.find(x=>x.name==="amego-allowance-status.js").module;
const {postAllowanceVoidOnce:voidOnce}=voidModule;
const {readOfficialAllowanceVoidStatus:statusQuery}=statusModule;
const key="SYNTHETIC_KEY_ONLY_NOT_REAL";
const now=new Date("2026-10-15T02:00:00Z");
const request={sellerInvoice:"12345678",appKey:key,allowanceNumber:"3821061800001"};
let count=0;
const validSignature=(form,payload)=>{
 assert.equal(form.get("invoice"),"12345678");
 assert.equal(form.get("data"),JSON.stringify(payload));
 assert.equal(form.get("time"),String(Math.floor(now.valueOf()/1000)));
 assert.equal(form.get("sign"),createHash("md5").update(form.get("data")+
   form.get("time")+key).digest("hex"));
};
function transport(expectedUrl,payload,reply={code:0,msg:""}){
 return async (url,options)=>{
   count++;
   assert.equal(url,"https://invoice-api.amego.tw/json/"+expectedUrl);
   assert.equal(options.method,"POST");
   assert.equal(options.redirect,"error");
   assert.equal(options.headers["content-type"],"application/x-www-form-urlencoded");
   validSignature(new URLSearchParams(options.body),payload);
   return Response.json(reply);
 };
}
const voidBody=[{CancelAllowanceNumber:request.allowanceNumber}];
assert.deepEqual(await voidOnce(request,transport("g0501",voidBody),()=>now),
 {kind:"accepted_unconfirmed",code:0});
assert.equal(count,1);
assert.deepEqual(await voidOnce(request,transport("g0501",voidBody,{code:42}),()=>now),
 {kind:"definite_rejection_manual",code:42});
assert.deepEqual(await voidOnce(request,transport("g0501",voidBody,{code:"bad"}),()=>now),
 {kind:"unknown_result"});
assert.deepEqual(await voidOnce(request,async()=>{throw Error("timeout")},()=>now),
 {kind:"unknown_result"});
assert.deepEqual(await voidOnce(request,async()=>new Response("not json"),()=>now),
 {kind:"unknown_result"});
assert.deepEqual(await voidOnce(request,async()=>new Response("x".repeat(66000)),()=>now),
 {kind:"unknown_result"});
const before=count;
for(const number of [""," 3821061800001","a".repeat(17),"\nAB012","AB\u0000"]){
 await assert.rejects(()=>voidOnce({...request,allowanceNumber:number},
   transport("g0501",voidBody),()=>now),/INPUT_INVALID/);
}
assert.equal(count,before,"Malformed requests must never reach the network");
// No plausible success result authorizes a second automatic g0501 request.
const statusBody=[{AllowanceNumber:request.allowanceNumber}];
const keyed={code:0,data:{[request.allowanceNumber]:{type:"D0501",status:99}}};
const complete=await statusQuery(request,transport("allowance_status",statusBody,keyed),()=>now);
assert.equal(complete.officialCancelled,true);
assert.equal(complete.definitiveOpen,false);
assert.equal(complete.safeToAutoClose,false,
 "An official status alone does not prove CYInvoice's D1 work item can be closed");
assert.equal(complete.safeToAutoResend,false);
assert.match(complete.statusEvidenceSha256,/^[a-f0-9]{64}$/);
const array={code:0,data:[{allowance_number:request.allowanceNumber,
 invoice_type:"D0501",invoice_status:"99"}]};
const arr=await statusQuery(request,transport("allowance_status",statusBody,array),()=>now);
assert.equal(arr.officialCancelled,true);
const stillOpen={code:0,data:{[request.allowanceNumber]:{type:"D0401",status:99}}};
const open=await statusQuery(request,transport("allowance_status",statusBody,stillOpen),()=>now);
assert.equal(open.officialCancelled,false);
assert.equal(open.definitiveOpen,true);
const pending={code:0,data:{[request.allowanceNumber]:{type:"D0501",status:31}}};
assert.equal((await statusQuery(request,transport("allowance_status",
 statusBody,pending),()=>now)).officialCancelled,false);
const missing={code:0,data:{[request.allowanceNumber]:{type:"NOT_FOUND",status:0}}};
assert.equal((await statusQuery(request,transport("allowance_status",
 statusBody,missing),()=>now)).officialCancelled,false);
for(const [body,pattern] of [
 [{code:51,data:null},/UNCONFIRMED/],
 [{code:0,data:{}},/WRONG_NUMBER/],
 [{code:0,data:{WRONG:{type:"D0501",status:99}}},/WRONG_NUMBER/],
 [{code:0,data:[{allowance_number:"WRONG",type:"D0501",status:99}]},/WRONG_NUMBER/],
 [{code:0,data:[{allowance_number:request.allowanceNumber,type:"D0501",status:99},
   {allowance_number:request.allowanceNumber,type:"D0501",status:99}]},/AMBIGUOUS/],
 [{code:0,data:{[request.allowanceNumber]:{type:"D0501",status:"broken"}}},/INCOMPLETE/],
 ])await assert.rejects(()=>statusQuery(request,
 transport("allowance_status",statusBody,body),()=>now),pattern);
await assert.rejects(()=>statusQuery(request,async()=>{throw Error("offline")},()=>now),
 /UNAVAILABLE/);
await assert.rejects(()=>statusQuery(request,async()=>new Response("a".repeat(70000)),()=>now),
 /RESPONSE_TOO_LARGE/);
console.log("g0501 one-shot + official status readback: signed, strict, no automatic replay/closure");
