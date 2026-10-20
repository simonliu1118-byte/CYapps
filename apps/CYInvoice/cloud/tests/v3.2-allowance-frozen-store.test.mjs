import assert from "node:assert/strict";
import {readFileSync,readdirSync} from "node:fs";
import {DatabaseSync} from "node:sqlite";
import {build} from "esbuild";
import {createHash} from "node:crypto";
// C3a standalone internal Worker service. No Cloudflare, no real CYID/AMEGO,
// no public HTTP route and no canonical schema migration.
const compiled=await build({entryPoints:["src/allowance-frozen-store.ts"],
 bundle:true,write:false,platform:"node",format:"esm",target:"node24"});
const {createEncryptedCloudAllowance:create,loadEncryptedCloudAllowance:load}=
 await import("data:text/javascript;base64,"+
  Buffer.from(compiled.outputFiles[0].text).toString("base64"));
const ws="ws_c3a",other="ws_other";
const devA="dev_11111111-1111-4111-8111-111111111111";
const devB="dev_22222222-2222-4222-8222-222222222222";
const key="aa".repeat(32),wrong="bb".repeat(32);
const context={encryptionKeyHex:key,environment:"test"};
const original="AB12345678";
const template=JSON.stringify({AllowanceType:2,AllowanceNumber:"",
  AllowanceDate:"20261019",BuyerIdentifier:"0000000000",
  BuyerName:"SYNTHETIC_NAME_NOT_REAL",
  ProductItem:[{OriginalInvoiceNumber:original,OriginalInvoiceDate:"20261019",
   OriginalDescription:"Synthetic refund row",Quantity:"1",UnitPrice:"50",
   Amount:"50",Tax:"0",TaxType:1}]});
const input={workspaceId:ws,applicationId:"application_c3a_one",
  deviceId:devA,environment:"test",originalInvoiceNumber:original,
  allowanceDay:"20261019",requestedGross:50,reason:"Synthetic classified reason",
  unnumberedTemplateJson:template};
function fixture(){
 const sql=new DatabaseSync(":memory:");
 const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
 assert.equal(migrations.at(-1),"0016_non_allowance_work_items.sql");
 for(const name of migrations.filter(x=>x<"0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 sql.prepare(
  "INSERT INTO workspaces(workspace_id,display_name) VALUES (?,'SYNTHETIC')").run(ws);
 for(const device of [devA,devB])sql.prepare(`INSERT INTO devices
 (device_id,workspace_id,display_name,status,employee_authority_state)
 VALUES (?,?,'SYNTHETIC','active','cloud')`).run(device,ws);
 sql.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
 for(const name of migrations.filter(x=>x>="0014"))
  sql.exec(readFileSync("migrations/"+name,"utf8"));
 sql.prepare(`INSERT INTO company_settings
 (workspace_id,seller_invoice,amego_app_key_ciphertext,revision,
 changed_by_employee_id,changed_by_device_id,changed_at)
 VALUES (?,'12345678','synthetic_no_credential',1,'admin',?,'now')`).run(ws,devA);
 sql.exec(readFileSync("drafts/0017_allowance_applications.sql","utf8"));
 sql.exec(readFileSync("drafts/0018_allowance_frozen_ledger.sql","utf8"));
 class Statement{
  constructor(q){this.q=q;this.args=[];}
  bind(...args){this.args=args;return this;}
  async first(){return sql.prepare(this.q).get(...this.args)??null;}
  async run(){return {meta:{changes:sql.prepare(this.q).run(...this.args).changes}};}
 }
 let beforeBatch=null;
 const db={prepare:q=>new Statement(q),
  async batch(statements){
   if(beforeBatch){const f=beforeBatch;beforeBatch=null;f();}
   sql.exec("BEGIN IMMEDIATE");
   try{
    const results=statements.map(s=>({
      meta:{changes:sql.prepare(s.q).run(...s.args).changes}}));
    sql.exec("COMMIT");return results;
   }catch(e){sql.exec("ROLLBACK");throw e;}
  }};
 return {sql,db,setBatchRace:f=>{beforeBatch=f;},close:()=>sql.close()};
}
const owner={workspaceId:ws,employeeId:"employee",workspaceRole:"USER"};
const manager={workspaceId:ws,employeeId:"manager",workspaceRole:"ADMIN"};
const ctx={...context};
const auth=(actor=owner)=>({revalidate:async()=>actor});
{
 const {sql,db,close}=fixture();
 try{
  const made=await create(db,input,ctx,auth());
  assert.deepEqual(made,{applicationId:input.applicationId,status:"open",revision:1});
  const stored=sql.prepare(`SELECT a.reference_hash,a.request_hash,
   f.original_invoice_fingerprint,f.template_ciphertext FROM allowance_applications a
   JOIN allowance_frozen_requests f ON f.application_id=a.application_id`).get();
  assert.match(stored.reference_hash,/^[a-f0-9]{64}$/);
  assert.match(stored.request_hash,/^[a-f0-9]{64}$/);
  assert.match(stored.original_invoice_fingerprint,/^[a-f0-9]{64}$/);
  assert.notEqual(stored.original_invoice_fingerprint,
   createHash("sha256").update(original).digest("hex"),"fingerprint must be keyed");
  assert.equal(stored.template_ciphertext.includes(original),false);
  assert.equal(stored.template_ciphertext.includes("SYNTHETIC_NAME"),false);
  assert.equal(stored.template_ciphertext.includes("Synthetic classified reason"),false);
  assert.match(stored.template_ciphertext,/^v1:/);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM work_items").get().n,0);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_application_audit_events").get().n,1);
  const ownerLoaded=await load(db,
   {workspaceId:ws,applicationId:input.applicationId,deviceId:devA},ctx,auth());
  assert.equal(ownerLoaded.unnumberedTemplateJson,template);
  assert.equal(ownerLoaded.reason,"Synthetic classified reason");
  assert.equal(ownerLoaded.requestedGross,50);
  assert.equal(ownerLoaded.sellerInvoice,"12345678");
  assert.equal(ownerLoaded.revision,1);
  const managerLoaded=await load(db,
   {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},ctx,auth(manager));
  assert.equal(managerLoaded.unnumberedTemplateJson,template);
  await assert.rejects(()=>load(db,
   {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},ctx,
   auth({...owner,employeeId:"stranger"})),/ALLOWANCE_READ_AUTH_REQUIRED/);
  await assert.rejects(()=>load(db,
   {workspaceId:other,applicationId:input.applicationId,deviceId:devA},ctx,
   auth({...manager,workspaceId:other})),/ALLOWANCE_READ_AUTH_REQUIRED/);
  await assert.rejects(()=>load(db,
   {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},
   {...ctx,environment:"production"},auth(manager)),/ALLOWANCE_FROZEN_UNAVAILABLE/);
  await assert.rejects(()=>load(db,
   {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},
   {...ctx,encryptionKeyHex:wrong},auth(manager)),/CIPHERTEXT_AUTH_FAILED/);
  await assert.rejects(()=>create(db,input,ctx,auth()),/UNIQUE constraint failed/,
   "lost reply must read by original ID, not create again");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,1);
  const second={...input,applicationId:"application_c3a_two"};
  await create(db,second,ctx,auth());
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,2);
  const related=sql.prepare(`SELECT a.reference_hash,f.original_invoice_fingerprint,
   a.request_hash FROM allowance_applications a JOIN allowance_frozen_requests f
   ON f.application_id=a.application_id ORDER BY a.application_id`).all();
  assert.equal(related[0].reference_hash,related[1].reference_hash,
   "separate legitimate partial allowances share only the keyed invoice reference");
  assert.equal(related[0].original_invoice_fingerprint,related[1].original_invoice_fingerprint);
  assert.notEqual(related[0].request_hash,related[1].request_hash,
   "independent operations have distinct frozen request identities");
  assert.throws(()=>sql.prepare("UPDATE allowance_frozen_requests SET template_ciphertext='tampered'").run(),
   /ALLOWANCE_FROZEN_IMMUTABLE/);
  assert.deepEqual(sql.prepare("PRAGMA foreign_key_check").all(),[]);
 }finally{close();}
}
{
 const {sql,db,close}=fixture();
 try{
  const wrongInvoice=JSON.stringify({...JSON.parse(template),ProductItem:[
    {...JSON.parse(template).ProductItem[0],OriginalInvoiceNumber:"ZZ99999999"}]});
  await assert.rejects(()=>create(db,{...input,unnumberedTemplateJson:wrongInvoice},
    ctx,auth()),/ALLOWANCE_TEMPLATE_SOURCE_MISMATCH/);
  await assert.rejects(()=>create(db,{...input,environment:"production"},ctx,auth()),
    /ALLOWANCE_REQUEST_INVALID/);
  await assert.rejects(()=>create(db,{...input,allowanceDay:"20260230"},ctx,auth()),
    /ALLOWANCE_REQUEST_INVALID/);
  let checks=0;
  await assert.rejects(()=>create(db,input,ctx,{
    revalidate:async()=>++checks===1?owner:{...owner,employeeId:"different"}
  }),/ALLOWANCE_ACTOR_CHANGED/);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,0);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_frozen_requests").get().n,0);
 }finally{close();}
}
{
 const {sql,db,setBatchRace,close}=fixture();
 try{
  setBatchRace(()=>sql.prepare("UPDATE company_settings SET revision=revision+1").run());
  await assert.rejects(()=>create(db,input,ctx,auth()),
    /ALLOWANCE_FROZEN_SOURCE_INVALID/);
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_applications").get().n,0,
   "batch rolls back partially inserted application if company rotates");
  assert.equal(sql.prepare("SELECT COUNT(*) n FROM allowance_frozen_requests").get().n,0);
 }finally{close();}
}
{
 const {sql,db,close}=fixture();
 try{
  await create(db,input,ctx,auth());
  let reads=0;
  const revoke={async revalidate(){
    if(++reads===2)sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(devB);
    return manager;
  }};
  await assert.rejects(()=>load(db,
    {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},ctx,revoke),
    /ALLOWANCE_READ_AUTH_CHANGED/);
  sql.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(devB);
  sql.prepare("UPDATE company_settings SET revision=revision+1").run();
  await assert.rejects(()=>load(db,
    {workspaceId:ws,applicationId:input.applicationId,deviceId:devB},ctx,
    auth(manager)),/ALLOWANCE_FROZEN_UNAVAILABLE/);
 }finally{close();}
}
console.log("CYInvoice C3a: encrypted frozen create/read, scoped HMAC, SQL atomic rollback and CYID/Device fences passed");
