import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {createHash} from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { checkSettings, checkMigrations, fingerprintRows, checkContinuity, checkHealth, snapshot, main, activeDeployment, bindingState, deployReviewedVersion, readCyIdPreparation, prepareCyIdConfig, verifyPreparedCyId, providerConfiguration, resolveCyIdPreparation, resolveActiveCyId, preservedDeployConfig } from '../scripts/deployment-preflight.mjs';

const activeId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
let activeReads = 0;
const active = await activeDeployment(async route => {
  assert.ok(!route.endsWith('/settings')); // Latest upload may be an undeployed probe.
  if (route.endsWith('/deployments')) { activeReads++; return {deployments:[{versions:[{version_id:activeId,percentage:100}]}]}; }
  return {id:activeId,bindings:[{type:'secret_text',name:'SECRET',text:'PRIVATE-DO-NOT-COPY'},{type:'d1',name:'DB',id:'original'}]};
},'synthetic-consumer');
assert.equal(activeReads,2);
assert.equal(JSON.stringify(active).includes('PRIVATE-DO-NOT-COPY'),false);
assert.equal(active.settings.bindings.find(b=>b.name==='DB').id,'original');
for (const kind of ['gradual','changed','wrong-version','missing-bindings']) {
  let n=0;
  await assert.rejects(activeDeployment(async route => {
    if (route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:kind==='changed' && ++n>1 ? 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' : activeId,percentage:kind==='gradual' ? 50 : 100}]}]};
    return {id:kind==='wrong-version' ? 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' : activeId,bindings:kind==='missing-bindings' ? undefined : []};
  },'synthetic-consumer'), /SINGLE_ACTIVE_DEPLOYMENT_REQUIRED|ACTIVE_DEPLOYMENT_CHANGED|ACTIVE_VERSION_BINDINGS_REQUIRED/);
}

const config = { d1_databases: [{ database_id: 'synthetic-consumer-db' }],
  ratelimits: [{ name: 'LOGIN', namespace_id: '1', simple: { limit: 12, period: 60 } }] };
const settings = { bindings: [
  { name: 'APP_ENV', type: 'plain_text', text: 'development' },
  { name: 'API_VERSION', type: 'plain_text', text: '1' },
  { name: 'DB', type: 'd1', id: 'synthetic-consumer-db' },
  { name: 'LOGIN', type: 'ratelimit', namespace_id: 1, simple: { limit: 12, period: 60 } },
  ...['BOOTSTRAP_KEY', 'OTP_PEPPER', 'EMAIL_FROM', 'BREVO_API_KEY'].map(name => ({ name, type: 'secret_text' })),
] };
assert.equal(checkSettings(settings, config).APP_ENV, 'development');
for (const [replacement, error] of [
  [{ name: 'APP_ENV', type: 'plain_text', text: 'production' }, 'DEVELOPMENT_ENVIRONMENT_REQUIRED'],
  [{ name: 'DB', type: 'd1', id: 'another-db' }, 'ORIGINAL_DATABASE_BINDING_REQUIRED'],
  [{ name: 'LOGIN', type: 'ratelimit', namespace_id: 2, simple: { limit: 12, period: 60 } }, 'RATE_LIMIT_BINDING_CHANGED'],
]) {
  const changed = structuredClone(settings);
  changed.bindings[changed.bindings.findIndex(b => b.name === replacement.name)] = replacement;
  assert.throws(() => checkSettings(changed, config), new RegExp(error));
}
for (const flag of [{ type: 'plain_text', text: 'true' }, { type: 'plain_text', text: 'unknown' }, { type: 'secret_text' }])
  assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'CYID_ENABLED', ...flag }] }, config), /BUILT_IN_AUTHORITY_REQUIRED/);
assert.equal(checkSettings({ bindings: [...settings.bindings, { name: 'CYID_ENABLED', type: 'plain_text', text: 'false' }] }, config).CYID_ENABLED, 'false');
assert.throws(() => checkSettings({ bindings: settings.bindings.filter(b => b.name !== 'OTP_PEPPER') }, config), /RUNTIME_SECRETS_REQUIRED/);
assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'KV', type: 'kv_namespace' }] }, config), /UNREVIEWED_RESOURCE_BINDING/);
assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'SECOND_DB', type: 'd1', id: 'other' }] }, config), /ORIGINAL_DATABASE_BINDING_REQUIRED/);
assert.throws(() => checkSettings({ bindings: settings.bindings.filter(b => b.name !== 'LOGIN') }, config), /RATE_LIMIT_BINDINGS_REQUIRED/);

const migrations = Array.from({ length: 12 }, (_, i) => `${String(i + 1).padStart(4, '0')}_existing.sql`).concat('0013_cyid_invitation_actor.sql');
assert.deepEqual(checkMigrations(migrations.slice(0, 12), migrations), ['0013_cyid_invitation_actor.sql']);
assert.deepEqual(checkMigrations(migrations, migrations, true), []);
assert.throws(() => checkMigrations(migrations.slice(0, 11), migrations), /UNREVIEWED_PENDING_MIGRATIONS/);
assert.throws(() => checkMigrations([...migrations, '0014_unreviewed.sql'], migrations), /MIGRATION_HISTORY_NOT_CANONICAL_PREFIX/);
assert.throws(() => checkMigrations(migrations.slice(1), migrations), /MIGRATION_HISTORY_NOT_CANONICAL_PREFIX/);
assert.throws(() => checkMigrations(migrations.slice(0, 12), migrations, true), /MIGRATION_NOT_COMPLETE/);
// 3.0's additive D1 SQL may be present in source while the original D1
// is still at storage13. Read-only preflight can inspect it, but a deploy
// or CYID_ONLY run must NEVER pass with an unapplied migration.
const v30Canonical = migrations.concat(
  '0014_invitation_recovery_metadata.sql','0015_company_settings.sql');
assert.deepEqual(checkMigrations(v30Canonical.slice(0, 12), v30Canonical),
  v30Canonical.slice(12));
assert.deepEqual(checkMigrations(v30Canonical.slice(0, 13), v30Canonical),
  v30Canonical.slice(13));
assert.deepEqual(checkMigrations(v30Canonical.slice(0, 14), v30Canonical),
  ['0015_company_settings.sql']);
assert.throws(() => checkMigrations(v30Canonical.slice(0, 13), v30Canonical, true),
  /MIGRATION_NOT_COMPLETE/);
assert.deepEqual(checkMigrations(v30Canonical,v30Canonical,true),[]);
const v31Canonical=[...v30Canonical,'0016_non_allowance_work_items.sql'];
assert.deepEqual(checkMigrations(v30Canonical,v31Canonical),['0016_non_allowance_work_items.sql']);
assert.throws(()=>checkMigrations(v30Canonical,v31Canonical,true),/MIGRATION_NOT_COMPLETE/);
assert.deepEqual(checkMigrations(v31Canonical,v31Canonical,true),[]);
const v32Canonical=[...v31Canonical,'0017_allowance_applications.sql',
  '0018_allowance_frozen_ledger.sql'];
assert.deepEqual(checkMigrations(v31Canonical,v32Canonical),
  ['0017_allowance_applications.sql','0018_allowance_frozen_ledger.sql']);
assert.deepEqual(checkMigrations(v32Canonical.slice(0,-1),v32Canonical),
  ['0018_allowance_frozen_ledger.sql']);
assert.throws(()=>checkMigrations(v31Canonical,v32Canonical,true),/MIGRATION_NOT_COMPLETE/);
assert.deepEqual(checkMigrations(v32Canonical,v32Canonical,true),[]);
assert.throws(()=>checkMigrations(v31Canonical,[...v32Canonical,'0019_unreviewed.sql']),
  /UNREVIEWED_PENDING_MIGRATIONS/);
assert.throws(()=>checkMigrations(v30Canonical,[...v31Canonical,'0017_unreviewed.sql']),/UNREVIEWED_PENDING_MIGRATIONS/);
assert.throws(() => checkMigrations(v30Canonical.slice(0, 12),
  [...v30Canonical,'0016_unreviewed.sql']),/UNREVIEWED_PENDING_MIGRATIONS/);
assert.throws(() => checkMigrations(migrations.slice(0, 12),
  [...migrations,'0014_unreviewed.sql','0015_company_settings.sql']),
  /UNREVIEWED_PENDING_MIGRATIONS/);

const fingerprints = fingerprintRows([{ id: 'device_synthetic', token_hash: 'synthetic_hash', status: 'active' }], 'id');
assert.equal(JSON.stringify(fingerprints).includes('device_synthetic'), false);
assert.equal(JSON.stringify(fingerprints).includes('synthetic_hash'), false);
assert.deepEqual(fingerprints, fingerprintRows([{ status: 'active', token_hash: 'synthetic_hash', id: 'device_synthetic' }], 'id'));
assert.throws(() => fingerprintRows(Array.from({ length: 10001 }, (_, i) => ({ id: String(i) })), 'id'), /AUDIT_ROW_LIMIT_EXCEEDED/);
assert.throws(() => fingerprintRows([{ id: 'duplicate' }, { id: 'duplicate' }], 'id'), /AUDIT_ID_DUPLICATED/);

const statements = [];
const before = await snapshot(async sql => {
  statements.push(sql);
  if (sql === 'PRAGMA foreign_key_check' || sql.startsWith('SELECT name FROM sqlite_master')) return [];
  const id = sql.match(/^SELECT ([a-z_]+)/)[1];
  return [{ [id]: 'synthetic_original', workspace_id: 'synthetic_workspace' }];
});
assert.ok(statements.every(sql => sql.startsWith('SELECT ') || sql === 'PRAGMA foreign_key_check'));
assert.ok(statements.every(sql => !sql.includes('credential_verifier') && !sql.includes('email_normalized')));
checkContinuity(before, structuredClone(before));
const appended = structuredClone(before);
appended.security_audit_events.newEvent = 'newHash';
checkContinuity(before, appended); // Ordinary new audit events do not erase history.
for (const table of Object.keys(before).filter(name => name !== 'storageProfile')) {
  const changed = structuredClone(before);
  changed[table][Object.keys(changed[table])[0]] = 'changed';
  assert.throws(() => checkContinuity(before, changed), /HISTORY_CHANGED_/);
}
await assert.rejects(snapshot(async sql => sql === 'PRAGMA foreign_key_check' ? [{ table: 'broken' }] : []), /FOREIGN_KEY_CHECK_FAILED/);

// A CYID-only D1 has intentionally retired Built-in account tables. Preserve
// its profile fingerprint but reject unexpected table leftovers and deployments
// that try to cross between profiles without an explicit maintenance gate.
const retired = await snapshot(async sql => {
  if (sql === "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'cyid_storage_profile'")
    return [{name:'cyid_storage_profile'}];
  if (sql.startsWith("SELECT name FROM sqlite_master WHERE type = 'table' AND name IN")) return [];
  if (sql.startsWith('SELECT profile, workspace_id, retired_at FROM cyid_storage_profile'))
    return [{profile:'CYID_ONLY',workspace_id:'synthetic_workspace',retired_at:'2026-10-13'}];
  if (sql === 'PRAGMA foreign_key_check') return [];
  const id = sql.match(/^SELECT ([a-z_]+)/)?.[1];
  assert.ok(id && !sql.includes('cloud_employees'), 'retired CYID query touched legacy Employee table');
  return [{[id]:'synthetic_original',workspace_id:'synthetic_workspace'}];
});
assert.equal(retired.storageProfile, 'CYID_ONLY');
assert.equal(Object.hasOwn(retired, 'cloud_employees'), false);
checkContinuity(retired, structuredClone(retired));
assert.throws(() => checkContinuity(before, retired), /STORAGE_PROFILE_CHANGED_UNREVIEWED/);
assert.throws(() => checkContinuity(retired, before), /STORAGE_PROFILE_CHANGED_UNREVIEWED/);
await assert.rejects(snapshot(async sql => {
  if (sql.endsWith("'cyid_storage_profile'")) return [{name:'cyid_storage_profile'}];
  if (sql.includes("AND name IN ('cloud_employees'")) return [{name:'cloud_employees'}];
  if (sql.startsWith('SELECT profile, workspace_id, retired_at FROM cyid_storage_profile'))
    return [{profile:'CYID_ONLY',workspace_id:'synthetic_workspace',retired_at:'2026-10-13'}];
  return [];
}), /CYID_LEGACY_TABLES_STILL_PRESENT/);


const health = { ok: true, storage: 'ok', service: 'cyinvoice-cloud', environment: 'development', apiVersion: '1',
  schemaVersion: '8', storageSchemaVersion: '13', cloudVersion: '0.9.3',
  capabilities: ['device-revoke-v1', 'device-self-status-v1', 'device-usage-v1', 'device-rename-v1', 'cyid-consumer-v1', 'runtime-sync-v1'] };
checkHealth(health, { version: '0.9.3', schema: 13 });
assert.throws(() => checkHealth({ ...health, cloudVersion: '0.9.0' }, { version: '0.9.3', schema: 13 }), /CLOUD_SOURCE_VERSION_MISMATCH/);
assert.throws(() => checkHealth({ ...health, capabilities: health.capabilities.slice(0, -1) }, { version: '0.9.3', schema: 13 }), /CLOUD_CAPABILITIES_MISSING/);
for (const field of ['environment', 'apiVersion', 'schemaVersion', 'storageSchemaVersion', 'storage'])
  assert.throws(() => checkHealth({ ...health, [field]: 'wrong' }, { version: '0.9.3', schema: 13 }), /CLOUD_HEALTH_CONTRACT_INVALID/);

// Exercise the deployment comparison across the actual table-rebuilding migration.
const db = new DatabaseSync(':memory:');
const canonical = fs.readdirSync('migrations').filter(n => n.endsWith('.sql')).sort();
for (const name of canonical.slice(0, 12)) db.exec(fs.readFileSync(`migrations/${name}`, 'utf8'));
db.exec(`
  INSERT INTO workspaces (workspace_id, display_name) VALUES ('workspace_synthetic', 'Synthetic');
  INSERT INTO devices (device_id, workspace_id, display_name, token_hash, status)
    VALUES ('device_synthetic', 'workspace_synthetic', 'Synthetic', 'synthetic_token_hash', 'active');
  INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name, email_normalized, role)
    VALUES ('employee_synthetic', 'workspace_synthetic', '0001', 'Synthetic', 'owner@example.test', 'SUPER_ADMIN');
  INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    delivery_state, expires_at, created_at) VALUES ('invitation_synthetic', 'workspace_synthetic', 'synthetic_code_hash',
    'device_synthetic', 'employee_synthetic', 'sent', '2026-10-12', '2026-10-11');
`);
const query = async sql => db.prepare(sql).all();
const schema12 = await snapshot(query);
db.exec(fs.readFileSync(`migrations/${canonical[12]}`, 'utf8'));
const schema13 = await snapshot(query);
checkContinuity(schema12, schema13);
assert.equal(db.prepare('SELECT issued_by_cyid_employee_id FROM device_invitations').get().issued_by_cyid_employee_id, null);
// Built-in issuance remains valid after 0013; external actors never overwrite history.
db.exec(`INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
  delivery_state, expires_at, created_at) VALUES ('invitation_after', 'workspace_synthetic', 'after_code_hash',
  'device_synthetic', 'employee_synthetic', 'pending', '2026-10-12', '2026-10-11')`);
checkContinuity(schema12, await snapshot(query));
db.exec("UPDATE devices SET token_hash='unexpected_replacement' WHERE device_id='device_synthetic'");
assert.throws(() => checkContinuity(schema12, undefined), /AUDIT_SNAPSHOT_INVALID/);
assert.throws(() => checkContinuity(schema12, { ...schema13, devices: fingerprintRows(db.prepare('SELECT device_id, workspace_id, token_hash, status, revoked_at, employee_authority_state FROM devices').all(), 'device_id') }), /HISTORY_CHANGED_DEVICES/);

// Exact-source inheritance must bypass a poisoned latest upload without secret reads.
const deployedId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const deploymentNow = Date.now();
const validConfig = {...config, name:'synthetic-consumer', compatibility_date:'2026-09-20',
  vars:{APP_ENV:'development',API_VERSION:'1',SCHEMA_VERSION:'13'},
  services:[{binding:'IDENTITY',service:'synthetic-provider',entrypoint:'default'}],
  d1_databases:[{database_id:'11111111-1111-4111-8111-111111111111'}]};
const validAudit = {worker:validConfig.name,databaseId:validConfig.d1_databases[0].database_id,
  capturedAt:new Date(deploymentNow).toISOString(),retainedProbeCount:1,
  deployments:{deployments:[{versions:[{version_id:activeId,percentage:100}]}]},
  settings:{bindings:[...settings.bindings.filter(b=>b.name!=='DB'),
    {name:'DB',type:'d1',id:validConfig.d1_databases[0].database_id},
    {name:'IDENTITY',type:'service',service:'synthetic-provider',entrypoint:'default'},
    {name:'CUSTOM_POLICY',type:'plain_text',text:'preserved-policy'}]},
  routes:[],workerSubdomain:{enabled:true,previews_enabled:true}};
for (const scenario of ['success','missing-secret','extra-probe-secret','wrong-db','wrong-service','wrong-rate-limit',
  'wrong-date','wrong-flags','changed-before-upload','changed-before-deploy','routes-changed','lost-upload-response','lost-deploy-response']) {
  const checkpoint=structuredClone(validAudit), operations=[];
  let current=activeId, uploadBody, saved, posts=0;
  const request=async (route,body) => {
    operations.push({route,body});
    if (body && route.endsWith('?deploy=false')) {
      posts++; uploadBody=body;
      assert.equal(saved.uploadAttempted,true);
      if (scenario==='lost-upload-response') throw new Error('RESPONSE_LOST');
      assert.ok(body.bindings.filter(b=>b.type==='inherit').every(b=>b.version_id===activeId));
      assert.deepEqual(body.bindings.filter(b=>b.type==='inherit').map(b=>b.name).sort(),
        validAudit.settings.bindings.filter(b=>b.type!=='plain_text').map(b=>b.name).sort());
      assert.equal(body.bindings.some(b=>b.type==='secret_text' || b.name==='PROBE_AUTH_KEY'),false);
      assert.equal(body.modules.length,1); assert.equal(body.main_module,'app.js');
      return {id:deployedId};
    }
    if (body && route.endsWith('/deployments')) {
      posts++; assert.equal(saved.uploadedVersionId,deployedId);
      assert.deepEqual(body,{strategy:'percentage',versions:[{version_id:deployedId,percentage:100}]});
      current=deployedId;
      if (scenario==='lost-deploy-response') throw new Error('RESPONSE_LOST');
      return {versions:body.versions};
    }
    if (route.endsWith('/deployments')) {
      const id=scenario==='changed-before-upload' || (scenario==='changed-before-deploy' && uploadBody) ? deployedId : current;
      return {deployments:[{versions:[{version_id:id,percentage:100}]}]};
    }
    if (route.endsWith('/routes')) return scenario==='routes-changed' ? [{pattern:'unexpected.example.test/*'}] : [];
    if (route.endsWith('/subdomain')) return validAudit.workerSubdomain;
    if (route.endsWith('/versions/'+activeId)) return {id:activeId,bindings:validAudit.settings.bindings};
    if (route.endsWith('/versions/'+deployedId)) {
      const bindings=uploadBody ? uploadBody.bindings.map(b=>b.type==='inherit' ? structuredClone(validAudit.settings.bindings.find(x=>x.name===b.name)) : b) : validAudit.settings.bindings;
      if (scenario==='missing-secret') bindings.splice(bindings.findIndex(b=>b.name==='OTP_PEPPER'),1);
      if (scenario==='extra-probe-secret') bindings.push({name:'PROBE_AUTH_KEY',type:'secret_text'});
      if (scenario==='wrong-db') bindings.find(b=>b.name==='DB').id='22222222-2222-4222-8222-222222222222';
      if (scenario==='wrong-service') bindings.find(b=>b.name==='IDENTITY').service='unexpected-provider';
      if (scenario==='wrong-rate-limit') bindings.find(b=>b.name==='LOGIN').simple.limit++;
      return {id:deployedId,bindings,compatibility_date:scenario==='wrong-date'?'2020-01-01':validConfig.compatibility_date,
        compatibility_flags:scenario==='wrong-flags'?['nodejs_compat']:[]};
    }
    throw new Error('UNEXPECTED_TEST_REQUEST');
  };
  const action=()=>deployReviewedVersion(checkpoint,validConfig,'export default {fetch(){return new Response("synthetic");}}',request,
    value=>{saved=structuredClone(value);},deploymentNow);
  if (scenario==='success') {
    await action(); assert.equal(posts,2); assert.equal(current,deployedId);
    assert.equal(uploadBody.bindings.find(b=>b.name==='CUSTOM_POLICY').text,'preserved-policy');
    assert.equal(uploadBody.bindings.find(b=>b.name==='CYID_ENABLED').text,'false');
  } else {
    await assert.rejects(action(),/UPLOADED_BINDINGS_MISMATCH|ACTIVE_DEPLOYMENT_CHANGED|WORKER_ROUTES_CHANGED|RESPONSE_LOST/);
    assert.equal(posts,scenario==='lost-deploy-response'?2:['changed-before-upload','routes-changed'].includes(scenario)?0:1);
  }
  if (saved) await assert.rejects(deployReviewedVersion(saved,validConfig,'export default {};',request,()=>{},deploymentNow),/DEPLOYMENT_ALREADY_ATTEMPTED/);
  assert.ok(!operations.some(op=>op.route.includes('/latest') || op.route.endsWith('/settings') || op.route.includes('/secrets')));
  assert.ok(operations.every(op=>!op.body || op.route.endsWith('?deploy=false') || op.route.endsWith('/deployments')));
}
for (const [patch,error] of [[{capturedAt:'invalid'},'AUDIT_SNAPSHOT_EXPIRED'],
  [{capturedAt:new Date(deploymentNow-21*60*1000).toISOString()},'AUDIT_SNAPSHOT_EXPIRED'],
  [{worker:'other-consumer'},'AUDIT_SCOPE_MISMATCH']]) {
  await assert.rejects(deployReviewedVersion({...validAudit,...patch},validConfig,'export default {};',()=>assert.fail('must not call API'),()=>{},deploymentNow),new RegExp(error));
}
assert.throws(()=>checkSettings({bindings:[...settings.bindings,settings.bindings[0]]},config),/BINDING_NAMES_INVALID/);
assert.equal(JSON.stringify(bindingState([{type:'secret_text',name:'KEY',text:'DO-NOT-STORE'}])).includes('DO-NOT-STORE'),false);

// Prepare real Consumer bindings with Built-in retained; all fixtures stay local.
const preparation = {CYID_WORKER_NAME:'synthetic-provider', IDENTITY_APPLICATION_ID:'CYINVOICE',
  IDENTITY_WORKSPACE_ID:'identity_workspace_synthetic', IDENTITY_CYINVOICE_WORKSPACE_ID:'workspace_synthetic'};
const providerDatabase='33333333-3333-4333-8333-333333333333';
const providerSettings={bindings:[{name:'APP_ENV',type:'plain_text',text:'development'},
  {name:'API_VERSION',type:'plain_text',text:'v1'},{name:'DB',type:'d1',id:providerDatabase},
  {name:'CYINVOICE_APPLICATION_ID',type:'plain_text',text:'CYINVOICE'},
  {name:'CYINVOICE_WORKSPACE_ID',type:'plain_text',text:'identity_workspace_synthetic'}]};
const hash=value=>createHash('sha256').update(JSON.stringify(value)).digest('hex');
const providerCheckpoint={worker:'synthetic-provider',versionId:activeId,databaseId:providerDatabase,
  bindingDigest:hash(bindingState(providerSettings.bindings))};
assert.deepEqual(resolveCyIdPreparation({settings:providerSettings},'synthetic-provider',[{workspace_id:'workspace_synthetic',status:'active'}]),preparation);
const missingScope={bindings:providerSettings.bindings.filter(b=>!b.name.startsWith('CYINVOICE_'))};
assert.equal(providerConfiguration(missingScope).scope,null);
assert.throws(()=>resolveCyIdPreparation({settings:missingScope},'synthetic-provider',[{workspace_id:'workspace_synthetic',status:'active'}]),/CYID_CONSUMER_SCOPE_REQUIRED/);
assert.equal(providerConfiguration({bindings:[...missingScope.bindings,{name:'CYACC_APPLICATION_ID',type:'plain_text',text:'CYACC'},{name:'CYACC_WORKSPACE_ID',type:'plain_text',text:'other_workspace'}]}).scope,null);
for(const workspaces of [[],[{workspace_id:'one',status:'disabled'}],[{workspace_id:'one',status:'active'},{workspace_id:'two',status:'active'}]])
  assert.throws(()=>resolveCyIdPreparation({settings:providerSettings},'synthetic-provider',workspaces),/SINGLE_ORIGINAL_WORKSPACE_REQUIRED/);
for(const patch of [
  {name:'APP_ENV',type:'plain_text',text:'production'},
  {name:'API_VERSION',type:'plain_text',text:'unknown'},
  {name:'DB',type:'d1',id:'invalid'},
  {name:'CYINVOICE_APPLICATION_ID',type:'secret_text'},
  {name:'CYINVOICE_WORKSPACE_ID',type:'plain_text',text:'bad workspace'},
]) assert.throws(()=>providerConfiguration({bindings:providerSettings.bindings.map(b=>b.name===patch.name?patch:b)}),/CYID_PROVIDER_ENVIRONMENT_INVALID|CYID_PROVIDER_DATABASE_BINDING_INVALID|CYID_CONSUMER_SCOPE_INVALID/);
assert.throws(()=>providerConfiguration({bindings:providerSettings.bindings.filter(b=>b.name!=='CYINVOICE_WORKSPACE_ID')}),/CYID_CONSUMER_SCOPE_INVALID/);
assert.throws(()=>providerConfiguration({bindings:[...providerSettings.bindings,providerSettings.bindings[0]]}),/CYID_PROVIDER_SETTINGS_INVALID/);
const preparationAudit = {...structuredClone(validAudit),state:{workspaces:fingerprintRows([{workspace_id:'workspace_synthetic'}],'workspace_id')},cyIdPreparation:preparation,provider:providerCheckpoint};
preparationAudit.settings.bindings = preparationAudit.settings.bindings.filter(b=>b.name!=='IDENTITY');
preparationAudit.settings.bindings.push({name:'UNRELATED',type:'service',service:'synthetic-other'});
const preparationBase = {...validConfig,services:[{binding:'UNRELATED',service:'synthetic-other'}]};
const preparedConfig = prepareCyIdConfig(preparationBase,preparationAudit.settings,preparation,preparationAudit.state.workspaces);
assert.equal(preparedConfig.vars.CYID_ENABLED,'false');
assert.equal(preparedConfig.services.length,3);
for (const key of Object.keys(preparation)) for (const value of ['',undefined])
  assert.throws(()=>readCyIdPreparation({...preparation,[key]:value}),/CYID_PREPARATION_SETTINGS_REQUIRED/);
for (const patch of [{IDENTITY_APPLICATION_ID:'lowercase'}, {IDENTITY_WORKSPACE_ID:' space '},
  {IDENTITY_WORKSPACE_ID:'x'.repeat(81)}, {CYID_WORKER_NAME:'https://wrong.example.test'}])
  assert.throws(()=>readCyIdPreparation({...preparation,...patch}),/CYID_PREPARATION_SETTINGS_INVALID/);
assert.throws(()=>prepareCyIdConfig(preparationBase,preparationAudit.settings,{...preparation,CYID_WORKER_NAME:preparationBase.name},preparationAudit.state.workspaces),/CYID_PROVIDER_TARGET_INVALID/);
assert.throws(()=>prepareCyIdConfig(preparationBase,preparationAudit.settings,{...preparation,IDENTITY_CYINVOICE_WORKSPACE_ID:'unknown_workspace'},preparationAudit.state.workspaces),/CYID_ORIGINAL_WORKSPACE_REQUIRED/);
for (const conflicting of [
  {name:'IDENTITY',type:'service',service:'wrong-provider'},
  {name:'IDENTITY_AUTHORITY',type:'service',service:'synthetic-provider'},
  {name:'IDENTITY',type:'secret_text'},
  {name:'IDENTITY',type:'service',service:'synthetic-provider',environment:'staging'},
  {name:'IDENTITY_APPLICATION_ID',type:'plain_text',text:'OTHER'},
  {name:'IDENTITY_WORKSPACE_ID',type:'secret_text'},
]) assert.throws(()=>prepareCyIdConfig(preparationBase,{bindings:[...preparationAudit.settings.bindings,conflicting]},preparation,preparationAudit.state.workspaces),/CYID_SERVICE_BINDING_CONFLICT|CYID_SCOPE_BINDING_CONFLICT/);
for (const scenario of ['new','existing','partial','wrong-readback','scope-drift','unreviewed-service','unreviewed-scope','provider-changed-before-upload','provider-changed-before-deploy']) {
  const checkpoint=structuredClone(preparationAudit);
  if (scenario==='existing' || scenario==='partial') checkpoint.settings.bindings.push(...preparedConfig.services
    .filter(b=>scenario==='existing'?b.binding!=='UNRELATED':b.binding==='IDENTITY')
    .map(({binding,...b})=>({name:binding,type:'service',...b})));
  let desired=prepareCyIdConfig(preparationBase,checkpoint.settings,preparation,checkpoint.state.workspaces);
  if (scenario==='scope-drift') desired.vars.IDENTITY_WORKSPACE_ID='changed_workspace';
  if (scenario==='unreviewed-service') delete checkpoint.cyIdPreparation;
  if (scenario==='unreviewed-scope') {
    delete checkpoint.cyIdPreparation;
    desired={...preparationBase,vars:{...validConfig.vars,IDENTITY_APPLICATION_ID:'CYINVOICE'}};
  }
  let current=activeId, uploaded, posts=0;
  const request=async (route,body)=>{
    if (route.includes('/synthetic-provider/')) {
      if (route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:activeId,percentage:100}]}]};
      const bindings=structuredClone(providerSettings.bindings);
      if(scenario==='provider-changed-before-upload' || (scenario==='provider-changed-before-deploy' && uploaded)) bindings.find(b=>b.name==='CYINVOICE_WORKSPACE_ID').text='changed_workspace';
      return {id:activeId,bindings};
    }
    if (body && route.endsWith('?deploy=false')) {
      posts++; uploaded=body;
      const originalResources=checkpoint.settings.bindings.filter(b=>b.type!=='plain_text');
      assert.deepEqual(body.bindings.filter(b=>b.type==='inherit').map(b=>b.name).sort(),originalResources.map(b=>b.name).sort());
      assert.ok(body.bindings.filter(b=>b.type==='inherit').every(b=>b.version_id===activeId));
      assert.equal(body.bindings.filter(b=>b.type==='service').length,scenario==='existing'?0:scenario==='partial'?1:2);
      assert.equal(body.bindings.some(b=>b.type==='secret_text' || b.name==='PROBE_AUTH_KEY'),false);
      assert.equal(body.bindings.find(b=>b.name==='CYID_ENABLED').text,'false');
      return {id:deployedId};
    }
    if (body && route.endsWith('/deployments')) {posts++;current=deployedId;return {};}
    if (route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:current,percentage:100}]}]};
    if (route.endsWith('/routes')) return checkpoint.routes;
    if (route.endsWith('/subdomain')) return checkpoint.workerSubdomain;
    if (route.endsWith('/versions/'+activeId)) return {id:activeId,bindings:checkpoint.settings.bindings};
    if (route.endsWith('/versions/'+deployedId)) {
      const bindings=uploaded.bindings.map(b=>b.type==='inherit'?structuredClone(checkpoint.settings.bindings.find(x=>x.name===b.name)):structuredClone(b));
      if (scenario==='wrong-readback') bindings.find(b=>b.name==='IDENTITY_AUTHORITY').entrypoint='default';
      return {id:deployedId,bindings,compatibility_date:desired.compatibility_date,compatibility_flags:[]};
    }
    throw new Error('UNEXPECTED_TEST_REQUEST');
  };
  const action=()=>deployReviewedVersion(checkpoint,desired,'export default {};',request,()=>{},deploymentNow);
  if (['new','existing','partial'].includes(scenario)) {await action();assert.equal(posts,2);}
  else {await assert.rejects(action(),/UPLOADED_BINDINGS_MISMATCH|CYID_SCOPE_BINDING_CONFLICT|UNREVIEWED_SERVICE_BINDING_CHANGE|CYID_SCOPE_PREPARATION_REQUIRED|CYID_PROVIDER_CHANGED/);assert.equal(posts,['wrong-readback','provider-changed-before-deploy'].includes(scenario)?1:0);}
}

// Explicit activation changes only the flag, after preparation, without authority SQL.
const activationSettings = {bindings:[...structuredClone(preparationAudit.settings.bindings),
  ...preparedConfig.services.filter(b=>b.binding!=='UNRELATED').map(({binding,...b})=>({name:binding,type:'service',...b})),
  ...Object.entries(preparedConfig.vars).map(([name,text])=>({name,type:'plain_text',text}))]};
// The fixture base already contains APP_ENV/API_VERSION; retain one binding per name.
activationSettings.bindings = [...new Map(activationSettings.bindings.map(b=>[b.name,b])).values()];
verifyPreparedCyId(preparedConfig,activationSettings,preparation,preparationAudit.state.workspaces);
for(const name of ['IDENTITY','IDENTITY_AUTHORITY',...Object.keys(preparation).filter(n=>n!=='CYID_WORKER_NAME')])
  assert.throws(()=>verifyPreparedCyId(preparedConfig,{bindings:activationSettings.bindings.filter(b=>b.name!==name)},
    preparation,preparationAudit.state.workspaces),/CYID_PREPARATION_REQUIRED/);
assert.equal(checkSettings({bindings:activationSettings.bindings.map(b=>b.name==='CYID_ENABLED'?{...b,text:'true'}:b)},
  preparedConfig,{allowCyId:true}).CYID_ENABLED,'true');
for(const scenario of ['success','unapproved-mode','missing-preparation','changed-variable','wrong-flag',
  'provider-before-upload','provider-before-deploy','lost-upload','lost-deploy']) {
  const checkpoint={...structuredClone(preparationAudit),operation:'activate-cyid',settings:structuredClone(activationSettings),
    cyIdActivation:preparation};
  delete checkpoint.cyIdPreparation;
  const desired={...preparedConfig,vars:{...preparedConfig.vars,CYID_ENABLED:'true'}};
  if(scenario==='unapproved-mode') checkpoint.operation='deploy';
  if(scenario==='missing-preparation') checkpoint.settings.bindings=checkpoint.settings.bindings.filter(b=>b.name!=='IDENTITY_AUTHORITY');
  if(scenario==='changed-variable') desired.vars.CUSTOM_POLICY='unexpected';
  let current=activeId,uploaded,posts=0,saved;
  const request=async(route,body)=>{
    if(route.includes('/synthetic-provider/')) {
      if(route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:activeId,percentage:100}]}]};
      const bindings=structuredClone(providerSettings.bindings);
      if(scenario==='provider-before-upload'||(scenario==='provider-before-deploy'&&uploaded)) bindings.find(b=>b.name==='CYINVOICE_WORKSPACE_ID').text='changed_workspace';
      return {id:activeId,bindings};
    }
    if(body&&route.endsWith('?deploy=false')) {
      posts++;uploaded=body;assert.equal(saved.uploadAttempted,true);
      assert.equal(body.bindings.find(b=>b.name==='CYID_ENABLED').text,'true');
      assert.ok(body.bindings.filter(b=>b.type!=='plain_text').every(b=>b.type==='inherit'&&b.version_id===activeId));
      assert.deepEqual(body.bindings.filter(b=>b.type==='inherit').map(b=>b.name).sort(),checkpoint.settings.bindings.filter(b=>b.type!=='plain_text').map(b=>b.name).sort());
      if(scenario==='lost-upload') throw Error('RESPONSE_LOST');
      return {id:deployedId};
    }
    if(body&&route.endsWith('/deployments')) {
      posts++;assert.equal(saved.deploymentRequested,true);current=deployedId;
      if(scenario==='lost-deploy') throw Error('RESPONSE_LOST');
      return {};
    }
    if(route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:current,percentage:100}]}]};
    if(route.endsWith('/routes')) return checkpoint.routes;
    if(route.endsWith('/subdomain')) return checkpoint.workerSubdomain;
    if(route.endsWith('/versions/'+activeId)) return {id:activeId,bindings:checkpoint.settings.bindings};
    if(route.endsWith('/versions/'+deployedId)) {
      const bindings=uploaded.bindings.map(b=>b.type==='inherit'?structuredClone(checkpoint.settings.bindings.find(x=>x.name===b.name)):structuredClone(b));
      if(scenario==='wrong-flag') bindings.find(b=>b.name==='CYID_ENABLED').text='false';
      return {id:deployedId,bindings,compatibility_date:desired.compatibility_date,compatibility_flags:[]};
    }
    throw Error('UNEXPECTED_TEST_REQUEST');
  };
  const action=()=>deployReviewedVersion(checkpoint,desired,'export default {};',request,value=>{saved=structuredClone(value);},deploymentNow);
  if(scenario==='success') {await action();assert.equal(posts,2);assert.equal(current,deployedId);}
  else {await assert.rejects(action(),/AUDIT_DEPLOYMENT_MODE_MISMATCH|CYID_PREPARATION_REQUIRED|CYID_ACTIVATION_VARIABLE_CHANGE|UPLOADED_BINDINGS_MISMATCH|CYID_PROVIDER_CHANGED|RESPONSE_LOST/);
    assert.equal(posts,scenario==='lost-deploy'?2:['wrong-flag','provider-before-deploy','lost-upload'].includes(scenario)?1:0);}
  if(saved) await assert.rejects(deployReviewedVersion(saved,desired,'export default {};',request,()=>{},deploymentNow),/DEPLOYMENT_ALREADY_ATTEMPTED/);
}

// Run the actual CLI orchestration against synthetic Cloudflare HTTP responses.
const deploymentConfig = JSON.parse(fs.readFileSync('wrangler.jsonc', 'utf8'));
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cyinvoice-deployment-test-'));
const generatedPath = path.resolve('wrangler.deploy.generated.jsonc');
const priorGenerated = fs.existsSync(generatedPath) ? fs.readFileSync(generatedPath) : null;
const originalFetch = globalThis.fetch;
const originalLog = console.log;
let applied = canonical.slice(0, 12), currentDatabase = '11111111-1111-4111-8111-111111111111';
let retainedProbe = false, configuredProviderScope = true, providerRevisionChanged = false, preparedRuntime = false, cyIdActive = false;
let providerEnvironment = 'development', runtimeSchema = '13', v3Enabled = false, historicalProbeService = 'synthetic-provider';
const retainedId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const retainedNonce = 'a'.repeat(32);
const calls = [], logs = [];
console.log = value => logs.push(value);
globalThis.fetch = async (url, options = {}) => {
  const u = new URL(url);
  calls.push([u.pathname, options.method ?? 'GET']);
  let result;
  if(u.pathname.includes('/synthetic-provider/') && u.pathname.endsWith('/versions/'+activeId)) {
    result={id:activeId,...structuredClone(configuredProviderScope?providerSettings:missingScope)};
    result.bindings.find(b=>b.name==='APP_ENV').text=providerEnvironment;
    if(providerRevisionChanged) result.bindings.find(b=>b.name==='API_VERSION').text='unknown';
  }
  else if (u.pathname.endsWith('/versions/'+retainedId)) result = {id:retainedId,annotations:{'workers/message':'CYID_BINDING_PROBE_'+retainedNonce},bindings:[
    {type:'plain_text',name:'PROBE_OWNER_NONCE',text:retainedNonce},
    {type:'plain_text',name:'PROBE_EXPIRES_AT',text:String(Date.now()-1000)},
    {type:'secret_text',name:'PROBE_AUTH_KEY'},
    {type:'service',name:'IDENTITY',service:historicalProbeService},
    {type:'service',name:'IDENTITY_AUTHORITY',service:historicalProbeService,entrypoint:'ConsumerAuthoritySync'},
  ]};
  else if (u.pathname.endsWith('/versions/'+activeId)) result = { id:activeId, bindings: [
    ...settings.bindings.filter(b => b.name !== 'DB' && b.name !== 'LOGIN'),
    { name: 'DB', type: 'd1', id: currentDatabase },
    { name: 'SCHEMA_VERSION', type: 'plain_text', text: runtimeSchema },
    ...(v3Enabled ? [
      {name:'COMPANY_SETTINGS_ENABLED',type:'plain_text',text:'true'},
      {name:'RECOVERY_ENABLED',type:'plain_text',text:'true'},
      {name:'COMPANY_SETTINGS_KEY_HEX',type:'secret_text'},
    ] : []),
    { name: 'CUSTOM_POLICY', type: 'plain_text', text: 'preserve-synthetic-policy' },
    ...deploymentConfig.ratelimits.map(r => ({ ...r, type: 'ratelimit' })),
    { name: 'IDENTITY', type: 'service', service: 'synthetic-provider' },
    { name: 'IDENTITY_AUTHORITY', type: 'service', service: 'synthetic-provider', entrypoint: 'ConsumerAuthoritySync' },
    ...(preparedRuntime ? Object.entries(preparation).filter(([name])=>name!=='CYID_WORKER_NAME')
      .map(([name,text])=>({name,type:'plain_text',text})) : []),
    ...(preparedRuntime ? [{name:'CYID_ENABLED',type:'plain_text',text:cyIdActive?'true':'false'}] : []),
  ] };
  else if (u.pathname.endsWith('/query')) {
    const { sql, params } = JSON.parse(options.body);
    assert.ok(sql.startsWith('SELECT ') || sql === 'PRAGMA foreign_key_check');
    const results = sql.startsWith('SELECT name FROM d1_migrations') ? applied.map(name => ({ name })) : db.prepare(sql).all(...(params ?? []));
    result = [{ success: true, results }];
  } else if (u.pathname.endsWith('/time_travel/bookmark')) {
    assert.ok(u.searchParams.get('timestamp'));
    result = { bookmark: 'synthetic-bookmark' };
  } else if (u.pathname.endsWith('/deployments')) result = { deployments: [{ id: 'synthetic-deployment', versions: [{ version_id:activeId, percentage:100 }] }] };
  else if (u.pathname.endsWith('/versions')) result = retainedProbe ? [{id:retainedId,annotations:{'workers/message':'CYID_BINDING_PROBE_'+retainedNonce}}] : [];
  else if (u.pathname.endsWith('/routes')) result = [];
  else if (u.pathname.includes('/workers/scripts/') && u.pathname.endsWith('/subdomain')) result = {enabled:true, previews_enabled:true};
  else if (u.pathname.endsWith('/subdomain')) result = { subdomain: 'synthetic-account' };
  else if (u.hostname.startsWith('synthetic-provider.') && u.pathname.endsWith('/v1/health')) return Response.json({ok:true,service:'cycloud-identity',environment:providerEnvironment,status:'ok',identity:'ready',serviceVersion:'0.3.6'});
  else if (u.pathname.endsWith('/v1/health')) return Response.json({ ...health, storageSchemaVersion: String(applied.length), cloudVersion: applied.length === 12 ? '0.8.9' : '0.9.3' });
  else if (u.pathname.endsWith('/' + currentDatabase)) result = { uuid: currentDatabase, name: deploymentConfig.d1_databases[0].database_name };
  else throw new Error('UNEXPECTED_TEST_REQUEST');
  return Response.json({ success: true, result });
};
try {
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account',CYID_WORKER_NAME:'synthetic-provider' };
  const statePath = path.join(tmp, 'checkpoint.json');
  await assert.rejects(main('before',statePath,{...env,CYID_WORKER_NAME:undefined}),/SHARED_CYID_PROVIDER_SETTING_REQUIRED/);
  assert.equal(calls.length,0);
  await main('before', statePath, env);
  const generated = JSON.parse(fs.readFileSync(generatedPath, 'utf8'));
  assert.equal(generated.d1_databases[0].database_id, currentDatabase);
  assert.equal(generated.vars.CYID_ENABLED, 'false');
  assert.equal(generated.vars.CUSTOM_POLICY, 'preserve-synthetic-policy');
  assert.equal(generated.vars.SCHEMA_VERSION, '18');
  assert.equal(generated.services[1].entrypoint, 'ConsumerAuthoritySync');
  await assert.rejects(main('before',statePath,{...env,...preparation,CYINVOICE_DEPLOYMENT_MODE:'prepare-cyid'}),/MIGRATION_NOT_COMPLETE/);
  // POSIX mode bits are enforced on the deployment's Linux runner. Windows
  // reports writable files as 0666; that is not a Windows ACL assertion.
  if (process.platform !== 'win32') {
    assert.equal(fs.statSync(statePath).mode & 0o777, 0o600);
    assert.equal(fs.statSync(generatedPath).mode & 0o777, 0o600);
  }
  applied = canonical;
  await main('after', statePath, env);
  const prepareEnv={...env,CYINVOICE_DEPLOYMENT_MODE:'prepare-cyid'};
  await main('before',statePath,prepareEnv);
  const prepared=JSON.parse(fs.readFileSync(generatedPath,'utf8'));
  assert.equal(prepared.vars.IDENTITY_CYINVOICE_WORKSPACE_ID,'workspace_synthetic');
  assert.equal(prepared.vars.CYID_ENABLED,'false');
  assert.deepEqual(prepared.services.map(b=>b.binding).sort(),['IDENTITY','IDENTITY_AUTHORITY']);
  await main('after',statePath,prepareEnv);
  const activateEnv={...env,CYINVOICE_DEPLOYMENT_MODE:'activate-cyid'};
  await assert.rejects(main('before',statePath,activateEnv),/CYID_PREPARATION_REQUIRED/);
  preparedRuntime=true;
  await main('before',statePath,activateEnv);
  assert.equal(JSON.parse(fs.readFileSync(generatedPath,'utf8')).vars.CYID_ENABLED,'true');
  assert.equal(JSON.parse(fs.readFileSync(statePath,'utf8')).cyIdActivation.IDENTITY_CYINVOICE_WORKSPACE_ID,'workspace_synthetic');
  // Failed activation can still audit the unchanged Built-in target; no success claim.
  await main('after',statePath,activateEnv);
  cyIdActive=true;
  await main('before',statePath,env);
  assert.equal(JSON.parse(fs.readFileSync(generatedPath,'utf8')).vars.CYID_ENABLED,'true');
  await main('after',statePath,env);
  // A previously activated CYID installation can receive reviewed code-only
  // deployments without silently reverting its authority to Built-in.
  for(const operation of ['activate-cyid','prepare-cyid'])
    await assert.rejects(main('before',statePath,{...env,CYINVOICE_DEPLOYMENT_MODE:operation}),/BUILT_IN_AUTHORITY_REQUIRED/);
  const cyidDeployEnv={...env,CYINVOICE_DEPLOYMENT_MODE:'deploy'};
  await main('before',statePath,cyidDeployEnv);
  assert.equal(JSON.parse(fs.readFileSync(generatedPath,'utf8')).vars.CYID_ENABLED,'true');
  await main('after',statePath,cyidDeployEnv);
  // A separately selected production provider may use a dynamic App registry.
  // Existing consumer scope/feature gates remain authoritative configuration;
  // no provider D1 or registry data is queried by a deployment audit.
  configuredProviderScope=false;providerEnvironment='production';runtimeSchema='15';v3Enabled=true;
  retainedProbe=true;historicalProbeService='synthetic-retired-provider';
  const productionEnv={...cyidDeployEnv,CYINVOICE_CYID_ENVIRONMENT:'production',
    CYINVOICE_RETAINED_PROBE_WORKER_NAME:historicalProbeService};
  await assert.rejects(main('before',statePath,cyidDeployEnv),/CYID_PROVIDER_ENVIRONMENT_INVALID/);
  await assert.rejects(main('before',statePath,{...productionEnv,CYINVOICE_RETAINED_PROBE_WORKER_NAME:undefined}),/RETAINED_PROBE_SCOPE_INVALID/);
  await main('before',statePath,productionEnv);
  const productionConfig=JSON.parse(fs.readFileSync(generatedPath,'utf8'));
  assert.equal(productionConfig.vars.SCHEMA_VERSION,'15');
  assert.equal(productionConfig.vars.COMPANY_SETTINGS_ENABLED,'true');
  assert.equal(productionConfig.vars.RECOVERY_ENABLED,'true');
  assert.equal(productionConfig.vars.IDENTITY_WORKSPACE_ID,preparation.IDENTITY_WORKSPACE_ID);
  const productionAudit=JSON.parse(fs.readFileSync(statePath,'utf8'));
  assert.equal(productionAudit.provider.environment,'production');
  assert.deepEqual(resolveActiveCyId(productionAudit.settings,'synthetic-provider',
    [{workspace_id:'workspace_synthetic',status:'active'}]),preparation);
  const staleTemplate={...productionConfig,vars:{...productionConfig.vars,SCHEMA_VERSION:'13',
    COMPANY_SETTINGS_ENABLED:'false',RECOVERY_ENABLED:'false'}};
  const preserved=preservedDeployConfig(staleTemplate,productionAudit.settings);
  for(const name of ['SCHEMA_VERSION','COMPANY_SETTINGS_ENABLED','RECOVERY_ENABLED'])
    assert.equal(preserved.vars[name],productionConfig.vars[name]);
  const wrongProvider={...productionAudit.settings,bindings:productionAudit.settings.bindings.map(b=>
    b.name==='IDENTITY'?{...b,service:'wrong-provider'}:b)};
  assert.throws(()=>verifyPreparedCyId(productionConfig,wrongProvider,preparation,productionAudit.state.workspaces),
    /CYID_SERVICE_BINDING_CONFLICT/);
  for(const name of ['SCHEMA_VERSION','COMPANY_SETTINGS_ENABLED','RECOVERY_ENABLED']) {
    const changed={...productionConfig,vars:{...productionConfig.vars,[name]:name==='SCHEMA_VERSION'?'13':'false'}};
    await assert.rejects(deployReviewedVersion(productionAudit,changed,'export default {};',
      ()=>assert.fail('must stop before API mutation'),()=>{},Date.parse(productionAudit.capturedAt)),/ACTIVE_RUNTIME_SETTING_CHANGE/);
  }
  let v3Current=activeId,v3Uploaded;
  const providerBindings=structuredClone(missingScope.bindings);
  providerBindings.find(b=>b.name==='APP_ENV').text='production';
  const v3Request=async(route,body)=>{
    if(route.includes('/synthetic-provider/')) return route.endsWith('/deployments')
      ? {deployments:[{versions:[{version_id:activeId,percentage:100}]}]}
      : {id:activeId,bindings:providerBindings};
    if(body && route.endsWith('?deploy=false')) {
      v3Uploaded=body;
      assert.ok(body.bindings.filter(b=>b.type!=='plain_text').every(b=>b.type==='inherit'&&b.version_id===activeId));
      assert.ok(body.bindings.some(b=>b.name==='COMPANY_SETTINGS_KEY_HEX'&&b.type==='inherit'));
      for(const name of ['COMPANY_SETTINGS_ENABLED','RECOVERY_ENABLED'])
        assert.equal(body.bindings.find(b=>b.name===name).text,'true');
      return {id:deployedId};
    }
    if(body && route.endsWith('/deployments')) {v3Current=deployedId;return {};}
    if(route.endsWith('/deployments')) return {deployments:[{versions:[{version_id:v3Current,percentage:100}]}]};
    if(route.endsWith('/routes')) return productionAudit.routes;
    if(route.endsWith('/subdomain')) return productionAudit.workerSubdomain;
    if(route.endsWith('/versions/'+activeId)) return {id:activeId,bindings:productionAudit.settings.bindings};
    if(route.endsWith('/versions/'+deployedId)) return {id:deployedId,compatibility_date:productionConfig.compatibility_date,
      compatibility_flags:[],bindings:v3Uploaded.bindings.map(b=>b.type==='inherit'
        ? productionAudit.settings.bindings.find(x=>x.name===b.name):b)};
    assert.fail('Unexpected deployment request');
  };
  await deployReviewedVersion(structuredClone(productionAudit),productionConfig,'export default {};',
    v3Request,()=>{},Date.parse(productionAudit.capturedAt));
  assert.equal(v3Current,deployedId);
  await main('after',statePath,productionEnv);
  await assert.rejects(main('before',statePath,{...productionEnv,CYINVOICE_CYID_ENVIRONMENT:'unknown'}),/CYID_PROVIDER_ENVIRONMENT_INVALID/);
  cyIdActive=false;
  await assert.rejects(main('before',statePath,productionEnv),/PRODUCTION_PROVIDER_REQUIRES_ACTIVE_CYID/);
  cyIdActive=true;
  configuredProviderScope=true;providerEnvironment='development';runtimeSchema='13';v3Enabled=false;
  retainedProbe=false;historicalProbeService='synthetic-provider';
  cyIdActive=false;preparedRuntime=false;
  await main('before',statePath,prepareEnv);
  const bundleDir=path.join(tmp,'business-bundle');fs.mkdirSync(bundleDir);
  const bundlePath=path.join(bundleDir,'app.js');fs.writeFileSync(bundlePath,'export default {};');
  const countBeforeRejectedDeploy=calls.length;
  await assert.rejects(main('deploy',statePath,{...env,CYINVOICE_DEPLOYMENT_MODE:'deploy'},bundlePath),/AUDIT_DEPLOYMENT_MODE_MISMATCH/);
  await assert.rejects(main('deploy',statePath,{...prepareEnv,CYID_WORKER_NAME:'changed-provider'},bundlePath),/AUDIT_SCOPE_MISMATCH/);
  assert.equal(calls.length,countBeforeRejectedDeploy);
  configuredProviderScope=false;
  await assert.rejects(main('before',statePath,{...prepareEnv,...preparation}),/CYID_CONSUMER_SCOPE_REQUIRED/);
  await main('before',statePath,env);
  assert.ok(logs.some(l=>l.includes('scope configured: false')));
  configuredProviderScope=true;
  db.exec("UPDATE workspaces SET status='disabled' WHERE workspace_id='workspace_synthetic'");
  await assert.rejects(main('before',statePath,prepareEnv),/SINGLE_ORIGINAL_WORKSPACE_REQUIRED/);
  db.exec("UPDATE workspaces SET status='active' WHERE workspace_id='workspace_synthetic'");
  currentDatabase = '22222222-2222-4222-8222-222222222222';
  await assert.rejects(main('after', statePath, env), /AUDIT_SCOPE_MISMATCH/);
  currentDatabase=providerDatabase;
  await assert.rejects(main('before',statePath,env),/ORIGINAL_DEVELOPMENT_DATABASE_REQUIRED/);
  currentDatabase = '11111111-1111-4111-8111-111111111111'; retainedProbe = true;
  await main('before',statePath,{...env,CYID_WORKER_NAME:'synthetic-provider'});
  assert.equal(JSON.parse(fs.readFileSync(statePath,'utf8')).retainedProbeCount,1);
  await main('after',statePath,{...env,CYID_WORKER_NAME:'synthetic-provider'});
  assert.ok(logs.some(l=>l.includes('Retained probe versions: 1; expired: 1')));
  assert.ok(calls.filter(([url])=>url.includes('/d1/database/')).every(([url])=>!url.includes(providerDatabase)));
  assert.ok(calls.every(([url, method]) => method === 'GET' || (method === 'POST' && url.endsWith('/query'))));
  assert.ok(!logs.join('\n').includes('synthetic-token') && !logs.join('\n').includes('synthetic-bookmark') &&
    !logs.join('\n').includes('11111111-1111-4111-8111-111111111111'));
} finally {
  globalThis.fetch = originalFetch; console.log = originalLog;
  if (priorGenerated) fs.writeFileSync(generatedPath, priorGenerated, { mode: 0o600 });
  else fs.rmSync(generatedPath, { force: true });
  fs.rmSync(tmp, { recursive: true, force: true });
  db.close();
}
console.log('Deployment audit: scope/authority/migrations/continuity/privacy/health regressions passed.');

for (const file of ['cyinvoice-cloud-dev-deploy.yml','cycloudidentity-deploy.yml']) {
  const workflow = fs.readFileSync(path.resolve('../../../.github/workflows',file),'utf8');
  assert.equal(/private-binding-probe|binding-version|binding-cleanup|binding-probe/.test(workflow),false);
}
assert.equal(fs.existsSync(path.resolve('../../CYCloudIdentity/scripts/private-binding-probe.mjs')),false);

for(const file of ['cycloudidentity-deploy.yml','cycloudidentity-production-deploy.yml','cyaccountingweb-deploy.yml']) {
  const workflow=fs.readFileSync(path.resolve('../../../.github/workflows',file),'utf8');
  assert.ok(workflow.includes('${{ secrets.CYID_WORKER_NAME }}'));
  assert.equal(workflow.includes('${{ vars.CYID_WORKER_NAME }}'),false);
}
const consumerWorkflow=fs.readFileSync(path.resolve('../../../.github/workflows/cyinvoice-cloud-dev-deploy.yml'),'utf8');
assert.ok(consumerWorkflow.includes('${{ secrets.CYINVOICE_CYID_WORKER_NAME || secrets.CYID_WORKER_NAME }}'));
assert.ok(consumerWorkflow.includes("${{ vars.CYINVOICE_CYID_ENVIRONMENT || 'development' }}"));
for(const name of ['CYID_D1_DATABASE_ID',...Object.keys(preparation).filter(n=>n.startsWith('IDENTITY_'))])
  assert.equal(consumerWorkflow.includes('${{ secrets.'+name+' }}'),false);
