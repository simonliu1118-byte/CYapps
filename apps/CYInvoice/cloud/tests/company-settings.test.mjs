import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {build} from "esbuild";
import {createHash} from "node:crypto";
// Synthetic D1 schema and cryptographic tests. No external Cloudflare or
// AMEGO actions, real company identifiers or credentials.
const output=await build({entryPoints:["src/company-settings.ts"],bundle:true,write:false,
  platform:"node",format:"esm",target:"node24"});
const {changeCompanySettings:change,getCompanySettingsStatus:getStatus,
  synchronizeCompanySettingsToDevice:syncToDevice,
  amegoCredentialsForEnvironment:credentialsForEnvironment, amegoTestAccount,
  loadAmegoCompanySettingsForGateway:loadGateway}=await import("data:text/javascript;base64,"+
    Buffer.from(output.outputFiles[0].text).toString("base64"));
const ws="ws_synthetic";
const other="ws_other";
const dev="dev_11111111-1111-4111-8111-111111111111";
const key="aa".repeat(32), wrongKey="bb".repeat(32);
function fixture(){
  const sql=new DatabaseSync(":memory:");
  sql.exec("PRAGMA foreign_keys=ON; "+
    "CREATE TABLE workspaces(workspace_id TEXT PRIMARY KEY,status TEXT NOT NULL DEFAULT 'active'); "+
    "CREATE TABLE devices(device_id TEXT PRIMARY KEY,workspace_id TEXT NOT NULL REFERENCES workspaces(workspace_id),"+
    "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud',token_hash TEXT);");
  sql.prepare("INSERT INTO workspaces(workspace_id) VALUES(?),(?)").run(ws,other);
  sql.prepare("INSERT INTO devices(device_id,workspace_id) VALUES(?,?)").run(dev,ws);
  sql.exec(readFileSync("migrations/0015_company_settings.sql","utf8"));
  sql.exec(readFileSync("migrations/0022_allowance_unsent_withdrawal.sql","utf8").split("-- Single formal company card;")[1].replace(/^ label only[^\n]*\n/,""));
  class Statement {
    constructor(q){this.q=q;this.args=[];}
    bind(...args){this.args=args;return this;}
    async first(){return sql.prepare(this.q).get(...this.args)??null;}
    async run(){return {meta:{changes:sql.prepare(this.q).run(...this.args).changes}};}
  }
  return {sql,db:{
    prepare:q=>new Statement(q),
    async batch(items){
      sql.exec("BEGIN IMMEDIATE");
      try {const r=items.map(s=>({meta:{changes:sql.prepare(s.q).run(...s.args).changes}}));
        sql.exec("COMMIT");return r;}
      catch(e){sql.exec("ROLLBACK");throw e;}
    }}};
}
const f=fixture();
let principal={workspaceId:ws,employeeId:"cyid_super",workspaceRole:"SUPER_ADMIN",
  isWorkspaceSuperAdmin:true,emailVerified:true};
const authority={revalidate:async()=>principal};
const input={workspaceId:ws,deviceId:dev,expectedRevision:0,sellerInvoice:"12345678",
  amegoAppKey:{action:"replace",value:"SYNTHETIC_APP_KEY_DO_NOT_USE"},
  moExcelPassword:{action:"replace",value:"SYNTHETIC_MO_PASSWORD"}};
// AMEGO App Key is trimmed by the existing Windows SettingsStore. A cloud
// value with spaces cannot safely receive an "applied revision" stamp.
await assert.rejects(()=>change(f.db,{
  ...input,amegoAppKey:{action:"replace",value:" PADDED_TEST_KEY "}
},authority,key),/COMPANY_SETTINGS_INVALID/);
assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM company_settings").get().n,0,
  "Rejected App Key must not create a card or audit event");
assert.equal((await change(f.db,input,authority,key)).revision,1);
const provisioned=(await syncToDevice(f.db,ws,dev,0,key,async()=>{})).credentials;
assert.equal(provisioned.amegoAppKey,input.amegoAppKey.value);
assert.equal((await syncToDevice(f.db,ws,dev,1,wrongKey,async()=>{})).credentials,undefined,
  "same version returns no secrets and does not decrypt/download credentials");
await assert.rejects(()=>syncToDevice(f.db,ws,dev,0,key,async()=>{throw new Error("ACCESS_DENIED");}),/ACCESS_DENIED/);
// Revoke a Device during the decryption/authentication window; a previously
// authenticated caller must not receive already decrypted company secrets.
let revokedDuringProvisionCalls=0;
const revokeDuringProvision=async()=>{
  if(++revokedDuringProvisionCalls===2)
    f.sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(dev);
};
await assert.rejects(()=>syncToDevice(f.db,ws,dev,0,key,revokeDuringProvision),/COMPANY_DEVICE_REVOKED/);
f.sql.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(dev);
const row=f.sql.prepare("SELECT * FROM company_settings").get();
assert.equal(row.seller_invoice,"12345678");
assert.equal(row.amego_app_key_ciphertext.includes("SYNTHETIC"),false);
assert.equal(row.mo_excel_password_ciphertext.includes("SYNTHETIC"),false);
assert.equal(row.amego_app_key_ciphertext.startsWith("v1:"),true);
const amegoOnly=await loadGateway(f.db,ws,key);
assert.equal(amegoOnly.amegoAppKey,input.amegoAppKey.value);
assert.equal(Object.hasOwn(amegoOnly,"moExcelPassword"),false,
  "AMEGO gateway must not receive unrelated MO password");
await assert.rejects(()=>loadGateway(f.db,ws,wrongKey),/CIPHERTEXT_AUTH_FAILED/);
// Copying ciphertext between secret fields is not sufficient to decrypt:
// AAD includes its slot and Workspace, preventing cross-slot substitution.
f.sql.prepare("UPDATE company_settings SET amego_app_key_ciphertext=? WHERE workspace_id=?")
  .run(row.mo_excel_password_ciphertext,ws);
await assert.rejects(()=>loadGateway(f.db,ws,key),/CIPHERTEXT_AUTH_FAILED/);
f.sql.prepare("UPDATE company_settings SET amego_app_key_ciphertext=? WHERE workspace_id=?")
  .run(row.amego_app_key_ciphertext,ws);
await assert.rejects(()=>loadGateway(f.db,other,key),/AMEGO_NOT_CONFIGURED/);
const publicStatus=await getStatus(f.db,ws,authority);
assert.deepEqual(publicStatus,
  {sellerInvoice:"12345678",companyName:"",revision:1,hasAmegoAppKey:true,hasMoExcelPassword:true});
assert.equal(JSON.stringify(publicStatus).includes("v1:"),false,
  "Admin read-back must not reveal encrypted envelopes or plaintext");
const changed=await change(f.db,{...input,expectedRevision:1,
  amegoAppKey:{action:"keep"},moExcelPassword:{action:"clear"}},authority,key);
assert.equal(changed.revision,2);
assert.equal(changed.hasMoExcelPassword,false);
const retained=f.sql.prepare("SELECT * FROM company_settings").get();
assert.equal(retained.amego_app_key_ciphertext,row.amego_app_key_ciphertext);
assert.equal(retained.mo_excel_password_ciphertext,null);
assert.equal(f.sql.prepare("SELECT COUNT(*) AS n FROM company_settings_audit_events").get().n,2);
assert.equal(f.sql.prepare("SELECT changed_fields FROM company_settings_audit_events WHERE revision=2").get().changed_fields,
  JSON.stringify(["moExcelPassword"]));
assert.equal(retained.revision,1,"MO-only update preserves the financial revision");
assert.equal(retained.card_revision,2,"MO-only update advances the card synchronization version");
await assert.rejects(()=>change(f.db,{...input,expectedRevision:1},authority,key),/STALE_REVISION/);
// A role revocation between the first and second check must stop the write.
let authCalls=0;
const changingAuthority={async revalidate(){
  authCalls++;
  return authCalls===1 ? principal : {...principal,workspaceRole:"USER"};
}};
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},changingAuthority,key),
  /AUTHORITY_CHANGED/);
assert.equal(f.sql.prepare("SELECT revision FROM company_settings").get().revision,1);
principal={...principal,workspaceRole:"ADMIN",isWorkspaceSuperAdmin:false};
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},authority,key),/SUPER_ADMIN_REQUIRED/);
assert.equal((await getStatus(f.db,ws,authority)).revision,2);
principal={...principal,workspaceRole:"USER"};
await assert.rejects(()=>getStatus(f.db,ws,authority),/STATUS_AUTH_REQUIRED/);
principal={...principal,workspaceRole:"SUPER_ADMIN",isWorkspaceSuperAdmin:true};
f.sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(dev);
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},authority,key),/CONFLICT_OR_DEVICE_REVOKED/);
assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM company_settings_audit_events").get().n,2);
assert.throws(()=>f.sql.prepare("DELETE FROM company_settings_audit_events WHERE revision=1").run(),
  /COMPANY_AUDIT_APPEND_ONLY/);
assert.throws(()=>f.sql.prepare("UPDATE company_settings_audit_events SET actor_employee_id='altered' WHERE revision=1").run(),
  /COMPANY_AUDIT_APPEND_ONLY/);
// An Audit uniqueness failure must roll back the configuration mutation.
f.sql.prepare("UPDATE devices SET status='active' WHERE device_id=?").run(dev);
f.sql.prepare("INSERT INTO company_settings_audit_events(event_id,workspace_id,revision,actor_employee_id,actor_device_id,changed_fields,created_at) VALUES(?,?,?,?,?,?,?)")
  .run("csa_interference",ws,3,"other_actor",dev,"[]","2026-10-15");
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},authority,key),
  /MUTATION_UNKNOWN_READBACK_REQUIRED/);
assert.equal(f.sql.prepare("SELECT revision FROM company_settings").get().revision,1);
assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
f.sql.close();
// HTTP contract is CYID-only and returns no App Key, MO password, envelope, or
// arbitrary client-supplied workspace. Identity Workspace != business Workspace.
const g=fixture();
const rb=await build({entryPoints:["src/company-settings-route.ts"],bundle:true,
  write:false,platform:"node",format:"esm",target:"node24"});
const {handleCompanySettings:route}=await import("data:text/javascript;base64,"+
  Buffer.from(rb.outputFiles[0].text).toString("base64"));
const token="cydev_"+"a".repeat(64);
g.sql.prepare("UPDATE devices SET token_hash=?").run(createHash("sha256").update(token).digest("hex"));
let actor={workspaceId:"identity_synthetic",employeeId:"super_admin",workspaceRole:"SUPER_ADMIN",
  isWorkspaceSuperAdmin:true,emailVerified:true};
const env={DB:g.db,CYID_ENABLED:"true",IDENTITY:{fetch:async()=>Response.json({})},
  IDENTITY_APPLICATION_ID:"CYINVOICE",IDENTITY_WORKSPACE_ID:"identity_synthetic",
  IDENTITY_CYINVOICE_WORKSPACE_ID:ws,COMPANY_SETTINGS_KEY_HEX:key,
  IDENTITY_AUTHORITY:{invalidateCache:async()=>({workspaceId:"identity_synthetic",applicationId:"CYINVOICE",available:true,invalidated:[]})},
  cyIdContext:{revalidate:async()=>actor}};
async function call(action,payload={},envOverride=env,bearer=token){
  const response=await route(new Request("https://cyinvoice.synthetic/v1/company-settings/"+action,{
    method:"POST",headers:{authorization:"Bearer "+bearer,
      "content-type":"application/json"},body:JSON.stringify(payload)
  }),envOverride);
  return {status:response.status,data:await response.json()};
}
const credentials={employeeNo:"1234",password:"SYNTHETIC_PASSWORD"};
assert.equal((await call("get",credentials,{...env,cyIdContext:undefined})).status,401);
assert.equal((await call("update",{...credentials,...input,workspaceId:ws})).status,400,
  "Caller must never supply a D1 business Workspace");
assert.equal((await call("get",credentials)).status,200);
assert.equal((await call("get",credentials)).data.company,null);
assert.equal((await call("sync",{knownRevision:0},{...env,IDENTITY_AUTHORITY:{invalidateCache:async()=>({available:false})}})).status,403,
  "revoked workspace/application access must stop automatic delivery");
assert.equal((await call("sync",{knownRevision:0,extra:"x".repeat(5000)})).status,400,
  "automatic Device sync bounds content before any secret read");
const req={...credentials,expectedRevision:0,sellerInvoice:"12345678",
  amegoAppKey:{action:"replace",value:"SYNTHETIC_ONLY_KEY"},
  moExcelPassword:{action:"replace",value:"SYNTHETIC_ONLY_MO"}};
const padded=await call("update",{...req,amegoAppKey:{
  action:"replace",value:" SYNTHETIC_ONLY_KEY "
}});
assert.equal(padded.status,400,"HTTP must reject App Keys that Windows would trim");
assert.equal(g.sql.prepare("SELECT COUNT(*) n FROM company_settings").get().n,0);
const created=await call("update",req);
assert.equal(created.status,200,JSON.stringify(created));
assert.deepEqual(created.data.company,{sellerInvoice:"12345678",companyName:"",revision:1,
  hasAmegoAppKey:true,hasMoExcelPassword:true});
const read=await call("get",credentials);
assert.equal(read.status,200);
const provision=await call("sync",{knownRevision:0});
assert.equal(provision.status,200);
assert.deepEqual(provision.data.credentials,{
  sellerInvoice:"12345678",companyName:"",revision:1,amegoAppKey:"SYNTHETIC_ONLY_KEY",
  moExcelPassword:"SYNTHETIC_ONLY_MO"});
assert.equal(provision.data.credentials.amegoAppKey.includes("v1:"),false);
assert.equal((await call("sync",{knownRevision:0},{...env,
  COMPANY_SETTINGS_KEY_HEX:"bb".repeat(32)})).status,503,
  "Wrong Worker KEK must never return partially decrypted credentials");
for (const result of [created,read]){
  const serialized=JSON.stringify(result);
  for(const secret of ["SYNTHETIC_ONLY_KEY","SYNTHETIC_ONLY_MO","v1:"])
    assert.equal(serialized.includes(secret),false);
}
actor={...actor,workspaceRole:"ADMIN",isWorkspaceSuperAdmin:false};
assert.equal((await call("get",credentials)).status,200);
assert.equal((await call("sync",{knownRevision:0})).status,200,
  "Automatic delivery uses the existing authorized Device, not a retained manager Session");
assert.equal((await call("update",{...req,expectedRevision:1})).status,403);
actor={...actor,workspaceRole:"USER"};
assert.equal((await call("get",credentials)).status,403);
actor={...actor,workspaceRole:"SUPER_ADMIN",isWorkspaceSuperAdmin:true};
assert.equal((await call("update",{...req,expectedRevision:0})).status,409);
assert.equal((await call("update",{...req,expectedRevision:1,
  amegoAppKey:{action:"keep"},moExcelPassword:{action:"clear"}})).status,200);
assert.equal((await call("sync",{knownRevision:0})).data.credentials,undefined,
  "Cannot install a half-configured company card into Windows");
assert.equal(g.sql.prepare("SELECT revision FROM company_settings").get().revision,1);
assert.equal(g.sql.prepare("SELECT COUNT(*) n FROM company_settings_audit_events").get().n,2);
actor={...actor,workspaceId:"other_identity_workspace"};
assert.equal((await call("get",credentials)).status,403);
actor={...actor,workspaceId:"identity_synthetic"};
assert.equal((await call("get",credentials,env,"wrong")).status,401);
g.sql.prepare("UPDATE devices SET status='revoked'").run();
assert.equal((await call("get",credentials)).status,401);
assert.equal((await call("sync",{knownRevision:0})).status,401);
assert.deepEqual(g.sql.prepare("PRAGMA foreign_key_check").all(),[]);
g.sql.close();
console.log("Draft company settings: AES-GCM, atomic audit, exact CYID scope, role-separated HTTP transport passed");

// A single Workspace/Worker can use the public test account or its own company.
const syntheticCompany={sellerInvoice:"12345675",amegoAppKey:"SYNTHETIC_PRODUCTION_KEY"};
assert.deepEqual(credentialsForEnvironment(syntheticCompany,"production"),{sellerInvoice:syntheticCompany.sellerInvoice,appKey:syntheticCompany.amegoAppKey});
assert.deepEqual(credentialsForEnvironment(syntheticCompany,"test"),amegoTestAccount);
assert.equal(syntheticCompany.amegoAppKey,"SYNTHETIC_PRODUCTION_KEY");
assert.throws(()=>credentialsForEnvironment(syntheticCompany,"development"),/ENVIRONMENT_INVALID/);
const windowsContracts=readFileSync("../src/CYInvoice.Core/AmegoContracts.cs","utf8");
assert.ok(windowsContracts.includes('"'+amegoTestAccount.sellerInvoice+'"'));
assert.ok(windowsContracts.includes('"'+amegoTestAccount.appKey+'"'));
