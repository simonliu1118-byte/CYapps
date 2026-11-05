import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {createRequire} from "node:module";
import {readFileSync} from "node:fs";
import {build} from "esbuild";

// Use the exact workerd/Miniflare shipped with pinned Wrangler. Unlike Node
// transport mocks, these adapters call the Workers-native fetch themselves.
const require=createRequire(import.meta.url);
const runtimeRequire=createRequire(require.resolve("wrangler/package.json"));
const {Miniflare,convertV4MiniflareOptions,Response:MockResponse}=runtimeRequire("miniflare");
const runtimeConfig=JSON.parse(readFileSync(new URL("../wrangler.jsonc",import.meta.url),"utf8"));
const key="SYNTHETIC_WORKERS_RUNTIME_KEY_DO_NOT_USE",number="A20261019001";
const original={OriginalInvoiceNumber:"AB12345678",OriginalInvoiceDate:20261019,
 OriginalDescription:"Synthetic item",Quantity:1,UnitPrice:50,Amount:50,Tax:3,TaxType:1};
const template={AllowanceNumber:"",AllowanceDate:"20261019",AllowanceType:2,
 BuyerIdentifier:"0000000000",BuyerName:"Synthetic buyer",TotalAmount:50,TaxAmount:3,ProductItem:[original]};
const allowance={allowance_number:number,allowance_date:20261019,invoice_type:"D0401",
 invoice_status:99,allowance_type:2,cancel_date:0,buyer_identifier:"0000000000",buyer_name:"Synthetic buyer",
 total_amount:50,tax_amount:3,wait:[],product_item:[{original_invoice_number:"AB12345678",
 original_invoice_date:20261019,original_description:"Synthetic item",quantity:1,unit_price:50,amount:50,tax:3,tax_type:1}]};
const invoice={invoice_number:"AB12345678",order_id:"SYNTHETIC_ORDER",invoice_date:20261019,
 status:99,cancel_date:0,total_amount:105,tax_amount:5,detail_vat:1,product_item:[],allowance:[],wait:[]};
const entry=`
 import {queryOfficialInvoiceReadOnly} from './src/amego-invoice-query';
 import {queryOfficialAllowanceDetail} from './src/amego-allowance-detail';
 import {queryFrozenAllowanceByOriginalNumber} from './src/amego-allowance-exact-readback';
 import {postNumberedAllowanceOnce} from './src/amego-allowance-issue';
 import {postAllowanceVoidOnce} from './src/amego-allowance-void';
 import {readOfficialAllowanceVoidStatus} from './src/amego-allowance-status';
 import {queryOfficialVoidTarget} from './src/amego-allowance-void-proof';
 const common={sellerInvoice:'12345678',appKey:${JSON.stringify(key)},allowanceNumber:${JSON.stringify(number)}};
 const numbered={...common,allowanceDay:'20261019',unnumberedTemplateJson:${JSON.stringify(JSON.stringify(template))}};
 // This entrypoint exists only in this in-memory test bundle, never app.ts.
 export default {async fetch(request){
  const {adapter}=await request.json();
  try{
   let result;
   switch(adapter){
    case 'invoice':result=await queryOfficialInvoiceReadOnly({...common,originalInvoiceNumber:'AB12345678'});break;
    case 'detail':result=await queryOfficialAllowanceDetail(common.sellerInvoice,common.appKey,common.allowanceNumber);break;
    case 'readback':result=await queryFrozenAllowanceByOriginalNumber(numbered);break;
    case 'status':result=await readOfficialAllowanceVoidStatus(common);break;
    case 'void-proof':result=await queryOfficialVoidTarget({...common,originalInvoiceNumber:'AB12345678'});break;
    case 'issue':result=await postNumberedAllowanceOnce(numbered);break;
    case 'void':result=await postAllowanceVoidOnce(common);break;
    default:throw Error('UNKNOWN_TEST_ADAPTER');
   }
   return new Response(JSON.stringify({ok:true,result},(_,v)=>typeof v==='bigint'?v.toString():v),{headers:{'content-type':'application/json'}});
  }catch(error){return Response.json({ok:false,code:error.message,diagnostic:error.diagnostic});}
 }};`;
const bundled=await build({stdin:{contents:entry,resolveDir:process.cwd(),sourcefile:"runtime-test-entry.ts"},
 bundle:true,write:false,format:"esm",platform:"neutral",target:"es2022"});
const calls=[];let redirectedStatus=0,location="",redirectAt=1,unexpected=0,checks=0;
const paths=new Set(["/json/invoice_query","/json/allowance_query","/json/allowance_status","/json/g0401","/json/g0501"]);
const mf=new Miniflare(convertV4MiniflareOptions({modules:true,compatibilityDate:runtimeConfig.compatibility_date,
 compatibilityFlags:runtimeConfig.compatibility_flags??[],
 script:bundled.outputFiles[0].text,
 // Intercept EVERY outbound request; no external DNS, sockets, API or secrets.
 outboundService:async request=>{
  const url=new URL(request.url);calls.push(url.href);
  if(url.origin!=="https://invoice-api.amego.tw"||!paths.has(url.pathname)){
   unexpected++;return new MockResponse("Unexpected redirect target",{status:403});
  }
  assert.equal(request.method,"POST");
  assert.equal(request.headers.get("content-type"),"application/x-www-form-urlencoded");
  const form=new URLSearchParams(await request.text());assert.equal(form.get("invoice"),"12345678");
  assert.equal(form.get("sign"),createHash("md5").update(form.get("data")+form.get("time")+key).digest("hex"));
  if(redirectedStatus&&calls.length===redirectAt)
   return new MockResponse(JSON.stringify({code:0,data:allowance}),{status:redirectedStatus,headers:{location}});
  const data=url.pathname.endsWith("/invoice_query")?[invoice]:url.pathname.endsWith("/allowance_query")?allowance:
   url.pathname.endsWith("/allowance_status")?{[number]:{type:"D0401",status:99}}:undefined;
  return new MockResponse(JSON.stringify({code:0,...(data===undefined?{}:{data})}),{headers:{"content-type":"application/json"}});
 }}));
async function run(adapter){
 const reply=await mf.dispatchFetch("http://localhost/",{method:"POST",headers:{"content-type":"application/json"},body:JSON.stringify({adapter})});
 assert.equal(reply.status,200);checks++;return reply.json();
}
try{
 for(const adapter of ["invoice","detail","readback","status","void-proof","issue","void"]){
  calls.length=0;redirectedStatus=0;redirectAt=1;
  const reply=await run(adapter);assert.equal(reply.ok,true,JSON.stringify({adapter,reply}));
  assert.equal(calls.length,adapter==="void-proof"?2:1,"normal adapter must not retry");
  if(adapter==="issue"||adapter==="void")assert.equal(reply.result.kind,"accepted_unconfirmed","code0 never proves financial completion");
  if(adapter==="readback"){assert.equal(reply.result.kind,"full_frozen_match_unattributed");assert.equal(reply.result.safeToAutoResend,false);}
  for(const status of [301,302,303,307,308])for(const target of ["https://redirect-target.invalid/private","https://invoice-api.amego.tw/json/redirect-target"]){
   calls.length=0;redirectedStatus=status;location=target;
   const redirected=await run(adapter);
   if(adapter==="issue"||adapter==="void")assert.deepEqual(redirected,{ok:true,result:{kind:"unknown_result"}});
   else {assert.equal(redirected.ok,false);assert.match(redirected.code,/REDIRECT_REJECTED/);}
   if(adapter==="invoice"){
    assert.equal(redirected.diagnostic.failureKind,"redirect");assert.equal(redirected.diagnostic.responseReceived,true);
    assert.equal(redirected.diagnostic.httpStatus,status);
   }
   assert.equal(calls.length,1,"never follow redirects, resend signed bodies or retry mutations");assert.equal(unexpected,0);
  }
 }
 calls.length=0;redirectedStatus=307;redirectAt=2;location="https://redirect-target.invalid/private";
 const second=await run("void-proof");assert.equal(second.ok,false);assert.equal(second.code,"ALLOWANCE_STATUS_REDIRECT_REJECTED");
 assert.equal(calls.length,2);assert.equal(unexpected,0);
 console.log(`Workers-native AMEGO adapters: ${checks} checks passed; signed POSTs, all7 adapters, 301/302/303/307/308, same/foreign targets, no follow/retry or external service access.`);
}finally{await mf.dispose();}
