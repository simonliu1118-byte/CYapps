import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { worker, fixture, token, testing } from "./authority-fixture.mjs";

const target = "employee-target";
function setup(options = {}) {
  const f = fixture(options);
  Object.assign(f.env, { ACCOUNT_PORTAL_URL: "https://portal.example.test/", APP_ENV: "production", EMAIL_DAILY_BUDGET: "50" });
  f.db.exec(`INSERT INTO employees(employee_id,workspace_id,employee_no,name,email_normalized,role_key,activated_at)
    VALUES ('employee-alternate','workspace-one','0003','Synthetic Alternate','alternate@example.test','ADMIN','2026-01-01');
    INSERT INTO employees(employee_id,workspace_id,employee_no,name,email_normalized,email_verified_at,role_key,activated_at)
    VALUES ('${target}','workspace-one','0002','Synthetic Target','target@example.test','2026-01-01','USER','2026-01-01');
    INSERT INTO employee_credentials(employee_id,algorithm,verifier) VALUES ('${target}','pbkdf2-sha256','${"x".repeat(96)}');
    INSERT INTO employee_application_access(workspace_id,employee_id,application_id) VALUES ('workspace-one','${target}','OTHER_APP');
    INSERT INTO identity_sessions(session_hash,workspace_id,employee_id,application_id,credential_version,employee_revision,created_at,expires_at)
      VALUES ('${"d".repeat(64)}','workspace-one','${target}','CORE_TEST',1,1,'2026-01-01','2999-01-01');
    INSERT INTO employee_initial_credentials(employee_id,algorithm,verifier,issued_at,expires_at,exchange_token_digest,exchange_expires_at)
      VALUES ('${target}','pbkdf2-sha256','synthetic','2026-01-01','2999-01-01','${"e".repeat(64)}','2999-01-01');
    INSERT INTO email_otp_challenges(challenge_id,purpose,scope_key,email_normalized,otp_digest,expires_at,resend_after)
      VALUES ('challenge-target','employee_password_reset','workspace-one:${target}','target@example.test','${"a".repeat(64)}','2999-01-01','2026-01-01');
    INSERT INTO identity_audit_events(event_id,workspace_id,target_employee_id,event_type) VALUES ('audit-existing','workspace-one','${target}','employee_password_changed');`);
  return f;
}
function req(path, method = "DELETE", body, application = "CORE_TEST") {
  return new Request("https://identity.test/v1/admin/identity/" + path, { method,
    headers: { authorization: "Bearer " + token, "x-identity-application": application, "content-type": "application/json" },
    ...(body ? { body: JSON.stringify(body) } : {}) });
}
const remove = (f, id = target, application) => worker.fetch(req("employees/" + id, "DELETE", undefined, application), f.env);
const exists = (f, table, column = "employee_id", value = target) => f.db.prepare(`SELECT count(*) AS n FROM ${table} WHERE ${column}=?`).get(value).n;

test("verified deletion cascades authentication material, retains history and allows a fresh identity", async () => {
  const f = setup();
  try {
    assert.equal((await remove(f)).status, 200);
    for (const table of ["employees", "employee_credentials", "employee_initial_credentials", "identity_sessions", "employee_application_access"])
      assert.equal(exists(f, table), 0, table);
    assert.equal(exists(f,"email_otp_challenges","challenge_id","challenge-target"),0);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM identity_audit_events").get().n,1);
    assert.equal(f.db.prepare("SELECT target_employee_id FROM identity_audit_events").get().target_employee_id,null);
    const response = await worker.fetch(req("employees", "POST", {employeeNo:"0002",displayName:"New Synthetic",email:"target@example.test"}),f.env);
    const body = await response.json();
    assert.equal(response.status,201);
    assert.notEqual(body.employee.employeeId,target);
    assert.equal(body.employee.emailVerified,false);
    assert.equal(body.emailVerificationDelivery.sent,false);
    assert.equal(exists(f,"employee_application_access","employee_id",body.employee.employeeId),0);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM identity_audit_events").get().n,1);
  } finally { f.db.close(); }
});
test("protected and foreign Workspace accounts cannot be deleted",async()=>{
  const f=setup();try{for(const id of ["employee-one","employee-two"]){assert.ok([403,404].includes((await remove(f,id)).status));assert.equal(exists(f,"employees","employee_id",id),1);}}finally{f.db.close();}
});
for(const [name,patch] of [
  ["ordinary admin","UPDATE workspaces SET super_admin_employee_id='employee-alternate' WHERE workspace_id='workspace-one'; UPDATE employees SET role_key='ADMIN' WHERE employee_id='employee-one'"],
  ["Identity Admin","UPDATE workspaces SET super_admin_employee_id='employee-alternate' WHERE workspace_id='workspace-one'; UPDATE employees SET role_key='ADMIN',identity_admin=1 WHERE employee_id='employee-one'"],
  ["revoked Session","UPDATE identity_sessions SET revoked_at='2026-01-02' WHERE employee_id='employee-one'"],
  ["expired Session","UPDATE identity_sessions SET expires_at='2026-01-02' WHERE employee_id='employee-one'"],
])test(`${name} cannot delete verified accounts`,async()=>{
  const f=setup();try{f.db.exec(patch);assert.notEqual((await remove(f)).status,200);assert.equal(exists(f,"employees"),1);}finally{f.db.close();}
});
for(const [name,patch] of [
  ["Session revoked","UPDATE identity_sessions SET revoked_at='2026-01-02' WHERE employee_id='employee-one'"],
  ["authority transferred","UPDATE workspaces SET super_admin_employee_id='employee-target' WHERE workspace_id='workspace-one'"],
  ["target changed","UPDATE employees SET revision=revision+1 WHERE employee_id='employee-target'"],
])test(`transaction rejects ${name} after the initial guard`,async()=>{
  const f=setup({beforeBatch:db=>db.exec(patch)});try{assert.equal((await remove(f)).status,409);assert.equal(exists(f,"employees"),1);assert.equal(exists(f,"email_otp_challenges","challenge_id","challenge-target"),1);}finally{f.db.close();}
});
test("non-core Session and restored defaults retain verified protection; pending deletion still works",async()=>{
  const f=setup({application:"OTHER_APP"});
  try{assert.equal((await remove(f,target,"OTHER_APP")).status,409);testing.allowVerifiedDeletion=false;
    f.db.exec("UPDATE identity_sessions SET application_id='CORE_TEST' WHERE employee_id='employee-one'");
    assert.equal((await remove(f)).status,409);
    f.db.exec("DELETE FROM employee_credentials WHERE employee_id='employee-target'; UPDATE employees SET activated_at=NULL,email_verified_at=NULL WHERE employee_id='employee-target'");
    testing.recordCreateDeleteAudit=true;
    assert.equal((await remove(f)).status,200);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM identity_audit_events WHERE event_type='employee_pending_activation_deleted'").get().n,1);
  }finally{testing.allowVerifiedDeletion=true;testing.recordCreateDeleteAudit=false;f.db.close();}
});
test("snapshot capability and HTML/text Email links come from the actual new-account pipeline",async()=>{
  const f=setup();const original=globalThis.fetch;let mail;
  try{
    const snapshot=await(await worker.fetch(req("snapshot","GET"),f.env)).json();assert.equal(snapshot.testingAccountDeletionAllowed,true);
    Object.assign(f.env,{EMAIL_PROVIDER:"brevo",BREVO_API_KEY:"synthetic-test-key",EMAIL_FROM:"Synthetic <sender@example.test>"});
    globalThis.fetch=async(url,init)=>{assert.equal(String(url),"https://api.brevo.com/v3/smtp/email");mail=JSON.parse(init.body);return new Response('{"messageId":"synthetic"}',{status:201});};
    const response=await worker.fetch(req("employees","POST",{employeeNo:"0019",displayName:"Synthetic Mail",email:"mail@example.test"}),f.env);
    assert.equal(response.status,201);assert.equal((await response.json()).emailVerificationDelivery.sent,true);
    assert.match(mail.textContent,/https:\/\/portal\.example\.test\//);assert.match(mail.htmlContent,/<a href="https:\/\/portal\.example\.test\/">/);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM identity_audit_events WHERE event_type IN ('employee_created_pending_activation','employee_activation_email_sent','employee_activation_email_failed')").get().n,0);
    f.db.exec("UPDATE employee_initial_credentials SET sent_at='2026-01-01' WHERE employee_id=(SELECT employee_id FROM employees WHERE employee_no='0019')");
    const id=f.db.prepare("SELECT employee_id FROM employees WHERE employee_no='0019'").get().employee_id;
    const resend=await worker.fetch(req(`employees/${id}/email-verification/resend-initial`,"POST",{}),f.env);
    assert.equal(resend.status,202);assert.match(mail.htmlContent,/<a href="https:\/\/portal\.example\.test\/">/);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM identity_audit_events WHERE event_type='employee_activation_email_resent'").get().n,1);
    const health=await(await worker.fetch(new Request("https://identity.test/v1/health"),f.env)).json();assert.equal(health.serviceVersion,readFileSync(new URL("../VERSION",import.meta.url),"utf8").trim());
  }finally{globalThis.fetch=original;f.db.close();}
});
