import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

/**
 * 部署識別碼解析器（CYapps 共用）。
 *
 * 本倉庫與其 Actions 紀錄皆為公開；runner 會在任何 ::add-mask:: 之前以明文印出每個步驟的 env 區塊，
 * 因此部署識別碼（Worker／D1／R2／服務名稱、應用程式與工作區 ID、網址）不得經由 GitHub variables 傳入。
 * 本腳本只收 Cloudflare 憑證與「原始碼中已公開的主機名稱」，從 Cloudflare 查出目前服務該主機名稱的
 * Worker 及其 bindings，先 ::add-mask:: 再寫入 GITHUB_ENV。
 *
 * 各應用程式的差異以 SPECS 描述（binding 名稱／型別／欄位對應），錯誤碼固定且不含任何識別碼。
 */
const workerName = /^[a-z0-9][a-z0-9._-]{1,62}$/i;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hostname = /^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;
const bucket = /^[a-z0-9][a-z0-9_-]{1,61}[a-z0-9]$/;
const applicationId = /^[A-Z0-9][A-Z0-9_-]{1,63}$/;
const workspaceId = /^[A-Za-z0-9][A-Za-z0-9._:-]{4,79}$/;
const databaseName = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
const httpsRoot = value => { try { const u = new URL(value); return u.protocol === "https:" && !!u.hostname && !u.username && !u.password && !u.search && !u.hash && u.pathname === "/"; } catch { return false; } };
const httpsUrl = value => { try { const u = new URL(value); return u.protocol === "https:" && !!u.hostname && !u.username && !u.password && !u.search && !u.hash; } catch { return false; } };
const fail = code => { throw new Error(code); };

/**
 * d1:      { binding, idEnv, nameEnv }                         D1 binding；以 D1 API 驗證 uuid 並取得名稱
 * services:[{ binding, env }]                                  service binding（必須不是 Worker 自己）
 * r2:      [{ binding, env }]                                  R2 binding 的 bucket_name
 * texts:   [{ binding, env, test, optional? }]                 plain_text 變數（optional：允許空字串）
 * markers: [{ binding, test }]                                 只驗證、不輸出；證明該主機名稱確實是本應用程式的 Worker
 */
export const SPECS = {
  cypricedesk: {
    workerEnv: "CF_WORKER_NAME",
    d1: { binding: "DB", idEnv: "CF_D1_DATABASE_ID", nameEnv: "CF_D1_DATABASE_NAME" },
    services: [{ binding: "IDENTITY", env: "CF_IDENTITY_SERVICE" }],
    r2: [],
    texts: [
      { binding: "CYID_APPLICATION_ID", env: "CF_CYID_APPLICATION_ID", test: v => applicationId.test(v) },
      { binding: "CYID_WORKSPACE_ID", env: "CF_CYID_WORKSPACE_ID", test: v => workspaceId.test(v) },
      { binding: "CYWEB_PORTAL_URL", env: "CF_CYWEB_PORTAL_URL", test: httpsRoot },
      { binding: "CYPD_FETCHER_URL", env: "CF_CYPD_FETCHER_URL", test: httpsUrl, optional: true },
    ],
    // CYPriceDesk 的 wrangler 範本一定會宣告這四個 vars；缺少代表該主機名稱不是本應用程式。
    markers: [
      { binding: "CYPD_FETCHER_URL", test: v => v === "" || httpsUrl(v) },
      { binding: "CYWEB_PORTAL_URL", test: httpsRoot },
    ],
  },
  cyaccountingweb: {
    workerEnv: "CF_WORKER_NAME",
    d1: { binding: "DB", idEnv: "CF_D1_DATABASE_ID", nameEnv: "CF_D1_DATABASE_NAME" },
    // CF_IDENTITY_SERVICE 與 CYID 應用程式／工作區 ID 由 resolve-cyid-production-runtime.mjs 負責。
    services: [],
    r2: [{ binding: "BACKUP_R2", env: "CF_R2_BACKUP_BUCKET" }],
    texts: [],
    markers: [
      { binding: "BACKUP_TOPOLOGY", test: v => ["legacy_gcs", "parallel_dual_provider"].includes(v) },
      { binding: "BACKUP_VERIFY_REPOSITORY_ID", test: v => /^\d{1,20}$/.test(v) },
    ],
  },
};

export async function resolveDeployRuntime({ spec, accountId, apiToken, publicHostname, fetchImpl = fetch }) {
  if (!spec || !accountId || !apiToken || !hostname.test(publicHostname ?? "")) fail("DEPLOY_RESOLVE_INPUT_INVALID");
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(accountId)}`;
  async function read(resource) {
    const response = await fetchImpl(api + resource, { method: "GET", redirect: "error",
      headers: { authorization: `Bearer ${apiToken}` }, signal: AbortSignal.timeout(20000) });
    const data = await response.json().catch(() => null);
    if (!response.ok || data?.success !== true)
      fail(`DEPLOY_RESOLVE_RESOURCE_UNAVAILABLE ${resource.split("/")[1].split("?")[0]} HTTP ${response.status} cf=${(data?.errors ?? []).map(e => e.code).join(",")}`);
    return data.result;
  }

  const domains = (await read(`/workers/domains?hostname=${encodeURIComponent(publicHostname)}`)) ?? [];
  const served = domains.filter(row => row.hostname === publicHostname && row.enabled !== false
    && [undefined, "production"].includes(row.environment));
  if (served.length !== 1 || !workerName.test(served[0].service ?? "")) fail("DEPLOY_RESOLVE_DOMAIN_UNVERIFIED");
  const worker = served[0].service;

  const bindings = (await read(`/workers/scripts/${encodeURIComponent(worker)}/settings`))?.bindings ?? [];
  const only = (name, type) => {
    const found = bindings.filter(b => b.name === name && b.type === type);
    if (found.length > 1) fail("DEPLOY_RESOLVE_BINDING_AMBIGUOUS " + name);
    return found[0] ?? null;
  };
  // 只有 binding 名稱（程式碼中公開）會出現在錯誤訊息，值絕不出現。
  const text = name => { const b = only(name, "plain_text"); return b === null ? null : String(b.text ?? "").trim(); };

  for (const marker of spec.markers) {
    const value = text(marker.binding);
    if (value === null || !marker.test(value)) fail("DEPLOY_RESOLVE_WORKER_NOT_EXPECTED_APP");
  }

  const values = { [spec.workerEnv]: worker };

  const db = only(spec.d1.binding, "d1");
  if (!uuid.test(db?.id ?? "")) fail("DEPLOY_RESOLVE_DB_UNBOUND");
  const database = await read(`/d1/database/${encodeURIComponent(db.id)}`);
  if (database?.uuid !== db.id || !databaseName.test(database?.name ?? "")) fail("DEPLOY_RESOLVE_DB_METADATA_MISMATCH");
  values[spec.d1.idEnv] = db.id;
  values[spec.d1.nameEnv] = database.name;

  for (const item of spec.services) {
    const service = only(item.binding, "service")?.service;
    if (!workerName.test(service ?? "") || service === worker) fail("DEPLOY_RESOLVE_SERVICE_UNBOUND " + item.binding);
    values[item.env] = service;
  }
  for (const item of spec.r2) {
    const name = only(item.binding, "r2_bucket")?.bucket_name;
    if (!bucket.test(name ?? "")) fail("DEPLOY_RESOLVE_BUCKET_UNBOUND " + item.binding);
    values[item.env] = name;
  }
  for (const item of spec.texts) {
    const value = text(item.binding);
    if (value === null || (value === "" && item.optional)) { if (item.optional) { values[item.env] = ""; continue; } fail("DEPLOY_RESOLVE_TEXT_UNBOUND " + item.binding); }
    if (!item.test(value)) fail("DEPLOY_RESOLVE_TEXT_INVALID " + item.binding);
    values[item.env] = value;
  }

  // wrangler 會印出 workers.dev 網址，其中含帳號子網域。
  const subdomain = (await read("/workers/subdomain"))?.subdomain ?? "";
  if (!/^[a-z0-9-]{1,63}$/i.test(subdomain)) fail("DEPLOY_RESOLVE_SUBDOMAIN_UNAVAILABLE");

  const masks = new Set([subdomain]);
  for (const value of Object.values(values)) {
    if (!value) continue;
    masks.add(value);
    // wrangler 會把 vars 的完整值印在 bindings 表中；網址另遮蔽其主機名稱。
    if (/^https:\/\//.test(value)) masks.add(new URL(value).host);
  }
  return { values, masks: [...masks] };
}

/** 先遮蔽、後寫入 GITHUB_ENV；順序由測試鎖定。 */
export function publishResolved({ values, masks }, githubEnv, log = console.log) {
  for (const value of masks) log("::add-mask::" + value);
  fs.appendFileSync(githubEnv, Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join(""), "utf8");
}

async function cli() {
  try {
    const arg = name => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : undefined; };
    if (!process.env.GITHUB_ENV) fail("DEPLOY_RESOLVE_RUNNER_REQUIRED");
    const { values, masks } = await resolveDeployRuntime({ spec: SPECS[arg("--app")], accountId: process.env.CLOUDFLARE_ACCOUNT_ID,
      apiToken: process.env.CLOUDFLARE_API_TOKEN, publicHostname: arg("--hostname") });
    publishResolved({ values, masks }, process.env.GITHUB_ENV);
    console.log(`DEPLOY_RUNTIME_RESOLVED: ${Object.keys(values).join(", ")}`);
  } catch (error) {
    const message = error?.message ?? "";
    console.error(/^DEPLOY_RESOLVE_[A-Z_]+( [A-Za-z0-9_ =,.-]{1,120})?$/.test(message) ? message : "DEPLOY_RESOLVE_UNAVAILABLE");
    process.exitCode = 1;
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) await cli();
