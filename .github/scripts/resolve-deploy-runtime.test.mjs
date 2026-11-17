import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { resolveDeployRuntime, publishResolved, SPECS } from "./resolve-deploy-runtime.mjs";

// 全部使用合成資料與假 fetch；不連任何真實網路。
const dbId = "31111111-1111-4111-8111-111111111111";
const plain = (name, text) => ({ type: "plain_text", name, text });
const HOST = "app.example.test";

const baseState = {
  cypricedesk: () => ({
    bindings: [plain("CYID_APPLICATION_ID", "SYNTHAPP"), plain("CYID_WORKSPACE_ID", "synthetic-workspace"),
      plain("CYWEB_PORTAL_URL", "https://portal.example.test/"), plain("CYPD_FETCHER_URL", "https://fetcher.example.test"),
      { type: "d1", name: "DB", id: dbId }, { type: "service", name: "IDENTITY", service: "synthetic-identity-prod" }],
  }),
  cyaccountingweb: () => ({
    bindings: [plain("BACKUP_TOPOLOGY", "legacy_gcs"), plain("BACKUP_VERIFY_REPOSITORY_ID", "12345"),
      { type: "d1", name: "DB", id: dbId }, { type: "r2_bucket", name: "BACKUP_R2", bucket_name: "synthetic-backup" },
      { type: "service", name: "IDENTITY", service: "synthetic-identity-prod" }],
  }),
};

function fixture(app, change) {
  const state = { domains: [{ hostname: HOST, service: "synthetic-worker", environment: "production", enabled: true }],
    database: { uuid: dbId, name: "synthetic-db" }, subdomain: "synthetic-sub", ...baseState[app]() };
  change?.(state);
  const calls = [];
  async function fetchImpl(url, init) {
    calls.push(url);
    assert.equal(init.method, "GET"); assert.equal(init.redirect, "error");
    assert.equal(new Headers(init.headers).get("authorization"), "Bearer synthetic-token");
    const u = new URL(url);
    assert.equal(u.origin, "https://api.cloudflare.com");
    const ok = result => Response.json({ success: true, result });
    if (u.pathname.endsWith("/workers/domains")) { assert.equal(u.searchParams.get("hostname"), HOST); return ok(state.domains); }
    if (u.pathname.endsWith("/workers/scripts/synthetic-worker/settings")) return ok({ bindings: state.bindings });
    if (u.pathname.endsWith(`/d1/database/${dbId}`)) return ok(state.database);
    if (u.pathname.endsWith("/workers/subdomain")) return ok({ subdomain: state.subdomain });
    return Response.json({ success: false, errors: [{ code: 10007 }] }, { status: 404 });
  }
  return { calls, options: { spec: SPECS[app], accountId: "synthetic-account", apiToken: "synthetic-token", publicHostname: HOST, fetchImpl } };
}

test("CYPriceDesk 解析所有識別碼並遮蔽每一個值", async () => {
  const f = fixture("cypricedesk");
  const { values, masks } = await resolveDeployRuntime(f.options);
  assert.deepEqual(values, {
    CF_WORKER_NAME: "synthetic-worker", CF_D1_DATABASE_ID: dbId, CF_D1_DATABASE_NAME: "synthetic-db",
    CF_IDENTITY_SERVICE: "synthetic-identity-prod", CF_CYID_APPLICATION_ID: "SYNTHAPP", CF_CYID_WORKSPACE_ID: "synthetic-workspace",
    CF_CYWEB_PORTAL_URL: "https://portal.example.test/", CF_CYPD_FETCHER_URL: "https://fetcher.example.test",
  });
  for (const value of [...Object.values(values), "synthetic-sub", "fetcher.example.test", "portal.example.test"]) assert.ok(masks.includes(value), value);
  assert.equal(f.calls.length, 4);
});

test("CYPriceDesk 的 fetcher 網址可為空，且空值不進入遮蔽清單", async () => {
  const f = fixture("cypricedesk", s => { s.bindings.find(b => b.name === "CYPD_FETCHER_URL").text = ""; });
  const { values, masks } = await resolveDeployRuntime(f.options);
  assert.equal(values.CF_CYPD_FETCHER_URL, "");
  assert.ok(!masks.includes(""));
});

test("CYAccountingWeb 解析 Worker、D1 與 R2 並遮蔽", async () => {
  const f = fixture("cyaccountingweb");
  const { values, masks } = await resolveDeployRuntime(f.options);
  assert.deepEqual(values, { CF_WORKER_NAME: "synthetic-worker", CF_D1_DATABASE_ID: dbId, CF_D1_DATABASE_NAME: "synthetic-db", CF_R2_BACKUP_BUCKET: "synthetic-backup" });
  for (const value of [...Object.values(values), "synthetic-sub"]) assert.ok(masks.includes(value), value);
});

test("拒絕未驗證的主機名稱、非本應用程式的 Worker 與缺少或重複的 binding", async () => {
  const cases = [
    ["cypricedesk", s => { s.domains = []; }, /DEPLOY_RESOLVE_DOMAIN_UNVERIFIED/],
    ["cypricedesk", s => { s.domains.push({ ...s.domains[0], service: "other" }); }, /DEPLOY_RESOLVE_DOMAIN_UNVERIFIED/],
    ["cypricedesk", s => { s.domains[0].environment = "staging"; }, /DEPLOY_RESOLVE_DOMAIN_UNVERIFIED/],
    ["cypricedesk", s => { s.domains[0].enabled = false; }, /DEPLOY_RESOLVE_DOMAIN_UNVERIFIED/],
    ["cypricedesk", s => { s.bindings = s.bindings.filter(b => b.name !== "CYPD_FETCHER_URL"); }, /DEPLOY_RESOLVE_WORKER_NOT_EXPECTED_APP/],
    ["cypricedesk", s => { s.bindings = s.bindings.filter(b => b.name !== "DB"); }, /DEPLOY_RESOLVE_DB_UNBOUND/],
    ["cypricedesk", s => { s.bindings.push({ type: "d1", name: "DB", id: dbId }); }, /DEPLOY_RESOLVE_BINDING_AMBIGUOUS DB/],
    ["cypricedesk", s => { s.database.uuid = "41111111-1111-4111-8111-111111111111"; }, /DEPLOY_RESOLVE_DB_METADATA_MISMATCH/],
    ["cypricedesk", s => { s.bindings = s.bindings.filter(b => b.name !== "IDENTITY"); }, /DEPLOY_RESOLVE_SERVICE_UNBOUND IDENTITY/],
    ["cypricedesk", s => { s.bindings.find(b => b.name === "IDENTITY").service = "synthetic-worker"; }, /DEPLOY_RESOLVE_SERVICE_UNBOUND IDENTITY/],
    ["cypricedesk", s => { s.bindings.find(b => b.name === "CYID_WORKSPACE_ID").text = "x"; }, /DEPLOY_RESOLVE_TEXT_INVALID CYID_WORKSPACE_ID/],
    ["cypricedesk", s => { s.subdomain = ""; }, /DEPLOY_RESOLVE_SUBDOMAIN_UNAVAILABLE/],
    ["cyaccountingweb", s => { s.bindings = s.bindings.filter(b => b.name !== "BACKUP_TOPOLOGY"); }, /DEPLOY_RESOLVE_WORKER_NOT_EXPECTED_APP/],
    ["cyaccountingweb", s => { s.bindings.find(b => b.name === "BACKUP_TOPOLOGY").text = "other"; }, /DEPLOY_RESOLVE_WORKER_NOT_EXPECTED_APP/],
    ["cyaccountingweb", s => { s.bindings = s.bindings.filter(b => b.name !== "BACKUP_R2"); }, /DEPLOY_RESOLVE_BUCKET_UNBOUND BACKUP_R2/],
  ];
  for (const [app, change, expected] of cases) {
    await assert.rejects(resolveDeployRuntime(fixture(app, change).options), expected);
  }
});

test("拒絕無效輸入，且 Cloudflare 錯誤不含任何識別碼", async () => {
  const f = fixture("cypricedesk");
  for (const bad of [{ publicHostname: "https://x.example.test" }, { publicHostname: "" }, { accountId: "" }, { apiToken: "" }, { spec: undefined }])
    await assert.rejects(resolveDeployRuntime({ ...f.options, ...bad }), /DEPLOY_RESOLVE_INPUT_INVALID/);
  const denied = async () => Response.json({ success: false, errors: [{ code: 10000 }] }, { status: 403 });
  await assert.rejects(resolveDeployRuntime({ ...f.options, fetchImpl: denied }), error => {
    assert.match(error.message, /^DEPLOY_RESOLVE_RESOURCE_UNAVAILABLE workers HTTP 403 cf=10000$/);
    for (const secret of [HOST, "synthetic-account", "synthetic-token"]) assert.ok(!error.message.includes(secret));
    return true;
  });
});

test("publishResolved 先遮蔽每個值，之後才寫入 GITHUB_ENV", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "resolve-deploy-"));
  const envFile = path.join(dir, "env");
  fs.writeFileSync(envFile, "");
  const events = [];
  const realAppend = fs.appendFileSync;
  fs.appendFileSync = (...args) => { events.push("write"); return realAppend(...args); };
  try { publishResolved({ values: { A: "v1", B: "v2" }, masks: ["v1", "v2", "sub"] }, envFile, line => events.push(line)); }
  finally { fs.appendFileSync = realAppend; }
  assert.deepEqual(events, ["::add-mask::v1", "::add-mask::v2", "::add-mask::sub", "write"]);
  assert.equal(fs.readFileSync(envFile, "utf8"), "A=v1\nB=v2\n");
  fs.rmSync(dir, { recursive: true, force: true });
});

test("CLI 輸入無效時安靜失敗、只輸出固定錯誤碼、不寫入 GITHUB_ENV", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "resolve-deploy-"));
  const envFile = path.join(dir, "env");
  fs.writeFileSync(envFile, "");
  const script = fileURLToPath(new URL("./resolve-deploy-runtime.mjs", import.meta.url));
  const run = args => spawnSync(process.execPath, [script, ...args], {
    env: { PATH: process.env.PATH, GITHUB_ENV: envFile, CLOUDFLARE_ACCOUNT_ID: "synthetic-account", CLOUDFLARE_API_TOKEN: "synthetic-token" },
    encoding: "utf8" });
  // 無效主機名稱：在任何網路呼叫之前就失敗。
  const bad = run(["--app", "cypricedesk", "--hostname", "not a host"]);
  assert.equal(bad.status, 1);
  assert.equal(bad.stdout, "");
  assert.equal(bad.stderr.trim(), "DEPLOY_RESOLVE_INPUT_INVALID");
  const unknown = run(["--app", "nope", "--hostname", HOST]);
  assert.equal(unknown.status, 1);
  assert.equal(fs.readFileSync(envFile, "utf8"), "");
  fs.rmSync(dir, { recursive: true, force: true });
});
