import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CYACC_ROOT = path.resolve(HERE, '..');
const CYID_ROOT = path.resolve(CYACC_ROOT, '..', 'CYCloudIdentity');

function readVersion(file) {
  return fs.readFileSync(file, 'utf8').trim();
}

function parseVersion(value) {
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(value);
  if (!match) throw new Error(`Invalid consumer contract version: ${value}`);
  return match.slice(1).map(Number);
}

function compare(a, b) {
  for (let i = 0; i < 3; i += 1) {
    if (a[i] !== b[i]) return a[i] - b[i];
  }
  return 0;
}

const adoptedRaw = readVersion(path.join(CYACC_ROOT, 'CYID_CONSUMER_VERSION'));
const currentRaw = readVersion(path.join(CYID_ROOT, 'CONSUMER_CONTRACT_VERSION'));
const minimumRaw = readVersion(path.join(CYID_ROOT, 'CONSUMER_MIN_COMPATIBLE_VERSION'));
const adopted = parseVersion(adoptedRaw);
const current = parseVersion(currentRaw);
const minimum = parseVersion(minimumRaw);

if (compare(adopted, minimum) < 0) {
  throw new Error(`CYAccountingWeb CYID consumer version ${adoptedRaw} is below provider minimum ${minimumRaw}.`);
}
if (compare(adopted, current) > 0) {
  throw new Error(`CYAccountingWeb CYID consumer version ${adoptedRaw} is newer than provider contract ${currentRaw}.`);
}

const projectRules = fs.readFileSync(path.join(CYACC_ROOT, 'PROJECT_RULES.md'), 'utf8');
if (!projectRules.includes('CONSUMER_INTEGRATION_STANDARD.md')) throw new Error('PROJECT_RULES.md must reference the canonical CYID consumer standard.');
if (!projectRules.includes('USER') || !projectRules.includes('Excel')) throw new Error('PROJECT_RULES.md must preserve CYACC USER read-only/export authorization.');
if (!projectRules.includes('獨立登入')) throw new Error('PROJECT_RULES.md must preserve the standalone login decision.');

console.log(`CYID consumer compatibility passed: adopted=${adoptedRaw}, supported=${minimumRaw}..${currentRaw}`);
