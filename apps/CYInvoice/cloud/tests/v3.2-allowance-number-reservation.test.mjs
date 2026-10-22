import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";

// C3c1 synthetic only. No g0401, no official transport, no Cloudflare.
// Canonical schema16 and REVIEW DRAFTS 17+18 are applied to in-memory SQLite.
async function bundle(file){
 const v=await build({entryPoints:["src/"+file],bundle:true,write:false,
  platform:"node",format:"esm",target:"node24"});
 return import("data:text/javascript;base64,"+
  Buffer.from(v.outputFiles[0].text).toString("base64"));
}
const {reserveApprovedCloudAllowanceNumber:reserve,
 allowanceNumberFromSequence:numberFor,MAX_ALLOWANCE_DAY_SEQUENCE:limit}
 =await bundle("allowance-number-reservation.ts");
const {createEncryptedCloudAllowance:create}=await bundle("allowance-frozen-store.ts");
const {encryptCompanyBusinessData:seal}=await bundle("company-settings.ts");
const ws="ws_c3c1",devA="dev_11111111-1111-4111-8111-111111111111",
 devB="dev_22222222-2222-4222-8222-222222222222";
const context={encryptionKeyHex:"aa".repeat(32),environment:"test"};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const employee={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const who=(actor=manager)=>({revalidate:async()=>actor});
const at=new Date("2026-10-19T08:00:00.000Z"),clock=()=>at;
const invoice="AB12345678",day="20261019",app1="application_c3c1_one",app2="application_c3c1_two";
const template=JSON.stringify({AllowanceNumber:"",AllowanceType:2,AllowanceDate:day,
 TotalAmount:50,TaxAmount:0,BuyerName:"Fake buyer",BuyerIdentifier:"0000000000",
 ProductItem:[{OriginalInvoiceNumber:invoice,OriginalInvoiceDate:20261019,
 OriginalDescription:"Synthetic item",Quantity:1,UnitPrice:50,Amount:50,Tax:0,TaxType:1}]});
const target=id=>({workspaceId:ws,applicationId:id,deviceId:devB,expectedRevision:3});
const make=id=>({workspaceId:ws,applicationId:id,deviceId:devA,
 environment:"test",originalInvoiceNumber:invoice,allowanceDay:day,
 requestedGross:50,unnumberedTemplateJson:template});
assert.equal(limit,34695);
for(const [i,n] of [[1,"001"],[999,"999"],[1000,"A00"],[1035,"A0Z"],
 [1036,"A10"],[2295,"AZZ"],[2296,"B00"],[limit,"ZZZ"]])
 assert.equal(numberFor(day,i),"A"+day+n);
for(const n of [0,-1,limit+1,1.5,NaN])
 assert.throws(()=>numberFor(day,n),/ALLOWANCE_SEQUENCE_INVALID/);
assert.throws(()=>numberFor("20260230",1),/ALLOWANCE_DATE_INVALID/);

async function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0020_allowance_void_coordination.sql");
 for(const f of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 sql.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'Synthetic')").run(ws);
 for(const dev of [devA,devB])
  sql.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state)
   VALUES (?,?,'Synthetic','active','cloud')`).run(dev,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const f of migrations.filter(x=>x>="0014"))
  sql.exec(readFileSync("migrations/"+f,"utf8"));
 const encrypted=await seal(context.encryptionKeyHex,ws,"amego","NOT_A_REAL_APPKEY");
 sql.prepare(`INSERT INTO company_settings
 (workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
 changed_by_employee_id,changed_by_device_id,changed_at)
 VALUES(?,'12345678',?,1,'manager',?,'2026-10-19T07:40:00.000Z')`)
 .run(ws,encrypted,devB);
 // Canonical 0017/0018 are included in the preceding migration sequence.
 let preWrite=null,throwAfter=false;
 class Statement {
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async run(){
   if(this.q.includes("INSERT INTO allowance_number_ledger")&&preWrite){
    const cb=preWrite;preWrite=null;cb();
   }
   const result={meta:{changes:sql.prepare(this.q).run(...this.args).changes}};
   if(this.q.includes("INSERT INTO allowance_number_ledger")&&throwAfter){
    throwAfter=false;throw new Error("fake network lost after write");
   }
   return result;
  }
 }
 const db={prepare:q=>new Statement(q),async batch(parts){
  sql.exec("BEGIN IMMEDIATE");
  try{
   const out=parts.map(p=>({meta:{changes:sql.prepare(p.q).run(...p.args).changes}}));
   sql.exec("COMMIT");return out;
  }catch(e){sql.exec("ROLLBACK");throw e;}
 }};
 async function approve(id){
  await create(db,make(id),context,who(employee));
  const h=sql.prepare("SELECT request_hash FROM allowance_applications WHERE application_id=?").get(id).request_hash;
  sql.prepare(`UPDATE allowance_applications
    SET status='review',revision=2,manager_actor_id='manager',actor_id='manager',
      updated_by_device_id=?,updated_at='2026-10-19T07:45:00.000Z'
    WHERE application_id=? AND status='open' AND revision=1`).run(devB,id);
  sql.prepare(`INSERT INTO allowance_execution_approvals
    (approval_id,application_id,workspace_id,reviewed_revision,request_hash,
     official_snapshot_hash,official_checked_at,manager_actor_id,
     manager_device_id,approved_at,valid_until_utc)
    VALUES (?, ?, ?,2,?,?,'2026-10-19T07:48:00.000Z',
     'manager',?,'2026-10-19T07:50:00.000Z','2026-10-19T08:10:00.000Z')`)
   .run("apr_"+id,id,ws,h,"c".repeat(64),devB);
  sql.prepare(`UPDATE allowance_applications SET status='approved',
    revision=3,actor_id='manager',updated_by_device_id=?,
    updated_at='2026-10-19T07:50:00.000Z'
    WHERE application_id=? AND status='review' AND revision=2`)
   .run(devB,id);
 }
 return {sql,db,approve,close:()=>sql.close(),
  beforeInsert:f=>preWrite=f,lostReply:()=>throwAfter=true};
}
const count=sql=>sql.prepare("SELECT COUNT(*) AS n FROM allowance_number_ledger").get().n;
{
 const {sql,db,approve,close}=await fixture();
 try{
  await approve(app1);
  const result=await reserve(db,target(app1),context,who(),clock);
  assert.equal(result.applicationId,app1);
  assert.match(result.ledgerId,/^al_[0-9a-f-]{36}$/);
  assert.equal(result.allowanceNumber,"A20261019001");
  assert.equal(result.status,"reserved");
  assert.equal(result.attempt,1);
  assert.equal(result.sequence,1);
  assert.equal(result.sentToAmego,false);
  assert.equal(result.maySendWithoutFreshOfficialCheck,false);
  assert.equal(count(sql),1);
  const reread=await reserve(db,target(app1),context,who(),
   ()=>new Date("2026-10-19T09:00:00.000Z"));
  assert.equal(reread.ledgerId,result.ledgerId,"repeat only returns same already reserved number");
  assert.equal(count(sql),1,"no duplicate attempt");
  assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM allowance_execution_approvals").get().n,1);
  assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM work_items").get().n,0);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
  await approve(app2);
  const second=await reserve(db,target(app2),context,who(),clock);
  assert.equal(second.allowanceNumber,"A20261019002");
  assert.equal(count(sql),2,"distinct valid partial allowance applications may reserve separately");
  sql.prepare("UPDATE allowance_number_ledger SET status='sending' WHERE ledger_id=?").run(result.ledgerId);
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
    /ALLOWANCE_RESERVE_PRIOR_UNSAFE_OR_NOT_RESERVABLE/,
    "no new number after sending or timeout");
  sql.prepare(`UPDATE allowance_number_ledger
    SET status='occupied_external',collision_code=4040161 WHERE ledger_id=?`)
   .run(result.ledgerId);
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
    /ALLOWANCE_RESERVE_PRIOR_UNSAFE_OR_NOT_RESERVABLE/,
    "confirmed collision does not auto-increment without a separate authorized flow");
  assert.equal(count(sql),2);
 }finally{close();}
}
{
 const {sql,db,approve,close}=await fixture();
 try{
  await approve(app1);
  for(const actor of [employee,null,{...manager,workspaceRole:"USER"},
    {...manager,workspaceId:"ws_wrong"}]){
   await assert.rejects(()=>reserve(db,target(app1),context,who(actor),clock),
     /ALLOWANCE_RESERVE_MANAGER_REQUIRED/);
  }
  await assert.rejects(()=>reserve(db,{...target(app1),expectedRevision:2},
    context,who(),clock),/ALLOWANCE_RESERVE_TARGET_INVALID/);
  sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB);
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
    /ALLOWANCE_RESERVE_DEVICE_REVOKED/);
  assert.equal(count(sql),0);
 }finally{close();}
}
{
 const {sql,db,approve,close}=await fixture();
 try{
  await approve(app1);
  sql.prepare("UPDATE company_settings SET revision=revision+1").run();
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
    /ALLOWANCE_FROZEN_UNAVAILABLE|ALLOWANCE_COMPANY_CHANGED/);
  assert.equal(count(sql),0);
 }finally{close();}
}
{
 const {sql,db,approve,close}=await fixture();
 try{
  await approve(app1);
  const expired=()=>new Date("2026-10-19T09:00:00.000Z");
  await assert.rejects(()=>reserve(db,target(app1),context,who(),expired),
    /ALLOWANCE_RESERVE_GRANT_EXPIRED/);
  assert.equal(count(sql),0);
 }finally{close();}
}
{
 const {sql,db,approve,beforeInsert,close}=await fixture();
 try{
  await approve(app1);await approve(app2);
  const hash=sql.prepare("SELECT request_hash FROM allowance_applications WHERE application_id=?").get(app2).request_hash;
  beforeInsert(()=>sql.prepare(`INSERT INTO allowance_number_ledger
   (ledger_id,application_id,workspace_id,seller_invoice,environment,
    allowance_day,sequence,allowance_number,attempt,request_hash,status,
    created_by_device_id,created_at,updated_at)
   VALUES ('al_competitor', ?,?,'12345678','test','20261019',
     1,'A20261019001',1,?,'reserved',?,
     '2026-10-19T08:00:00.000Z','2026-10-19T08:00:00.000Z')`)
   .run(app2,ws,hash,devB));
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
   /ALLOWANCE_RESERVE_READBACK_REQUIRED/,
   "atomic SQL max+1 condition must reject stale independent Device candidate");
  assert.equal(count(sql),1);
  assert.equal(sql.prepare("SELECT application_id FROM allowance_number_ledger").get().application_id,app2);
  assert.equal(sql.prepare("SELECT COUNT(*) AS n FROM allowance_number_ledger WHERE application_id=?").get(app1).n,0);
 }finally{close();}
}
{
 const {sql,db,approve,lostReply,close}=await fixture();
 try{
  await approve(app1);
  lostReply();
  await assert.rejects(()=>reserve(db,target(app1),context,who(),clock),
    /ALLOWANCE_RESERVE_READBACK_REQUIRED/);
  assert.equal(count(sql),1,"remote commit with lost reply still has only one reservation");
  const recovered=await reserve(db,target(app1),context,who(),clock);
  assert.equal(recovered.allowanceNumber,"A20261019001");
  assert.equal(count(sql),1);
 }finally{close();}
}
console.log("CYInvoice C3c1: approved Cloud reserve, daily suffix encoding, Device/revision fences, idempotent readback, lost reply and independent CAS competition passed");
