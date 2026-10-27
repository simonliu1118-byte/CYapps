import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {build} from "esbuild";

// C3c3a entirely isolated: signed read-only AMEGO synthetic transport.
// The full match is UNATTRIBUTED and NEVER grants sending, retry,
// number reallocation, or Ledger confirmation.
const built=await build({entryPoints:["src/amego-allowance-exact-readback.ts"],
 bundle:true,format:"esm",platform:"node",target:"node24",write:false});
const {queryFrozenAllowanceByOriginalNumber:query}=await import(
 "data:text/javascript;base64,"+
 Buffer.from(built.outputFiles[0].text).toString("base64"));
const day="20261019",number="A20261019001";
const key="SYNTHETIC_APP_KEY_DO_NOT_USE";
const sourceItem={OriginalInvoiceNumber:"AB12345678",
 OriginalInvoiceDate:20261019,OriginalDescription:"Synthetic item",
 Quantity:"1",UnitPrice:"50",Amount:"50",Tax:"3",TaxType:1};
const frozen={
 AllowanceNumber:"",AllowanceDate:day,AllowanceType:2,
 BuyerIdentifier:"0000000000",BuyerName:"Synthetic buyer",
 TotalAmount:"50",TaxAmount:"3",ProductItem:[sourceItem]
};
const input={sellerInvoice:"12345678",appKey:key,allowanceDay:day,
 allowanceNumber:number,unnumberedTemplateJson:JSON.stringify(frozen)};
const officialItem={
 original_invoice_number:"AB12345678",original_invoice_date:20261019,
 original_description:"Synthetic item",quantity:1,unit_price:50,amount:50,tax:3,
 tax_type:1
};
const official={allowance_number:number,allowance_date:20261019,
 invoice_type:"D0401",invoice_status:99,allowance_type:2,cancel_date:0,
 buyer_identifier:"0000000000",buyer_name:"Synthetic buyer",
 total_amount:"50.0000000",tax_amount:"3",wait:[],
 product_item:[officialItem]};
const at=new Date("2026-10-19T08:00:00.000Z"),clock=()=>at;
function makeTransport(reply={code:0,data:official}){
 const urls=[];
 return {urls,send:async (url,options)=>{
  urls.push(url);
  assert.equal(url,"https://invoice-api.amego.tw/json/allowance_query");
  assert.equal(options.method,"POST");
  assert.equal(options.redirect,"error");
  assert.equal(options.headers["content-type"],"application/x-www-form-urlencoded");
  const form=new URLSearchParams(options.body);
  assert.equal(form.get("invoice"),input.sellerInvoice);
  assert.deepEqual(JSON.parse(form.get("data")),{allowance_number:number});
  assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+key).digest("hex"));
  return Response.json(reply);
 }};
}
{
 const fake=makeTransport();
 const r=await query(input,fake.send,clock);
 assert.equal(r.allowanceNumber,number);
 assert.match(r.officialSnapshotHash,/^[a-f0-9]{64}$/);
 assert.equal(r.kind,"full_frozen_match_unattributed");
 assert.equal(r.safeToAutoResend,false);
 assert.equal(r.safeToAutoConfirm,false);
 assert.equal(r.canReallocateNumber,false);
 assert.equal(fake.urls.length,1);
 assert.equal(JSON.stringify(r).includes("Synthetic buyer"),false);
 assert.equal(JSON.stringify(r).includes(key),false);
}
{
 const alt={...official,product_item:[{
  ...officialItem,original_description:undefined,
  description:"Synthetic item"}]};
 const r=await query(input,makeTransport({code:0,data:alt}).send,clock);
 assert.equal(r.kind,"full_frozen_match_unattributed");
 assert.equal(r.safeToAutoConfirm,false);
}
async function mismatch(changed,pattern,overrides=input){
 const fake=makeTransport({code:0,data:{...official,...changed}});
 await assert.rejects(()=>query(overrides,fake.send,clock),pattern);
 assert.equal(fake.urls.length,1);
}
await mismatch({buyer_identifier:"11111111"},/PAYLOAD_MISMATCH/);
await mismatch({buyer_name:"Another buyer"},/PAYLOAD_MISMATCH/);
await mismatch({total_amount:"51"},/PAYLOAD_MISMATCH/);
await mismatch({tax_amount:"4"},/PAYLOAD_MISMATCH/);
await mismatch({allowance_number:"A20261019002"},/OFFICIAL_NOT_FINAL/);
await mismatch({allowance_date:20261018},/OFFICIAL_NOT_FINAL/);
await mismatch({invoice_type:"D0501"},/OFFICIAL_NOT_FINAL/);
await mismatch({invoice_status:91},/OFFICIAL_NOT_FINAL/);
await mismatch({cancel_date:20261020},/OFFICIAL_NOT_FINAL/);
await mismatch({wait:[{invoice_type:"D0401"}]},/OFFICIAL_NOT_FINAL/);
await mismatch({product_item:[]},/PAYLOAD_MISMATCH/);
await mismatch({product_item:[{...officialItem,original_invoice_number:"XY87654321"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,original_description:"Changed"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,quantity:"2"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,unit_price:"40"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,amount:"49"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,tax:"2"}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,tax_type:2}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,original_invoice_date:20261018}]},
 /ITEM_MISMATCH/);
await mismatch({product_item:[{...officialItem,original_description:undefined,
 description:undefined}]},/INCOMPLETE/);
const code51=makeTransport({code:51,data:null});
await assert.rejects(()=>query(input,code51.send,clock),/UNCONFIRMED/);
assert.equal(code51.urls.length,1);
await assert.rejects(()=>query(input,async()=>{throw Error("network lost")},clock),
 /UNAVAILABLE/);
await assert.rejects(()=>query(input,async()=>new Response("broken"),clock),
 /INCOMPLETE/);
await assert.rejects(()=>query(input,async()=>new Response("a".repeat(300000)),clock),
 /TOO_LARGE/);
for(const payload of [
 {...frozen,AllowanceNumber:number},
 {...frozen,ProductItem:[]},
 {...frozen,ProductItem:[{...sourceItem,Amount:"51"}]},
 {...frozen,ProductItem:[{...sourceItem,OriginalInvoiceNumber:"bad"}]},
 {...frozen,AllowanceDate:"20260230"}
]){
 const fake=makeTransport();
 await assert.rejects(()=>query({...input,unnumberedTemplateJson:
  JSON.stringify(payload)},fake.send,clock),
 /READBACK_FROZEN|READBACK_DATE/);
 assert.equal(fake.urls.length,0,"invalid frozen source must not query provider");
}
const mismatchDate=makeTransport();
await assert.rejects(()=>query({...input,allowanceDay:"20261018"},
 mismatchDate.send,clock),/INPUT_INVALID/);
assert.equal(mismatchDate.urls.length,0);
// Identical-looking external record is not attributable to our earlier
// g0401 attempt by number alone. Never auto-confirm or reallocate.
const external=await query(input,makeTransport().send,clock);
assert.equal(external.kind,"full_frozen_match_unattributed");
assert.equal(external.safeToAutoConfirm,false);
console.log("CYInvoice C3c3a: signed exact-number readback and full buyer/item match, pending/unknown/mismatch fail-closed; no auto-confirm or retry passed");
