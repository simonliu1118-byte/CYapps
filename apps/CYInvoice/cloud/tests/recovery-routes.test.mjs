import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

const result = await build({entryPoints:["src/recovery-routes.ts"],
  bundle:true, write:false, platform:"node", target:"node24", format:"esm"});
const { handleRecoveryRoutes } = await import("data:text/javascript;base64,"+
  Buffer.from(result.outputFiles[0].text).toString("base64"));
const ws="business_workspace", identityWs="identity_workspace";
const code="a".repeat(40);
const token="cydev_"+"f".repeat(64);
const issuer="cyid_admin";
const operation="11111111-1111-4111-8111-111111111111";
const data={code,employeeNo:"0001",password:"SyntheticPass12"};
const principal={
  workspaceId:identityWs,employeeId:issuer,employeeNo:"0001",
  workspaceRole:"SUPER_ADMIN",isWorkspaceSuperAdmin:true,emailVerified:true,
  credentialVersion:3,employeeRevision:7,
};
class Statement {
  constructor(db,sql){this.db=db;this.sql=sql;this.args=[]}
  bind(...args){this.args=args;return this}
  async first(){return this.db.prepare(this.sql).get(...this.args)??null}
  async run(){return {meta:{changes:this.db.prepare(this.sql).run(...this.args).changes}}}
}
function makeDb(){
  const db=new DatabaseSync(":memory:");
  const migrationFiles=readdirSync("migrations").filter(x=>/^[0-9]{4}_.*[.]sql$/.test(x)).sort();
  for(const name of migrationFiles.filter(x=>x<"0014"))
    db.exec(readFileSync("migrations/"+name,"utf8"));
  db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES(?,'Original Business')").run(ws);
  db.prepare(`INSERT INTO devices(device_id,workspace_id,display_name,token_hash,status,employee_authority_state)
    VALUES('old_device',?,'Old PC','original_hash','active','cloud')`).run(ws);
  db.prepare(`INSERT INTO cloud_employees(employee_id,workspace_id,employee_no,name,email_normalized,role,enabled)
    VALUES('legacy_admin',?,'0001','Legacy','legacy@example.test','SUPER_ADMIN',1)`).run(ws);
  function apply(sql){
    db.exec("BEGIN");
    try{db.exec(sql);db.exec("COMMIT")}
    catch(error){db.exec("ROLLBACK");throw error}
  }
  apply(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
  apply(readFileSync("migrations/0014_invitation_recovery_metadata.sql","utf8"));
  apply(readFileSync("maintenance/enable-cyweb-recovery-issuer.sql","utf8"));
  db.prepare(`INSERT INTO device_invitations(
      invitation_id,workspace_id,code_hash,issued_by_device_id,issued_by_cyid_employee_id,
      delivery_state,expires_at,created_at,issuer_channel,purpose,
      issuer_credential_version,issuer_employee_revision,recovery_operation_id)
    VALUES('inv_22222222-2222-4222-8222-222222222222',?,?,NULL,?,
      'sent','2030-01-01','2026-10-14','CYWEB','RECOVERY',3,7,?)`)
    .run(ws,createHash("sha256").update(code).digest("hex"),issuer,operation);
  return db;
}
function runtime(db){
  let current=principal;
  const env={
    CYID_ENABLED:"true",
    IDENTITY_APPLICATION_ID:"CYINVOICE",
    IDENTITY_WORKSPACE_ID:identityWs,
    IDENTITY_CYINVOICE_WORKSPACE_ID:ws,
    // App-binding exists in production; actual CYID login/Resolve lifecycle
    // is separately covered by recovery-authority.test.mjs.
    IDENTITY:{ async fetch(){throw new Error("Synthetic test must not call provider fetch")} },
    DB:{
      prepare(sql){return new Statement(db,sql)},
      async batch(statements){
        db.exec("BEGIN");
        try{
          const out=[];
          for(const s of statements)out.push(await s.run());
          db.exec("COMMIT");return out;
        }catch(error){db.exec("ROLLBACK");throw error}
      },
    },
    cyIdContext:{
      async revalidate(){return current},
      async verifiedEmail(){return "admin@example.test"},
    },
  };
  return {env,setPrincipal(value){current=value}};
}
const db=makeDb();
const {env,setPrincipal}=runtime(db);
async function send(path,body=data,headers={}){
  const request=new Request("https://cloud.example.test"+path,{
    method:"POST",headers:{"content-type":"application/json",...headers},
    body:JSON.stringify(body),
  });
  const response=await handleRecoveryRoutes(request,env);
  assert.ok(response);return {status:response.status,body:await response.json()};
}
const url="/v1/device-recovery/preview";
const preview=await send(url);
assert.equal(preview.status,200);
assert.equal(preview.body.workspace.workspaceId,ws);
assert.equal(preview.body.workspace.displayName,"Original Business");
assert.equal(preview.body.identity.identityWorkspaceId,identityWs);
assert.equal(preview.body.identity.applicationId,"CYINVOICE");
assert.equal(preview.body.invitation.invitationId,"inv_22222222-2222-4222-8222-222222222222");
const wrongCode=await send(url,{...data,code:"c".repeat(40)});
assert.equal(wrongCode.status,409);
setPrincipal({...principal,workspaceRole:"USER",isWorkspaceSuperAdmin:false});
await assert.rejects(()=>send(url),/SUPER_ADMIN_REQUIRED/);
setPrincipal(principal);
const extra=await send(url,{...data,workspaceId:"other"});
assert.equal(extra.status,400);
const invalid=await send(url,{...data,password:"bad"});
assert.equal(invalid.status,400);
const wrongMode=await handleRecoveryRoutes(new Request("https://cloud.example.test"+url,{
  method:"POST",headers:{"content-type":"application/json"},body:JSON.stringify(data),
}),{...env,CYID_ENABLED:"false"});
assert.equal(wrongMode.status,409);
const claimData={...data,deviceToken:token,deviceDisplayName:"Recovered PC",clientVersion:"3.0.0"};
const first=await send("/v1/device-recovery/claim",claimData);
assert.equal(first.status,201);
assert.equal(first.body.recovered,false);
assert.equal(first.body.workspace.workspaceId,ws);
assert.equal(first.body.device.displayName,"Recovered PC");
assert.equal(db.prepare("SELECT status FROM devices WHERE device_id='old_device'").get().status,"revoked");
const previous=await send("/v1/device-recovery/claim",claimData);
assert.equal(previous.status,200);
assert.equal(previous.body.recovered,true);
assert.equal(previous.body.device.deviceId,first.body.device.deviceId);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM devices WHERE status='active'").get().n,1);
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM security_audit_events WHERE event_type='recovery_device_joined'").get().n,1);
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
db.close();
console.log("CYInvoice public recovery preview/claim: fixed workspace, app-scoped CYID SUPER_ADMIN, no old bearer, one new Device and old fleet revoked");
