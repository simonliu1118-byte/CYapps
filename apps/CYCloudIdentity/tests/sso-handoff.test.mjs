import assert from "node:assert/strict";
import test from "node:test";
import { createHash } from "node:crypto";
import { worker, fixture, token, canonicalSsoReturnUrl } from "./authority-fixture.mjs";

// Synthetic SSO fixture on top of the shared authority fixture:
// - employee-one: Workspace Super Admin of workspace-one (portal token `token`)
// - employee-user: ordinary USER with a CORE_TEST portal Session (`userToken`)
//   and direct Application Access to OTHER_APP only
// - OTHER_APP / THIRD_APP: registered with SSO return URLs; NO_SSO_APP: no URL
const CORE = "CORE_TEST";
const OTHER_URL = "https://other.example.com/auth/sso/callback";
const THIRD_URL = "https://third.example.com/auth/sso/callback";
const userToken = `cyid_${"c".repeat(64)}`;
const sha256 = value => createHash("sha256").update(value).digest("hex");

function rateLimiter(limit = 3) {
  const counts = new Map();
  return {
    keys: [],
    async limit({ key }) {
      this.keys.push(key);
      const n = (counts.get(key) ?? 0) + 1;
      counts.set(key, n);
      return { success: n <= limit };
    },
  };
}

function setup({ limit = 3 } = {}) {
  const f = fixture();
  f.db.exec(`
    INSERT INTO applications(application_id, display_name) VALUES ('THIRD_APP', 'Third App'), ('NO_SSO_APP', 'No SSO App');
    INSERT INTO workspace_applications(workspace_id, application_id) VALUES ('workspace-one', 'THIRD_APP'), ('workspace-one', 'NO_SSO_APP');
    UPDATE applications SET sso_return_url = '${OTHER_URL}' WHERE application_id = 'OTHER_APP';
    UPDATE applications SET sso_return_url = '${THIRD_URL}' WHERE application_id = 'THIRD_APP';
    INSERT INTO employees(employee_id, workspace_id, employee_no, name, email_normalized, email_verified_at, role_key, activated_at)
      VALUES ('employee-user', 'workspace-one', '0002', 'Synthetic User', 'user@example.test', '2026-01-01', 'USER', '2026-01-01');
    INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version) VALUES ('employee-user', 'pbkdf2-sha256', '${"x".repeat(96)}', 1);
    INSERT INTO employee_application_access(workspace_id, employee_id, application_id, enabled)
      VALUES ('workspace-one', 'employee-user', 'OTHER_APP', 1), ('workspace-one', 'employee-user', 'NO_SSO_APP', 1);
    INSERT INTO identity_sessions(session_hash, workspace_id, employee_id, application_id, credential_version, employee_revision, created_at, expires_at)
      VALUES ('${sha256(userToken)}', 'workspace-one', 'employee-user', '${CORE}', 1, 1, '2026-01-01', '2999-01-01');`);
  f.limiter = rateLimiter(limit);
  Object.assign(f.env, { LOGIN_RATE_LIMITER: f.limiter, SESSION_TTL_SECONDS: "3600" });
  return f;
}

function post(path, { bearer, application, body }) {
  const headers = { "content-type": "application/json" };
  if (bearer) headers.authorization = `Bearer ${bearer}`;
  if (application) headers["x-identity-application"] = application;
  return new Request(`https://identity.test/v1/identity/sso/handoff/${path}`, {
    method: "POST", headers, body: typeof body === "string" ? body : JSON.stringify(body),
  });
}
async function call(f, request) {
  const response = await worker.fetch(request, f.env);
  const text = await response.text();
  return { status: response.status, text, body: JSON.parse(text), headers: response.headers };
}
const issue = (f, body = { applicationId: "OTHER_APP" }, { bearer = userToken, application = CORE } = {}) =>
  call(f, post("issue", { bearer, application, body }));
const redeem = (f, code, application = "OTHER_APP") =>
  call(f, post("redeem", { application, body: { code } }));
const count = (f, sql, ...values) => f.db.prepare(`SELECT count(*) AS n FROM ${sql}`).get(...values).n;
const audits = (f, type) => f.db.prepare(
  "SELECT workspace_id, actor_employee_id, target_employee_id, application_id, detail_json FROM identity_audit_events WHERE event_type = ? ORDER BY created_at, rowid",
).all(type).map(row => ({ ...row, detail: JSON.parse(row.detail_json) }));

async function issued(f, body, options) {
  const result = await issue(f, body, options);
  assert.equal(result.status, 200, result.text);
  return result.body;
}

// ---------------------------------------------------------------------------
// §8.1 / §8.2 — return URL is only the registered value, exact equality.

test("§8.1 issue returns the registered return URL and a 43-char base64url code stored only as SHA-256", async () => {
  const f = setup();
  try {
    const result = await issue(f);
    assert.equal(result.status, 200);
    assert.equal(result.body.returnUrl, OTHER_URL);
    assert.match(result.body.code, /^[A-Za-z0-9_-]{43}$/);
    assert.equal(Buffer.from(result.body.code, "base64url").length, 32);
    assert.equal(result.headers.get("cache-control"), "no-store");
    assert.equal(result.headers.get("referrer-policy"), "no-referrer");
    const row = f.db.prepare("SELECT * FROM sso_handoffs").get();
    assert.equal(row.code_hash, sha256(result.body.code));
    assert.equal(row.application_id, "OTHER_APP");
    assert.equal(row.portal_application_id, CORE);
    assert.equal(row.employee_id, "employee-user");
    assert.equal(row.issued_session_hash, sha256(userToken));
    assert.equal(row.used_at, null);
    assert.equal(Date.parse(row.expires_at) - Date.parse(row.created_at), 60_000);
    assert.equal(result.body.expiresAt, row.expires_at);
    assert.equal(JSON.stringify(row).includes(result.body.code), false);
  } finally { f.db.close(); }
});

test("§8.1 portal cannot choose the destination: redirect fields, unregistered, unknown and core targets are refused", async () => {
  const f = setup();
  try {
    for (const body of [
      { applicationId: "OTHER_APP", redirectUri: "https://evil.example.net/cb" },
      { applicationId: "OTHER_APP", return_url: OTHER_URL },
      { applicationId: "OTHER_APP", returnUrl: null },
      { applicationId: "OTHER_APP", workspaceId: "workspace-two" },
      { applicationId: "!" },
      {},
    ]) {
      const result = await issue(f, body);
      assert.equal(result.status, 400, JSON.stringify(body));
      assert.equal(result.body.error.code, "INVALID_SSO_HANDOFF_REQUEST");
    }
    for (const applicationId of ["NO_SSO_APP", "MISSING_APP", CORE]) {
      const result = await issue(f, { applicationId });
      assert.equal(result.status, 404, applicationId);
      assert.equal(result.body.error.code, "SSO_APPLICATION_UNAVAILABLE");
    }
    // Disabled for this Workspace or globally -> unavailable.
    f.db.exec("UPDATE workspace_applications SET enabled = 0 WHERE workspace_id = 'workspace-one' AND application_id = 'THIRD_APP'");
    assert.equal((await issue(f, { applicationId: "THIRD_APP" }, { bearer: token })).status, 404);
    f.db.exec("UPDATE applications SET status = 'disabled' WHERE application_id = 'OTHER_APP'");
    assert.equal((await issue(f)).status, 404);
    assert.equal(count(f, "sso_handoffs"), 0);
  } finally { f.db.close(); }
});

test("§8.2 return URL assertion is exact string equality: lookalike host, case, slashes, query and fragment are refused", async () => {
  const f = setup();
  try {
    for (const returnUrl of [
      "https://other.example.com.evil.com/auth/sso/callback",
      "https://evil-other.example.com/auth/sso/callback",
      "https://OTHER.example.com/auth/sso/callback",
      "https://other.example.com/Auth/SSO/callback",
      "HTTPS://other.example.com/auth/sso/callback",
      "https://other.example.com//auth/sso/callback",
      "https://other.example.com/auth//sso/callback",
      "https://other.example.com/auth/sso/callback/",
      "https://other.example.com/auth/sso/callback?next=/",
      "https://other.example.com/auth/sso/callback#x",
      "https://other.example.com:443/auth/sso/callback",
      "http://other.example.com/auth/sso/callback",
      " https://other.example.com/auth/sso/callback",
      THIRD_URL,
    ]) {
      const result = await issue(f, { applicationId: "OTHER_APP", returnUrl });
      assert.equal(result.status, 400, returnUrl);
      assert.equal(result.body.error.code, "SSO_RETURN_URL_MISMATCH", returnUrl);
    }
    assert.equal(count(f, "sso_handoffs"), 0);
    const exact = await issue(f, { applicationId: "OTHER_APP", returnUrl: OTHER_URL });
    assert.equal(exact.status, 200);
    assert.equal(exact.body.returnUrl, OTHER_URL);
  } finally { f.db.close(); }
});

test("§8.2 return URL canonicalization accepts only https domain callbacks without credentials, port, query or fragment", () => {
  for (const [input, canonical] of [
    ["https://price.example.com/auth/sso/callback", "https://price.example.com/auth/sso/callback"],
    ["https://PRICE.Example.COM/auth/sso/callback", "https://price.example.com/auth/sso/callback"],
    ["HTTPS://price.example.com/Auth/Callback", "https://price.example.com/Auth/Callback"],
    ["https://a-b.xn--kpry57d/cb", "https://a-b.xn--kpry57d/cb"],
  ]) assert.equal(canonicalSsoReturnUrl(input), canonical, input);
  for (const input of [
    "http://price.example.com/auth/sso/callback",
    "https://user:pass@price.example.com/auth/sso/callback",
    "https://user@price.example.com/auth/sso/callback",
    "https://price.example.com/auth/sso/callback?x=1",
    "https://price.example.com/auth/sso/callback?",
    "https://price.example.com/auth/sso/callback#",
    "https://price.example.com/auth/sso/callback#frag",
    "https://price.example.com:443/auth/sso/callback",
    "https://price.example.com:8443/auth/sso/callback",
    "https://127.0.0.1/auth/sso/callback",
    "https://0x7f.0.0.1/auth/sso/callback",
    "https://2130706433/auth/sso/callback",
    "https://[::1]/auth/sso/callback",
    "https://localhost/auth/sso/callback",
    "https://price.example.com./auth/sso/callback",
    "https://price..example.com/auth/sso/callback",
    "https://-price.example.com/auth/sso/callback",
    "https://price.example.123/auth/sso/callback",
    "https://price.example.com",
    "https://price.example.com//auth/sso/callback",
    "https://price.example.com/auth/./sso/callback",
    "https://price.example.com/auth/../callback",
    "https://price.example.com/auth/%2e%2e/callback",
    "https://price.example.com/auth\\sso",
    "https://price.example.com/auth sso",
    " https://price.example.com/auth/sso/callback",
    "https://價格.example.com/auth/sso/callback",
    "javascript://price.example.com/x",
    `https://price.example.com/${"a".repeat(512)}`,
    null, 42, {},
  ]) assert.equal(canonicalSsoReturnUrl(input), null, String(input));
});

// Registration (CONSUMER_INTEGRATION_STANDARD §17.1) sets/clears the return URL.
function register(f, body, bearer = token) {
  return call(f, new Request("https://identity.test/v1/admin/identity/applications", {
    method: "POST",
    headers: { "content-type": "application/json", authorization: `Bearer ${bearer}`, "x-identity-application": CORE },
    body: JSON.stringify(body),
  }));
}

test("registration stores the canonical return URL, clears it with null and audits each real change", async () => {
  const f = setup();
  try {
    let result = await register(f, { applicationId: "APP_NEW", displayName: "New App", ssoReturnUrl: "https://NEW.Example.com/auth/sso/callback" });
    assert.equal(result.status, 200, result.text);
    assert.equal(result.body.application.ssoReturnUrl, "https://new.example.com/auth/sso/callback");
    assert.equal(f.db.prepare("SELECT sso_return_url FROM applications WHERE application_id = 'APP_NEW'").get().sso_return_url,
      "https://new.example.com/auth/sso/callback");
    let changes = audits(f, "application_sso_return_url_updated");
    assert.equal(changes.length, 1);
    assert.deepEqual(changes[0].detail, { ssoReturnUrl: "https://new.example.com/auth/sso/callback" });
    assert.equal(changes[0].actor_employee_id, "employee-one");
    assert.equal(changes[0].application_id, "APP_NEW");

    // Re-sending the same value changes nothing and adds no URL audit.
    result = await register(f, { applicationId: "APP_NEW", displayName: "New App", ssoReturnUrl: "https://new.example.com/auth/sso/callback" });
    assert.equal(result.status, 200);
    assert.equal(audits(f, "application_sso_return_url_updated").length, 1);
    // Omitting the field leaves the value unchanged.
    result = await register(f, { applicationId: "APP_NEW", displayName: "New App" });
    assert.equal(result.body.application.ssoReturnUrl, "https://new.example.com/auth/sso/callback");

    result = await register(f, { applicationId: "APP_NEW", displayName: "New App", ssoReturnUrl: null });
    assert.equal(result.status, 200);
    assert.equal(result.body.application.ssoReturnUrl, null);
    changes = audits(f, "application_sso_return_url_updated");
    assert.equal(changes.length, 2);
    assert.deepEqual(changes[1].detail, { ssoReturnUrl: null });

    // Snapshot exposes the registered value for the portal registration UI.
    const snapshot = await (await worker.fetch(new Request("https://identity.test/v1/admin/identity/snapshot", {
      headers: { authorization: `Bearer ${token}`, "x-identity-application": CORE },
    }), f.env)).json();
    assert.equal(snapshot.applications.find(a => a.application_id === "OTHER_APP").sso_return_url, OTHER_URL);
    assert.equal(snapshot.applications.find(a => a.application_id === "APP_NEW").sso_return_url, null);
  } finally { f.db.close(); }
});

test("registration rejects invalid return URLs without any write", async () => {
  const f = setup();
  try {
    for (const ssoReturnUrl of [
      "http://new.example.com/cb", "https://new.example.com/cb?x=1", "https://new.example.com/cb#x",
      "https://u:p@new.example.com/cb", "https://10.0.0.1/cb", "https://new.example.com//cb",
      "https://new.example.com:8443/cb", "", 7, true,
    ]) {
      const result = await register(f, { applicationId: "APP_NEW", displayName: "New App", ssoReturnUrl });
      assert.equal(result.status, 400, String(ssoReturnUrl));
      assert.equal(result.body.error.code, "INVALID_SSO_RETURN_URL");
    }
    assert.equal(count(f, "applications WHERE application_id = 'APP_NEW'"), 0);
    assert.equal(count(f, "identity_audit_events"), 0);
  } finally { f.db.close(); }
});

test("registration refuses return URL changes for an application shared with another Workspace or by non-Super-Admin", async () => {
  const f = setup();
  try {
    // OTHER_APP is registered by workspace-one and workspace-two in the fixture.
    const result = await register(f, { applicationId: "OTHER_APP", displayName: "Other App", ssoReturnUrl: "https://evil.example.com/cb" });
    assert.equal(result.status, 409);
    assert.equal(result.body.error.code, "SSO_RETURN_URL_SHARED_APPLICATION");
    assert.equal(f.db.prepare("SELECT sso_return_url FROM applications WHERE application_id = 'OTHER_APP'").get().sso_return_url, OTHER_URL);
    // An ordinary Employee's core Session cannot register or change a URL.
    const denied = await register(f, { applicationId: "THIRD_APP", displayName: "Third App", ssoReturnUrl: null }, userToken);
    assert.equal(denied.status, 403);
    assert.equal(f.db.prepare("SELECT sso_return_url FROM applications WHERE application_id = 'THIRD_APP'").get().sso_return_url, THIRD_URL);
    assert.equal(count(f, "identity_audit_events"), 0);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// Redeem success: same Session/principal as login.

test("redeem creates a new app Session exactly like login and returns the login shape", async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    const before = Date.now();
    const result = await redeem(f, code);
    assert.equal(result.status, 200, result.text);
    assert.deepEqual(Object.keys(result.body.session).sort(), ["expiresAt", "token"]);
    assert.match(result.body.session.token, /^cyid_[0-9a-f]{64}$/);
    assert.notEqual(result.body.session.token, userToken);
    const ttl = Date.parse(result.body.session.expiresAt) - before;
    assert.ok(ttl > 3_595_000 && ttl <= 3_601_000, String(ttl));
    const stored = f.db.prepare("SELECT * FROM identity_sessions WHERE session_hash = ?").get(sha256(result.body.session.token));
    assert.equal(stored.application_id, "OTHER_APP");
    assert.equal(stored.employee_id, "employee-user");
    assert.equal(stored.credential_version, 1);
    assert.equal(stored.expires_at, result.body.session.expiresAt);
    assert.equal(count(f, "identity_sessions WHERE session_hash = ?", result.body.session.token), 0);

    // The new token resolves for OTHER_APP only, with the same principal.
    const resolved = await call(f, new Request("https://identity.test/v1/identity/session/resolve", {
      method: "POST", headers: { authorization: `Bearer ${result.body.session.token}`, "x-identity-application": "OTHER_APP" },
    }));
    assert.equal(resolved.status, 200);
    assert.deepEqual(resolved.body.principal, result.body.principal);
    assert.equal(result.body.principal.workspaceRole, "USER");
    assert.equal(result.body.principal.employeeId, "employee-user");
    const crossApp = await worker.fetch(new Request("https://identity.test/v1/identity/session/resolve", {
      method: "POST", headers: { authorization: `Bearer ${result.body.session.token}`, "x-identity-application": CORE },
    }), f.env);
    assert.equal(crossApp.status, 401);
    assert.equal(f.db.prepare("SELECT used_at FROM sso_handoffs").get().used_at !== null, true);
  } finally { f.db.close(); }
});

test("§6 Super Admin enters a Workspace-enabled App without a direct grant; ordinary Employee needs Application Access", async () => {
  const f = setup();
  try {
    const admin = await issued(f, { applicationId: "THIRD_APP" }, { bearer: token });
    assert.equal((await redeem(f, admin.code, "THIRD_APP")).body.principal.workspaceRole, "SUPER_ADMIN");
    const denied = await issue(f, { applicationId: "THIRD_APP" });
    assert.equal(denied.status, 403);
    assert.equal(denied.body.error.code, "APPLICATION_ACCESS_DENIED");
    f.db.exec("UPDATE employee_application_access SET enabled = 0 WHERE employee_id = 'employee-user' AND application_id = 'OTHER_APP'");
    assert.equal((await issue(f)).body.error.code, "APPLICATION_ACCESS_DENIED");
    assert.equal(count(f, "sso_handoffs"), 1);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// §8.3 replay and concurrent redeem.

test("§8.3 replayed code is SSO_HANDOFF_INVALID", async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    assert.equal((await redeem(f, code)).status, 200);
    const replay = await redeem(f, code);
    assert.equal(replay.status, 401);
    assert.deepEqual(Object.keys(replay.body.error).sort(), ["code", "message"]);
    assert.equal(replay.body.error.code, "SSO_HANDOFF_INVALID");
    assert.equal(count(f, "identity_sessions WHERE application_id = 'OTHER_APP'"), 1);
    assert.equal(audits(f, "sso_handoff_redeem_failed").at(-1).detail.reason, "USED");
  } finally { f.db.close(); }
});

test("§8.3 concurrent redeem of one code: exactly one succeeds", async () => {
  const f = setup({ limit: 100 });
  try {
    const { code } = await issued(f);
    const results = await Promise.all(Array.from({ length: 5 }, () => redeem(f, code)));
    assert.deepEqual(results.map(r => r.status).sort(), [200, 401, 401, 401, 401]);
    for (const r of results.filter(r => r.status !== 200)) assert.equal(r.body.error.code, "SSO_HANDOFF_INVALID");
    assert.equal(count(f, "identity_sessions WHERE application_id = 'OTHER_APP'"), 1);
    assert.equal(audits(f, "sso_handoff_redeemed").length, 1);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// §8.4 expiry.

test("§8.4 expired code (>60 s) is refused and later purged by the bounded cleanup", async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    f.db.exec("UPDATE sso_handoffs SET created_at = '2026-01-01T00:00:00.000Z', expires_at = '2026-01-01T00:01:00.000Z'");
    const result = await redeem(f, code);
    assert.equal(result.status, 401);
    assert.equal(result.body.error.code, "SSO_HANDOFF_INVALID");
    assert.equal(audits(f, "sso_handoff_redeem_failed")[0].detail.reason, "EXPIRED");
    assert.equal(count(f, "identity_sessions WHERE application_id = 'OTHER_APP'"), 0);

    // Expired (used or unused) rows are removed on the next issue; fresh rows stay.
    const used = await issued(f);
    assert.equal((await redeem(f, used.code)).status, 200);
    f.db.exec(`UPDATE sso_handoffs SET created_at = '2026-01-01T00:00:00.000Z', expires_at = '2026-01-01T00:01:00.000Z'
      WHERE code_hash = '${sha256(used.code)}'`);
    assert.equal(count(f, "sso_handoffs"), 1); // the first expired row was purged by this issue
    const fresh = await issued(f);
    assert.deepEqual(f.db.prepare("SELECT code_hash FROM sso_handoffs").all().map(r => r.code_hash), [sha256(fresh.code)]);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// §8.5 wrong application.

test("§8.5 a code issued for one App cannot be redeemed by another App and is burned", async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    const wrong = await redeem(f, code, "THIRD_APP");
    assert.equal(wrong.status, 401);
    assert.equal(wrong.body.error.code, "SSO_HANDOFF_INVALID");
    const failure = audits(f, "sso_handoff_redeem_failed")[0];
    assert.equal(failure.detail.reason, "WRONG_APPLICATION");
    assert.equal(failure.detail.boundApplicationId, "OTHER_APP");
    assert.equal(failure.application_id, "THIRD_APP");
    // Presentation by the wrong App consumes the code (fail closed).
    assert.equal((await redeem(f, code, "OTHER_APP")).body.error.code, "SSO_HANDOFF_INVALID");
    assert.equal(count(f, "identity_sessions WHERE application_id IN ('OTHER_APP','THIRD_APP')"), 0);
    // Core portal or unknown/malformed headers are refused the same way.
    const other = await issued(f);
    for (const application of [CORE, "UNKNOWN_APP", "!", undefined]) {
      const r = await call(f, post("redeem", { application, body: { code: other.code } }));
      assert.equal(r.body.error.code, "SSO_HANDOFF_INVALID", String(application));
    }
  } finally { f.db.close(); }
});

test("malformed redeem requests return the same stable code without detail", async () => {
  const f = setup({ limit: 100 });
  try {
    const { code } = await issued(f);
    for (const body of ["not json", "[]", {}, { code: code.slice(1) }, { code: `${code}=` }, { code, extra: 1 }, { code: 1 }, { code: "a".repeat(43) }]) {
      const r = await call(f, post("redeem", { application: "OTHER_APP", body }));
      assert.equal(r.status, 401, JSON.stringify(body));
      assert.deepEqual(r.body.error, { code: "SSO_HANDOFF_INVALID", message: "Single sign-on handoff is invalid." });
    }
    assert.equal((await redeem(f, code)).status, 200);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// §8.7 authority changes between issue and redeem.

for (const [name, patch, reason] of [
  ["Employee disabled", "UPDATE employees SET enabled = 0 WHERE employee_id = 'employee-user'", "EMPLOYEE_INVALID"],
  ["Application Access revoked", "UPDATE employee_application_access SET enabled = 0 WHERE employee_id = 'employee-user' AND application_id = 'OTHER_APP'", "ACCESS_DENIED"],
  ["Application Access deleted", "DELETE FROM employee_application_access WHERE employee_id = 'employee-user' AND application_id = 'OTHER_APP'", "ACCESS_DENIED"],
  ["credential version changed", "UPDATE employee_credentials SET credential_version = 2 WHERE employee_id = 'employee-user'", "CREDENTIAL_CHANGED"],
  ["portal Session revoked (logout)", `UPDATE identity_sessions SET revoked_at = '2026-01-02' WHERE session_hash = '${sha256(userToken)}'`, "PORTAL_SESSION_INVALID"],
  ["portal Session expired", `UPDATE identity_sessions SET created_at = '2000-01-01', expires_at = '2001-01-01' WHERE session_hash = '${sha256(userToken)}'`, "PORTAL_SESSION_INVALID"],
  ["Workspace disabled", "UPDATE workspaces SET status = 'disabled' WHERE workspace_id = 'workspace-one'", "EMPLOYEE_INVALID"],
  ["Application disabled for Workspace", "UPDATE workspace_applications SET enabled = 0 WHERE workspace_id = 'workspace-one' AND application_id = 'OTHER_APP'", "APPLICATION_UNAVAILABLE"],
  ["Application globally disabled", "UPDATE applications SET status = 'disabled' WHERE application_id = 'OTHER_APP'", "APPLICATION_UNAVAILABLE"],
  ["return URL cleared", "UPDATE applications SET sso_return_url = NULL WHERE application_id = 'OTHER_APP'", "APPLICATION_UNAVAILABLE"],
  ["Employee returned to first-login state", "UPDATE employees SET activated_at = NULL WHERE employee_id = 'employee-user'", "EMPLOYEE_INVALID"],
]) test(`§8.7 redeem fails after issue when ${name}`, async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    f.db.exec(patch);
    const result = await redeem(f, code);
    assert.equal(result.status, 401);
    assert.deepEqual(result.body.error, { code: "SSO_HANDOFF_INVALID", message: "Single sign-on handoff is invalid." });
    assert.equal(count(f, "identity_sessions WHERE application_id = 'OTHER_APP'"), 0);
    const failure = audits(f, "sso_handoff_redeem_failed")[0];
    assert.equal(failure.detail.reason, reason);
    assert.equal(failure.target_employee_id, "employee-user");
    // The code stays burned even if authority is restored.
    assert.equal(f.db.prepare("SELECT used_at FROM sso_handoffs").get().used_at !== null, true);
  } finally { f.db.close(); }
});

test("§8.7 deleting the issuing portal Session removes outstanding handoffs", async () => {
  const f = setup();
  try {
    const first = await issued(f);
    f.db.exec(`DELETE FROM identity_sessions WHERE session_hash = '${sha256(userToken)}'`);
    assert.equal(count(f, "sso_handoffs"), 0);
    assert.equal((await redeem(f, first.code)).body.error.code, "SSO_HANDOFF_INVALID");
    assert.deepEqual(f.db.prepare("PRAGMA foreign_key_check").all(), []);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// §8.8 only the portal may issue; first-login refusal; invalid portal Session.

test("§8.8 non-portal caller cannot issue (403), even with a valid Super Admin Session for that App", async () => {
  const f = setup();
  try {
    const otherToken = `cyid_${"e".repeat(64)}`;
    f.db.prepare(`INSERT INTO identity_sessions(session_hash, workspace_id, employee_id, application_id, credential_version, employee_revision, created_at, expires_at)
      VALUES (?, 'workspace-one', 'employee-one', 'OTHER_APP', 1, 1, '2026-01-01', '2999-01-01')`).run(sha256(otherToken));
    for (const [bearer, application] of [[otherToken, "OTHER_APP"], [token, "OTHER_APP"], [userToken, "THIRD_APP"], [userToken, null], [null, "OTHER_APP"]]) {
      const result = await issue(f, { applicationId: "THIRD_APP" }, { bearer, application });
      assert.equal(result.status, 403, String(application));
      assert.equal(result.body.error.code, "SSO_PORTAL_REQUIRED");
    }
    assert.equal(count(f, "sso_handoffs"), 0);
    const failures = audits(f, "sso_handoff_issue_failed");
    assert.equal(failures.length, 5);
    assert.equal(failures[0].detail.reason, "SSO_PORTAL_REQUIRED");
    assert.equal(failures[0].detail.callerApplicationId, "OTHER_APP");
    assert.equal(failures[0].workspace_id, null);
  } finally { f.db.close(); }
});

test("portal request without a valid portal Session is refused", async () => {
  const f = setup();
  try {
    for (const bearer of [null, `cyid_${"f".repeat(64)}`, "not-a-token"]) {
      const result = await issue(f, undefined, { bearer });
      assert.equal(result.status, 401);
      assert.equal(result.body.error.code, "SESSION_INVALID");
    }
    f.db.exec(`UPDATE identity_sessions SET revoked_at = '2026-01-02' WHERE session_hash = '${sha256(userToken)}'`);
    assert.equal((await issue(f)).status, 401);
    assert.equal(count(f, "sso_handoffs"), 0);
  } finally { f.db.close(); }
});

test("§8.10 first-login-required Employee is refused with FIRST_LOGIN_REQUIRED and no code", async () => {
  for (const patch of [
    "UPDATE employees SET activated_at = NULL WHERE employee_id = 'employee-user'",
    "INSERT INTO employee_initial_credentials(employee_id, algorithm, verifier, issued_at, expires_at) VALUES ('employee-user', 'pbkdf2-sha256', 'synthetic', '2026-01-01', '2999-01-01')",
  ]) {
    const f = setup();
    try {
      f.db.exec(patch);
      const result = await issue(f);
      assert.equal(result.status, 403);
      assert.equal(result.body.error.code, "FIRST_LOGIN_REQUIRED");
      assert.equal("code" in result.body, false);
      assert.equal(count(f, "sso_handoffs"), 0);
      const failure = audits(f, "sso_handoff_issue_failed")[0];
      assert.equal(failure.detail.reason, "FIRST_LOGIN_REQUIRED");
      assert.equal(failure.actor_employee_id, "employee-user");
    } finally { f.db.close(); }
  }
});

// ---------------------------------------------------------------------------
// §8.11 audit present; no code or token anywhere in Audit, errors or logs.

test("§8.11 issue/redeem success and failure are audited without codes or tokens in Audit, responses or logs", async () => {
  const f = setup({ limit: 100 });
  const logged = [];
  const originals = {};
  for (const level of ["log", "info", "warn", "error", "debug"]) {
    originals[level] = console[level];
    console[level] = (...args) => { logged.push(JSON.stringify(args)); };
  }
  const responses = [];
  try {
    const keep = r => { responses.push(r.text); return r; };
    const ok = keep(await issue(f));
    const success = keep(await redeem(f, ok.body.code));
    keep(await redeem(f, ok.body.code));                                    // replay failure
    keep(await issue(f, { applicationId: "THIRD_APP" }));                   // issue failure (access)
    const wrong = keep(await issue(f));
    keep(await redeem(f, wrong.body.code, "THIRD_APP"));                    // wrong app
    keep(await redeem(f, "A".repeat(43)));                                  // unknown
    // A storage failure must not leak request material into the logged error.
    f.db.exec("CREATE TRIGGER synthetic_failure BEFORE UPDATE ON sso_handoffs BEGIN SELECT RAISE(ABORT, 'synthetic storage failure'); END");
    const boom = keep(await issue(f));
    const failed = keep(await redeem(f, boom.body.code));
    assert.equal(failed.status, 500);
    assert.equal(failed.body.error.code, "IDENTITY_REQUEST_FAILED");

    const issuedOk = audits(f, "sso_handoff_issued");
    assert.equal(issuedOk.length, 3);
    assert.deepEqual(issuedOk[0].detail, { result: "success", callerApplicationId: CORE });
    assert.equal(issuedOk[0].workspace_id, "workspace-one");
    assert.equal(issuedOk[0].actor_employee_id, "employee-user");
    assert.equal(issuedOk[0].application_id, "OTHER_APP");
    const issueFailed = audits(f, "sso_handoff_issue_failed");
    assert.equal(issueFailed.length, 1);
    assert.equal(issueFailed[0].detail.reason, "APPLICATION_ACCESS_DENIED");
    assert.equal(issueFailed[0].application_id, "THIRD_APP");
    const redeemed = audits(f, "sso_handoff_redeemed");
    assert.equal(redeemed.length, 1);
    assert.equal(redeemed[0].target_employee_id, "employee-user");
    assert.equal(redeemed[0].application_id, "OTHER_APP");
    assert.deepEqual(audits(f, "sso_handoff_redeem_failed").map(a => a.detail.reason), ["USED", "WRONG_APPLICATION", "UNKNOWN"]);

    const secrets = [
      ok.body.code, wrong.body.code, boom.body.code, sha256(ok.body.code), sha256(wrong.body.code),
      userToken, sha256(userToken), token, success.body.session.token, sha256(success.body.session.token),
    ];
    const auditText = JSON.stringify(f.db.prepare("SELECT * FROM identity_audit_events").all());
    const errorResponses = responses.filter(text => JSON.parse(text).error);
    for (const secret of secrets) {
      assert.equal(auditText.includes(secret), false, "audit contains secret material");
      for (const text of errorResponses) assert.equal(text.includes(secret), false, "error response contains secret material");
      for (const line of logged) assert.equal(line.includes(secret), false, "log contains secret material");
    }
    assert.ok(logged.some(line => line.includes("sso_handoff_redeem_failed")), "sanitized failure was logged");
    assert.equal(logged.some(line => line.includes("synthetic storage failure")), false);
    // Raw codes never reach the handoff table.
    const handoffText = JSON.stringify(f.db.prepare("SELECT * FROM sso_handoffs").all());
    for (const code of [ok.body.code, wrong.body.code, boom.body.code]) assert.equal(handoffText.includes(code), false);
  } finally {
    Object.assign(console, originals);
    f.db.close();
  }
});

// ---------------------------------------------------------------------------
// §8.13 rate limit on redeem failures.

test("§8.13 redeem failures are rate-limited per calling application with the existing limiter", async () => {
  const f = setup({ limit: 3 });
  try {
    const { code } = await issued(f);
    for (let i = 0; i < 3; i += 1) assert.equal((await redeem(f, "B".repeat(43))).status, 401);
    const limited = await redeem(f, "B".repeat(43));
    assert.equal(limited.status, 429);
    assert.equal(limited.body.error.code, "AUTH_RATE_LIMITED");
    assert.equal(limited.headers.get("retry-after"), "60");
    // Limited failures add no further Audit rows (bounded writes).
    assert.equal(audits(f, "sso_handoff_redeem_failed").length, 3);
    // Keys are per application and never contain the code.
    assert.deepEqual(new Set(f.limiter.keys), new Set([sha256("sso-redeem-failure:OTHER_APP")]));
    // Another application has its own budget; successes never consume budget.
    const third = await issued(f, { applicationId: "THIRD_APP" }, { bearer: token });
    assert.equal((await redeem(f, "C".repeat(43), "THIRD_APP")).status, 401);
    assert.equal((await redeem(f, third.code, "THIRD_APP")).status, 200);
    assert.equal((await redeem(f, code)).status, 200);
    assert.equal(f.limiter.keys.length, 5);
  } finally { f.db.close(); }
});

test("redeem fails closed when the rate limiter binding is missing", async () => {
  const f = setup();
  try {
    const { code } = await issued(f);
    delete f.env.LOGIN_RATE_LIMITER;
    const result = await redeem(f, code);
    assert.equal(result.status, 503);
    assert.equal(result.body.error.code, "AUTH_RATE_LIMITER_UNAVAILABLE");
    assert.equal(f.db.prepare("SELECT used_at FROM sso_handoffs").get().used_at, null);
  } finally { f.db.close(); }
});

// ---------------------------------------------------------------------------
// Preview (contract 1.2.0): read-only, same checks as issue, no returnUrl.

const preview = (f, body = { applicationId: "OTHER_APP" }, { bearer = userToken, application = CORE } = {}) =>
  call(f, post("preview", { bearer, application, body }));
const writeCounts = f => ({
  handoffs: count(f, "sso_handoffs"),
  audit: count(f, "identity_audit_events"),
  sessions: count(f, "identity_sessions"),
});

test("preview: allowed returns name and access, no returnUrl, and writes nothing", async () => {
  const f = setup();
  try {
    const before = writeCounts(f);
    const result = await preview(f);
    assert.equal(result.status, 200, result.text);
    assert.deepEqual(result.body.application, { applicationId: "OTHER_APP", displayName: "Other App", displayNameZh: null, origin: new URL(OTHER_URL).origin });
    assert.notEqual(result.body.application.origin, OTHER_URL, "only the origin, never the callback path");
    assert.equal(result.body.access, "allowed");
    assert.equal(result.headers.get("cache-control"), "no-store");
    assert.equal(result.headers.get("referrer-policy"), "no-referrer");
    assert.equal(result.text.includes("returnUrl"), false);
    assert.equal(result.text.includes(OTHER_URL), false);
    assert.equal("code" in result.body, false);
    assert.deepEqual(writeCounts(f), before);
    // Lowercase / padded application ids normalize like issue.
    assert.equal((await preview(f, { applicationId: " other_app " })).body.application.applicationId, "OTHER_APP");
    // Super Admin enters a Workspace-enabled App without a direct grant.
    assert.equal((await preview(f, { applicationId: "THIRD_APP" }, { bearer: token })).body.access, "allowed");
  } finally { f.db.close(); }
});

test("preview: denied when the Employee has no Application Access (same rule as issue)", async () => {
  const f = setup();
  try {
    const before = writeCounts(f);
    const result = await preview(f, { applicationId: "THIRD_APP" });
    assert.equal(result.status, 200);
    assert.equal(result.body.access, "denied");
    assert.equal(result.body.application.displayName, "Third App");
    assert.equal(result.text.includes("returnUrl"), false);
    assert.equal((await issue(f, { applicationId: "THIRD_APP" })).body.error.code, "APPLICATION_ACCESS_DENIED");
    f.db.exec("UPDATE employee_application_access SET enabled = 0 WHERE employee_id = 'employee-user' AND application_id = 'OTHER_APP'");
    assert.equal((await preview(f)).body.access, "denied");
    assert.equal(count(f, "sso_handoffs"), before.handoffs);
  } finally { f.db.close(); }
});

test("preview: first_login_required when first-login is incomplete (same rule as issue)", async () => {
  for (const patch of [
    "UPDATE employees SET activated_at = NULL WHERE employee_id = 'employee-user'",
    "INSERT INTO employee_initial_credentials(employee_id, algorithm, verifier, issued_at, expires_at) VALUES ('employee-user', 'pbkdf2-sha256', 'synthetic', '2026-01-01', '2999-01-01')",
  ]) {
    const f = setup();
    try {
      f.db.exec(patch);
      const before = writeCounts(f);
      const result = await preview(f);
      assert.equal(result.status, 200);
      assert.equal(result.body.access, "first_login_required");
      assert.equal(result.body.application.displayName, "Other App");
      assert.equal((await issue(f)).body.error.code, "FIRST_LOGIN_REQUIRED");
      assert.equal(count(f, "sso_handoffs"), before.handoffs);
    } finally { f.db.close(); }
  }
});

test("preview: unknown, disabled, URL-less and core targets are 404 SSO_APPLICATION_UNAVAILABLE without writes", async () => {
  const f = setup();
  try {
    f.db.exec("UPDATE workspace_applications SET enabled = 0 WHERE workspace_id = 'workspace-one' AND application_id = 'THIRD_APP'");
    f.db.exec("INSERT INTO applications(application_id, display_name, status, sso_return_url) VALUES ('GLOBAL_OFF', 'Global Off', 'disabled', 'https://off.example.com/cb')");
    f.db.exec("INSERT INTO workspace_applications(workspace_id, application_id) VALUES ('workspace-one', 'GLOBAL_OFF')");
    const before = writeCounts(f);
    for (const applicationId of ["MISSING_APP", "THIRD_APP", "GLOBAL_OFF", "NO_SSO_APP", CORE]) {
      const result = await preview(f, { applicationId });
      assert.equal(result.status, 404, applicationId);
      assert.equal(result.body.error.code, "SSO_APPLICATION_UNAVAILABLE");
      assert.equal("application" in result.body, false);
    }
    assert.deepEqual(writeCounts(f), before);
  } finally { f.db.close(); }
});

test("preview: only the core portal may call it (403) and a valid portal Session is required (401)", async () => {
  const f = setup();
  try {
    const otherToken = `cyid_${"e".repeat(64)}`;
    f.db.prepare(`INSERT INTO identity_sessions(session_hash, workspace_id, employee_id, application_id, credential_version, employee_revision, created_at, expires_at)
      VALUES (?, 'workspace-one', 'employee-one', 'OTHER_APP', 1, 1, '2026-01-01', '2999-01-01')`).run(sha256(otherToken));
    const before = writeCounts(f);
    for (const [bearer, application] of [[otherToken, "OTHER_APP"], [token, "OTHER_APP"], [userToken, "THIRD_APP"], [userToken, null], [null, "OTHER_APP"]]) {
      const result = await preview(f, { applicationId: "THIRD_APP" }, { bearer, application });
      assert.equal(result.status, 403, String(application));
      assert.equal(result.body.error.code, "SSO_PORTAL_REQUIRED");
    }
    for (const bearer of [null, `cyid_${"f".repeat(64)}`, "not-a-token"]) {
      const result = await preview(f, undefined, { bearer });
      assert.equal(result.status, 401);
      assert.equal(result.body.error.code, "SESSION_INVALID");
    }
    f.db.exec(`UPDATE identity_sessions SET revoked_at = '2026-01-02' WHERE session_hash = '${sha256(userToken)}'`);
    assert.equal((await preview(f)).status, 401);
    assert.deepEqual(writeCounts(f), before);
  } finally { f.db.close(); }
});

test("preview: body must be exactly {applicationId}", async () => {
  const f = setup();
  try {
    const before = writeCounts(f);
    for (const body of [
      { applicationId: "OTHER_APP", returnUrl: OTHER_URL },
      { applicationId: "OTHER_APP", redirectUri: "https://evil.example.net/cb" },
      { applicationId: "OTHER_APP", workspaceId: "workspace-two" },
      { applicationId: "!" },
      { applicationId: 5 },
      {},
      [],
      "not-json",
    ]) {
      const result = await call(f, post("preview", { bearer: userToken, application: CORE, body }));
      assert.equal(result.status, 400, JSON.stringify(body));
      assert.equal(result.body.error.code, "INVALID_SSO_HANDOFF_REQUEST");
    }
    assert.deepEqual(writeCounts(f), before);
  } finally { f.db.close(); }
});

test("preview: GET is not routed and the session token never appears in the response", async () => {
  const f = setup();
  try {
    const get = await worker.fetch(new Request("https://identity.test/v1/identity/sso/handoff/preview", {
      method: "GET", headers: { authorization: `Bearer ${userToken}`, "x-identity-application": CORE },
    }), f.env);
    assert.equal(get.status, 404);
    const result = await preview(f);
    assert.equal(result.text.includes(userToken), false);
  } finally { f.db.close(); }
});


// ---------------------------------------------------------------------------
// Chinese display name (contract 1.3.0) and portal application list.

const zhAudits = f => audits(f, "application_zh_name_updated");

test("registration sets, keeps, clears and audits the Chinese name only on real change", async () => {
  const f = setup();
  try {
    let result = await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh: "  新系統  " });
    assert.equal(result.status, 200, result.text);
    assert.equal(result.body.application.displayNameZh, "新系統");
    assert.equal(f.db.prepare("SELECT display_name_zh FROM applications WHERE application_id = 'APP_NEW'").get().display_name_zh, "新系統");
    assert.equal(zhAudits(f).length, 1);
    assert.deepEqual(zhAudits(f)[0].detail, { displayNameZh: "新系統" });
    assert.equal(zhAudits(f)[0].actor_employee_id, "employee-one");
    // Same value: no new audit. Omitted: unchanged.
    result = await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh: "新系統" });
    assert.equal(result.status, 200);
    assert.equal(zhAudits(f).length, 1);
    result = await register(f, { applicationId: "APP_NEW", displayName: "New App" });
    assert.equal(result.body.application.displayNameZh, "新系統");
    assert.equal(result.body.application.displayName, "New App");
    // Changing the Chinese name does not touch the return URL.
    assert.equal(result.body.application.ssoReturnUrl, null);
    result = await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh: null });
    assert.equal(result.body.application.displayNameZh, null);
    assert.equal(zhAudits(f).length, 2);
    assert.deepEqual(zhAudits(f)[1].detail, { displayNameZh: null });
    // Clearing an already-empty name is not a change.
    await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh: null });
    assert.equal(zhAudits(f).length, 2);
    const snapshot = await (await worker.fetch(new Request("https://identity.test/v1/admin/identity/snapshot", {
      headers: { authorization: `Bearer ${token}`, "x-identity-application": CORE },
    }), f.env)).json();
    assert.equal(snapshot.applications.find(a => a.application_id === "APP_NEW").display_name_zh, null);
    assert.equal(snapshot.applications.find(a => a.application_id === "OTHER_APP").display_name_zh, null);
  } finally { f.db.close(); }
});

test("registration rejects invalid Chinese names without any write", async () => {
  const f = setup();
  try {
    for (const displayNameZh of ["", "   ", "a".repeat(61), "bad\u0007name", "bad\nname", 7, true, {}]) {
      const result = await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh });
      assert.equal(result.status, 400, String(displayNameZh));
      assert.equal(result.body.error.code, "INVALID_APPLICATION_ZH_NAME");
    }
    assert.equal((await register(f, { applicationId: "APP_NEW", displayName: "New App", displayNameZh: "中".repeat(60) })).status, 200);
    assert.equal(count(f, "applications WHERE application_id = 'APP_NEW'"), 1);
    assert.equal(zhAudits(f).length, 1);
    // The database CHECK also refuses untrimmed / empty values.
    assert.throws(() => f.db.exec("UPDATE applications SET display_name_zh = ' x' WHERE application_id = 'APP_NEW'"));
    assert.throws(() => f.db.exec("UPDATE applications SET display_name_zh = '' WHERE application_id = 'APP_NEW'"));
  } finally { f.db.close(); }
});

test("registration refuses Chinese name changes for shared applications or non-Super-Admin", async () => {
  const f = setup();
  try {
    const result = await register(f, { applicationId: "OTHER_APP", displayName: "Other App", displayNameZh: "他人" });
    assert.equal(result.status, 409);
    assert.equal(result.body.error.code, "APPLICATION_SHARED_ACROSS_WORKSPACES");
    assert.equal(f.db.prepare("SELECT display_name_zh FROM applications WHERE application_id = 'OTHER_APP'").get().display_name_zh, null);
    const denied = await register(f, { applicationId: "THIRD_APP", displayName: "Third App", displayNameZh: "第三" }, userToken);
    assert.equal(denied.status, 403);
    assert.equal(f.db.prepare("SELECT display_name_zh FROM applications WHERE application_id = 'THIRD_APP'").get().display_name_zh, null);
    assert.equal(zhAudits(f).length, 0);
  } finally { f.db.close(); }
});

test("preview returns displayNameZh (null when unset) and nothing else changes", async () => {
  const f = setup();
  try {
    f.db.exec("UPDATE applications SET display_name_zh = '其他系統' WHERE application_id = 'OTHER_APP'");
    const result = await preview(f);
    assert.deepEqual(result.body.application, { applicationId: "OTHER_APP", displayName: "Other App", displayNameZh: "其他系統", origin: new URL(OTHER_URL).origin });
    assert.equal((await preview(f, { applicationId: "THIRD_APP" }, { bearer: token })).body.application.displayNameZh, null);
  } finally { f.db.close(); }
});

const listApps = (f, body = {}, { bearer = userToken, application = CORE } = {}) =>
  call(f, new Request("https://identity.test/v1/identity/sso/applications", {
    method: "POST",
    headers: { "content-type": "application/json", ...(bearer ? { authorization: `Bearer ${bearer}` } : {}), ...(application ? { "x-identity-application": application } : {}) },
    body: typeof body === "string" ? body : JSON.stringify(body),
  }));

test("application list: only enterable Apps with a return URL, origin only, read-only", async () => {
  const f = setup();
  try {
    f.db.exec("UPDATE applications SET display_name_zh = '其他系統' WHERE application_id = 'OTHER_APP'");
    const before = writeCounts(f);
    let result = await listApps(f);
    assert.equal(result.status, 200, result.text);
    // USER: OTHER_APP only (THIRD_APP not granted; NO_SSO_APP has no URL; core excluded).
    assert.deepEqual(result.body.applications, [
      { applicationId: "OTHER_APP", displayName: "Other App", displayNameZh: "其他系統", origin: new URL(OTHER_URL).origin },
    ]);
    assert.equal(result.headers.get("cache-control"), "no-store");
    assert.equal(result.text.includes("callback"), false);
    assert.deepEqual(writeCounts(f), before);
    // Super Admin sees every Workspace-enabled App that has a return URL.
    result = await listApps(f, {}, { bearer: token });
    assert.deepEqual(result.body.applications.map(a => a.applicationId), ["OTHER_APP", "THIRD_APP"]);
    assert.equal(result.body.applications[1].displayNameZh, null);
    // Disabled grant / disabled Workspace App / disabled global App drop out.
    f.db.exec("UPDATE employee_application_access SET enabled = 0 WHERE employee_id = 'employee-user' AND application_id = 'OTHER_APP'");
    assert.deepEqual((await listApps(f)).body.applications, []);
    f.db.exec("UPDATE workspace_applications SET enabled = 0 WHERE workspace_id = 'workspace-one' AND application_id = 'THIRD_APP'");
    assert.deepEqual((await listApps(f, {}, { bearer: token })).body.applications.map(a => a.applicationId), ["OTHER_APP"]);
    f.db.exec("UPDATE applications SET status = 'disabled' WHERE application_id = 'OTHER_APP'");
    assert.deepEqual((await listApps(f, {}, { bearer: token })).body.applications, []);
    assert.deepEqual(writeCounts(f), before);
  } finally { f.db.close(); }
});

test("application list: portal-only, session required, empty body, first login incomplete", async () => {
  const f = setup();
  try {
    assert.equal((await listApps(f, {}, { application: "OTHER_APP" })).body.error.code, "SSO_PORTAL_REQUIRED");
    assert.equal((await listApps(f, {}, { application: null })).body.error.code, "SSO_PORTAL_REQUIRED");
    assert.equal((await listApps(f, {}, { bearer: null })).body.error.code, "SESSION_INVALID");
    assert.equal((await listApps(f, {}, { bearer: `cyid_${"d".repeat(64)}` })).body.error.code, "SESSION_INVALID");
    for (const body of [{ applicationId: "OTHER_APP" }, "not json", "[]"]) {
      const result = await listApps(f, body);
      assert.equal(result.status, 400, String(body));
      assert.equal(result.body.error.code, "INVALID_SSO_HANDOFF_REQUEST");
    }
    f.db.exec("UPDATE employees SET activated_at = NULL WHERE employee_id = 'employee-user'");
    assert.deepEqual((await listApps(f)).body.applications, []);
  } finally { f.db.close(); }
});
