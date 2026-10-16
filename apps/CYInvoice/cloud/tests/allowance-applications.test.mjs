import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {build} from "esbuild";
import {createHash} from "node:crypto";
const built=await build({entryPoints:["src/allowance-applications.ts"],bundle:true,
  platform:"node",format:"esm",target:"node24",write:false});
const {createFrozenAllowanceApplication:create,loadFrozenAllowanceApplication:load}=
  await import("data:text/javascript;base64,"+
    Buffer.from(built.outputFiles[0].text).toString("base64"));
const ws="ws_synthetic",dev="dev_11111111-1111-4111-8111-111111111111";
const key="aa".repeat(32);
function fixture(){
  const sql=new DatabaseSync(":memory:");
  sql.exec("PRAGMA foreign_keys=ON;"+
    "CREATE TABLE workspaces(workspace_id TEXT PRIMARY KEY,status TEXT NOT NULL DEFAULT 'active');"+
    "CREATE TABLE devices(device_id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),"+
    "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud',token_hash TEXT);");
  sql.prepare("INSERT INTO workspaces(workspace_id) VALUES(?)").run(ws);
  sql.prepare("INSERT INTO devices(device_id,workspace_id) VALUES(?,?)").run(dev,ws);
  for(const file of ["0015_work_items.sql","0016_allowance_number_ledger.sql",
    "0017_company_settings.sql","0018_allowance_applications.sql"])
    sql.exec(readFileSync("drafts/"+file,"utf8"));
  sql.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,"+
    "revision,changed_by_employee_id,changed_by_device_id,changed_at) VALUES(?,?,'synthetic_envelope',1,?,?,?)")
    .run(ws,"12345678","super_admin",dev,"2026-10-15");
  class Statement{
    constructor(q){this.q=q;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return sql.prepare(this.q).get(...this.args)??null;}
    async all(){return {results:sql.prepare(this.q).all(...this.args)};}
    async run(){return {meta:{changes:sql.prepare(this.q).run(...this.args).changes}};}
  }
  return {sql,db:{prepare:q=>new Statement(q),async batch(items){
    sql.exec("BEGIN IMMEDIATE");
    try{const res=items.map(x=>({meta:{changes:sql.prepare(x.q).run(...x.args).changes}}));
      sql.exec("COMMIT");return res;}
    catch(e){sql.exec("ROLLBACK");throw e;}
  }}};
}
const fx=fixture(),runtime={environment:"test",companyEncryptionKeyHex:key};
let actor={workspaceId:ws,employeeId:"cyid_employee",workspaceRole:"USER"};
const authority={revalidate:async()=>actor};
const input={workspaceId:ws,deviceId:dev,workItemId:"wi_frozen_allowance_1",
  originalInvoiceNumber:"AB12345678",allowanceDay:"20261015",
  requestedGrossAmount:50,reason:"Synthetic partial refund",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"",AllowanceDate:"20261015",
    ProductItem:[{OriginalInvoiceNumber:"AB12345678",OriginalDescription:"Synthetic item",
      Quantity:1,Amount:50,Tax:0,TaxType:1}],TotalAmount:50,TaxAmount:0})};
const item=await create(fx.db,input,authority,runtime);
assert.equal(item.workItemId,input.workItemId);
const stored=fx.sql.prepare("SELECT * FROM allowance_applications").get();
assert.equal(stored.applicant_employee_id,actor.employeeId);
assert.equal(stored.original_invoice_number,input.originalInvoiceNumber);
assert.equal(stored.template_ciphertext.includes("Synthetic item"),false);
assert.equal(Object.hasOwn(stored,"template_json"),false);
assert.equal(fx.sql.prepare("SELECT kind FROM work_items").get().kind,"ALLOWANCE");
await assert.rejects(()=>load(fx.db,ws,input.workItemId,1,key),/NOT_REVIEWABLE/);
fx.sql.prepare("UPDATE work_items SET status='review',revision=2").run();
const frozen=await load(fx.db,ws,input.workItemId,2,key);
assert.equal(frozen.templateJson,input.unnumberedTemplateJson);
assert.equal(frozen.referenceHash,item.referenceHash);
assert.equal(frozen.requestedGrossAmount,50);
await assert.rejects(()=>load(fx.db,ws,input.workItemId,2,"bb".repeat(32)),/AUTH_FAILED/);
await assert.rejects(()=>create(fx.db,input,authority,runtime),/READBACK_REQUIRED/);
const invalid={...input,workItemId:"wi_template_invalid",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"A20261015001",
    AllowanceDate:"20261015",ProductItem:[{OriginalInvoiceNumber:"AB12345678"}]})};
await assert.rejects(()=>create(fx.db,invalid,authority,runtime),/TEMPLATE_MISMATCH/);
const foreign={...input,workItemId:"wi_wrong_invoice",
  unnumberedTemplateJson:JSON.stringify({AllowanceNumber:"",AllowanceDate:"20261015",
    ProductItem:[{OriginalInvoiceNumber:"XY12345678"}]})};
await assert.rejects(()=>create(fx.db,foreign,authority,runtime),/TEMPLATE_MISMATCH/);
actor={...actor,workspaceRole:"USER",employeeId:""};
await assert.rejects(()=>create(fx.db,{...input,workItemId:"wi_no_user_credential"},
  authority,runtime),/AUTH_REQUIRED/);
actor={...actor,employeeId:"cyid_employee"};
fx.sql.prepare("UPDATE devices SET status='revoked'").run();
await assert.rejects(()=>create(fx.db,{...input,workItemId:"wi_revoked_device"},
  authority,runtime),/UNKNOWN_READBACK_REQUIRED/);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,1,
  "A rejected Device cannot leave a half-created application");
fx.sql.prepare("UPDATE devices SET status='active'").run();
fx.sql.prepare("UPDATE company_settings SET revision=2").run();
await assert.rejects(()=>load(fx.db,ws,input.workItemId,2,key),/COMPANY_CHANGED/);
assert.throws(()=>fx.sql.prepare("DELETE FROM allowance_applications").run(),/IMMUTABLE/);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,1);
assert.deepEqual(fx.sql.prepare("PRAGMA foreign_key_check").all(),[]);
// Protected route: the CYID identity Workspace must be explicitly converted
// to the Business Workspace binding; client scope and seller fields are refused.
fx.sql.prepare("UPDATE company_settings SET revision=1").run();
const bundle=await build({entryPoints:["src/allowance-applications-route.ts"],
  bundle:true,format:"esm",platform:"node",target:"node24",write:false});
const {handleAllowanceApplications:route}=await import("data:text/javascript;base64,"+
  Buffer.from(bundle.outputFiles[0].text).toString("base64"));
const token="cydev_"+"a".repeat(64);
fx.sql.prepare("UPDATE devices SET token_hash=?").run(createHash("sha256").update(token).digest("hex"));
let who={workspaceId:"identity_synthetic",employeeId:"cyid_employee",
  workspaceRole:"USER",emailVerified:true};
const env={
  DB:fx.db,CYID_ENABLED:"true",
  IDENTITY:{fetch:async()=>Response.json({})},
  IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_synthetic",
  IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
  ALLOWANCE_OPERATION_ENVIRONMENT:"test",
  COMPANY_SETTINGS_KEY_HEX:key,
  cyIdContext:{revalidate:async()=>who}
};
const createBody={
  employeeNo:"1234",password:"SYNTHETIC_PASS",itemId:"wi_allowance_route_1",
  originalInvoiceNumber:input.originalInvoiceNumber,allowanceDay:input.allowanceDay,
  requestedGrossAmount:input.requestedGrossAmount,reason:input.reason,
  unnumberedTemplateJson:input.unnumberedTemplateJson
};
async function call(action,body,config=env,bearer=token){
  const r=await route(new Request("https://cyinvoice.synthetic/v1/allowance-applications/"+action,{
    method:"POST",headers:{"authorization":"Bearer "+bearer,
      "content-type":"application/json"},body:JSON.stringify(body)
  }),config);
  return {status:r.status,data:await r.json()};
}
assert.equal((await call("create",createBody,{...env,cyIdContext:undefined})).status,401);
assert.equal((await call("create",{...createBody,workspaceId:"ws_attacker"})).status,400);
assert.equal((await call("create",{...createBody,environment:"production"})).status,400);
assert.equal((await call("create",createBody,{...env,ALLOWANCE_OPERATION_ENVIRONMENT:undefined})).status,503);
const created=await call("create",createBody);
assert.equal(created.status,201,JSON.stringify(created));
assert.deepEqual(Object.keys(created.data).sort(),["itemId","ok","revision","status"]);
const view=await call("get",{itemId:createBody.itemId});
assert.equal(view.status,200,JSON.stringify(view));
assert.equal(view.data.application.originalInvoiceNumber,input.originalInvoiceNumber);
assert.equal(view.data.application.applicantEmployeeId,"cyid_employee");
assert.equal(JSON.stringify(view).includes("Synthetic item"),false);
assert.equal(JSON.stringify(view).includes("synthetic_envelope"),false);
assert.equal((await call("create",createBody)).status,409);
assert.equal((await call("get",{itemId:createBody.itemId})).status,200,
  "Lost create response must be reconciled by the same Work Item ID");
who={...who,employeeId:"other_cyid_employee"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,404);
who={...who,workspaceRole:"ADMIN"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,200);
// Review is a read-only preview of the tamper-checked frozen Work Item.
// It must never be mistaken for approval or a verified official invoice.
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:1})).status,409);
fx.sql.prepare("UPDATE work_items SET status='review',revision=2 WHERE work_item_id=?")
  .run(createBody.itemId);
const review=await call("review-preview",{itemId:createBody.itemId,expectedRevision:2});
assert.equal(review.status,200,JSON.stringify(review));
assert.equal(review.data.review.canApprove,false);
assert.equal(review.data.review.officialVerification,"NOT_PERFORMED");
assert.equal(review.data.review.items.length,1);
assert.equal(review.data.review.items[0].description,"Synthetic item");
assert.equal(review.data.review.applicantEmployeeId,"cyid_employee");
for(const forbidden of ["SYNTHETIC_APP_KEY","SYNTHETIC_MO","v1:","BuyerEmailAddress",
  "unnumberedTemplateJson","templateJson"])
  assert.equal(JSON.stringify(review.data).includes(forbidden),false);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:1})).status,409);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:2,officialApproved:true})).status,400);
const page1=await call("list",{limit:1});
assert.equal(page1.status,200,JSON.stringify(page1));
assert.equal(page1.data.applications.length,1);
assert.notEqual(page1.data.nextCursor,null);
const page2=await call("list",{limit:1,...page1.data.nextCursor});
assert.equal(page2.status,200);
assert.equal(page2.data.applications.length,1);
assert.equal(page2.data.nextCursor,null);
assert.notEqual(page1.data.applications[0].itemId,page2.data.applications[0].itemId);
assert.equal((await call("list",{limit:51})).status,400);
assert.equal((await call("list",{limit:10,workspaceId:"ws_other"})).status,400);
who={...who,workspaceRole:"USER"};
assert.equal((await call("list",{limit:1})).status,403);
assert.equal((await call("review-preview",{itemId:createBody.itemId,
  expectedRevision:2})).status,403);
who={...who,workspaceRole:"ADMIN"};
// Official READ-ONLY query: the company key must be loaded from D1 instead of
// applicant inputs; no g0401, no implicit approval, and manager revocation
// must stop queries before reaching the provider.
const companyBundle=await build({entryPoints:["src/company-settings.ts"],
  bundle:true,format:"esm",platform:"node",target:"node24",write:false});
const {encryptCompanyBusinessData:encryptSecret}=await import(
  "data:text/javascript;base64,"+
  Buffer.from(companyBundle.outputFiles[0].text).toString("base64"));
const officialTestKey="SYNTHETIC_OFFICIAL_KEY_DO_NOT_USE";
const secret=await encryptSecret(key,ws,"amego",officialTestKey);
fx.sql.prepare("UPDATE company_settings SET amego_app_key_ciphertext=?")
  .run(secret);
let officialCalls=0;
const originalFetch=globalThis.fetch;
globalThis.fetch=async (url,opts)=>{
  officialCalls++;
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(JSON.parse(form.get("data")).invoice_number,"AB12345678");
  assert.equal(form.get("sign"),createHash("md5").update(
    form.get("data")+form.get("time")+officialTestKey).digest("hex"));
  return Response.json({code:0,msg:"",data:[{
    invoice_number:"AB12345678",status:99,cancel_date:0,
    total_amount:"100",allowance:[],product_item:[{description:"Synthetic"}]
  }]});
};
try {
  const firstInspection=await call("official-inspection",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(firstInspection.status,200,JSON.stringify(firstInspection));
  assert.equal(firstInspection.data.inspection.canApprove,false);
  assert.equal(firstInspection.data.inspection.status,"OFFICIAL_READ_ONLY_NOT_APPROVED");
  assert.equal(firstInspection.data.inspection.requiresPerItemVerification,true);
  assert.equal(firstInspection.data.inspection.zeroExistingAllowances,true);
  assert.deepEqual(firstInspection.data.inspection.allowanceHistory,[]);
  assert.deepEqual(firstInspection.data.inspection.historicalAllocationChecks,[]);
  assert.equal(firstInspection.data.inspection.historicalAllocationNotQueriedCount,0);
  assert.equal(firstInspection.data.inspection.historicalAllocationsComplete,false);
  assert.equal(firstInspection.data.inspection.historicalCoverageReason,
    "NO_HISTORY_REPORTED_NOT_PROOF");
  assert.equal(firstInspection.data.inspection.safeToInferRemainingBalance,false);
  assert.equal(firstInspection.data.inspection.invoiceGrossEnough,true);
  assert.equal(firstInspection.data.inspection.officialProductsPresent,true);
  assert.equal(firstInspection.data.inspection.officialPendingMutation,false);
  assert.equal(firstInspection.data.inspection.itemCheck.fullyMatched,false);
  assert.equal(firstInspection.data.inspection.itemCheck.reason,"ORIGINAL_TAX_BASIS_NOT_VERIFIED");
  assert.equal(officialCalls,1);
  assert.equal(JSON.stringify(firstInspection.data).includes(officialTestKey),false);
  assert.equal((await call("official-inspection",{itemId:createBody.itemId,
    expectedRevision:1})).status,409);
  assert.equal(officialCalls,1,"stale revision does not contact AMEGO");
  assert.equal((await call("official-inspection",{itemId:createBody.itemId,
    expectedRevision:2,canApprove:true})).status,400);
  assert.equal(officialCalls,1,"client-supplied approval flags are rejected");
  who={...who,workspaceRole:"USER"};
  assert.equal((await call("official-inspection",{itemId:createBody.itemId,
    expectedRevision:2})).status,403);
  assert.equal(officialCalls,1);
  who={...who,workspaceRole:"ADMIN"};
  let historicInvoiceCalls=0,historicAllowanceCalls=0;
  globalThis.fetch=async(url,opts)=>{
    const data=JSON.parse(new URLSearchParams(opts.body).get("data"));
    if(url==="https://invoice-api.amego.tw/json/invoice_query"){
      historicInvoiceCalls++;
      assert.equal(data.invoice_number,"AB12345678");
      return Response.json({code:0,data:[{
        invoice_number:"AB12345678",status:99,cancel_date:0,total_amount:100,
        allowance:[{allowance_number:"3821061800001",allowance_date:20261015,
          total_amount:"75",tax_amount:"4",invoice_status:99,cancel_date:0}],
        product_item:[{description:"Synthetic"}]
      }]});
    }
    assert.equal(url,"https://invoice-api.amego.tw/json/allowance_query");
    historicAllowanceCalls++;
    assert.equal(data.allowance_number,"3821061800001");
    return Response.json({code:0,data:{
      allowance_number:"3821061800001",invoice_type:"D0401",
      invoice_status:99,allowance_date:"20261015",allowance_type:2,
      cancel_date:0,total_amount:"75",tax_amount:"4",
      product_item:[
        {original_invoice_number:"AB12345678",amount:"50",tax:"3"},
        {original_invoice_number:"XY12345678",amount:"25",tax:"1"}
      ]
    }});
  };
  const history=await call("official-inspection",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(history.status,200,JSON.stringify(history));
  assert.equal(historicInvoiceCalls,1,"exactly one original invoice query");
  assert.equal(historicAllowanceCalls,1,"one known historical allowance");
  assert.deepEqual(history.data.inspection.historicalAllocationChecks,[{
    allowanceNumber:"3821061800001",
    reason:"ALLOCATED_LINES_MATCH_HEADER",lineTotalsVerified:true,
    originalNet:"50",originalTax:"3",originalGross:"53",
    safeToInferRemainingBalance:false
  }]);
  assert.equal(history.data.inspection.historicalAllocationNotQueriedCount,0);
  assert.equal(history.data.inspection.historicalAllocationsComplete,false);
  assert.equal(history.data.inspection.historicalCoverageReason,"HISTORY_RANGE_NOT_PROVEN");
  assert.equal(history.data.inspection.safeToInferRemainingBalance,false);
  assert.equal(history.data.inspection.canApprove,false);
  assert.equal(JSON.stringify(history.data).includes(officialTestKey),false);
  // Duplicate official source numbers must not inflate verified coverage.
  const fetchHistory=globalThis.fetch;
  globalThis.fetch=async(url,opts)=>{
    const response=await fetchHistory(url,opts);
    if(url!=="https://invoice-api.amego.tw/json/invoice_query")return response;
    const payload=await response.json();
    payload.data[0].allowance.push({...payload.data[0].allowance[0]});
    return Response.json(payload);
  };
  const duplicate=await call("official-inspection",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(duplicate.status,200,JSON.stringify(duplicate));
  assert.equal(duplicate.data.inspection.historicalCoverageReason,
    "DUPLICATE_HISTORY_NUMBER");
  assert.equal(duplicate.data.inspection.historicalAllocationChecks.length,1);
  assert.equal(duplicate.data.inspection.historicalAllocationNotQueriedCount,1);
  // A malformed second summary remains unqueried, never zero-valued.
  globalThis.fetch=async(url,opts)=>{
    const response=await fetchHistory(url,opts);
    if(url!=="https://invoice-api.amego.tw/json/invoice_query")return response;
    const payload=await response.json();
    payload.data[0].allowance.push({
      allowance_number:"3821061800002",allowance_date:"20261340",
      total_amount:"50",invoice_status:99,cancel_date:0
    });
    return Response.json(payload);
  };
  const incomplete=await call("official-inspection",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(incomplete.status,200,JSON.stringify(incomplete));
  assert.equal(incomplete.data.inspection.historicalCoverageReason,
    "SUMMARY_METADATA_INCOMPLETE");
  assert.equal(incomplete.data.inspection.historicalAllocationNotQueriedCount,1);
  // Official readback unavailability cannot be silently called "covered".
  globalThis.fetch=async(url,opts)=>{
    if(url==="https://invoice-api.amego.tw/json/allowance_query")
      return Response.json({code:51,data:null});
    return fetchHistory(url,opts);
  };
  const uncertain=await call("official-inspection",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(uncertain.status,200,JSON.stringify(uncertain));
  assert.equal(uncertain.data.inspection.historicalCoverageReason,
    "DETAIL_READBACK_UNVERIFIED");
  assert.equal(uncertain.data.inspection.historicalAllocationChecks[0].originalGross,null);
  assert.equal(uncertain.data.inspection.canApprove,false);
}finally{globalThis.fetch=originalFetch;}

// Ambiguous g0401 recovery: only the originally D1-reserved number may be
// queried; a plausible official match never silently marks it confirmed.
fx.sql.prepare("INSERT INTO allowance_number_ledger("+
  "ledger_id,workspace_id,work_item_id,seller_invoice,environment,allowance_day,"+
  "sequence,allowance_number,attempt,request_sha256,status,created_by_device_id,created_at,updated_at) "+
  "VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)").run("al_synthetic_readback",ws,
  createBody.itemId,"12345678","test","20261015",1,"A20261015001",1,
  fx.sql.prepare("SELECT template_sha256 FROM allowance_applications WHERE work_item_id=?")
    .get(createBody.itemId).template_sha256,"sending",dev,
  "2026-10-15T00:00:00Z","2026-10-15T00:00:00Z");
let readbackCalls=0,listCalls=0,listPresent=true;
globalThis.fetch=async (url,opts)=>{
  if(url==="https://invoice-api.amego.tw/json/allowance_list"){
    listCalls++;
    const payload=JSON.parse(new URLSearchParams(opts.body).get("data"));
    assert.ok(payload.date_select===1||payload.date_select===2);
    assert.equal(payload.limit,20);
    assert.equal(payload.page,1);
    return Response.json({code:0,page_now:1,page_total:listPresent?1:0,
      data_total:listPresent?1:0,data:listPresent?[{
        allowance_number:"A20261015001",invoice_type:"D0401",
        invoice_status:99,allowance_date:20261015}]:[]});
  }
  readbackCalls++;
  assert.equal(url,"https://invoice-api.amego.tw/json/allowance_query");
  const p=new URLSearchParams(opts.body);
  assert.deepEqual(JSON.parse(p.get("data")),{allowance_number:"A20261015001"});
  assert.equal(p.get("sign"),createHash("md5").update(
    p.get("data")+p.get("time")+officialTestKey).digest("hex"));
  return Response.json({code:0,data:{
    allowance_number:"A20261015001",invoice_type:"D0401",invoice_status:99,
    allowance_date:"20261015",allowance_type:2,cancel_date:0,
    total_amount:"50",tax_amount:"0",
    product_item:[{original_invoice_number:"AB12345678"}]
  }});
};
try{
  const r=await call("number-readback",{itemId:createBody.itemId,expectedRevision:2});
  assert.equal(r.status,200,JSON.stringify(r));
  assert.equal(r.data.readback.allowanceNumber,"A20261015001");
  assert.equal(r.data.readback.possibleMatch,true);
  assert.equal(r.data.readback.safeToAutoConfirm,false);
  assert.equal(r.data.readback.safeToAutoResend,false);
  assert.equal(fx.sql.prepare("SELECT status FROM allowance_number_ledger").get().status,"sending");
  assert.equal(readbackCalls,1);
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0,
    "An ordinary number readback is not a manual reconciliation Audit");
  assert.equal((await call("number-readback",{itemId:createBody.itemId,
    expectedRevision:1})).status,404);
  assert.equal((await call("number-readback",{itemId:createBody.itemId,
    expectedRevision:2,allowanceNumber:"A20261015002"})).status,400);
  assert.equal(readbackCalls,1,"Invalid client number cannot be used in queries");
  who={...who,workspaceRole:"USER"};
  assert.equal((await call("reconcile",{
    itemId:createBody.itemId,expectedRevision:2})).status,403);
  who={...who,workspaceRole:"ADMIN"};
  assert.equal((await call("reconcile",{
    itemId:createBody.itemId,expectedRevision:2,sellerInvoice:"87654321"})).status,400);
  assert.equal(listCalls,0,"Forged seller cannot reach official API");
  assert.equal((await call("reconcile",{
    itemId:createBody.itemId,expectedRevision:1})).status,404);
  assert.equal(listCalls,0);
  const matched=await call("reconcile",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(matched.status,200,JSON.stringify(matched));
  assert.equal(matched.data.reconciliation.listReason,
    "OFFICIAL_STATUS_CHANGED_OR_PENDING");
  assert.equal(matched.data.readback.allowanceNumber,"A20261015001");
  assert.equal(matched.data.readback.safeToAutoResend,false);
  assert.equal(matched.data.readback.safeToAutoConfirm,false);
  assert.equal(listCalls,2);
  assert.equal(readbackCalls,2,"Only D1 original number can be queried");
  const audit=fx.sql.prepare("SELECT * FROM allowance_reconciliation_checks").get();
  assert.equal(audit.work_item_id,createBody.itemId);
  assert.equal(audit.allowance_number,"A20261015001");
  assert.equal(audit.checked_by_employee_id,who.employeeId,
    "The audit must name the current verified CYID manager");
  assert.equal(audit.checked_by_device_id,dev);
  assert.match(audit.official_snapshot_sha256,/^[a-f0-9]{64}$/);
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1);
  assert.throws(()=>fx.sql.prepare(
    "UPDATE allowance_reconciliation_checks SET official_upload_status=0").run(),
    /ALLOWANCE_RECONCILIATION_APPEND_ONLY/);
  assert.throws(()=>fx.sql.prepare(
    "DELETE FROM allowance_reconciliation_checks").run(),
    /ALLOWANCE_RECONCILIATION_APPEND_ONLY/);
  listPresent=false;
  const notSeen=await call("reconcile",{
    itemId:createBody.itemId,expectedRevision:2});
  assert.equal(notSeen.status,200,JSON.stringify(notSeen));
  assert.equal(notSeen.data.reconciliation.listReason,"NOT_SEEN_NOT_PROOF");
  assert.equal(notSeen.data.readback,undefined);
  assert.equal(notSeen.data.reconciliation.safeToAutoResend,false);
  assert.equal(readbackCalls,2,"List absence never triggers an extra query");
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1,
    "List absence must never forge an official result");
  assert.equal(fx.sql.prepare("SELECT status FROM allowance_number_ledger")
    .get().status,"sending","Reconciliation cannot mutate ledger");
  listPresent=true;
  who={...who,workspaceRole:"USER"};
  assert.equal((await call("number-readback",{itemId:createBody.itemId,
    expectedRevision:2})).status,403);
  who={...who,workspaceRole:"ADMIN"};
  assert.equal(readbackCalls,2,"Unauthorized requests must not trigger extra official I/O");
}finally{globalThis.fetch=originalFetch;}

// A strict zero-prior, net-price test case may write an append-only
// manager grant ONLY after fresh official read-only verification.
const approvedId="wi_approved_strict_01";
// This other original invoice must stay independent of the earlier AB invoice
// already in a sending state, and may still be processed safely.
const strictBody={...createBody,itemId:approvedId,
  originalInvoiceNumber:"XY12345678",requestedGrossAmount:53,
  reason:"SYNTHETIC_VERIFY_AND_APPROVE",
  unnumberedTemplateJson:JSON.stringify({
    AllowanceNumber:"",AllowanceDate:"20261015",AllowanceType:2,
    BuyerIdentifier:"0000000000",BuyerName:"Synthetic consumer",
    ProductItem:[{OriginalInvoiceNumber:"XY12345678",OriginalInvoiceDate:20261015,
      OriginalDescription:"Service",Quantity:1,UnitPrice:50,Amount:50,Tax:3,TaxType:1}],
    TotalAmount:50,TaxAmount:3
  })};
assert.equal((await call("create",strictBody)).status,201);
fx.sql.prepare("UPDATE work_items SET status='review',revision=2 WHERE work_item_id=?")
  .run(approvedId);
let approvedOfficialCalls=0;
globalThis.fetch=async (url,opts)=>{
  approvedOfficialCalls++;
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(JSON.parse(form.get("data")).invoice_number,"XY12345678");
  return Response.json({code:0,data:[{
    invoice_number:"XY12345678",status:99,cancel_date:0,total_amount:105,
    invoice_date:20261015,detail_vat:0,tax_amount:5,allowance:[],wait:[],
    product_item:[{description:"Service",quantity:2,unit_price:50,
      amount:100,tax_type:1}]
  }]});
};
try{
  const authorized=await call("approve",{itemId:approvedId,expectedRevision:2});
  assert.equal(authorized.status,201,JSON.stringify(authorized));
  assert.equal(authorized.data.approval.canIssueNow,false);
  assert.equal(authorized.data.approval.requiresFinalPreflight,true);
  assert.equal(approvedOfficialCalls,1);
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?")
    .get(approvedId).n,1);
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger WHERE work_item_id=?")
    .get(approvedId).n,0,"Approval must not allocate or issue");
  // Reservation is permitted only after that exact grant. No AMEGO mutation
  // occurs and a repeated reservation returns exactly the original number.
  const reserved=await call("reserve",{itemId:approvedId,expectedRevision:2});
  assert.equal(reserved.status,201,JSON.stringify(reserved));
  assert.equal(reserved.data.number.status,"reserved");
  assert.equal(reserved.data.number.allowanceNumber,"A20261015002");
  assert.equal(reserved.data.number.canAutoIssue,false);
  const status=await call("number-status",{itemId:approvedId,expectedRevision:2});
  assert.equal(status.status,200,JSON.stringify(status));
  assert.equal(status.data.number.allowanceNumber,"A20261015002");
  assert.equal(status.data.number.mustReadbackBeforeAnyNewAttempt,false);
  const again=await call("reserve",{itemId:approvedId,expectedRevision:2});
  assert.equal(again.status,201);
  assert.equal(again.data.number.allowanceNumber,"A20261015002");
  assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger WHERE work_item_id=?")
    .get(approvedId).n,1,"No new attempt for same reserved number");
  assert.equal(approvedOfficialCalls,1,"Reserve may not silently re-query or g0401");
  assert.equal((await call("reserve",{itemId:approvedId,expectedRevision:2,
    allowanceNumber:"A20261015999"})).status,400);
  const earlierOfficial=globalThis.fetch;
  let signedMutations=0,finalQueries=0;
  globalThis.fetch=async(url,opts)=>{
    if(url==="https://invoice-api.amego.tw/json/invoice_query"){
      finalQueries++;
      return earlierOfficial(url,opts);
    }
    if(url==="https://invoice-api.amego.tw/json/g0401"){
      signedMutations++;
      const post=new URLSearchParams(opts.body);
      const payload=JSON.parse(post.get("data"));
      assert.equal(payload.length,1);
      assert.equal(payload[0].AllowanceNumber,"A20261015002");
      assert.equal(payload[0].AllowanceType,2);
      assert.equal(post.get("sign"),createHash("md5").update(
        post.get("data")+post.get("time")+officialTestKey).digest("hex"));
      return Response.json({code:0,msg:""});
    }
    throw Error("Unexpected AMEGO endpoint in synthetic test");
  };
  try{
    const issued=await call("send-once",{itemId:approvedId,expectedRevision:2});
    assert.equal(issued.status,202,JSON.stringify(issued));
    assert.equal(issued.data.attempt.result,"ACCEPTED_PENDING_OFFICIAL");
    assert.equal(issued.data.attempt.safeToAutoConfirm,false);
    assert.equal(issued.data.attempt.mustReadbackByOriginalNumber,true);
    assert.equal(signedMutations,1);
    assert.equal(finalQueries,1);
    assert.equal(fx.sql.prepare("SELECT status FROM allowance_number_ledger WHERE work_item_id=?")
      .get(approvedId).status,"sending");
    const noSecond=await call("send-once",{itemId:approvedId,expectedRevision:2});
    assert.equal(noSecond.status,409);
    assert.equal(signedMutations,1,"Never resend an already claimed first attempt");
    assert.equal(finalQueries,1,"No extra official check after sending");
    const saved=await call("number-status",{itemId:approvedId,expectedRevision:2});
    assert.equal(saved.status,200);
    assert.equal(saved.data.number.mustReadbackBeforeAnyNewAttempt,true);
  }finally{globalThis.fetch=earlierOfficial;}

  assert.equal((await call("approve",{itemId:approvedId,expectedRevision:1})).status,409);
  assert.equal(approvedOfficialCalls,2,"stale Work Item cannot query AMEGO");
  assert.equal((await call("approve",{itemId:approvedId,expectedRevision:2,
    officialApproved:true})).status,400);
  assert.equal(approvedOfficialCalls,2);
  who={...who,workspaceRole:"USER"};
  assert.equal((await call("approve",{itemId:approvedId,expectedRevision:2})).status,403);
  assert.equal(approvedOfficialCalls,2);
  who={...who,workspaceRole:"ADMIN"};
}finally{globalThis.fetch=originalFetch;}

// A second manager may reserve for the SAME AB original invoice. That does
// not permit a second first-send after a different Work Item is "sending".
// Test the real SQLite Draft trigger, not a non-atomic preflight SELECT.
const competingSource=fx.sql.prepare(
  "SELECT template_sha256 FROM allowance_applications WHERE work_item_id=?")
  .get(input.workItemId);
assert.ok(competingSource,"Independent frozen AB application is present");
const nowUtc="2026-10-16T02:00:00Z";
fx.sql.prepare(
 "INSERT INTO allowance_number_ledger(ledger_id,workspace_id,work_item_id,"+
 "seller_invoice,environment,allowance_day,sequence,allowance_number,attempt,"+
 "request_sha256,status,created_by_device_id,created_at,updated_at) "+
 "VALUES(?,?,?,?,?,?,?,?,?,?,'reserved',?,?,?)"
).run("al_competing_original",ws,input.workItemId,"12345678","test",
 "20261015",3,"A20261015003",1,competingSource.template_sha256,
 dev,nowUtc,nowUtc);
assert.throws(()=>fx.sql.prepare(
 "UPDATE allowance_number_ledger SET status='sending' WHERE ledger_id=?")
 .run("al_competing_original"),/ALLOWANCE_ORIGINAL_INVOICE_IN_FLIGHT/,
 "A concurrent second Work Item for AB cannot start another g0401");
assert.equal(fx.sql.prepare(
 "SELECT status FROM allowance_number_ledger WHERE ledger_id=?")
 .get("al_competing_original").status,"reserved",
 "The blocked original number remains safely unissued");
assert.equal(fx.sql.prepare(
 "SELECT COUNT(*) n FROM allowance_number_ledger WHERE status='sending'")
 .get().n,2,"Different originals AB and XY may each have one sending attempt");

who={...who,workspaceId:"attacker_identity_workspace"};
assert.equal((await call("get",{itemId:createBody.itemId})).status,403);
assert.equal(fx.sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,3);
assert.deepEqual(fx.sql.prepare("PRAGMA foreign_key_check").all(),[]);
fx.sql.close();
console.log("Draft allowance applications: immutable encrypted template, atomic SQL, scoped CYID protected routes and readback passed");
