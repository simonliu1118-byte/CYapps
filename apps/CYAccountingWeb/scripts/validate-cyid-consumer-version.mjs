import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(here, "..");
const cyidRoot = path.resolve(appRoot, "..", "CYCloudIdentity");

function readVersion(file) {
  const value = fs.readFileSync(file, "utf8").trim();
  if (!/^\d+\.\d+\.\d+$/.test(value)) {
    throw new Error(`Invalid version in ${file}: ${JSON.stringify(value)}`);
  }
  return {
    text: value,
    parts: value.split(".").map(Number),
  };
}

function compare(a, b) {
  for (let i = 0; i < 3; i += 1) {
    if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  }
  return 0;
}

const adopted = readVersion(path.join(appRoot, "CYID_CONSUMER_VERSION"));
const minimum = readVersion(path.join(cyidRoot, "CONSUMER_MIN_COMPATIBLE_VERSION"));
const current = readVersion(path.join(cyidRoot, "CONSUMER_CONTRACT_VERSION"));
const standard = fs.readFileSync(
  path.join(cyidRoot, "docs", "CONSUMER_INTEGRATION_STANDARD.md"),
  "utf8",
);
const projectRules = fs.readFileSync(path.join(appRoot, "PROJECT_RULES.md"), "utf8");

if (compare(minimum.parts, current.parts) > 0) {
  throw new Error(
    `CYID minimum compatible version ${minimum.text} exceeds current contract ${current.text}`,
  );
}
if (
  compare(adopted.parts, minimum.parts) < 0
  || compare(adopted.parts, current.parts) > 0
) {
  throw new Error(
    `CYAccountingWeb CYID_CONSUMER_VERSION ${adopted.text} is outside provider support window ${minimum.text}..${current.text}`,
  );
}
if (!standard.includes(`Contract version:** \`${current.text}\``)) {
  throw new Error("CYID Consumer Integration Standard current-version header is inconsistent.");
}
if (!standard.includes(`Minimum compatible consumer version:** \`${minimum.text}\``)) {
  throw new Error("CYID Consumer Integration Standard minimum-version header is inconsistent.");
}
if (!projectRules.includes("CONSUMER_INTEGRATION_STANDARD.md")) {
  throw new Error("CYAccountingWeb PROJECT_RULES does not adopt the CYID Consumer Integration Standard.");
}

console.log(
  `CYID consumer contract supported: CYAccountingWeb=${adopted.text}, CYID=${minimum.text}..${current.text}`,
);
