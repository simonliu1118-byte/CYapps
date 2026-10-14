import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

const bundle = await build({
  entryPoints: ["src/recovery-issuer.ts"],
  bundle: true, write: false, format: "esm", platform: "node", target: "node24",
});
const { issueCyWebRecoveryInvitation, recoveryInvitationStatus, revokeRecoveryInvitation } = await import(
  "data:text/javascript;base64," + Buffer.from(bundle.outputFiles[0].text).toString("base64"),
);

const workspace = "business_workspace";
const identityWorkspace = "identity_workspace";
const operationId = "11111111-1111-4111-8111-111111111111";
const token = "cyid_" + "a".repeat(64);
const principal = {
  workspaceId: identityWorkspace, employeeId: "external_cyid_admin", employeeNo: "0001",
  displayName: "Synthetic administrator", workspaceRole: "SUPER_ADMIN",
  isIdentityAdmin: false, isWorkspaceSuperAdmin: true, emailVerified: true,
  credentialVersion: 4, employeeRevision: 7,
};
const input = {
  operationId, employeeNo: "0001", password: "syntheticpass",
  expectedEmployeeId: principal.employeeId,
  expectedIdentityWorkspaceId: identityWorkspace,
  expectedCredentialVersion: 4, expectedEmployeeRevision: 7,
};
function transaction(db, sql) {
  db.exec("BEGIN");
  try { db.exec(sql); db.exec("COMMIT"); }
  catch (error) { db.exec("ROLLBACK"); throw error; }
}
function fixture() {
  const db = new DatabaseSync(":memory:");
  const migrations = readdirSync("migrations")
    .filter(name => /^[0-9]{4}_.*[.]sql$/.test(name)).sort();
  for (const migration of migrations.filter(name => name < "0014"))
    db.exec(readFileSync("migrations/" + migration, "utf8"));
  db.prepare("INSERT INTO workspaces (workspace_id, display_name) VALUES (?,?)")
    .run(workspace, "Synthetic workspace");
  db.prepare(`INSERT INTO devices
    (device_id,workspace_id,display_name,token_hash,status,employee_authority_state)
    VALUES ('device_old',?,'Old device','old-hash','active','cloud')`).run(workspace);
  db.prepare(`INSERT INTO cloud_employees
    (employee_id,workspace_id,employee_no,name,email_normalized,role,enabled)
    VALUES ('legacy_owner',?,'0001','Legacy Owner','old@example.test','SUPER_ADMIN',1)`)
    .run(workspace);
  transaction(db, readFileSync("maintenance/retire-built-in-for-cyid-only.sql", "utf8"));
  transaction(db, readFileSync("migrations/0014_invitation_recovery_metadata.sql", "utf8"));
  transaction(db, readFileSync("maintenance/enable-cyweb-recovery-issuer.sql", "utf8"));
  return db;
}
class Statement {
  constructor(db, sql) { this.db=db; this.sql=sql; this.params=[]; }
  bind(...params) { this.params=params; return this; }
  async first() { return this.db.prepare(this.sql).get(...this.params) ?? null; }
  async run() { return { meta: { changes: this.db.prepare(this.sql).run(...this.params).changes } }; }
}
function mockD1(db) {
  return {
    prepare(sql) { return new Statement(db, sql); },
    async batch(statements) {
      db.exec("BEGIN");
      try {
        const results = [];
        for (const statement of statements) results.push(await statement.run());
        db.exec("COMMIT");
        return results;
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    },
  };
}
function envFor(db) {
  let livePrincipal=principal;
  let logins=0, logouts=0;
  const env = {
    CYID_ENABLED:"true",
    IDENTITY_APPLICATION_ID:"CYINVOICE",
    IDENTITY_WORKSPACE_ID:identityWorkspace,
    IDENTITY_CYINVOICE_WORKSPACE_ID:workspace,
    DB:mockD1(db),
    IDENTITY:{
      async fetch(req) {
        const url=new URL(req.url);
        assert.equal(url.origin, "https://identity.internal");
        assert.equal(req.headers.get("x-identity-application"), "CYINVOICE");
        if(url.pathname==="/v1/identity/login") {
          logins++;
          const data=await req.json();
          assert.equal(data.applicationId, "CYINVOICE");
          assert.equal(data.password,"syntheticpass");
          return Response.json({ok:true,principal:livePrincipal,session:{
            token,expiresAt:new Date(Date.now()+300000).toISOString(),
          }});
        }
        assert.equal(req.headers.get("authorization"), "Bearer "+token);
        if(url.pathname==="/v1/identity/session/resolve")
          return Response.json({ok:true,principal:livePrincipal});
        if(url.pathname==="/v1/admin/identity/snapshot")
          return Response.json({ok:true,workspaceId:identityWorkspace,employees:[{
            employee_id:principal.employeeId,email_verified_at:"2026-10-01",
            email_normalized:"admin@example.test",
          }]});
        if(url.pathname==="/v1/identity/logout") {
          logouts++;
          return Response.json({ok:true,loggedOut:true});
        }
        throw new Error("Unrecognized Identity request");
      },
    },
  };
  return {env,setPrincipal(value){livePrincipal=value;},sessions(){return {logins,logouts}}};
}
function row(db) {
  return db.prepare(`SELECT invitation_id, workspace_id, code_hash,
    issued_by_device_id, issued_by_cyid_employee_id, delivery_state,
    expires_at, revoked_at, consumed_at, issuer_channel, purpose,
    issuer_credential_version, issuer_employee_revision, recovery_operation_id
    FROM device_invitations WHERE recovery_operation_id=?`).get(operationId);
}
function count(db, table) { return db.prepare("SELECT COUNT(*) AS n FROM "+table).get().n; }
function senderHarness(mode="accepted") {
  const sent=[];
  return {sent,sender:{ async send(message) {
    sent.push(message);
    if(mode==="uncertain") throw new Error("network closed after provider request");
    return {provider:"synthetic",providerMessageId:"mock"};
  }}};
}

// Delivery acceptance and DB sent-state + Audit; never store the raw code.
const db=fixture();
const one=envFor(db);
const mailbox=senderHarness();
const success=await issueCyWebRecoveryInvitation(one.env,input,mailbox.sender);
assert.equal(success.status,"sent");
assert.equal(count(db,"device_invitations"),1);
assert.equal(count(db,"security_audit_events"),1);
assert.equal(mailbox.sent.length,1);
assert.equal(mailbox.sent[0].to,"admin@example.test");
const code=/\n([0-9a-f]{40})\n/.exec(mailbox.sent[0].text)?.[1];
assert.ok(code,"only a private email receives the 40-character secret");
assert.equal(row(db).code_hash,createHash("sha256").update(code).digest("hex"));
assert.equal(row(db).issued_by_device_id,null);
assert.equal(row(db).issued_by_cyid_employee_id,principal.employeeId);
assert.equal(row(db).delivery_state,"sent");
assert.equal(row(db).issuer_channel,"CYWEB");
assert.equal(row(db).purpose,"RECOVERY");
assert.equal(row(db).issuer_credential_version,4);
assert.equal(row(db).issuer_employee_revision,7);
assert.equal(row(db).recovery_operation_id,operationId);
assert.equal(JSON.stringify(row(db)).includes(code),false);
assert.equal(JSON.stringify(row(db)).includes("syntheticpass"),false);
const repeat=await issueCyWebRecoveryInvitation(one.env,input,mailbox.sender);
assert.deepEqual(repeat,success,"same operation is read-only after provider success");
assert.equal(mailbox.sent.length,1);
assert.deepEqual(await recoveryInvitationStatus(one.env,input),success);
assert.equal(count(db,"security_audit_events"),1);
await assert.rejects(() => issueCyWebRecoveryInvitation(one.env,{
  ...input,operationId:"22222222-2222-4222-8222-222222222222",
},mailbox.sender),/RECOVERY_ALREADY_PENDING/);
assert.equal(mailbox.sent.length,1,"second outstanding operation must never email");
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
// Explicit current SUPER_ADMIN cancellation frees the single-open-grant slot,
// without revoking any device. It is separate from successful Device recovery.
const cancelled=await revokeRecoveryInvitation(one.env,input);
assert.deepEqual(cancelled,{status:"revoked"});
assert.equal(row(db).revoked_at !== null,true);
assert.equal(count(db,"security_audit_events"),2);
assert.equal(count(db,"devices"),1);
assert.deepEqual(await recoveryInvitationStatus(one.env,input),{status:"revoked"});
assert.deepEqual(await revokeRecoveryInvitation(one.env,input),{status:"revoked"});
assert.equal(count(db,"security_audit_events"),2,"idempotent cancellation no double audit");
const secondOperation={...input,operationId:"22222222-2222-4222-8222-222222222222"};
assert.equal((await issueCyWebRecoveryInvitation(one.env,secondOperation,mailbox.sender)).status,"sent");
assert.equal(mailbox.sent.length,2,"explicit revocation permits a new, distinct operation");
assert.equal(one.sessions().logins,one.sessions().logouts);
db.close();

// SMTP/provider can time out AFTER it accepted a message. A pending code must
// never become claimable, and same operationId never resends.
const uncertainDb=fixture();
const uncertain=envFor(uncertainDb);
const brokenMail=senderHarness("uncertain");
const first=await issueCyWebRecoveryInvitation(uncertain.env,input,brokenMail.sender);
assert.deepEqual(first,{status:"pending"});
assert.equal(row(uncertainDb).delivery_state,"pending");
assert.deepEqual(await issueCyWebRecoveryInvitation(uncertain.env,input,brokenMail.sender),first);
assert.deepEqual(await recoveryInvitationStatus(uncertain.env,input),first);
assert.equal(brokenMail.sent.length,1);
assert.equal(count(uncertainDb,"security_audit_events"),0);
assert.deepEqual(await revokeRecoveryInvitation(uncertain.env,input),{status:"revoked"});
assert.equal(row(uncertainDb).revoked_at !== null,true);
assert.equal(count(uncertainDb,"security_audit_events"),1);
assert.equal(uncertain.sessions().logins,uncertain.sessions().logouts);
uncertainDb.close();

// Consumed and expired are NOT administrative revocation and must never
// unlock CYWEB's operation checkpoint by reporting explicit "revoked".
const consumedDb=fixture();
const consumed=envFor(consumedDb);
assert.equal((await issueCyWebRecoveryInvitation(consumed.env,input,senderHarness().sender)).status,"sent");
consumedDb.prepare("UPDATE device_invitations SET consumed_at=? WHERE recovery_operation_id=?")
  .run(new Date().toISOString(),operationId);
assert.deepEqual(await recoveryInvitationStatus(consumed.env,input),{status:"failed"});
assert.deepEqual(await revokeRecoveryInvitation(consumed.env,input),{status:"failed"});
assert.equal(row(consumedDb).revoked_at,null);
assert.equal(count(consumedDb,"security_audit_events"),1);
consumedDb.close();

const expiredDb=fixture();
const expired=envFor(expiredDb);
assert.equal((await issueCyWebRecoveryInvitation(expired.env,input,senderHarness().sender)).status,"sent");
expiredDb.prepare("UPDATE device_invitations SET expires_at=? WHERE recovery_operation_id=?")
  .run("2026-01-01T00:00:00.000Z",operationId);
assert.deepEqual(await recoveryInvitationStatus(expired.env,input),{status:"failed"});
assert.deepEqual(await revokeRecoveryInvitation(expired.env,input),{status:"failed"});
assert.equal(row(expiredDb).revoked_at,null);
assert.equal(count(expiredDb,"security_audit_events"),1);
expiredDb.close();

// D1 can COMMIT the revoke and then lose the response. Caller must keep
// the original operation ID and use read-only status, not revoke/reissue.
const lostDb=fixture();
const lost=envFor(lostDb);
assert.equal((await issueCyWebRecoveryInvitation(lost.env,input,senderHarness().sender)).status,"sent");
const realLostBatch=lost.env.DB.batch.bind(lost.env.DB);
lost.env.DB.batch=async statements=>{
  await realLostBatch(statements);
  throw new Error("synthetic lost response after D1 COMMIT");
};
await assert.rejects(()=>revokeRecoveryInvitation(lost.env,input),/RECOVERY_RESULT_UNKNOWN/);
assert.deepEqual(await recoveryInvitationStatus(lost.env,input),{status:"revoked"});
assert.deepEqual(await revokeRecoveryInvitation(lost.env,input),{status:"revoked"});
assert.equal(count(lostDb,"security_audit_events"),2);
assert.equal(count(lostDb,"devices"),1);
lostDb.close();

// Claim may consume the invitation between the initial read and revoke's
// transaction. SQL-time guard must roll back revoke/Audit as one batch.
const racedDb=fixture();
const raced=envFor(racedDb);
assert.equal((await issueCyWebRecoveryInvitation(raced.env,input,senderHarness().sender)).status,"sent");
const realRaceBatch=raced.env.DB.batch.bind(raced.env.DB);
raced.env.DB.batch=async statements=>{
  racedDb.prepare("UPDATE device_invitations SET consumed_at=? WHERE recovery_operation_id=?")
    .run(new Date().toISOString(),operationId);
  return realRaceBatch(statements);
};
await assert.rejects(()=>revokeRecoveryInvitation(raced.env,input),/RECOVERY_RESULT_UNKNOWN/);
assert.deepEqual(await recoveryInvitationStatus(raced.env,input),{status:"failed"});
assert.equal(row(racedDb).revoked_at,null);
assert.equal(count(racedDb,"security_audit_events"),1);
racedDb.close();

// Identity credential/role changes after email request means fail closed.
// A delivered code cannot be accepted unless its D1 state becomes "sent".
const changedDb=fixture();
const changed=envFor(changedDb);
let actualMessages=0;
const changedSender={async send() {
  actualMessages++;
  changed.setPrincipal({...principal,employeeRevision:8});
  return {provider:"synthetic",providerMessageId:"mock"};
}};
await assert.rejects(() => issueCyWebRecoveryInvitation(changed.env,input,changedSender),
  /RECOVERY_AUTHORITY_CHANGED/);
assert.equal(actualMessages,1);
assert.equal(row(changedDb).delivery_state,"pending");
assert.equal(count(changedDb,"security_audit_events"),0);
changedDb.close();

// A failed sent-state UPDATE cannot leave a success audit or make the code
// claimable. The provider may already have accepted the email, so no replay.
const failedDb=fixture();
const failed=envFor(failedDb);
failedDb.exec(`CREATE TRIGGER deny_recovery_delivery BEFORE UPDATE OF delivery_state
  ON device_invitations WHEN NEW.delivery_state='sent'
  BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;`);
const failedMail=senderHarness();
assert.deepEqual(await issueCyWebRecoveryInvitation(failed.env,input,failedMail.sender),
  {status:"pending"});
assert.equal(failedMail.sent.length,1);
assert.equal(row(failedDb).delivery_state,"pending");
assert.equal(count(failedDb,"security_audit_events"),0);
failedDb.close();

// Missing recovery profile or non-CYID mode never enters mail path.
const profileDb=fixture();
profileDb.exec("DELETE FROM cyweb_recovery_storage_profile");
const absent=envFor(profileDb);
const noSend=senderHarness();
await assert.rejects(() => issueCyWebRecoveryInvitation(absent.env,input,noSend.sender),
  /RECOVERY_STORAGE_NOT_READY/);
assert.equal(noSend.sent.length,0);
assert.equal(count(profileDb,"device_invitations"),0);
await assert.rejects(() => issueCyWebRecoveryInvitation({
  ...absent.env,CYID_ENABLED:"false"},input,noSend.sender),/IDENTITY_PROVIDER_MISMATCH/);
profileDb.close();

console.log("CYInvoice private recovery issuer: scoped CYID login, first-write-only email, hashed code, single open grant, sent-state atomic Audit, explicit revoked readback, consumed/expired isolation, claim/revoke race and lost-response fail closed");
