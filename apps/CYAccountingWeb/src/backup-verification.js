import { createR2BackupStorageProvider } from './r2-backup-provider.js';
import { createGcsBackupStorageProvider } from './gcs-backup-provider.js';
import { validateBackupSetBytes } from './gcs-backup.js';
import { backupPackageDigest } from './backup-package.js';
import { authenticateBackupVerifier } from './backup-verification-auth.js';
import metadata from './build-metadata.json' with { type:'json' };
// GitHub scheduled runs start hours late (observed 3–6.5 h). The external check
// starts at 03:45 and may claim the job until 12:00 Taipei; only then does the
// Worker fallback begin (user 2026-11-10).
const EXTERNAL_CLAIM_UNTIL = '12:00:00';
const externalDeadline = now => new Date(`${workDay(now)}T${EXTERNAL_CLAIM_UNTIL}+08:00`).toISOString();

// Operational metadata/snapshots live outside backup packages and the ledger.
// One CAS-protected owner controls both verification channels and notifications.
const ROOT = 'CYAccountingWebOperations/verification/';
export const VERIFICATION_STATE_KEY = `${ROOT}active.json`;
export const VERIFICATION_CRON = '*/5 * * * *';
const PROVIDERS = ['cloudflare_r2', 'google_cloud_storage'];
const MAX_BYTES = 8 * 1024 * 1024; // Existing restore source bound, not a new data limit.
const LEASE_MS = 120_000;
const PARTS = ['manifest', 'data'];

async function readState(env) {
  const object = await env.BACKUP_R2.get(VERIFICATION_STATE_KEY);
  return { state: object ? JSON.parse(await object.text()) : null, etag: object?.etag };
}
async function saveState(env, state, etag) {
  const saved = await env.BACKUP_R2.put(VERIFICATION_STATE_KEY, JSON.stringify(state), {
    onlyIf: etag ? { etagMatches: etag } : { etagDoesNotMatch: '*' }
  });
  if (!saved) throw Object.assign(new Error('VERIFICATION_CONFLICT'), { code:'VERIFICATION_CONFLICT' });
  return saved.etag;
}
function day(now) { return new Date(now.getTime() + 8 * 3600000).toISOString().slice(0,10); }
function workDay(now) { return day(new Date(now.getTime() - 3.5 * 3600000)); }
function workspace(state, provider) { return `${ROOT}${state.id}/${provider}/`; }
function adapter(env, name, options) {
  return name === PROVIDERS[0] ? options.r2Provider || createR2BackupStorageProvider(env)
    : options.gcsProvider || createGcsBackupStorageProvider(env, options.gcsProviderOptions);
}
function done(state) { return state.external.status === 'success' || state.fallback.status === 'success'; }

async function startState(env, now, previous, options) {
  const sources = [];
  for (const provider of PROVIDERS) {
    let row = null;
    try {
      if (options.databaseUnavailable) throw new Error('unavailable');
      row = await env.DB.prepare(`SELECT bs.*, bc.status AS copy_status FROM backup_sets bs
        LEFT JOIN backup_copies bc ON bc.backup_id=bs.backup_id AND bc.provider=?
        WHERE (?='cloudflare_r2' OR bs.gcs_expected=1) ORDER BY bs.created_at DESC LIMIT 1`)
        .bind(provider, provider).first();
    } catch { sources.push({ provider, code:'BACKUP_DATABASE_UNAVAILABLE' }); continue; }
    let code = !row ? 'BACKUP_SOURCE_MISSING' : row.copy_status !== 'success' ? 'BACKUP_COPY_FAILED' : '';
    if (row && !code && (now.getTime() - Date.parse(row.created_at) > (provider === PROVIDERS[0] ? 36 : 120) * 3600000
      || Date.parse(row.created_at) > now.getTime() + 300000
      || provider === PROVIDERS[0] && day(new Date(row.created_at)) !== workDay(now))) code = 'BACKUP_COPY_STALE';
    sources.push({ provider, code, backupId:row?.backup_id, packageSha256:row?.package_sha256,
      dataSha256:row?.data_sha256, manifestSha256:row?.manifest_sha256,
      schemaVersion:Number(row?.schema_version || 0), rowCount:Number(row?.record_count || 0),
      dataBytes:Number(row?.data_byte_length || 0), manifestBytes:Number(row?.manifest_byte_length || 0) });
  }
  return { id:crypto.randomUUID(), day:workDay(now), createdAt:now.toISOString(), sources,
    lastSuccessAt:done(previous || {external:{},fallback:{}}) ? previous.completedAt : previous?.lastSuccessAt || null,
    external:{ status:'waiting', deadline:externalDeadline(now) },
    fallback:{ status:'waiting', providerIndex:0, phase:'download', part:0, step:0, failures:0, rows:0 },
    lease:null, incidentId:null, emailSentAt:null };
}

async function acquire(env, now, options = {}) {
  let { state, etag } = await readState(env);
  if (state?.lease?.until > now.toISOString()) return null;
  const terminal = state && (done(state) || state.external.status === 'failed' && state.fallback.status === 'failed');
  if (!state || state.day !== workDay(now) && terminal
    || options.prepare && !options.databaseUnavailable && state.sources.some(source => source.code)
      && state.external.status !== 'running') {
    // Retire only this finished job's isolated namespace. No backup objects or
    // bucket-wide scans. A live, progressing job continues across midnight.
    if (state) await cleanWorkspace(env, state);
    state = await startState(env, now, state, options);
  }
  if (state.lease?.kind === 'fallback') {
    state.fallback.failures++;
    if (state.fallback.failures >= 3) {
      state.fallback.status = 'failed'; state.fallback.code = 'VERIFICATION_STALLED';
    }
  }
  state.lease = { token:crypto.randomUUID(), kind:options.leaseKind || 'metadata', until:new Date(now.getTime() + LEASE_MS).toISOString() };
  etag = await saveState(env, state, etag);
  return { state, etag };
}

async function cleanWorkspace(env, state) {
  const prefix = `${ROOT}${state.id}/`;
  const listed = await env.BACKUP_R2.list({ prefix, limit:1000 });
  const keys = (listed.objects || []).map(object => object.key).filter(key => key.startsWith(prefix));
  if (keys.length) await env.BACKUP_R2.delete(keys);
}

export async function runBackupVerification(env, options = {}) {
  try { return await runCoordinator(env, options); }
  catch (error) {
    if (error?.code === 'VERIFICATION_CONFLICT') return {skipped:true,reason:'check_in_progress'};
    return coordinatorUnavailable(env, options.now instanceof Date ? options.now : new Date(), !options.prepare);
  }
}

async function runCoordinator(env, options) {
  const now = options.now instanceof Date ? options.now : new Date();
  // Idle wake-ups perform only one small R2 GET: no D1 scan, export or SQLite.
  const existing = await readState(env);
  if (existing.state?.day === workDay(now) && done(existing.state)) return healthModel(existing.state, now);
  if (existing.state?.day === workDay(now) && existing.state.emailSentAt
    && existing.state.external.status === 'failed' && existing.state.fallback.status === 'failed') return healthModel(existing.state, now);
  if (existing.state?.external.status === 'waiting' && existing.state.external.deadline > now.toISOString()
    && !options.prepare) return healthModel(existing.state, now);
  try {
    if (!options.databaseUnavailable) {
      const hold = await env.DB.prepare("SELECT value FROM meta WHERE key='recovery_hold'").first();
      if (hold?.value === '1') return { skipped:true, reason:'recovery_workspace' };
    }
  } catch { options = { ...options, databaseUnavailable:true }; }
  const lock = await acquire(env, now, { ...options, leaseKind:options.prepare ? 'metadata' : 'fallback' });
  if (!lock) return { skipped:true, reason:'check_in_progress' };
  const { state } = lock;
  if (options.prepare) {
    state.lease = null;
    await saveState(env, state, lock.etag);
    return healthModel(state, now);
  }
  if (state.external.status !== 'success' && state.external.deadline <= now.toISOString()) {
    state.external.status = 'failed'; state.external.code = 'EXTERNAL_CHECK_INCOMPLETE';
  }
  if (state.external.status === 'failed' && !['success','failed'].includes(state.fallback.status)) {
    state.fallback.status = 'running';
    try {
      await advanceFallback(env, state, options);
      state.fallback.failures = 0;
      state.fallback.lastProgressAt = now.toISOString();
    } catch {
      state.fallback.failures++;
      if (state.fallback.failures >= 3) {
        state.fallback.status = 'failed'; state.fallback.code = 'FALLBACK_CHECK_FAILED';
      }
    }
  }
  if (done(state)) state.completedAt = now.toISOString();
  state.lease = null;
  const etag = await saveState(env, state, lock.etag);
  if (state.fallback.obsolete) await env.BACKUP_R2.delete(state.fallback.obsolete);
  await maybeNotify(env, state, etag, now);
  if (!options.databaseUnavailable && (done(state) || state.fallback.status === 'failed')) await publishHealthProjection(env,state,now);
  return healthModel(state, now);
}

// The existing D1 health row is only a small status projection / outage counter,
// never another job/checkpoint owner. If R2 itself is down, both channels cannot
// use their coordinator; avoid losing the independent CYID notification route.
async function publishHealthProjection(env,state,now) {
  try {
    const health=healthModel(state,now);
    await env.DB.prepare(`UPDATE backup_health_state SET status=?,checked_at=?,details_json=? WHERE id=1`)
      .bind(health.verified?'healthy':'failed',now.toISOString(),JSON.stringify({coordinator:'r2',day:state.day,health})).run();
  } catch { /* A completed R2 state remains authoritative during a D1 outage. */ }
}
async function coordinatorUnavailable(env,now,scheduled) {
  let prior;
  try {
    const row=await env.DB.prepare('SELECT details_json FROM backup_health_state WHERE id=1').first();
    prior=JSON.parse(row?.details_json || '{}');
  } catch { /* No reliable proof of channel state: show the outage without inventing failure. */ }
  const unavailable={status:'warning',issues:[{provider:'',code:'VERIFICATION_STATE_UNAVAILABLE'}],verified:false,emailSent:false,emailPending:false};
  if (!scheduled) {
    if (prior?.coordinator==='r2' && prior.health
      && now.getTime()-Date.parse(prior.health.checkedAt)<=36*3600000) return {...prior.health,status:'warning',issues:unavailable.issues};
    if (prior?.coordinator==='unavailable' && prior.day===workDay(now) && prior.failures>=3
      && now.toISOString()>=externalDeadline(now)) {
      return {...unavailable,status:'failed',emailSent:Boolean(prior.emailSent),emailPending:!prior.emailSent};
    }
    return unavailable;
  }
  if (prior?.coordinator==='r2' && prior.day===workDay(now) && prior.health?.verified) return {...unavailable,verified:true};
  if (!prior) return unavailable;
  const slot=Math.floor(now.getTime()/300000);
  const consecutive=prior.coordinator==='unavailable' && prior.day===workDay(now) ? prior.failures+(prior.slot===slot?0:1) : 1;
  const failed=consecutive>=3 && now.toISOString()>=externalDeadline(now);
  const record={coordinator:'unavailable',day:workDay(now),slot,failures:consecutive,
    incidentId:prior.coordinator==='unavailable' && prior.day===workDay(now) ? prior.incidentId : crypto.randomUUID(),emailSent:prior.day===workDay(now) && Boolean(prior.emailSent)};
  const persist=()=>env.DB.prepare("UPDATE backup_health_state SET status=?,checked_at=?,details_json=? WHERE id=1")
    .bind(failed?'failed':'unknown',now.toISOString(),JSON.stringify(record)).run();
  try {
    await persist();
    if (failed && !record.emailSent) {
      const result=await env.BACKUP_NOTIFICATIONS.notifySuperAdmin({workspaceId:String(env.CYID_WORKSPACE_ID||''),
        applicationId:String(env.CYID_APPLICATION_ID||''),type:'backup_health_failed',eventId:record.incidentId});
      record.emailSent=['delivered','already_delivered'].includes(result?.status);
      await persist();
    }
  } catch { /* Retry the same incident after storage / notification recovers. */ }
  return {...unavailable,status:failed?'failed':'warning',emailSent:record.emailSent,emailPending:failed&&!record.emailSent};
}

async function advanceFallback(env, state, options) {
  if (options.databaseUnavailable) throw new Error('BACKUP_DATABASE_UNAVAILABLE');
  const f = state.fallback, source = state.sources[f.providerIndex];
  if (source.code) throw new Error(source.code);
  const prefix = workspace(state, source.provider);
  if (f.phase === 'download') {
    const part = PARTS[f.part], key = `${prefix}${crypto.randomUUID()}-${part}.json`;
    const stream = await adapter(env, source.provider, options).getObjectStream(`CYAccountingWeb/${source.backupId}/${part}.json`);
    await env.BACKUP_R2.put(key, stream);
    f.parts = { ...f.parts, [part]:key };
    if (++f.part === PARTS.length) f.phase = 'validate';
    return;
  }
  if (f.phase === 'validate') {
    const manifestBytes = await workspaceBytes(env, f.parts.manifest);
    const dataBytes = await workspaceBytes(env, f.parts.data);
    const validated = await validateBackupSetBytes(manifestBytes, dataBytes, source.backupId);
    if (await backupPackageDigest({manifestBytes,dataBytes}) !== source.packageSha256
      || validated.dataSha256 !== source.dataSha256 || validated.manifestSha256 !== source.manifestSha256) throw new Error('RESTORE_CATALOG_MISMATCH');
    f.phase = 'restore'; return;
  }
  const backup = {
    manifest:JSON.parse(new TextDecoder().decode(await workspaceBytes(env, f.parts.manifest))),
    data:JSON.parse(new TextDecoder().decode(await workspaceBytes(env, f.parts.data)))
  };
  const snapshot = f.snapshot ? await workspaceBytes(env, f.snapshot, false) : null;
  const advance = options.advanceRestore || (await import('./backup-restore-runtime.js')).advanceRestoreSnapshot;
  const result = await advance(snapshot, backup, f.cursor);
  if (result.proof) {
    await recordProof(env, state, 'fallback', source, result.proof);
    f.providerIndex++;
    f.obsolete = f.snapshot;
    f.snapshot = null; f.cursor = null; f.phase = 'download'; f.part = 0; f.parts = null;
    if (f.providerIndex === state.sources.length) f.status = 'success';
  } else {
    // Copy-on-write, unique per attempt. A killed / late invocation never
    // overwrites the committed snapshot, and CAS publishes its cursor last.
    const key = `${prefix}${crypto.randomUUID()}.sqlite`;
    await env.BACKUP_R2.put(key, result.snapshot);
    const previous = f.snapshot;
    f.snapshot = key; f.cursor = result.cursor; f.rows += result.written || 0; f.step++;
    // The prior committed snapshot is retained until the new state is durable.
    f.obsolete = previous || null;
  }
}

async function workspaceBytes(env, key, bounded = true) {
  const object = await env.BACKUP_R2.get(key);
  if (!object || bounded && object.size > MAX_BYTES) throw new Error('RESTORE_SIZE_OR_SOURCE');
  return new Uint8Array(await object.arrayBuffer());
}

async function recordProof(env, state, channel, source, proof) {
  if (!proof?.fullRowRoundtrip || !proof.integrityCheck || !proof.foreignKeyCheck
    || !proof.isolated || proof.liveAccountingModified !== false
    || proof.rowCount !== source.rowCount || proof.sourceSchemaVersion !== source.schemaVersion
    || proof.targetSchemaVersion !== metadata.schemaVersion) throw new Error('RESTORE_PROOF_INCOMPLETE');
  const id = `${state.id}:${channel}:${source.provider}`;
  await env.DB.prepare(`INSERT OR IGNORE INTO backup_restore_checks(id,attempt_id,backup_id,provider,package_sha256,
    source_schema_version,target_schema_version,status,result_json,actor_json,created_at) VALUES(?,?,?,?,?,?,?,'success',?,?,?)`)
    .bind(id, state.id, source.backupId, source.provider, source.packageSha256, source.schemaVersion, proof.targetSchemaVersion,
      JSON.stringify({ isolated:true, liveAccountingModified:false, fullRowRoundtrip:true, integrityCheck:true,
        foreignKeyCheck:true, rowCount:proof.rowCount, targetSchemaVersion:proof.targetSchemaVersion }),
      JSON.stringify({type:'scheduled_backup_health',channel}), new Date().toISOString()).run();
}

async function maybeNotify(env, state, etag, now) {
  if (done(state) || state.external.status !== 'failed' || state.fallback.status !== 'failed' || state.emailSentAt) return;
  state.incidentId ||= state.id;
  // Save the incident before delivery; CYID deduplicates this exact event ID.
  etag = await saveState(env, state, etag);
  try {
    const result = await env.BACKUP_NOTIFICATIONS.notifySuperAdmin({
      workspaceId:String(env.CYID_WORKSPACE_ID || ''), applicationId:String(env.CYID_APPLICATION_ID || ''),
      type:'backup_health_failed', eventId:state.incidentId
    });
    if (['delivered','already_delivered'].includes(result?.status)) state.emailSentAt = now.toISOString();
  } catch { /* Retry the same event on the next wake-up, never log private errors. */ }
  await saveState(env, state, etag);
}

export async function readVerificationHealth(env, now = new Date()) {
  try { return healthModel((await readState(env)).state, now); }
  catch { return coordinatorUnavailable(env,now,false); }
}
function healthModel(state, now) {
  if (!state) return { status:'unknown', issues:[], emailSent:false, emailPending:false };
  if (done(state) && now.getTime() - Date.parse(state.completedAt) > 36 * 3600000) {
    return {status:'warning',checkedAt:state.completedAt,verified:false,
      issues:[{provider:'',code:'BACKUP_HEALTH_OVERDUE'}],emailSent:false,emailPending:false};
  }
  const externalIncomplete = state.external.status === 'failed' || state.external.status !== 'success' && state.external.deadline <= now.toISOString();
  const stalled = state.fallback.status === 'failed' || state.fallback.failures >= 2 && state.lease?.until <= now.toISOString();
  const healthy = done(state);
  return { status:healthy ? externalIncomplete ? 'warning' : 'healthy' : externalIncomplete && stalled ? 'failed' : externalIncomplete ? 'warning' : 'unknown',
    checkedAt:state.completedAt || state.fallback.lastProgressAt || state.createdAt,
    externalStatus:externalIncomplete ? 'incomplete' : state.external.status,
    fallbackStatus:state.fallback.status, processedRows:state.fallback.rows,
    verified:healthy, issues:healthy && !externalIncomplete ? [] : externalIncomplete
      ? [{provider:'',code:stalled && !healthy ? 'BOTH_CHECKS_FAILED' : 'EXTERNAL_CHECK_INCOMPLETE'}] : [],
    emailSent:Boolean(state.emailSentAt), emailPending:Boolean(state.incidentId && !state.emailSentAt) };
}

export async function handleVerificationApi(request, env, options = {}) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/verification/')) return null;
  let identity;
  try { identity = await authenticateBackupVerifier(request, env, options); }
  catch { return response({ok:false,code:'VERIFIER_UNAUTHORIZED'}, 403); }
  const now = options.now instanceof Date ? options.now : new Date();
  try {
    const hold = await env.DB.prepare("SELECT value FROM meta WHERE key='recovery_hold'").first();
    if (hold?.value === '1') return response({ok:false,code:'RECOVERY_WORKSPACE_HOLD'},409);
    if (url.pathname === '/api/backup/verification/start' && request.method === 'POST') {
      const lock = await acquire(env, now, options);
      if (!lock) return response({ok:false,code:'VERIFICATION_BUSY'},409);
      const { state } = lock;
      if (done(state)) {
        state.lease = null; await saveState(env,state,lock.etag);
        return response({ok:true,complete:true});
      }
      state.external = {status:'running',run:identity.run,deadline:new Date(now.getTime()+20*60000).toISOString()};
      state.lease = null;
      await saveState(env,state,lock.etag);
      return response({ok:true,jobId:state.id,sources:state.sources});
    }
    const { state } = await readState(env);
    if (!state || state.external.run !== identity.run || url.searchParams.get('job') !== state.id) return response({ok:false,code:'VERIFICATION_JOB_MISMATCH'},409);
    if (done(state)) return response({ok:true,complete:true});
    const source = state.sources.find(item => item.provider === url.searchParams.get('provider'));
    const part = url.searchParams.get('part');
    if (url.pathname === '/api/backup/verification/source' && request.method === 'GET' && source && PARTS.includes(part)) {
      if (source.code) return response({ok:false,code:'VERIFICATION_SOURCE_UNAVAILABLE'},409);
      const stream = await adapter(env, source.provider, options).getObjectStream(`CYAccountingWeb/${source.backupId}/${part}.json`);
      return new Response(stream,{headers:{'content-type':'application/octet-stream','cache-control':'no-store','x-content-type-options':'nosniff'}});
    }
    if (url.pathname === '/api/backup/verification/complete' && request.method === 'POST') {
      // Only small allow-listed evidence is accepted, never rows / raw errors.
      const text = await request.text();
      if (text.length > 8192) return response({ok:false,code:'VERIFICATION_RESULT_INVALID'},400);
      const body = JSON.parse(text);
      if (!Array.isArray(body.results)) return response({ok:false,code:'VERIFICATION_RESULT_INVALID'},400);
      const lock = await acquire(env,now,options);
      if (!lock) return response({ok:false,code:'VERIFICATION_BUSY'},409);
      const active = lock.state;
      if (active.id !== state.id || active.external.run !== identity.run) {
        active.lease = null;
        await saveState(env,active,lock.etag);
        return response({ok:false,code:'VERIFICATION_JOB_MISMATCH'},409);
      }
      let success = body.results.length === active.sources.length;
      for (const item of active.sources) {
        const result = body.results.find(result => result?.provider === item.provider);
        if (item.code || result?.status !== 'success' || result.packageSha256 !== item.packageSha256
          || result.dataSha256 !== item.dataSha256 || result.manifestSha256 !== item.manifestSha256) { success = false; continue; }
        try { await recordProof(env,active,'external',item,result.proof); }
        catch { success = false; }
      }
      active.external.status = success ? 'success' : 'failed';
      active.external.code = success ? '' : 'EXTERNAL_CHECK_FAILED';
      if (success) active.completedAt = now.toISOString();
      active.lease = null;
      const etag = await saveState(env,active,lock.etag);
      await maybeNotify(env,active,etag,now);
      if (success || active.fallback.status==='failed') await publishHealthProjection(env,active,now);
      return response({ok:true,status:active.external.status});
    }
    return response({ok:false,code:'VERIFICATION_ROUTE_NOT_FOUND'},404);
  } catch { return response({ok:false,code:'VERIFICATION_REQUEST_FAILED'},409); }
}
function response(body,status=200) { return new Response(JSON.stringify(body),{status,headers:{'content-type':'application/json','cache-control':'no-store'}}); }
