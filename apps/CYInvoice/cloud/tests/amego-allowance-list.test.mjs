import assert from "node:assert/strict";
import {createHash} from "node:crypto";
import {build} from "esbuild";
const built=await build({entryPoints:["src/amego-allowance-list.ts"],write:false,
  platform:"node",format:"esm",target:"node24",bundle:true});
const {fetchOfficialAllowancePage:query,selectAllowanceExceptionHints:select,scanAllowanceExceptionPages:scan}=
 await import("data:text/javascript;base64,"+
 Buffer.from(built.outputFiles[0].text).toString("base64"));
const key="SYNTHETIC_APP_KEY_NEVER_REAL";
const now=new Date("2026-10-15T02:00:00Z");
const base={sellerInvoice:"12345678",appKey:key,
 dateSelect:1,dateStart:"20261001",dateEnd:"20261015",page:1,limit:20};
const number="A20261015001";
const sample={code:0,page_total:1,page_now:1,data_total:1,
 data:[{allowance_number:number,invoice_type:"D0401",
    invoice_status:99,allowance_date:20261015,buyer_name:"PRIVATE_DO_NOT_RETURN"}]};
let calls=0;
function provider(reply=sample){
 return async(url,opts)=>{
   calls++;
   assert.equal(url,"https://invoice-api.amego.tw/json/allowance_list");
   assert.equal(opts.method,"POST");
   assert.equal(opts.redirect,"error");
   const form=new URLSearchParams(opts.body);
   assert.equal(form.get("invoice"),"12345678");
   assert.deepEqual(JSON.parse(form.get("data")),{date_select:base.dateSelect,
     date_start:base.dateStart,date_end:base.dateEnd,limit:20,page:1});
   assert.equal(form.get("sign"),createHash("md5").update(
     form.get("data")+form.get("time")+key).digest("hex"));
   return Response.json(reply);
 };
}
const page=await query(base,provider(),()=>now);
assert.equal(page.items.length,1);
assert.deepEqual(page.items[0],{number,type:"D0401",status:99,allowanceDate:"20261015"});
assert.equal(page.canAutoConfirm,false);
assert.match(page.snapshotSha256,/^[0-9a-f]{64}$/);
assert.equal(JSON.stringify(page).includes("PRIVATE_DO_NOT_RETURN"),false);
assert.equal(calls,1,"One page = one HTTP call, never retry");
const watch=[{allowanceNumber:number,workItemId:"wi_synthetic_pending_01"},
 {allowanceNumber:"A20261015002",workItemId:"wi_synthetic_pending_02"}];
const hints=select(watch,[page]);
assert.equal(hints[0].needsSelectiveQuery,true);
assert.equal(hints[0].reason,"OFFICIAL_STATUS_CHANGED_OR_PENDING");
assert.equal(hints[1].needsSelectiveQuery,false);
assert.equal(hints[1].reason,"NOT_SEEN_NOT_PROOF",
  "List absence is not permission to resend or mark g0401 rejected");
const divergent={...page,dateSelect:2,items:[{...page.items[0],type:"D0501"}]};
assert.equal(select(watch,[page,divergent])[0].reason,"CONFLICTING_LIST_RESULTS");
await assert.rejects(async()=>select([watch[0],watch[0]],[page]),/WATCH_INVALID/);
await assert.rejects(async()=>select(watch,[{...page,canAutoConfirm:true}]),/UNTRUSTED_PAGE/);
for(const [wire,pattern] of [
 [{...sample,page_now:2},/PAGINATION_CHANGED/],
 [{...sample,page_total:0},/PAGINATION_CHANGED/],
 [{...sample,data_total:0},/INCOMPLETE/],
 [{...sample,data:[...sample.data,...sample.data]},/DUPLICATE_OR_BAD_NUMBER/],
 [{...sample,data:[{...sample.data[0],invoice_status:"unknown"}]},/INCOMPLETE/],
 [{...sample,data:[{...sample.data[0],invoice_type:""}]},/INCOMPLETE/],
 [{...sample,data:[{...sample.data[0],allowance_date:20260230}]},/INCOMPLETE/],
 [{code:51},/OFFICIAL_REJECTED/]
 ])await assert.rejects(()=>query(base,provider(wire),()=>now),pattern);
const before=calls;
await assert.rejects(()=>query({...base,dateStart:"20260230"},
 provider(),()=>now),/INPUT_INVALID/);
await assert.rejects(()=>query({...base,limit:501},
 provider(),()=>now),/INPUT_INVALID/);
await assert.rejects(()=>query({...base,page:0},
 provider(),()=>now),/INPUT_INVALID/);
assert.equal(calls,before,"Invalid window and paging never cause network I/O");
await assert.rejects(()=>query(base,async()=>{throw Error("offline")},()=>now),
 /UNAVAILABLE/);
await assert.rejects(()=>query(base,async()=>new Response("x".repeat(530000)),()=>now),
 /TOO_LARGE/);
// One bounded on-demand sweep covers both official date_select dimensions.
let scanCalls=0;
const scanTransport=async(url,opts)=>{
  scanCalls++;
  const data=JSON.parse(new URLSearchParams(opts.body).get("data"));
  assert.equal(data.limit,20);
  assert.ok(data.date_select===1||data.date_select===2);
  assert.equal(data.page,1);
  return Response.json(sample);
};
const scanInput={sellerInvoice:base.sellerInvoice,appKey:key,
  dateStart:base.dateStart,dateEnd:base.dateEnd};
const scanResult=await scan(scanInput,watch,scanTransport,()=>now);
assert.equal(scanCalls,2,"One page for each official date dimension");
assert.equal(scanResult.pages.length,2);
assert.equal(scanResult.hints[0].needsSelectiveQuery,true);
assert.equal(scanResult.hints[1].reason,"NOT_SEEN_NOT_PROOF");
assert.equal(scanResult.canAutoConfirm,false);
assert.equal(scanResult.canAutoResend,false);
await assert.rejects(()=>scan({...scanInput,dateStart:"20260101"},
  watch,scanTransport,()=>now),/WINDOW_TOO_LARGE/);
await assert.rejects(()=>scan(scanInput,[watch[0],watch[0]],
  scanTransport,()=>now),/WATCH_INVALID/);
assert.equal(scanCalls,2,"Invalid scan must not hit AMEGO");
assert.deepEqual((await scan(scanInput,[],scanTransport,()=>now)).pages,[]);
assert.equal(scanCalls,2,"No pending exceptions means zero official calls");
// Reject huge windows rather than silently trusting the first page.
await assert.rejects(()=>scan(scanInput,watch,async()=>{
  scanCalls++;
  return Response.json({...sample,page_total:5,data_total:100});
},()=>now),/WINDOW_TOO_LARGE/);
assert.equal(scanCalls,3,"Oversized scan aborts after its first page");
await assert.rejects(()=>scan(scanInput,watch,async()=>{
  scanCalls++;
  return Response.json({...sample,data_total:2});
},()=>now),/PAGINATION_INCOMPLETE/);
assert.equal(scanCalls,4,"Missing rows cannot be considered reconciled");
// Different snapshots in both date dimensions must never auto-close.
const changing=await scan(scanInput,watch,async(url,opts)=>{
  scanCalls++;
  const data=JSON.parse(new URLSearchParams(opts.body).get("data"));
  return Response.json({...sample,data:[{...sample.data[0],
    invoice_type:data.date_select===1?"D0401":"D0501"}]});
},()=>now);
assert.equal(changing.hints[0].reason,"CONFLICTING_LIST_RESULTS");
assert.equal(changing.canAutoResend,false);

console.log("Official allowance_list: signed page, anomaly-only whitelist, strict paging, bounded two-date scan, no auto-close passed");
