import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

// D1 Draft-only ledger tested with actual SQLite syntax and constraints.
// No AMEGO API, Cloudflare deployment, or original D1 is involved.
const built=await build({entryPoints:["src/allowance-number-ledger.ts"],
  bundle:true,write:false,platform:"node",format:"esm",target:"node24"});
const {
  reserveCloudAllowanceNumber:reserve,
  markCloudAllowanceSending:markSending,
  markCloudAllowanceExternalCollision:markCollision,
  markCloudAllowanceConfirmed:markConfirmed,
}=await import("data:text/javascript;base64,"+
  Buffer.from(built.outputFiles[0].text).toString("base64"));

const approvalBuild=await build({entryPoints:["src/allowance-review-approval.ts"],
  bundle:true,write:false,platform:"node",format:"esm",target:"node24"});
const {approveAllowanceAfterOfficialReview:approveOfficial}=
  await import("data:text/javascript;base64,"+
    Buffer.from(approvalBuild.outputFiles[0].text).toString("base64"));

const ws="synthetic_ws";
const devA="dev_11111111-1111-4111-8111-111111111111";
const devB="dev_22222222-2222-4222-8222-222222222222";
const base={workspaceId:ws,workItemId:"wi_allowance_one",deviceId:devA,
  expectedRevision:2,sellerInvoice:"12345678",environment:"test",
  allowanceDay:"20261015",requestSha256:"a".repeat(64)};
function fixture(){
  const db=new DatabaseSync(":memory:");
  const migrations=readdirSync("migrations").filter(n=>/^\d{4}_.*\.sql$/.test(n)).sort();
  for(const name of migrations.filter(n=>n<"0014"))
    db.exec(readFileSync("migrations/"+name,"utf8"));
  db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,?)").run(ws,"Synthetic");
  for(const id of [devA,devB])
    db.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
      VALUES (?,?,'Device','active','cloud')`).run(id,ws);
  db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
  for(const name of migrations.filter(n=>n>="0014"))
    db.exec(readFileSync("migrations/"+name,"utf8"));
  db.exec(readFileSync("drafts/0015_work_items.sql","utf8"));
  db.exec(readFileSync("drafts/0016_allowance_number_ledger.sql","utf8"));
  let race;
  class Statement {
    constructor(sql){this.sql=sql;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return db.prepare(this.sql).get(...this.args)??null;}
    async run(){
      if(race){const f=race;race=null;f();}
      return {meta:{changes:db.prepare(this.sql).run(...this.args).changes}};
    }
  }
  const approve=(id,revision=2,environment="test",deviceId=devA)=>{
    const now=new Date();
    db.prepare(`INSERT INTO allowance_execution_approvals
      (approval_id,workspace_id,work_item_id,approved_revision,seller_invoice,environment,
       allowance_day,template_sha256,official_snapshot_sha256,official_checked_at,
       approved_by_employee_id,approved_by_device_id,approved_at,valid_until_utc)
      VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)`)
      .run("apr_"+crypto.randomUUID(),ws,id,revision,base.sellerInvoice,environment,base.allowanceDay,
        base.requestSha256,"b".repeat(64),now.toISOString(),
        "cyid_manager",deviceId,now.toISOString(),
        new Date(now.getTime()+15*60_000).toISOString());
  };
  const addWork=(id, kind="ALLOWANCE",status="review",revision=2,environment="test",granted=true)=>{
    db.prepare(`INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,
      updated_by_device_id,environment,kind,reference_hash,status,revision,
      actor_id,created_at,updated_at)
      VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`)
      .run(id,ws,devA,devA,environment,kind,createHash("sha256").update(id).digest("hex"),status,
        revision,id,"2026-10-15","2026-10-15");
    if(granted && kind==="ALLOWANCE" && status==="review")approve(id,revision,environment);
  };
  addWork(base.workItemId);
  return {db,dbApi:{prepare:sql=>new Statement(sql)},addWork,approve,race:fn=>{race=fn;}};
}
function status(db,ledgerId) {
  return db.prepare("SELECT status FROM allowance_number_ledger WHERE ledger_id=?")
    .get(ledgerId)?.status;
}
const {db,dbApi,addWork,approve,race}=fixture();
const first=await reserve(dbApi,base);
assert.equal(first.allowanceNumber,"A20261015001");
assert.equal(first.sequence,1);
assert.equal(first.status,"reserved");
assert.equal(await reserve(dbApi,base).then(x=>x.ledgerId),first.ledgerId,
  "Same pending Work Item must reuse exact same number");
const stored=db.prepare("SELECT * FROM allowance_number_ledger WHERE ledger_id=?").get(first.ledgerId);
assert.equal(stored.request_sha256,base.requestSha256);
assert.equal(stored.work_item_id,base.workItemId);
assert.equal(Object.hasOwn(stored,"payload_json"),false,"D1 cannot store raw AMEGO payload");

addWork("wi_allowance_two");
const second=await reserve(dbApi,{...base,workItemId:"wi_allowance_two"});
assert.equal(second.allowanceNumber,"A20261015002");
await assert.rejects(()=>reserve(dbApi,{...base,requestSha256:"b".repeat(64)}),
  /ALLOWANCE_WORK_ITEM_IMMUTABLE/);
await assert.rejects(()=>reserve(dbApi,{...base,sellerInvoice:"87654321"}),
  /ALLOWANCE_WORK_ITEM_IMMUTABLE/);
await assert.rejects(()=>reserve(dbApi,{...base,allowanceDay:"20261016"}),
  /ALLOWANCE_WORK_ITEM_IMMUTABLE/);
await assert.rejects(()=>reserve(dbApi,{...base,allowanceDay:"20260230"}),
  /ALLOWANCE_DATE_INVALID/);
assert.equal(await markSending(dbApi,base,first),true);
assert.equal(await markSending(dbApi,base,first),false,"No second live send");
assert.throws(()=>db.prepare("UPDATE work_items SET status='closed' WHERE work_item_id=?")
  .run(base.workItemId),/ALLOWANCE_IN_FLIGHT_NOT_CLOSABLE/);
assert.throws(()=>db.prepare("DELETE FROM allowance_number_ledger WHERE ledger_id=?")
  .run(first.ledgerId),/ALLOWANCE_NUMBER_PERMANENT/);
assert.throws(()=>db.prepare("UPDATE allowance_number_ledger SET sequence=5 WHERE ledger_id=?")
  .run(first.ledgerId),/ALLOWANCE_NUMBER_PERMANENT/);
await assert.rejects(()=>reserve(dbApi,base),/REQUIRES_RECONCILIATION/);
await assert.rejects(()=>markCollision(dbApi,base,first,4040163),
  /COLLISION_NOT_CONFIRMED/);
assert.equal(await markCollision(dbApi,base,first,4040161),true);
assert.equal(await markCollision(dbApi,base,first,4040162),false,
  "A collision can be processed only once");
const third=await reserve(dbApi,base);
assert.equal(third.allowanceNumber,"A20261015003");
assert.equal(third.attempt,2);
assert.equal(status(db,first.ledgerId),"occupied_external");
assert.equal(await markSending(dbApi,base,third),true);
assert.equal(await markConfirmed(dbApi,base,third),true);
assert.equal(await markConfirmed(dbApi,base,third),false);
assert.equal(db.prepare("UPDATE work_items SET status='closed' WHERE work_item_id=?")
  .run(base.workItemId).changes,1,"A confirmed allowance can close the work item");
await assert.rejects(()=>reserve(dbApi,base),/REQUIRES_RECONCILIATION/);
assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,3,
  "No allocated number is recycled/deleted");

addWork("wi_allowance_four","VOID");
await assert.rejects(()=>reserve(dbApi,{...base,workItemId:"wi_allowance_four"}),
  /AUTHORITY_OR_REVISION_CHANGED/);
addWork("wi_allowance_five");
await assert.rejects(()=>reserve(dbApi,{...base,workItemId:"wi_allowance_five",
  expectedRevision:1}),/AUTHORITY_OR_REVISION_CHANGED/);
addWork("wi_allowance_six");
race(()=>db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devA));
await assert.rejects(()=>reserve(dbApi,{...base,workItemId:"wi_allowance_six"}),
  /AUTHORITY_OR_REVISION_CHANGED/);
const cross=await reserve(dbApi,{...base,deviceId:devB,workItemId:"wi_allowance_six"});
assert.equal(cross.allowanceNumber,"A20261015004",
  "Revoked Device did not consume the sequence");
assert.equal(await markSending(dbApi,{...base,workItemId:"wi_allowance_six"},cross),false,
  "Revoked original Device cannot begin sending after another Device reserved");
assert.equal(await markSending(dbApi,{...base,deviceId:devB,workItemId:"wi_allowance_six"},cross),true,
  "Only active Device with same Work Item revision may initiate operation");

addWork("wi_allowance_late");
race(()=>db.prepare("UPDATE work_items SET revision=revision+1 WHERE work_item_id=?")
  .run("wi_allowance_late"));
await assert.rejects(()=>reserve(dbApi,{...base,deviceId:devB,
  workItemId:"wi_allowance_late"}),/AUTHORITY_OR_REVISION_CHANGED/);
assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,4);
// review is NOT approval: the exact manager-authorized official snapshot
// grant must exist and match revision, company, environment and payload digest.
addWork("wi_no_approval","ALLOWANCE","review",2,"test",false);
await assert.rejects(()=>reserve(dbApi,{...base,deviceId:devB,
  workItemId:"wi_no_approval"}),/AUTHORITY_OR_REVISION_CHANGED/);
const approvedBefore=db.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n;
assert.equal(approvedBefore,4);
approve("wi_no_approval",2,"test",devB);
const granted=await reserve(dbApi,{...base,deviceId:devB,workItemId:"wi_no_approval"});
assert.equal(granted.allowanceNumber,"A20261015005");
assert.throws(()=>db.prepare("UPDATE allowance_execution_approvals SET valid_until_utc='2099-01-01' WHERE work_item_id=?")
  .run("wi_no_approval"),/ALLOWANCE_APPROVAL_APPEND_ONLY/,
  "A caller must never extend a grant by mutating the past approval");
assert.throws(()=>db.prepare("DELETE FROM allowance_execution_approvals WHERE work_item_id=?")
  .run("wi_no_approval"),/ALLOWANCE_APPROVAL_APPEND_ONLY/,
  "A former approving manager must remain in the D1 audit history");
// Advance only synthetic test time; the stored approval stays immutable.
// This proves the approval TTL fence rather than faking a modified grant.
const RealDate=globalThis.Date;
globalThis.Date=class extends RealDate {
  constructor(...args){super(...(args.length?args:["2099-01-01T00:00:00.000Z"]));}
  static now(){return RealDate.parse("2099-01-01T00:00:00.000Z");}
};
try {
  assert.equal(await markSending(dbApi,{...base,deviceId:devB,
    workItemId:"wi_no_approval"},granted),false,
    "Expired official review approval must not permit sending");
} finally {
  globalThis.Date=RealDate;
}
assert.equal(await markSending(dbApi,{...base,deviceId:devB,
  workItemId:"wi_no_approval"},granted),true);
assert.equal(await markSending(dbApi,{...base,deviceId:devB,
  workItemId:"wi_no_approval"},granted),false);
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
db.close();

// Boundary numbering without generating thousands of real AMEGO operations.
const boundary=fixture();
boundary.addWork("wi_allowance_boundary");
boundary.db.prepare(`INSERT INTO allowance_number_ledger
  (ledger_id,workspace_id,work_item_id,seller_invoice,environment,allowance_day,
   sequence,allowance_number,attempt,request_sha256,status,created_by_device_id,created_at,updated_at)
  VALUES ('al_synthetic',?,?,?,?,?,?,?,?,?,'confirmed',?,'2026-10-15','2026-10-15')`)
  .run(ws,base.workItemId,base.sellerInvoice,"test",base.allowanceDay,
    999,"A20261015999",1,base.requestSha256,devA);
const at1000=await reserve(boundary.dbApi,{...base,workItemId:"wi_allowance_boundary"});
assert.equal(at1000.allowanceNumber,"A20261015A00");
boundary.db.close();

const last=fixture();
last.addWork("wi_allowance_last");
last.db.prepare(`INSERT INTO allowance_number_ledger
  (ledger_id,workspace_id,work_item_id,seller_invoice,environment,allowance_day,
   sequence,allowance_number,attempt,request_sha256,status,created_by_device_id,created_at,updated_at)
  VALUES ('al_penultimate',?,?,?,?,?,?,?,?,?,'confirmed',?,'2026-10-15','2026-10-15')`)
  .run(ws,base.workItemId,base.sellerInvoice,"test",base.allowanceDay,
    34694,"A20261015ZZY",1,base.requestSha256,devA);
const atMaximum=await reserve(last.dbApi,{...base,workItemId:"wi_allowance_last"});
assert.equal(atMaximum.allowanceNumber,"A20261015ZZZ");
last.addWork("wi_allowance_overflow");
await assert.rejects(()=>reserve(last.dbApi,{...base,workItemId:"wi_allowance_overflow"}),
  /SEQUENCE_INVALID/);
last.db.close();
// Two managers may read the same next sequence. A concurrent D1 insert
// must make the second SQL-time MAX guard refuse, not double-allocate.
const concurrent=fixture();
concurrent.addWork("wi_allowance_race");
concurrent.addWork("wi_allowance_competitor");
concurrent.race(()=>concurrent.db.prepare(`INSERT INTO allowance_number_ledger
  (ledger_id,workspace_id,work_item_id,seller_invoice,environment,allowance_day,
   sequence,allowance_number,attempt,request_sha256,status,
   created_by_device_id,created_at,updated_at)
  VALUES ('al_competitor',?,?,?,?,?,?,?,?,?,'reserved',?,'2026-10-15','2026-10-15')`)
  .run(ws,"wi_allowance_competitor",base.sellerInvoice,"test",base.allowanceDay,
    1,"A20261015001",1,base.requestSha256,devA));
await assert.rejects(()=>reserve(concurrent.dbApi,{...base,
  workItemId:"wi_allowance_race"}),/AUTHORITY_OR_REVISION_CHANGED/);
const afterRace=await reserve(concurrent.dbApi,{...base,workItemId:"wi_allowance_race"});
assert.equal(afterRace.allowanceNumber,"A20261015002");
assert.equal(concurrent.db.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,2);
concurrent.db.close();

// Internal approval writer: official evidence and two current CYID checks are
// mandatory before an append-only grant can unlock the first reservation.
{
  const fx=fixture();
  const {db,dbApi,addWork,race}=fx;
  const id="wi_official_review";
  addWork(id,"ALLOWANCE","review",2,"test",false);
  const target={workspaceId:ws,workItemId:id,deviceId:devA,expectedRevision:2};
  const request={originalInvoiceNumber:"AB12345678",sellerInvoice:base.sellerInvoice,
    environment:"test",allowanceDay:base.allowanceDay,
    templateSha256:base.requestSha256,requestedGrossAmount:50};
  let actor={workspaceId:ws,employeeId:"manager_A",role:"ADMIN"};
  let official={originalInvoiceNumber:request.originalInvoiceNumber,
    checkedAtUtc:new Date().toISOString(),officialSnapshotSha256:"c".repeat(64),
    invoiceUploadStatus:99,invoiceCancelled:false,
    allowanceSummaryPresent:true,existingAllowanceCount:0,
    originalInvoiceGrossAmount:100,originalItemsAndRequestFullyVerified:true};
  let managerCalls=0, officialCalls=0;
  const authority={
    async revalidateManager(){managerCalls++;return actor;},
    async loadFrozenApplication(reviewTarget){
      return {...request,
        referenceHash:createHash("sha256").update(reviewTarget.workItemId).digest("hex")};
    },
    async queryAndVerifyOfficialInvoice(frozenRequest) {
      officialCalls++;
      assert.equal(frozenRequest.originalInvoiceNumber,request.originalInvoiceNumber);
      assert.equal(frozenRequest.sellerInvoice,request.sellerInvoice);
      assert.equal(frozenRequest.environment,request.environment);
      return {...official,sellerInvoice:frozenRequest.sellerInvoice,
        environment:frozenRequest.environment};
    }
  };
  const reserveTarget={...base,workItemId:id};
  await assert.rejects(()=>reserve(dbApi,reserveTarget),/AUTHORITY_OR_REVISION_CHANGED/);
  actor={...actor,role:"USER"};
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/MANAGER_AUTH_REQUIRED/);
  actor={...actor,role:"ADMIN"};
  official={...official,allowanceSummaryPresent:false};
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/REVIEW_NOT_AUTHORITATIVE/);
  official={...official,allowanceSummaryPresent:true,existingAllowanceCount:1};
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/REVIEW_NOT_AUTHORITATIVE/);
  official={...official,existingAllowanceCount:0,
    originalItemsAndRequestFullyVerified:false};
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/REVIEW_NOT_AUTHORITATIVE/);
  official={...official,originalItemsAndRequestFullyVerified:true,
    checkedAtUtc:"2020-01-01T00:00:00.000Z"};
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/REVIEW_NOT_AUTHORITATIVE/);
  official={...official,checkedAtUtc:new Date().toISOString()};
  // A valid invoice number from a DIFFERENT seller/environment must never
  // authorize allocation against the selected company's frozen Work Item.
  const wrongSeller={...authority,queryAndVerifyOfficialInvoice:async()=>
    ({...official,sellerInvoice:"87654321",environment:request.environment})};
  await assert.rejects(()=>approveOfficial(dbApi,target,wrongSeller),
    /REVIEW_NOT_AUTHORITATIVE/);
  const wrongEnvironment={...authority,queryAndVerifyOfficialInvoice:async()=>
    ({...official,sellerInvoice:request.sellerInvoice,environment:"production"})};
  await assert.rejects(()=>approveOfficial(dbApi,target,wrongEnvironment),
    /REVIEW_NOT_AUTHORITATIVE/);
  assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?")
    .get(id).n,0);
  // Grant must be linked to the exact persisted Work Item source, not just
  // a matching seller or a client-supplied official result.
  const different={...authority,
    loadFrozenApplication:async()=>({...request,referenceHash:"e".repeat(64)})};
  await assert.rejects(()=>approveOfficial(dbApi,target,different),/CONFLICT_OR_REVOKED/);
  const first=await approveOfficial(dbApi,target,authority);
  assert.equal(first.approvedByEmployeeId,"manager_A");
  assert.equal(first.templateSha256,base.requestSha256);
  assert.equal(managerCalls>=2,true);
  assert.equal(officialCalls>=1,true);
  let row=db.prepare("SELECT * FROM allowance_execution_approvals WHERE approval_id=?")
    .get(first.approvalId);
  assert.equal(row.approved_by_employee_id,"manager_A");
  assert.equal(row.official_snapshot_sha256,"c".repeat(64));
  assert.equal(await reserve(dbApi,reserveTarget).then(x=>x.allowanceNumber),"A20261015001");
  // Another manager's repeated official review is appended, never destructive.
  actor={...actor,employeeId:"manager_B"};
  const next=await approveOfficial(dbApi,target,authority);
  assert.notEqual(first.approvalId,next.approvalId);
  assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?").get(id).n,2);
  assert.equal(db.prepare("SELECT approved_by_employee_id actor FROM allowance_execution_approvals WHERE approval_id=?")
    .get(first.approvalId).actor,"manager_A");
  // Once the first official grant exists, a new approving device cannot
  // quietly change the frozen seller / environment / day for the same Work Item.
  request.sellerInvoice="87654321";
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/CONFLICT_OR_REVOKED/);
  request.sellerInvoice=base.sellerInvoice;
  request.allowanceDay="20261016";
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),/CONFLICT_OR_REVOKED/);
  request.allowanceDay=base.allowanceDay;
  assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?")
    .get(id).n,2);


  const reserved=await reserve(dbApi,reserveTarget);
  assert.equal(await markSending(dbApi,reserveTarget,reserved),true);
  await assert.rejects(()=>approveOfficial(dbApi,target,authority),
    /CONFLICT_OR_REVOKED/);
  assert.equal(await markConfirmed(dbApi,reserveTarget,reserved),true);

  // New revision / revoked device / role change during official call are
  // independently rejected without any new approval row.
  addWork("wi_official_stale","ALLOWANCE","review",2,"test",false);
  const stale={...target,workItemId:"wi_official_stale"};
  actor={...actor,role:"USER"};
  await assert.rejects(()=>approveOfficial(dbApi,stale,authority),
    /MANAGER_AUTH_REQUIRED/);
  actor={...actor,role:"ADMIN"};
  race(()=>db.prepare("UPDATE work_items SET revision=revision+1 WHERE work_item_id=?")
    .run("wi_official_stale"));
  await assert.rejects(()=>approveOfficial(dbApi,stale,authority),
    /CONFLICT_OR_REVOKED/);
  assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?").get(id).n,2);

  addWork("wi_official_revoked","ALLOWANCE","review",2,"test",false);
  race(()=>db.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devA));
  await assert.rejects(()=>approveOfficial(dbApi,
    {...target,workItemId:"wi_official_revoked"},authority),
    /CONFLICT_OR_REVOKED/);
  assert.equal(db.prepare("SELECT COUNT(*) n FROM allowance_execution_approvals WHERE work_item_id=?").get(id).n,2);
  assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
  db.close();
}

console.log("CYID D1 allowance number ledger: durable Work Item scoping, collision-only rollover, active Device/revision fencing, no D1 raw payload, A00/ZZZ limits passed");
