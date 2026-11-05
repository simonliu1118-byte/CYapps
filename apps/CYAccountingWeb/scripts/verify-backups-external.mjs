// Production data is processed only in memory. Never log responses, errors,
// backup identifiers, digests or rows; this repository's Actions logs are public.
import { readFileSync, readdirSync } from 'node:fs';
import initSqlJs from 'sql.js';
import { validateBackupSetBytes } from '../src/gcs-backup.js';
import { backupPackageDigest } from '../src/backup-package.js';
import { verifyRestoreIntoEmptySqlite } from '../src/backup-restore.js';

const write = process.stdout.write.bind(process.stdout);
console.log = console.warn = console.error = () => {};
process.on('uncaughtException', () => { write('Backup verification interrupted.\n'); process.exit(1); });
process.on('unhandledRejection', () => { write('Backup verification interrupted.\n'); process.exit(1); });
const MAX_BYTES = 8 * 1024 * 1024;

async function request(origin, path, init = {}) {
  // Fetch a fresh job-bound token instead of storing long-lived cloud keys.
  const oidc = new URL(process.env.ACTIONS_ID_TOKEN_REQUEST_URL);
  oidc.searchParams.set('audience', origin);
  const tokenResponse = await fetch(oidc, {
    headers:{authorization:`Bearer ${process.env.ACTIONS_ID_TOKEN_REQUEST_TOKEN}`},
    signal:AbortSignal.timeout(30000)
  });
  if (!tokenResponse.ok) throw new Error('AUTH_FAILED');
  const { value } = await tokenResponse.json();
  if (!value) throw new Error('AUTH_FAILED');
  for (let attempt=0; attempt<4; attempt++) {
    const result = await fetch(origin + path, { ...init, redirect:'error',
      headers:{...init.headers,authorization:`Bearer ${value}`}, signal:AbortSignal.timeout(60000) });
    if (result.status === 409 && attempt < 3) {
      await result.body?.cancel();
      await new Promise(resolve => setTimeout(resolve, 45000));
      continue;
    }
    if (!result.ok) { await result.body?.cancel(); throw new Error('REQUEST_FAILED'); }
    return result;
  }
}

async function readSource(origin, path) {
  const response = await request(origin,path), reader = response.body.getReader();
  const chunks = []; let length=0;
  try {
    while (true) {
      const {done,value} = await reader.read(); if (done) break;
      length += value.byteLength;
      if (length > MAX_BYTES) throw new Error('SOURCE_TOO_LARGE');
      chunks.push(value);
    }
  } finally { await reader.cancel(); }
  const bytes = new Uint8Array(length); let offset=0;
  for (const chunk of chunks) { bytes.set(chunk,offset); offset+=chunk.byteLength; }
  return bytes;
}

async function main() {
  const origin = new URL(`https://${process.env.CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN}`).origin;
  const job = await (await request(origin,'/api/backup/verification/start',{method:'POST'})).json();
  if (job.complete) { write('Backup verification already complete.\n'); return; }
  const SQL = await initSqlJs({print(){},printErr(){}});
  const root = new URL('../migrations/',import.meta.url);
  const schemas = readdirSync(root).filter(name => name.endsWith('.sql')).sort()
    .map(name => readFileSync(new URL(name,root),'utf8'));
  const results = [];
  for (const source of job.sources) {
    let result = {provider:source.provider,status:'failed'};
    if (!source.code) try {
      const query = new URLSearchParams({job:job.jobId,provider:source.provider});
      const manifestBytes = await readSource(origin,`/api/backup/verification/source?${query}&part=manifest`);
      const dataBytes = await readSource(origin,`/api/backup/verification/source?${query}&part=data`);
      const backup = await validateBackupSetBytes(manifestBytes,dataBytes,source.backupId);
      const packageSha256 = await backupPackageDigest({manifestBytes,dataBytes});
      if (packageSha256 !== source.packageSha256 || backup.dataSha256 !== source.dataSha256
        || backup.manifestSha256 !== source.manifestSha256) throw new Error('SOURCE_CHANGED');
      // Each provider gets a fresh isolated SQLite DB, never the live D1.
      const db = new SQL.Database();
      try {
        const proof = await verifyRestoreIntoEmptySqlite(db,schemas,backup);
        result = {provider:source.provider,status:'success',packageSha256,
          dataSha256:backup.dataSha256,manifestSha256:backup.manifestSha256,
          proof:{isolated:proof.isolated,liveAccountingModified:proof.liveAccountingModified,
            fullRowRoundtrip:proof.fullRowRoundtrip,integrityCheck:proof.integrityCheck,
            foreignKeyCheck:proof.foreignKeyCheck,rowCount:proof.rowCount,
            sourceSchemaVersion:proof.sourceSchemaVersion,targetSchemaVersion:proof.targetSchemaVersion}};
      } finally { db.close(); }
    } catch { /* Only a fixed failure status crosses the public-log boundary. */ }
    results.push(result);
  }
  const complete = await (await request(origin,`/api/backup/verification/complete?job=${encodeURIComponent(job.jobId)}`,
    {method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({results})})).json();
  if (complete.complete) { write('Backup verification already complete.\n'); return; }
  write(complete.status === 'success' ? 'Backup verification passed.\n' : 'Backup verification incomplete; fallback will continue.\n');
  if (complete.status !== 'success') process.exitCode=1;
}
main().catch(() => { write('Backup verification incomplete; fallback will continue.\n'); process.exitCode=1; });
