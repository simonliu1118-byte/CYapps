import assert from "node:assert/strict";
import {readFileSync,readdirSync,mkdtempSync,copyFileSync,rmSync} from "node:fs";
import {tmpdir} from "node:os";
import {join,dirname} from "node:path";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";
import {createHash} from "node:crypto";
// C3c2 ONLY synthetic source execution. No Cloudflare D1, Worker route,
// official AMEGO network or g0401/g0501 financial mutations.
async function bundle(path){
 const b=await build({entryPoints:["src/"+path],bundle:true,write:false,
  format:"esm",platform:"node",target:"node24"});
 return import("data:text/javascript;base64,"+
  Buffer.from(b.outputFiles[0].text).toString("base64"));
}
const {executeCloudAllowanceExactlyOnce:execute}=await bundle("allowance-one-shot-coordinator.ts");
const {approveCloudAllowanceAfterOfficialProof:approve}=
 await bundle("allowance-manager-approval.ts");
const {reserveApprovedCloudAllowanceNumber:reserve,
 reserveCloudAllowanceAfterExternalCollision:reserveCollision}=
 await bundle("allowance-number-reservation.ts");
const {createEncryptedCloudAllowance:create}=await bundle("allowance-frozen-store.ts");
const {encryptCompanyBusinessData:seal}=await bundle("company-settings.ts");

const ws="ws_c3c2",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const context={encryptionKeyHex:"aa".repeat(32),environment:"test"};
const employee={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const authority=(a=manager)=>({revalidate:async()=>a});
const time=new Date("2026-10-19T08:00:00.000Z"),clock=()=>time;
const invoice="AB12345678";
const template=JSON.stringify({AllowanceNumber:"",AllowanceType:2,
 AllowanceDate:"20261019",TotalAmount:50,TaxAmount:0,
 BuyerName:"FAKE BUYER",BuyerIdentifier:"0000000000",
 ProductItem:[{OriginalInvoiceNumber:invoice,OriginalInvoiceDate:20261019,
  OriginalDescription:"FAKE ITEM",Quantity:1,UnitPrice:50,
  Amount:50,Tax:0,TaxType:1}]});
const app1="application_c3c2_one",app2="application_c3c2_two";
const input=id=>({workspaceId:ws,applicationId:id,deviceId:devA,
 environment:"test",originalInvoiceNumber:invoice,allowanceDay:"20261019",
 requestedGross:50,unnumberedTemplateJson:template});
const approvalTarget=id=>({workspaceId:ws,applicationId:id,
 deviceId:devB,expectedRevision:2});
const reserveTarget=id=>({...approvalTarget(id),expectedRevision:3});
const sendTarget=(id,number)=>({...reserveTarget(id),
 ledgerId:number.ledgerId,allowanceNumber:number.allowanceNumber});
const invoiceRecord={invoice_number:invoice,status:99,cancel_date:0,
 total_amount:100,invoice_date:20261019,detail_vat:1,tax_amount:5,
 product_item:[{description:"FAKE ORIGINAL"}],allowance:[],wait:[]};
function fakeOfficial(row=invoiceRecord,onCall=()=>{}){
 let calls=0;
 return {get count(){return calls;},transport:async (url,opts)=>{
  calls++;onCall(url);
  assert.equal(url,"https://invoice-api.amego.tw/json/invoice_query");
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  const form=new URLSearchParams(opts.body);
  assert.equal(form.get("invoice"),"12345678");
  assert.deepEqual(JSON.parse(form.get("data")),
   {type:"invoice",invoice_number:invoice});
  assert.equal(form.get("sign"),
   createHash("md5").update(form.get("data")+form.get("time")+
     "SYNTHETIC_APP_KEY_DO_NOT_USE").digest("hex"));
  return Response.json({code:0,data:[row]});
 }};
}
async function fixture(schema19=false,filename=":memory:"){
 const sql=new DatabaseSync(filename);
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0019_allowance_manual_review_audit.sql");
 for(const f of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES(?,'Synthetic')").run(ws);
 for(const device of [devA,devB])
  sql.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,
   status,employee_authority_state)
   VALUES(?,?,'Synthetic','active','cloud')`).run(device,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of migrations.filter(x=>x>="0014"&&x<"0019"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 if(schema19)sql.exec(readFileSync("migrations/0019_allowance_manual_review_audit.sql","utf8"));
 const appKey=await seal(context.encryptionKeyHex,ws,"amego",
   "SYNTHETIC_APP_KEY_DO_NOT_USE");
 sql.prepare(`INSERT INTO company_settings(
  workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
  changed_by_employee_id,changed_by_device_id,changed_at)
  VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:40:00.000Z')`)
  .run(ws,appKey,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 let beforeSend=null,loseReply=false,afterSend=null,beforeBatch=null,lostBatch=false;
 class Statement {
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async run(){
   if(this.q.includes("UPDATE allowance_number_ledger")&&beforeSend){
    const cb=beforeSend;beforeSend=null;cb();
   }
   const result={meta:{changes:sql.prepare(this.q).run(...this.args).changes}};
   if(this.q.includes("UPDATE allowance_number_ledger")&&afterSend){
    const cb=afterSend;afterSend=null;cb();
   }
   if(this.q.includes("UPDATE allowance_number_ledger")&&loseReply){
    loseReply=false;throw new Error("lost response AFTER successful write");
   }
   return result;
  }
 }
 const db={prepare:q=>new Statement(q),async batch(parts){
  if(beforeBatch){const f=beforeBatch;beforeBatch=null;f();}
  sql.exec("BEGIN IMMEDIATE");
  let result;
  try{
   result=parts.map(p=>({meta:{
    changes:sql.prepare(p.q).run(...p.args).changes}}));
   sql.exec("COMMIT");
  }catch(e){sql.exec("ROLLBACK");throw e;}
  if(lostBatch){lostBatch=false;throw new Error("lost batch reply AFTER commit");}
  return result;
 }};
 async function prepare(id){
  await create(db,input(id),context,authority(employee));
  sql.prepare(`UPDATE allowance_applications SET status='review',revision=2,
   manager_actor_id='manager',actor_id='manager',updated_by_device_id=?,
   updated_at='2026-10-19T07:58:00.000Z' WHERE application_id=?`)
  .run(devB,id);
  const official=fakeOfficial();
  await approve(db,approvalTarget(id),context,authority(),
   official.transport,clock);
  assert.equal(official.count,1);
  const num=await reserve(db,reserveTarget(id),context,authority(),clock);
  assert.equal(num.status,"reserved");
  return sendTarget(id,num);
 }
 return {sql,db,prepare,close:()=>sql.close(),
  beforeSend:f=>beforeSend=f,afterSend:f=>afterSend=f,
  lostReply:()=>loseReply=true,beforeBatch:f=>beforeBatch=f,lostBatch:()=>lostBatch=true};
}

const status=(sql,id)=>sql.prepare("SELECT status FROM allowance_number_ledger WHERE application_id=?").get(id)?.status;
function providerFor(target,options={}){
 const urls=[];
 const item={original_invoice_number:options.otherOriginal?"CD87654321":invoice,original_invoice_date:20261019,
  original_description:"FAKE ITEM",quantity:1,unit_price:50,
  amount:50,tax:0,tax_type:1};
 const full={allowance_number:target.allowanceNumber,allowance_date:20261019,
  invoice_type:"D0401",invoice_status:99,allowance_type:2,
  buyer_identifier:options.otherBuyer?"87654321":"0000000000",buyer_name:options.otherBuyer?"EXTERNAL":"FAKE BUYER",
  total_amount:50,tax_amount:0,cancel_date:0,wait:[],product_item:[item]};
 return {urls,send:async(url,opts)=>{
  urls.push(url);
  const form=new URLSearchParams(opts.body);
  assert.equal(opts.method,"POST");
  assert.equal(opts.redirect,"error");
  assert.equal(form.get("invoice"),"12345678");
  assert.equal(form.get("sign"),createHash("md5").update(
   form.get("data")+form.get("time")+"SYNTHETIC_APP_KEY_DO_NOT_USE").digest("hex"));
  if(url.endsWith("/invoice_query"))return Response.json({code:0,data:[invoiceRecord]});
  if(url.endsWith("/g0401")){
   assert.deepEqual(JSON.parse(form.get("data"))[0].AllowanceNumber,target.allowanceNumber);
   if(options.lostPost)throw Error("response lost after official transaction");
   return Response.json({code:options.collision?4040161:0});
  }
  if(url.endsWith("/allowance_query")){
   assert.equal(JSON.parse(form.get("data")).allowance_number,target.allowanceNumber);
   if(options.unavailableReadback)return Response.json({code:71,data:null});
   return Response.json({code:0,data:full});
  }
  throw Error("unexpected official mutation");
 }};
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target);
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,1);
  assert.equal(p.urls.filter(x=>x.endsWith("/invoice_query")).length,1);
  assert.equal(p.urls.filter(x=>x.endsWith("/allowance_query")).length,1);
  assert.equal(result.issueKind,"accepted_unconfirmed");
  assert.equal(result.originalNumberReadback,"full_match_unattributed");
  assert.equal(result.manualDisposition,"full_match_unattributed");
  assert.equal(result.manualReviewRequired,true);
  assert.equal(result.proofOfThisPost,false);
  assert.equal(result.recordedReadback,true);
  assert.equal(result.safeToAutoConfirm,false);
  assert.equal(result.safeToAutoRetry,false);
  assert.equal(result.canAllocateAnotherNumber,false);
  assert.equal(status(sql,app1),"sending");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1);
  const audit=sql.prepare("SELECT official_snapshot_hash FROM allowance_reconciliation_checks").get();
  assert.match(audit.official_snapshot_hash,/^[a-f0-9]{64}$/);
  assert.throws(()=>sql.prepare("DELETE FROM allowance_reconciliation_checks").run(),/APPEND_ONLY/);
  const repeat=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),repeat.send,clock),
   /ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(repeat.urls.length,0,"never a second provider request on repeated call");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,1);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target,{lostPost:true,unavailableReadback:true});
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(result.issueKind,"unknown_result");
  assert.equal(result.manualDisposition,"unknown_outcome_requires_investigation");
  assert.equal(result.originalNumberReadback,"unconfirmed");
  assert.equal(result.recordedReadback,false);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,1);
  assert.equal(status(sql,app1),"sending");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0);
  const second=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),second.send,clock),
   /ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(second.urls.length,0);
 }finally{close();}
}
{
 const {sql,db,prepare,close}=await fixture();
 try{
  const target=await prepare(app1);
  const p=providerFor(target,{collision:true,otherBuyer:true});
  const result=await execute(db,target,context,authority(),p.send,clock);
  assert.equal(result.issueKind,"confirmed_number_collision");
  assert.equal(result.manualDisposition,"number_collision_requires_investigation");
  assert.equal(result.originalNumberReadback,"unconfirmed");
  assert.equal(result.recordedReadback,false);
  assert.equal(status(sql,app1),"sending","collision alone never renumbers or confirms");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,1);
 }finally{close();}
}
{
 const {sql,db,prepare,afterSend,close}=await fixture();
 try{
  const target=await prepare(app1);
  afterSend(()=>sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  const p=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),p.send,clock),
   /ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,0);
  assert.equal(status(sql,app1),"sending","post-CAS revocation is irreversibly readback-only");
 }finally{close();}
}
{
 const {sql,db,prepare,lostReply,close}=await fixture();
 try {
  const target=await prepare(app1);
  lostReply();
  const p=providerFor(target);
  await assert.rejects(()=>execute(db,target,context,authority(),p.send,clock),
   /ALLOWANCE_SENDING_READBACK_ONLY/);
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,0);
  assert.equal(status(sql,app1),"sending");
 }finally{close();}
}
// H rehearsal: a populated schema19 backup must remain recoverable after the
// unpromoted schema20 draft. Use the existing g0401 owner to create real-shaped
// synthetic sending/evidence/audit records; never infer anything about live D1.
{
 const dir=mkdtempSync(join(tmpdir(),"cyinvoice-schema20-"));
 const live=join(dir,"schema19.sqlite"),backup=join(dir,"backup19.sqlite"),
  restored=join(dir,"restored19.sqlite");
 const f=await fixture(true,live);
 const definitions=db=>db.prepare("SELECT name,type,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name").all();
 const rows=(db,names)=>Object.fromEntries(names.map(name=>
  [name,db.prepare('SELECT * FROM "'+name+'" ORDER BY rowid').all()]));
 try{
  f.sql.exec("PRAGMA journal_mode=WAL");
  const target=await f.prepare(app1);
  await execute(f.db,target,context,authority(),providerFor(target).send,clock);
  f.sql.prepare(`INSERT INTO allowance_manual_review_audit
   (review_id,application_id,workspace_id,ledger_id,allowance_number,
    ledger_status_at_review,disposition,reviewed_by_actor_id,reviewed_by_device_id,reviewed_at)
   VALUES('review_schema20',?,?,?,?,'sending','unknown_outcome','manager',?,?)`)
   .run(app1,ws,target.ledgerId,target.allowanceNumber,devB,time.toISOString());
  // Include migration bookkeeping in the backup, not just business tables.
  f.sql.exec("CREATE TABLE d1_migrations(id INTEGER PRIMARY KEY,name TEXT NOT NULL UNIQUE)");
  const canonical=readdirSync("migrations").filter(n=>/^\d{4}_.*\.sql$/.test(n)).sort();
  for(const [i,name] of canonical.entries())
   f.sql.prepare("INSERT INTO d1_migrations VALUES(?,?)").run(i+1,name);
  const priorSchema=definitions(f.sql),names=priorSchema.filter(x=>x.type==="table").map(x=>x.name);
  const prior=rows(f.sql,names);
  assert.equal(prior.allowance_number_ledger[0].status,"sending");
  assert.equal(prior.allowance_manual_review_audit.length,1);
  assert.equal(prior.allowance_reconciliation_checks.length,1);
  f.sql.exec("PRAGMA wal_checkpoint(TRUNCATE)");copyFileSync(live,backup);
  const ddl=readFileSync("drafts/0020_allowance_void_coordination.sql","utf8");
  const migrate=(injected=false)=>{
   f.sql.exec("BEGIN IMMEDIATE");
   try{
    f.sql.exec(ddl);
    if(injected)f.sql.exec("SELECT * FROM __missing_schema20_probe");
    f.sql.prepare("INSERT INTO d1_migrations VALUES(20,?)").run("0020_allowance_void_coordination.sql");
    f.sql.exec("COMMIT");
   }catch(error){f.sql.exec("ROLLBACK");throw error;}
  };
  assert.throws(()=>migrate(true),/no such table/);
  assert.deepEqual(definitions(f.sql),priorSchema,"failed draft20 must roll back every DDL object");
  assert.deepEqual(rows(f.sql,names),prior,"failed draft20 must preserve all schema19 rows/history");
  migrate();
  for(const original of priorSchema)
   assert.deepEqual(definitions(f.sql).find(x=>x.name===original.name),original);
  assert.deepEqual(rows(f.sql,names.filter(n=>n!=="d1_migrations")),
   Object.fromEntries(Object.entries(prior).filter(([n])=>n!=="d1_migrations")));
  assert.throws(()=>migrate(),/already exists/);
  assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM d1_migrations").get().n,20);
  const blocked=providerFor(target);
  await assert.rejects(()=>execute(f.db,target,context,authority(),blocked.send,clock),
   /ALLOWANCE_SENDING_LEDGER_NOT_RESERVED/);
  assert.equal(blocked.urls.filter(u=>u.endsWith("/g0401")).length,0,
   "draft20 never releases an existing unknown g0401 claim");
  const reopened=new DatabaseSync(live,{readOnly:true});
  try{
   assert.deepEqual(rows(reopened,names),rows(f.sql,names));
   assert.deepEqual(reopened.prepare("PRAGMA foreign_key_check").all(),[]);
   assert.equal(reopened.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  }finally{reopened.close();}
  copyFileSync(backup,restored);
  const recovery=new DatabaseSync(restored,{readOnly:true});
  try{
   assert.deepEqual(definitions(recovery),priorSchema);
   assert.deepEqual(rows(recovery,names),prior,"schema19 recovery must retain every original row");
   assert.deepEqual(recovery.prepare("PRAGMA foreign_key_check").all(),[]);
   assert.equal(recovery.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  }finally{recovery.close();}
  console.log("V3.2 H: populated schema19 backup, atomic draft20 failure/success, preserved g0401 claim, reopen and complete schema19 restore OK");
 }finally{f.close();assert.equal(dirname(dir),tmpdir());rmSync(dir,{recursive:true,force:true});}
}
// Cloud counterpart of the existing Local, explicitly reviewed collision flow.
// A duplicate error alone, a lost POST, same buyer or any shared original must
// never unlock a number, even with the independent collision feature enabled.
for(const options of [
 {collision:true,otherBuyer:true}, {collision:true,otherOriginal:true},
 {collision:true,otherBuyer:true,otherOriginal:true,unavailableReadback:true},
 {lostPost:true,otherBuyer:true,otherOriginal:true},
 {otherBuyer:true,otherOriginal:true}
]){
 const f=await fixture(true);
 try{
  const target=await f.prepare(app1),p=providerFor(target,options);
  const result=await execute(f.db,target,context,authority(),p.send,clock,true);
  assert.equal(result.ledgerStatus,"sending");assert.equal(status(f.sql,app1),"sending");
  const read=providerFor(target,{otherBuyer:true,otherOriginal:true});
  await assert.rejects(()=>reserveCollision(f.db,reserveTarget(app1),context,authority(),
   "collision_review_test",target.allowanceNumber,
   `確認外部占號 ${target.allowanceNumber} 並預留下一號`,read.send,clock),/NOT_ELIGIBLE/);
  assert.equal(read.urls.length,0);assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,1);
 }finally{f.close();}
}
async function collided(){
 const f=await fixture(true),t=await f.prepare(app1);
 const p=providerFor(t,{collision:true,otherBuyer:true,otherOriginal:true});
 const result=await execute(f.db,t,context,authority(),p.send,clock,true);
 assert.equal(result.ledgerStatus,"occupied_external");
 assert.equal(result.recordedReadback,false,"external evidence is NOT exact frozen match");
 assert.equal(f.sql.prepare("SELECT evidence_kind FROM allowance_reconciliation_checks").get().evidence_kind,
  "external_collision");
 return {...f,t};
}
{
 const dir=mkdtempSync(join(tmpdir(),"cyinvoice-schema19-"));
 const live=join(dir,"schema18.sqlite"),backup=join(dir,"backup18.sqlite"),restored=join(dir,"restored18.sqlite");
 const f=await fixture(false,live);
 try{
  const target=await f.prepare(app1);
  await execute(f.db,target,context,authority(),providerFor(target).send,clock);
  const prior=f.sql.prepare("SELECT * FROM allowance_reconciliation_checks").get();
  const definitions=()=>f.sql.prepare("SELECT name,type,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name").all();
  const priorSchema=definitions();
  f.sql.exec("PRAGMA wal_checkpoint(TRUNCATE)");copyFileSync(live,backup);
  // A populated schema18 must retain its real exact-match evidence. Exercise
  // transactional DDL rollback before applying the proposed migration once.
  f.sql.exec("BEGIN IMMEDIATE");
  try{
   f.sql.exec(readFileSync("migrations/0019_allowance_manual_review_audit.sql","utf8"));
   f.sql.exec("INSERT INTO nonexistent_migration_probe VALUES (1)");
   assert.fail("expected synthetic migration failure");
  }catch{f.sql.exec("ROLLBACK");}
  assert.deepEqual(definitions(),priorSchema,"failed schema19 must roll back all DDL and indexes");
  assert.deepEqual(f.sql.prepare("SELECT * FROM allowance_reconciliation_checks").get(),prior);
  assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE name='allowance_manual_review_audit'").get().n,0);
  f.sql.exec(readFileSync("migrations/0019_allowance_manual_review_audit.sql","utf8"));
  assert.equal(f.sql.prepare("SELECT evidence_kind FROM allowance_reconciliation_checks").get().evidence_kind,
   "full_match_unattributed");
  assert.equal(f.sql.prepare("SELECT official_snapshot_hash FROM allowance_reconciliation_checks").get().official_snapshot_hash,
   prior.official_snapshot_hash);
  assert.throws(()=>f.sql.exec(readFileSync("migrations/0019_allowance_manual_review_audit.sql","utf8")),/duplicate column/);
  assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
  assert.equal(f.sql.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  copyFileSync(backup,restored);
  const recovery=new DatabaseSync(restored,{readOnly:true});
  try{
   assert.deepEqual(recovery.prepare("SELECT name,type,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name").all(),priorSchema);
   assert.deepEqual(recovery.prepare("SELECT * FROM allowance_reconciliation_checks").get(),prior);
   assert.deepEqual(recovery.prepare("PRAGMA foreign_key_check").all(),[]);
   assert.equal(recovery.prepare("PRAGMA integrity_check").get().integrity_check,"ok");
  }finally{recovery.close();}
 }finally{f.close();assert.equal(dirname(dir),tmpdir());rmSync(dir,{recursive:true,force:true});}
}
{
 const f=await fixture(true);
 try{
  const target=await f.prepare(app1);
  f.sql.exec(`CREATE TRIGGER reject_external_check BEFORE INSERT ON allowance_reconciliation_checks
   WHEN NEW.evidence_kind='external_collision' BEGIN SELECT RAISE(ABORT,'fake unavailable audit'); END;`);
  const p=providerFor(target,{collision:true,otherBuyer:true,otherOriginal:true});
  const result=await execute(f.db,target,context,authority(),p.send,clock,true);
  assert.equal(result.ledgerStatus,"sending");assert.equal(status(f.sql,app1),"sending");
  assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM allowance_reconciliation_checks").get().n,0);
  assert.equal(p.urls.filter(url=>url.endsWith("/g0401")).length,1);
 }finally{f.close();}
}
const words=t=>`確認外部占號 ${t.allowanceNumber} 並預留下一號`;
for(const mode of ["success","lost_reply","audit_failure","company_race","device_race","parallel"]){
 const f=await collided();
 try{
  const p=providerFor(f.t,{otherBuyer:true,otherOriginal:true});
  const action=()=>reserveCollision(f.db,reserveTarget(app1),context,authority(),
   "collision_review_test",f.t.allowanceNumber,words(f.t),p.send,clock);
  if(mode==="lost_reply")f.lostBatch();
  if(mode==="audit_failure")f.sql.exec(`CREATE TRIGGER fail_audit BEFORE INSERT
   ON allowance_manual_review_audit BEGIN SELECT RAISE(ABORT,'fake audit failure'); END;`);
  if(mode==="company_race")f.beforeBatch(()=>f.sql.prepare(
   "UPDATE company_settings SET revision=revision+1 WHERE workspace_id=?").run(ws));
  if(mode==="device_race")f.beforeBatch(()=>f.sql.prepare(
   "UPDATE devices SET status='revoked' WHERE device_id=?").run(devB));
  if(mode==="parallel"){
   const outcomes=await Promise.allSettled([action(),action()]);
   assert.equal(outcomes.filter(x=>x.status==="fulfilled").length,1);
  }else if(mode!=="success")await assert.rejects(action,/READBACK_REQUIRED/);
  else {
   const next=await action();
   assert.equal(next.attempt,2);assert.equal(next.status,"reserved");
   assert.notEqual(next.allowanceNumber,f.t.allowanceNumber);
   assert.equal(next.sentToAmego,false);
   const reRead=await reserve(f.db,reserveTarget(app1),context,authority(),clock);
   assert.equal(reRead.ledgerId,next.ledgerId,"first-reserve endpoint is read-only for existing next attempt");
   const p2=providerFor(sendTarget(app1,next));
   const sent=await execute(f.db,sendTarget(app1,next),context,authority(),p2.send,clock);
   assert.equal(sent.ledgerStatus,"sending");
   assert.equal(p2.urls.filter(x=>x.endsWith("/g0401")).length,1);
   await assert.rejects(()=>execute(f.db,sendTarget(app1,next),context,authority(),p2.send,clock),/NOT_RESERVED/);
   assert.equal(p2.urls.filter(x=>x.endsWith("/g0401")).length,1);
  }
  const committed=["success","lost_reply","parallel"].includes(mode);
  assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM allowance_number_ledger").get().n,committed?2:1);
  assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM allowance_manual_review_audit").get().n,committed?1:0);
  const old=f.sql.prepare("SELECT status,collision_code FROM allowance_number_ledger WHERE ledger_id=?").get(f.t.ledgerId);
  assert.equal(old.status,"occupied_external");assert.equal(old.collision_code,4040161);
  if(committed){
   const replay=providerFor(f.t,{otherBuyer:true,otherOriginal:true});
   await assert.rejects(()=>reserveCollision(f.db,reserveTarget(app1),context,authority(),
    "collision_review_again",f.t.allowanceNumber,words(f.t),replay.send,clock),/NOT_ELIGIBLE/);
   assert.equal(replay.urls.length,0);
   assert.throws(()=>f.sql.exec("DELETE FROM allowance_manual_review_audit"),/APPEND_ONLY/);
  }
  assert.equal(p.urls.filter(x=>x.endsWith("/g0401")).length,0,"review does not post any financial mutation");
  assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{f.close();}
}
console.log("CYInvoice one-shot + reviewed external collision: immutable evidence, atomic next reservation, races/rollback/lost reply and zero blind repost passed");
