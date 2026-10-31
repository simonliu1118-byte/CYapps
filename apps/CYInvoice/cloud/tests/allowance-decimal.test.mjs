import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import {createRequire} from "node:module";
import {createHash} from "node:crypto";
import {build} from "esbuild";
const built=await build({entryPoints:["src/allowance-decimal.ts"],bundle:true,write:false,format:"esm",platform:"node"});
const {fixed7,parseAllowanceFinancialJson:parse}=
 await import("data:text/javascript;base64,"+Buffer.from(built.outputFiles[0].text).toString("base64"));
for(const [raw,expected] of [["314.2857143",3142857143n],["-78.0952381",-780952381n],
 ["0.5",5000000n],["0.0000001",1n],["900719925474.0000001",9007199254740000001n]]){
 assert.equal(fixed7(parse('{"UnitPrice":'+raw+'}').UnitPrice,true),expected);
 assert.equal(fixed7(parse('{"UnitPrice":'+JSON.stringify(raw)+'}').UnitPrice,true),expected);
}
for(const raw of ["0.00000001","0.10000000","1e-7","1e400","900719925474.00000001","1000000000000"])
 assert.throws(()=>fixed7(parse('{"Amount":'+raw+'}').Amount,true),/AMOUNT_INVALID/);
for(const raw of ["NaN","Infinity","01","1."])assert.throws(()=>parse('{"Amount":'+raw+'}'));
assert.throws(()=>fixed7(0.5),/AMOUNT_INVALID/,"already-rounded JS numbers never become financial authority");
assert.throws(()=>fixed7(parse('{"Quantity":-0.5}').Quantity),/AMOUNT_INVALID/);
assert.equal(parse('{"note":"{\\\"Amount\\\":0.12345678}","revision":1}').revision,1);
const fixtures=JSON.parse(readFileSync(new URL("fixtures/allowance-desktop-decimal.json",import.meta.url),"utf8"));
const key="SYNTHETIC_DECIMAL_CONTRACT_KEY_DO_NOT_USE",number="A20261016001";
const entry=`
 import {reconcileOfficialAllowanceForOriginal} from './src/allowance-official-proof';
 import {queryOfficialAllowanceDetail} from './src/amego-allowance-detail';
 import {queryFrozenAllowanceByOriginalNumber} from './src/amego-allowance-exact-readback';
 import {postNumberedAllowanceOnce} from './src/amego-allowance-issue';
 const fixtures=${JSON.stringify(fixtures)},key=${JSON.stringify(key)},number=${JSON.stringify(number)};
 export default {async fetch(request){
  const {index,action,override}=await request.json(),draft=fixtures[index];
  const raw=override??draft.UnnumberedTemplateJson;
  const frozen=Object.freeze({applicationId:draft.ApplicationId,workspaceId:'ws_decimal_contract',environment:'test',
   status:'open',revision:1,requesterActorId:'synthetic_employee',sellerInvoice:'12345678',companyRevision:1,
   originalInvoiceNumber:draft.OriginalInvoiceNumber,allowanceDay:draft.AllowanceDay,
   requestedGross:draft.RequestedGross,requestHash:'a'.repeat(64),unnumberedTemplateJson:raw});
  const credentials={sellerInvoice:'12345678',appKey:key},input={...credentials,allowanceNumber:number,
   allowanceDay:draft.AllowanceDay,unnumberedTemplateJson:raw};
  try{
   let result;
   if(action==='proof')result=await reconcileOfficialAllowanceForOriginal(frozen,credentials);
   else if(action==='detail')result=await queryOfficialAllowanceDetail('12345678',key,number);
   else if(action==='readback')result=await queryFrozenAllowanceByOriginalNumber(input);
   else if(action==='send')result=await postNumberedAllowanceOnce(input);
   else throw Error('UNKNOWN_SYNTHETIC_ACTION');
   return new Response(JSON.stringify({ok:true,result,originalUnchanged:frozen.unnumberedTemplateJson===raw},(_,v)=>typeof v==='bigint'?v.toString():v));
  }catch(error){return Response.json({ok:false,code:error.message,originalUnchanged:frozen.unnumberedTemplateJson===raw});}
 }};`;
const worker=await build({stdin:{contents:entry,resolveDir:process.cwd(),sourcefile:"decimal-contract-entry.ts"},
 bundle:true,write:false,format:"esm",platform:"neutral",target:"es2022"});
const require=createRequire(import.meta.url),runtimeRequire=createRequire(require.resolve("wrangler/package.json"));
const {Miniflare,convertV4MiniflareOptions,Response:MockResponse}=runtimeRequire("miniflare");
const config=JSON.parse(readFileSync(new URL("../wrangler.jsonc",import.meta.url),"utf8"));
let current=fixtures[0],calls=[],sent="";
function official(){
 const source=parse(current.UnnumberedTemplateJson);
 return {allowance_number:number,allowance_date:20261016,invoice_type:"D0401",invoice_status:99,
  allowance_type:2,cancel_date:0,wait:[],buyer_identifier:source.BuyerIdentifier,buyer_name:source.BuyerName,
  total_amount:source.TotalAmount,tax_amount:source.TaxAmount,product_item:source.ProductItem.map(x=>({
   original_invoice_number:x.OriginalInvoiceNumber,original_invoice_date:x.OriginalInvoiceDate,
   original_description:x.OriginalDescription,quantity:x.Quantity,unit_price:x.UnitPrice,amount:x.Amount,tax:x.Tax,tax_type:x.TaxType}))};
}
function response(value){
 // Real provider numeric JSON tokens, including values JS Number cannot hold.
 return new MockResponse(JSON.stringify(value,(key,value)=>
  ["quantity","unit_price","amount","tax","total_amount","tax_amount"].includes(key)&&typeof value==="string"
   ?JSON.rawJSON(value):value),{headers:{"content-type":"application/json"}});
}
function numericTokens(text){const tokens=[];JSON.parse(text,(key,value,context)=>{
 if(typeof value==="number")tokens.push([key,context.source]);return value;});return tokens;}
const mf=new Miniflare(convertV4MiniflareOptions({modules:true,script:worker.outputFiles[0].text,
 compatibilityDate:config.compatibility_date,compatibilityFlags:config.compatibility_flags??[],
 outboundService:async request=>{
  const url=new URL(request.url);assert.equal(url.origin,"https://invoice-api.amego.tw");calls.push(url.pathname);
  assert.equal(request.method,"POST");const form=new URLSearchParams(await request.text());
  assert.equal(form.get("sign"),createHash("md5").update(form.get("data")+form.get("time")+key).digest("hex"));
  if(url.pathname==="/json/invoice_query")return response({code:0,data:[{invoice_number:"AB12345678",
   invoice_date:20261015,status:99,cancel_date:0,total_amount:1000000,allowance:[],wait:[],product_item:[]}]});
  if(url.pathname==="/json/allowance_query")return response({code:0,data:official()});
  if(url.pathname==="/json/g0401"){
   sent=form.get("data");assert.deepEqual(numericTokens(sent),numericTokens(current.UnnumberedTemplateJson));
   assert.deepEqual(JSON.parse(sent),[{...JSON.parse(current.UnnumberedTemplateJson),AllowanceNumber:number}]);
   return response({code:0});
  }
  throw Error("Unexpected provider operation");
 }}));
async function run(index,action,override){current=fixtures[index];calls=[];sent="";
 const r=await mf.dispatchFetch("http://localhost/",{method:"POST",body:JSON.stringify({index,action,override})});return r.json();}
try{
 for(let index=0;index<fixtures.length;index++){
  for(const action of ["proof","detail","readback","send"]){
   const r=await run(index,action);assert.equal(r.ok,true,JSON.stringify({index,action,r}));assert.equal(r.originalUnchanged,true);
   assert.equal(calls.length,1,"no retry or additional financial action");
   if(action==="proof"){assert.equal(r.result.approvalRecorded,false);assert.equal(r.result.numberReserved,false);}
   if(action==="readback"){assert.equal(r.result.kind,"full_frozen_match_unattributed");assert.equal(r.result.safeToAutoResend,false);}
   if(action==="send"){
    assert.equal(r.result.kind,"accepted_unconfirmed");
    if(index===2){assert.ok(sent.includes('"UnitPrice":900719925474.0000001'));assert.ok(sent.includes('"Quantity":0.0000001'));}
   }
  }
 }
 const raw=fixtures[1].UnnumberedTemplateJson;
 for(const bad of [raw.replace('"Quantity":0.5','"Quantity":0.50000001'),raw.replace('"UnitPrice":120','"UnitPrice":1.2e2')]){
  const r=await run(1,"proof",bad);assert.equal(r.ok,false);assert.equal(r.code,"ALLOWANCE_OFFICIAL_PROOF_SOURCE_AMOUNT_INVALID");
  assert.equal(r.originalUnchanged,true);assert.deepEqual(calls,["/json/invoice_query"]);
 }
 const mismatch=await run(1,"proof",raw.replace('"Amount":60','"Amount":59'));
 assert.equal(mismatch.ok,false);assert.equal(mismatch.code,"ALLOWANCE_OFFICIAL_PROOF_TOTAL_MISMATCH");
 console.log("Decimal contract: actual desktop fixtures pass Workers-native proof/detail/readback/signed send; exact7, discounts, fractional quantity, large price tokens, invalid precision and total mismatch passed; no real service access.");
}finally{await mf.dispose();}
