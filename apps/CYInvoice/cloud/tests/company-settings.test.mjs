import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync} from "node:fs";
import {build} from "esbuild";
// Synthetic D1 schema and cryptographic tests. No external Cloudflare or
// AMEGO actions, real company identifiers or credentials.
const output=await build({entryPoints:["src/company-settings.ts"],bundle:true,write:false,
  platform:"node",format:"esm",target:"node24"});
const {changeCompanySettings:change,getCompanySettingsStatus:getStatus,
  loadCompanySettingsForGateway:loadGateway}=await import("data:text/javascript;base64,"+
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
    "status TEXT NOT NULL DEFAULT 'active',employee_authority_state TEXT NOT NULL DEFAULT 'cloud');");
  sql.prepare("INSERT INTO workspaces(workspace_id) VALUES(?),(?)").run(ws,other);
  sql.prepare("INSERT INTO devices(device_id,workspace_id) VALUES(?,?)").run(dev,ws);
  sql.exec(readFileSync("drafts/0017_company_settings.sql","utf8"));
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
assert.equal((await change(f.db,input,authority,key)).revision,1);
const row=f.sql.prepare("SELECT * FROM company_settings").get();
assert.equal(row.seller_invoice,"12345678");
assert.equal(row.amego_app_key_ciphertext.includes("SYNTHETIC"),false);
assert.equal(row.mo_excel_password_ciphertext.includes("SYNTHETIC"),false);
assert.equal(row.amego_app_key_ciphertext.startsWith("v1:"),true);
assert.equal((await loadGateway(f.db,ws,key)).amegoAppKey,input.amegoAppKey.value);
await assert.rejects(()=>loadGateway(f.db,ws,wrongKey),/CIPHERTEXT_AUTH_FAILED/);
await assert.rejects(()=>loadGateway(f.db,other,key),/AMEGO_NOT_CONFIGURED/);
assert.deepEqual(await getStatus(f.db,ws,authority),
  {sellerInvoice:"12345678",revision:1,hasAmegoAppKey:true,hasMoExcelPassword:true});
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
await assert.rejects(()=>change(f.db,{...input,expectedRevision:1},authority,key),/STALE_REVISION/);
principal={...principal,workspaceRole:"ADMIN",isWorkspaceSuperAdmin:false};
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},authority,key),/SUPER_ADMIN_REQUIRED/);
assert.equal((await getStatus(f.db,ws,authority)).revision,2);
principal={...principal,workspaceRole:"USER"};
await assert.rejects(()=>getStatus(f.db,ws,authority),/STATUS_AUTH_REQUIRED/);
principal={...principal,workspaceRole:"SUPER_ADMIN",isWorkspaceSuperAdmin:true};
f.sql.prepare("UPDATE devices SET status='revoked' WHERE device_id=?").run(dev);
await assert.rejects(()=>change(f.db,{...input,expectedRevision:2},authority,key),/CONFLICT_OR_DEVICE_REVOKED/);
assert.equal(f.sql.prepare("SELECT COUNT(*) n FROM company_settings_audit_events").get().n,2);
assert.deepEqual(f.sql.prepare("PRAGMA foreign_key_check").all(),[]);
f.sql.close();
console.log("Draft company settings: encrypted, revision CAS, actor/device/role guard, audit without secrets passed");
