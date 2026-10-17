import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

const bundle = await build({
  entryPoints: ["src/recovery-claim.ts"], bundle: true, format: "esm",
  write: false, platform: "node", target: "node24",
});
const { claimRecoveryAndRevokeOldDevices } = await import(
  "data:text/javascript;base64," + Buffer.from(bundle.outputFiles[0].text).toString("base64"),
);
const op = "11111111-1111-4111-8111-111111111111";
const code = "a".repeat(40);
const codeHash = createHash("sha256").update(code).digest("hex");
const token1 = "cydev_" + "1".repeat(64);
const token2 = "cydev_" + "2".repeat(64);
const workspace = "business_workspace";
const actor = {
  operationId: op, workspaceId: workspace, identityWorkspaceId: "identity_workspace",
  employeeId: "external_cyid_admin", employeeNo: "0001",
  email: "admin@example.test", credentialVersion: 3, employeeRevision: 7,
};
const payload = {
  code, deviceToken: "cydev_" + "f".repeat(64),
  deviceDisplayName: "Recovered computer", clientVersion: "3.0.0",
  requestId: "test-recovery-001",
};

function transaction(db, script) {
  db.exec("BEGIN");
  try { db.exec(script); db.exec("COMMIT"); }
  catch (err) { db.exec("ROLLBACK"); throw err; }
}
function dbFixture() {
  const db = new DatabaseSync(":memory:");
  const names = readdirSync("migrations").filter(x => /^[0-9]{4}_.*[.]sql$/.test(x)).sort();
  for (const name of names.filter(x => x < "0014"))
    db.exec(readFileSync("migrations/" + name, "utf8"));
  db.prepare("INSERT INTO workspaces (workspace_id, display_name) VALUES (?, ?)")
    .run(workspace, "Synthetic Business");
  for (const [i, token] of [[1, token1], [2, token2]]) {
    db.prepare(`INSERT INTO devices (
        device_id,workspace_id,display_name,token_hash,client_version,status,employee_authority_state)
      VALUES (?,?,'Old computer',?,'2.6.19','active','cloud')`)
      .run("old_device_" + i, workspace, createHash("sha256").update(token).digest("hex"));
  }
  db.prepare(`INSERT INTO cloud_employees (
      employee_id,workspace_id,employee_no,name,email_normalized,role,enabled)
    VALUES ('old_owner',?,'0001','Old owner','old@example.test','SUPER_ADMIN',1)`).run(workspace);
  db.prepare(`INSERT INTO device_invitations (
     invitation_id,workspace_id,code_hash,issued_by_device_id,issued_by_employee_id,
     delivery_state,expires_at,created_at)
     VALUES ('old_join_inv',?,?, 'old_device_1','old_owner','sent','2030-01-01','2026-10-01')`)
    .run(workspace, "a".repeat(64));
  db.prepare(`INSERT INTO device_pairing_codes (
      pairing_id,workspace_id,code_hash,created_by_device_id,expires_at)
      VALUES ('old_pairing',?,'b'||'bbbbbbbb','old_device_1','2030-01-01')`).run(workspace);
  transaction(db, readFileSync("maintenance/retire-built-in-for-cyid-only.sql", "utf8"));
  transaction(db, readFileSync("migrations/0014_invitation_recovery_metadata.sql", "utf8"));
  transaction(db, readFileSync("maintenance/enable-cyweb-recovery-issuer.sql", "utf8"));
  db.prepare(`INSERT INTO device_invitations (
    invitation_id,workspace_id,code_hash,issued_by_device_id,issued_by_cyid_employee_id,
    delivery_state,expires_at,created_at,issuer_channel,purpose,
    issuer_credential_version,issuer_employee_revision,recovery_operation_id)
    VALUES ('inv_recovery',?,?,NULL,?,'sent','2030-01-01','2026-10-13',
      'CYWEB','RECOVERY',3,7,?)`).run(workspace, codeHash, actor.employeeId, op);
  return db;
}
class Statement {
  constructor(db, sql) { this.db = db; this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() { return this.db.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: this.db.prepare(this.sql).all(...this.args) }; }
  async run() { return { meta: { changes: this.db.prepare(this.sql).run(...this.args).changes } }; }
}
function fakeD1(db, opts = {}) {
  return {
    prepare(sql) { return new Statement(db, sql); },
    async batch(items) {
      db.exec("BEGIN");
      try {
        const results = [];
        for (const s of items) results.push(await s.run());
        if (opts.afterStatements) await opts.afterStatements(db);
        db.exec("COMMIT");
        return results;
      } catch (error) {
        db.exec("ROLLBACK");
        throw error;
      }
    },
  };
}
function count(db, table, clause = "") {
  return db.prepare("SELECT COUNT(*) AS n FROM " + table + " " + clause).get().n;
}
function tokenStatus(db, token) {
  const hash = createHash("sha256").update(token).digest("hex");
  return db.prepare("SELECT status FROM devices WHERE token_hash=?").get(hash)?.status;
}
async function run(db, claim = payload, verifyNow = async () => actor, opts = {}) {
  return claimRecoveryAndRevokeOldDevices({ DB: fakeD1(db, opts) }, claim, verifyNow);
}

// Atomic success: new active, both old Devices revoked, all other grants
// invalidated, exact audit and all original Workspace/history preserved.
const db = dbFixture();
assert.equal(count(db, "devices"), 2);
const result = await run(db);
assert.equal(result.recovered, false);
assert.equal(result.workspaceId, workspace);
assert.equal(tokenStatus(db, payload.deviceToken), "active");
assert.equal(tokenStatus(db, token1), "revoked");
assert.equal(tokenStatus(db, token2), "revoked");
assert.equal(count(db, "devices", "WHERE status='active'"), 1);
assert.equal(count(db, "devices", "WHERE status='revoked'"), 2);
assert.equal(count(db, "security_audit_events", "WHERE event_type='device_revoked' AND reason_code='RECOVERY_CUTOVER'"), 2);
assert.equal(count(db, "security_audit_events", "WHERE event_type='recovery_device_joined'"), 1);
assert.equal(count(db, "cyweb_recovery_claim_guard"), 0);
assert.equal(count(db, "device_invitations", "WHERE invitation_id='old_join_inv' AND revoked_at IS NOT NULL"), 1);
assert.equal(count(db, "device_pairing_codes", "WHERE pairing_id='old_pairing' AND revoked_at IS NOT NULL"), 1);
assert.equal(count(db, "device_invitations", "WHERE invitation_id='inv_recovery' AND consumed_by_device_id IS NOT NULL"), 1);
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(), []);

const resume = await run(db);
assert.equal(resume.recovered, true, "Same protected pending token recovers after lost response");
assert.equal(resume.deviceId, result.deviceId);
assert.equal(count(db, "devices"), 3);
assert.equal(count(db, "security_audit_events", "WHERE event_type='recovery_device_joined'"), 1);
await assert.rejects(() => run(db, { ...payload, deviceToken: "cydev_" + "e".repeat(64) }),
  /RECOVERY_INVITATION_UNAVAILABLE/);
await assert.rejects(() => run(db, { ...payload, deviceToken: token1 }),
  /RECOVERY_TOKEN_CONFLICT/, "Previously revoked token cannot regain authority");

// DR-09: a previously authorized issuer can race with recovery. Extract the
// actual Worker SQL, not a weaker reimplementation: no pairing grant,
// ordinary invitation grant or JOIN claim may commit after issuer revocation.
const workerSource = readFileSync("src/index.ts", "utf8");
function actualSql(body, marker) {
  const start = body.indexOf("`" + marker);
  assert.ok(start >= 0, "Expected Worker SQL marker: " + marker);
  const end = body.indexOf("`", start + 1);
  assert.ok(end > start, "Unterminated Worker SQL marker: " + marker);
  return body.slice(start + 1, end);
}
const pairingIssueSql = actualSql(workerSource, "INSERT INTO device_pairing_codes (");
const stalePair = db.prepare(pairingIssueSql).run(
  "pair_stale", workspace, "old_device_1", "c".repeat(64),
  "2030-01-01T00:00:00Z", "2026-10-14T00:00:00Z");
assert.equal(stalePair.changes, 0, "revoked issuer must not create new pairing code");

const inviteIssueSql = actualSql(workerSource, "INSERT INTO device_invitations (");
const staleInvite = db.prepare(inviteIssueSql).run(
  "inv_33333333-3333-4333-8333-333333333333",
  workspace, "d".repeat(64), "old_device_1", null,
  "2030-01-01T00:00:00Z", "2026-10-14T00:00:00Z",
  actor.employeeId);
assert.equal(staleInvite.changes, 0, "revoked issuer must not create pending invitation");

// Simulate an invitation that a buggy old issuer managed to persist after
// the fleet revoke. The receiving JOIN must independently fence the issuer
// at the exact Device INSERT, not merely at preview or authentication.
const syntheticInv = "inv_44444444-4444-4444-8444-444444444444";
db.prepare(`INSERT INTO device_invitations (
  invitation_id,workspace_id,code_hash,issued_by_device_id,
  issued_by_cyid_employee_id,delivery_state,expires_at,created_at)
  VALUES(?,?,?,'old_device_1',?,'sent','2030-01-01T00:00:00Z','2026-10-14T00:00:00Z')`)
  .run(syntheticInv,workspace,"e".repeat(64),actor.employeeId);
const claimFunctionSource = workerSource.slice(workerSource.indexOf("async function claimInvitation("));
const joinClaimSql = actualSql(claimFunctionSource, "INSERT INTO devices (device_id, workspace_id, display_name, token_hash,");
const staleJoin = db.prepare(joinClaimSql).run(
  "dev_55555555-5555-4555-8555-555555555555",workspace,
  "Unauthorized computer","f".repeat(64),"3.0.0",
  syntheticInv, "2026-10-14T00:00:00Z");
assert.equal(staleJoin.changes,0,"revoked issuer cannot authorize late JOIN");
assert.equal(count(db,"devices","WHERE status='active'"),1,
  "only recovery winner can be active after an old issuer's in-flight request");
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
db.close();

// A failed audit must roll back the new Device, old fleet revocation, and
// invitation consumption in the *same* transaction.
const rollback = dbFixture();
rollback.exec(`CREATE TRIGGER deny_recovery_audit BEFORE INSERT ON security_audit_events
  WHEN NEW.event_type='recovery_device_joined'
  BEGIN SELECT RAISE(ABORT,'synthetic recovery audit failure'); END;`);
await assert.rejects(() => run(rollback), /RECOVERY_RESULT_UNKNOWN/);
assert.equal(count(rollback, "devices"), 2);
assert.equal(count(rollback, "devices", "WHERE status='active'"), 2);
assert.equal(count(rollback, "device_invitations", "WHERE consumed_at IS NOT NULL"), 0);
assert.equal(count(rollback, "device_invitations", "WHERE revoked_at IS NOT NULL"), 0);
assert.equal(count(rollback, "device_pairing_codes", "WHERE revoked_at IS NOT NULL"), 0);
assert.equal(count(rollback, "security_audit_events", "WHERE event_type='device_revoked'"), 0);
assert.equal(count(rollback, "cyweb_recovery_claim_guard"), 0);
rollback.close();

// Race after preflight: a competing Device takes the one-time invitation.
// SQL's CHECK guard must reject and not revoke anybody.
const raced = dbFixture();
const racedHash = createHash("sha256").update("cydev_" + "c".repeat(64)).digest("hex");
let checkCount = 0;
await assert.rejects(() => run(raced, payload, async () => {
  // After the initial invitation read but before the atomic batch, another
  // claimant wins. Our SQLite guard must fail without revoking old Devices.
  checkCount++;
  if (checkCount === 2) {
    raced.prepare(`INSERT INTO devices (device_id,workspace_id,display_name,token_hash,
      status,invitation_id) VALUES ('winner',?,'Winner',?,'active','inv_recovery')`)
      .run(workspace, racedHash);
    raced.prepare(`UPDATE device_invitations SET consumed_at='2026-10-14',
      consumed_by_device_id='winner' WHERE invitation_id='inv_recovery'`).run();
  }
  return actor;
}), /RECOVERY_INVITATION_UNAVAILABLE|RECOVERY_RESULT_UNKNOWN/);
assert.equal(count(raced, "devices"), 3);
assert.equal(count(raced, "devices", "WHERE status='active'"), 3);
assert.equal(count(raced, "security_audit_events", "WHERE event_type='recovery_device_joined'"), 0);
raced.close();

// Identity role/revision change before commit: fail before any D1 write.
const denied = dbFixture();
let checks = 0;
await assert.rejects(() => run(denied, payload, async () => {
  checks++;
  if (checks > 1) throw new Error("RECOVERY_AUTHORITY_CHANGED");
  return actor;
}), /RECOVERY_AUTHORITY_CHANGED/);
assert.equal(count(denied, "devices"), 2);
assert.equal(count(denied, "device_invitations", "WHERE consumed_at IS NOT NULL"), 0);
assert.equal(count(denied, "cyweb_recovery_claim_guard"), 0);
denied.close();

console.log("CYInvoice recovery cutover passed: atomic fleet revoke, outstanding grant invalidation, same-token readback, audit rollback, race and authority guards");
