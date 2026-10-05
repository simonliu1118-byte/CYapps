import assert from 'node:assert/strict';
import test from 'node:test';
import { buildProductionConfig, productionWorkerName, requireProductionCoreConsumer } from '../scripts/render-production-config.mjs';
const id='11111111-1111-4111-8111-111111111111';
function input(){
  const values={APP_ENV:'production',API_VERSION:'v1',SESSION_TTL_SECONDS:'3600',EMAIL_PROVIDER:'brevo',EMAIL_FROM:'Synthetic <test@example.test>',EMAIL_DAILY_BUDGET:'50',CORE_ACCOUNT_APPLICATION_ID:'APP_TEST',ACCOUNT_PORTAL_URL:'https://portal.example.test',CYACC_APPLICATION_ID:'APP_ACCOUNTING_TEST'};
  return {workerName:'identity-test-prod',developmentDatabaseId:'22222222-2222-4222-8222-222222222222',database:{uuid:id,name:'identity_test_prod'},settings:{bindings:[...Object.entries(values).map(([name,text])=>({type:'plain_text',name,text})),{type:'d1',name:'DB',id},{type:'ratelimit',name:'LOGIN_RATE_LIMITER',namespace_id:'900102',simple:{limit:10,period:60}},{type:'secret_text',name:'BREVO_API_KEY'}]}};
}
test('production names preserve the established isolated target derivation',()=>{
  assert.equal(productionWorkerName('identity-test-development'),'identity-test-production');
  assert.equal(productionWorkerName('identity-test-dev'),'identity-test-prod');
  assert.equal(productionWorkerName('identity-test'),'identity-test-prod');
});
test('ordinary production config preserves existing vars, DB and rate limit without secret material',()=>{
  const args=input(),config=buildProductionConfig(args);
  assert.equal(config.vars.CYACC_APPLICATION_ID,'APP_ACCOUNTING_TEST');
  assert.equal(config.vars.APP_ENV,'production');
  assert.equal(config.d1_databases[0].database_id,id);
  assert.equal(config.ratelimits[0].namespace_id,'900102');
  assert.equal('BREVO_API_KEY' in config.vars,false);
  assert.equal(config.compatibility_flags.includes('nodejs_compat'),true);
});
for(const [name,mutate] of [
  ['development D1',args=>{args.developmentDatabaseId=id;}],
  ['development environment',args=>{args.settings.bindings.find(b=>b.name==='APP_ENV').text='development';}],
  ['missing Email secret',args=>{args.settings.bindings=args.settings.bindings.filter(b=>b.type!=='secret_text');}],
  ['missing rate limit',args=>{args.settings.bindings=args.settings.bindings.filter(b=>b.type!=='ratelimit');}],
  ['unknown binding',args=>{args.settings.bindings.push({type:'service',name:'UNMODELED'});}],
  ['wrong database metadata',args=>{args.database.uuid=args.developmentDatabaseId;}],
  ['non-HTTPS portal',args=>{args.settings.bindings.find(b=>b.name==='ACCOUNT_PORTAL_URL').text='http://portal.example.test';}],
])test(`production config fails closed for ${name}`,()=>{const args=input();mutate(args);assert.throws(()=>buildProductionConfig(args));});

function consumer() {
  return { providerName: 'identity-test-prod', applicationId: 'APP_TEST', consumerVersion: '1.0.2',
    settings: { success: true, result: { bindings: [
      { type: 'service', name: 'IDENTITY', service: 'identity-test-prod' },
      { type: 'plain_text', name: 'IDENTITY_APPLICATION_ID', text: 'APP_TEST' },
      { type: 'plain_text', name: 'IDENTITY_CONSUMER_VERSION', text: '1.0.2' },
    ] } }, health: { ok: true, data: { service: 'cyweb', database: 'ok', identityConsumerVersion: '1.0.2' } },
  };
}
test('production readiness requires a healthy consumer bound to the intended production authority', () => {
  assert.doesNotThrow(() => requireProductionCoreConsumer(consumer()));
});
for (const [name, mutate] of [
  ['healthy development provider binding', value => { value.settings.result.bindings[0].service = 'identity-test-dev'; }],
  ['development Service environment', value => { value.settings.result.bindings[0].environment = 'development'; }],
  ['wrong core application', value => { value.settings.result.bindings[1].text = 'APP_OTHER'; }],
  ['undeclared consumer migration', value => { value.settings.result.bindings[2].text = '1.0.1'; }],
  ['unhealthy D1', value => { value.health.data.database = 'unavailable'; }],
]) test(`production readiness rejects ${name}`, () => { const value = consumer(); mutate(value); assert.throws(() => requireProductionCoreConsumer(value), /PRODUCTION_CORE_CONSUMER_NOT_READY/); });
