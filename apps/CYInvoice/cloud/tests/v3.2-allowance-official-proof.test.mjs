import assert from "node:assert/strict";
import {build} from "esbuild";
import {createHash} from "node:crypto";
// C3b2a: proof from full official per-original-line amounts, NOT invoice_query
// summary totals. Synthetic official GET/POST only: no Cloudflare/AMEGO network.
const out=await build({entryPoints:["src/allowance-official-proof.ts"],
  bundle:true,write:false,format:"esm",platform:"node",target:"node24"});
const {reconcileOfficialAllowanceForOriginal:verify}=
 await import("data:text/javascript;base64,"+
  Buffer.from(out.outputFiles[0].text).toString("base64"));
const at=new Date("2026-10-19T08:00:00.000Z"),origin="AB12345678";
const credentials={sellerInvoice:"12345678",appKey:"SYNTHETIC_APP_KEY_DO_NOT_USE"};
const item=(inv,amount)=>({
 original_invoice_number:inv,original_invoice_date:20261019,
 tax_type:1,amount, tax:0,quantity:1,unit_price:amount});
const frozen={applicationId:"application_c3b2a_one",workspaceId:"ws_synthetic",
 environment:"test",status:"review",revision:2,
 requesterActorId:"employee",sellerInvoice:"12345678",companyRevision:1,
 originalInvoiceNumber:origin,allowanceDay:"20261019",requestedGross:60,
 requestHash:"a".repeat(64),
 unnumberedTemplateJson:JSON.stringify({
  AllowanceNumber:"",AllowanceDate:"20261019",AllowanceType:2,
  TotalAmount:60,TaxAmount:0,ProductItem:[{
   OriginalInvoiceNumber:origin,OriginalInvoiceDate:20261019,
   OriginalDescription:"Synthetic edited item",Quantity:1,UnitPrice:60,
   Amount:60,Tax:0,TaxType:1
  }]
 })};
const number1="A20261019001",number2="A20261019002";
const summary=(number,net,cancel="0")=>({
 allowance_number:number,allowance_date:20261019,total_amount:net,
 tax_amount:0,invoice_status:99,cancel_date:cancel
});
const invoice=(history=[])=>({invoice_number:origin,status:99,cancel_date:0,
 total_amount:"200",invoice_date:20261019,detail_vat:1,
 product_item:[{description:"synthetic original"}],
 allowance:history,wait:[]});
const detail=(number,lines,cancel_date=0)=>({
 allowance_number:number,invoice_type:"D0401",invoice_status:99,
 allowance_date:20261019,allowance_type:2,cancel_date,wait:[],
 total_amount:lines.reduce((a,x)=>a+Number(x.amount),0),
 tax_amount:lines.reduce((a,x)=>a+Number(x.tax),0),
 product_item:lines
});
function transport(original,rows={}){
 const called=[];
 return {called,send:async(url,options)=>{
  called.push(url);
  assert.equal(options.method,"POST");
  assert.equal(options.redirect,"error");
  const form=new URLSearchParams(options.body);
  assert.equal(form.get("invoice"),credentials.sellerInvoice);
  assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+credentials.appKey).digest("hex"));
  if(url.endsWith("/invoice_query")){
   assert.deepEqual(JSON.parse(form.get("data")),{type:"invoice",invoice_number:origin});
   return Response.json({code:0,data:[original]});
  }
  if(url.endsWith("/allowance_query")){
   const {allowance_number}=JSON.parse(form.get("data"));
   return Response.json(rows[allowance_number]??{code:71,msg:"not found"});
  }
  throw Error("unexpected provider mutation path");
 }};
}
function row(x){return {code:0,msg:"",data:x};}
const secondInvoice="XY88888888";
{
 const t=transport(invoice());
 const proof=await verify(frozen,credentials,t.send,()=>at);
 assert.equal(proof.officialRemainingBeforeRequest,(200n*10000000n).toString());
 assert.equal(proof.verifiedExistingAllowanceNumbers,0);
 assert.equal(proof.approvalRecorded,false);
 assert.equal(proof.numberReserved,false);
 assert.equal(t.called.length,1);
}
const mixedDetail=detail(number1,[item(origin,30),item(secondInvoice,70)]);
{
 // Official allowance summary is 100, but this original invoice used only 30.
 const t=transport(invoice([summary(number1,100)]),{[number1]:row(mixedDetail)});
 const proof=await verify(frozen,credentials,t.send,()=>at);
 assert.equal(proof.officialEffectiveAlreadyAllowed,(30n*10000000n).toString());
 assert.equal(proof.officialRemainingBeforeRequest,(170n*10000000n).toString());
 assert.equal(proof.verifiedExistingAllowanceNumbers,1);
 assert.equal(t.called.length,2);
 assert.ok(!JSON.stringify(proof).includes("SYNTHETIC_APP_KEY"));
}
{
 const prior=detail(number2,[item(origin,20)],1792368000);
 const t=transport(invoice([summary(number1,100),
   summary(number2,20,"20261020")]),
   {[number1]:row(mixedDetail),[number2]:row(prior)});
 const proof=await verify(frozen,credentials,t.send,()=>at);
 assert.equal(proof.officialEffectiveAlreadyAllowed,(30n*10000000n).toString(),
  "confirmed void does not consume the original invoice's allowance capacity");
 assert.equal(proof.verifiedExistingAllowanceNumbers,2);
 assert.equal(t.called.length,3);
}
async function fails(record,rows,regex=/ALLOWANCE_OFFICIAL_PROOF|OFFICIAL_DETAIL/,
  request=frozen){
 const t=transport(record,rows);
 await assert.rejects(()=>verify(request,credentials,t.send,()=>at),regex);
 assert.ok(t.called.every(x=>x.endsWith("/invoice_query")||
                          x.endsWith("/allowance_query")));
 return t.called.length;
}
await fails(invoice([summary(number1,100)]),{
 [number1]:row(detail(number1,[item(origin,50),item(secondInvoice,70)]))
},/OFFICIAL_PROOF_ALLOWANCE_SUMMARY_DETAIL_MISMATCH/);
await fails(invoice([summary(number1,100)]),{
 [number1]:row(detail(number1,[item(secondInvoice,100)]))
},/OFFICIAL_PROOF_ALLOWANCE_NOT_FOR_THIS_INVOICE/);
await fails(invoice([summary(number1,100)]),{
 [number1]:row({...mixedDetail,wait:[{invoice_type:"D0501"}]})
},/OFFICIAL_DETAIL_PENDING_OR_UNKNOWN/);
await fails(invoice([summary(number1,100)]),{
 [number1]:row({...mixedDetail,invoice_status:91})
},/OFFICIAL_DETAIL_PENDING_OR_UNKNOWN/);
await fails(invoice([summary(number1,100)]),{
 [number1]:row({...mixedDetail,product_item:[item(origin,30)],
 total_amount:100})
},/OFFICIAL_DETAIL_TOTAL_MISMATCH/);
await fails(invoice([summary(number1,100)]),{
 [number1]:{code:71,msg:"not found"}
},/OFFICIAL_DETAIL_NOT_FOUND_OR_UNCONFIRMED/);
await fails(invoice([summary(number1,100,"20261019")]),{
 [number1]:row(mixedDetail)
},/OFFICIAL_PROOF_ALLOWANCE_VOID_MISMATCH/);
await fails(invoice([summary(number1,100),summary(number1,100)]),{
 [number1]:row(mixedDetail)
},/OFFICIAL_PROOF_ALLOWANCE_SUMMARY_INCOMPLETE/);
await fails(invoice([{allowance_number:number1}]),{
 [number1]:row(mixedDetail)
},/OFFICIAL_PROOF_ALLOWANCE_SUMMARY_INCOMPLETE/);
await fails({...invoice(),wait:[{invoice_type:"D0401"}]},{},
 /OFFICIAL_PROOF_INVOICE_INCOMPLETE_OR_PENDING/);
await fails({...invoice(),allowance:undefined},{},
 /OFFICIAL_PROOF_INVOICE_INCOMPLETE_OR_PENDING/);
await fails({...invoice(),invoice_date:undefined},{},
 /OFFICIAL_PROOF_INVOICE_DATE_OR_AMOUNT_INVALID/);
await fails(invoice(),{},/OFFICIAL_PROOF_TOTAL_MISMATCH/,
 {...frozen,requestedGross:90});
await fails(invoice([summary(number1,100)]),{
 [number1]:row(detail(number1,[item(origin,100)]))
},/OFFICIAL_PROOF_EXCEEDS_OFFICIAL_REMAINING/,
 {...frozen,requestedGross:150,unnumberedTemplateJson:
  frozen.unnumberedTemplateJson.replaceAll("60","150")});
const failingNetwork=await fails(invoice([summary(number1,100)]),{},
 /OFFICIAL_DETAIL_NOT_FOUND_OR_UNCONFIRMED/);
assert.equal(failingNetwork,2);
console.log("CYInvoice C3b2a: AMEGO full per-original line reconciliation, void/pending, summary mismatch and safe frozen gross proof passed");
