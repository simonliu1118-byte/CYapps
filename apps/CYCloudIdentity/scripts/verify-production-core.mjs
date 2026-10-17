import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { parseCyidProductionSettings, verifyCyWebProductionPortal } from "../../CYAccountingWeb/scripts/resolve-cyid-production-runtime.mjs";

// Reuse the canonical managed-domain -> Worker -> Binding/Workspace/core App/
// declared consumer/health guard, without needing a duplicate protected target.
export async function verifyProductionCore({ provider, workerName, account, token, consumerVersion, minimumVersion = fs.readFileSync(new URL("../CONSUMER_MIN_COMPATIBLE_VERSION", import.meta.url), "utf8").trim(), fetchImpl }) {
  const runtime = parseCyidProductionSettings(provider, workerName);
  const portal = await verifyCyWebProductionPortal({ account, token, runtime, provider, fetchImpl });
  const response = await fetchImpl(new URL("/api/health", portal));
  const health = await response.json();
  const adopted = health.data?.identityConsumerVersion;
  const parse = value => typeof value === "string" && /^\d+\.\d+\.\d+$/.test(value)
    ? value.split(".").map(Number) : null;
  const lower = parse(minimumVersion), upper = parse(consumerVersion), actual = parse(adopted);
  const compare = (a, b) => a.reduce((result, part, index) => result || Math.sign(part - b[index]), 0);
  if (!response.ok || health?.ok !== true || !lower || !upper || !actual
      || compare(lower, upper) > 0 || compare(actual, lower) < 0 || compare(actual, upper) > 0)
    throw new Error("PRODUCTION_CORE_CONSUMER_NOT_READY");
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) try {
  await verifyProductionCore({
    provider: JSON.parse(fs.readFileSync(process.env.RUNNER_TEMP + "/cyid-prod-settings.json", "utf8")),
    workerName: process.env.PROD_CYID_WORKER_NAME, account: process.env.CLOUDFLARE_ACCOUNT_ID,
    token: process.env.CLOUDFLARE_API_TOKEN,
    consumerVersion: fs.readFileSync("CONSUMER_CONTRACT_VERSION", "utf8").trim(),
    minimumVersion: fs.readFileSync("CONSUMER_MIN_COMPATIBLE_VERSION", "utf8").trim(),
    fetchImpl: (url, init = {}) => fetch(url, { ...init, signal: AbortSignal.timeout(10000), redirect: "error" }),
  });
  console.log("Canonical production portal authority, Workspace, core App and supported consumer contract verified.");
} catch {
  console.error("PRODUCTION_CORE_CONSUMER_NOT_READY");
  process.exitCode = 1;
}
