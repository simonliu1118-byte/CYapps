import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import {createTestEnv} from './support/env.js';
import {installFakeFetcher} from './support/fake-fetcher.js';

const product = {name:'3M 紙膠',model:'1530-1',attributes:{尺寸:'1吋'},packageQuantity:12,packageUnit:'入',keyword:'1530-1'};
const title = '3M 1530-1 0.5吋／1吋 12入';
const spec = {model:product.model,attributes:product.attributes,packageQuantity:12,packageUnit:'入'};
const suggestion = {verdict:'same',option:'1 吋 12 入／盒',spec,price:189,priceConditions:'公開售價',
  seller:'測試賣家',url:'https://market.example.test/1',checkedAt:new Date().toISOString(),evidence:'選項及盒身都標示 1530-1'};
const request = (path, method, body, headers) => new Request('https://pricedesk.test' + path, {
  method, headers:{...headers,'content-type':'application/json'},body:body===undefined ? undefined : JSON.stringify(body)
});
async function setup(t) {
  const {env,identity} = createTestEnv();
  const fetcher = installFakeFetcher();
  const sql = env.DB.sqlite;
  const cookie = identity.cookieFor('0001');
  Object.assign(env,{GROK_WEBHOOK_URL:'https://grok.example.test/hook',GROK_WEBHOOK_TOKEN:'test-hook',GROK_API_TOKEN:'test-api'});
  const deliveries = [];
  let sent = true;
  const fallback = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    if (url!==env.GROK_WEBHOOK_URL) return fallback(url,options);
    deliveries.push({headers:options.headers,body:JSON.parse(options.body)});
    return new Response(null,{status:sent?204:502});
  };
  t.after(()=>{globalThis.fetch=fallback;fetcher.restore();});
  const human = (method,path,body) => worker.fetch(request(path,method,body,{cookie}),env);
  const bot = (method,path,body,token='test-api') => worker.fetch(request('/api/grok/tasks/'+path,method,body,{authorization:'Bearer '+token}),env);
  const {product:{id}} = await (await human('POST','/api/tracked',product)).json();
  const items = Array.from({length:18},(_,i)=>({title,code:'M'+i,price:250+i,relevance:3,match_label:'型號相關',url:'https://market.example.test/'+i}));
  fetcher.respond = ({path}) => path==='/v1/momo'
    ? {status:'ok',raw_count:items.length,results:items}
    : {status:'ok',markets:{shopee:{status:'ok',results:[]},coupang:{status:'ok',results:[]}}};
  const run = async () => {
    sql.exec('DELETE FROM search_cache; UPDATE upstream_state SET last_request_at=0,blocked_until=0');
    const response = await human('POST','/api/tracked/'+id+'/run');
    assert.equal(response.status,200);
  };
  const create = async data => {
    const response = await human('POST','/api/grok-tasks',data);
    assert.equal(response.status,201);
    return response.json();
  };
  const draft = condition => create({kind:'draft',condition,fields:{model:product.model}});
  const judge = items => create({kind:'judge',productId:id,items});
  return {env,sql,human,bot,id,run,judge,draft,deliveries,fail:()=>{sent=false;},recover:()=>{sent=true;}};
}
const humanTask = id => '/api/grok-tasks/'+id;

test('judge 派工、重送、領取、回填、稽核及過期檢查', async t => {
  const f = await setup(t);
  await f.run();
  const one = await f.judge();
  assert.equal(one.status,'sent');
  const first = f.deliveries[0];
  assert.equal(first.headers.authorization,'Bearer test-hook');
  assert.deepEqual(first.body,{schemaVersion:1,event:'pricedesk.task.ready',eventId:first.body.eventId,taskId:one.id,taskVersion:1});
  f.fail();
  const two = await f.judge();
  assert.equal(two.status,'send_failed');
  f.recover();
  assert.equal((await (await f.human('POST',humanTask(two.id)+'/resend')).json()).status,'sent');
  assert.deepEqual(f.deliveries[1],f.deliveries[2]);
  assert.equal((await f.bot('GET',two.id,undefined,'wrong')).status,401);
  const job = await (await f.bot('GET',two.id)).json();
  assert.equal(job.status,'working');
  assert.equal(job.items.length,18);
  assert.equal((await f.bot('PUT',two.id+'/results/momo/unknown',suggestion)).status,404);
  assert.equal((await f.bot('PUT',two.id+'/results/momo/M0',suggestion)).status,200);
  assert.equal(f.sql.prepare("SELECT status FROM grok_task_items WHERE task_id=? AND listing_key='M0'").get(two.id).status,'answered');
  assert.equal(f.sql.prepare("SELECT actor_employee_id FROM audit_log WHERE action='grok.results'").get().actor_employee_id,'grok-bot');
  assert.equal(f.sql.prepare('SELECT count(*) n FROM listing_judgments').get().n,0);
  const tracked = await (await f.human('GET','/api/tracked/'+f.id)).json();
  assert.equal(tracked.candidates.find(c=>c.listingKey==='M0').price,250);
  assert.equal((await f.human('PUT','/api/tracked/'+f.id,{...product,model:'1530-0',revision:tracked.product.revision})).status,200);
  assert.equal((await f.bot('PUT',two.id+'/results/momo/M1',suggestion)).status,409);
  assert.equal(f.sql.prepare("SELECT status FROM grok_task_items WHERE task_id=? AND listing_key='M1'").get(two.id).status,'expired');
});

test('同意與修正共用正式判斷，更新本輪觀測並保留原判讀', async t => {
  const f = await setup(t);
  await f.run();
  const {id} = await f.judge();
  await f.bot('GET',id);
  for (const k of ['M0','M1']) assert.equal((await f.bot('PUT',id+'/results/momo/'+k,suggestion)).status,200);
  const c = f.sql.prepare("SELECT * FROM latest_candidates WHERE listing_key='M0'").get();
  f.sql.prepare('INSERT INTO price_observations (run_id,product_id,source,listing_key,title,price,observed_at) VALUES(?,?,?,?,?,?,?)')
    .run(c.run_id,f.id,c.source,c.listing_key,c.title,250,suggestion.checkedAt);
  const originalBatch = f.env.DB.batch;
  const before = [];
  f.env.DB.batch = async statements => {
    before.push(statements.length);
    return originalBatch(statements);
  };
  assert.equal((await f.human('POST',humanTask(id)+'/items/momo/M0/approve')).status,200);
  assert.equal(before.length,1);
  assert.deepEqual(f.sql.prepare("SELECT price FROM price_observations WHERE run_id=? AND listing_key='M0'").all(c.run_id).map(r=>r.price),[189]);
  assert.equal(f.sql.prepare("SELECT verdict FROM listing_judgments WHERE listing_key='M0'").get().verdict,'same');
  const change = {source:'momo',listingKey:'M1',grokTaskId:id,verdict:'other_package',packageQuantity:6,packageUnit:'入'};
  assert.equal((await f.human('PUT','/api/tracked/'+f.id+'/judgments',change)).status,200);
  const row = f.sql.prepare("SELECT * FROM grok_task_items WHERE listing_key='M1'").get();
  assert.equal(row.status,'reviewed');
  assert.equal(row.reviewed_by,'emp-0001');
  assert.equal(JSON.parse(row.result_json).verdict,'same');
  assert.equal(JSON.parse(row.review_json).packageQuantity,6);
  assert.equal(f.sql.prepare("SELECT actor_employee_id FROM audit_log WHERE action='grok.review'").get().actor_employee_id,'emp-0001');
});

test('建檔提問、回填與人工儲存', async t => {
  const f = await setup(t);
  const {id} = await f.draft('3M 1530-1，一吋，一盒 12 捲');
  await f.bot('GET',id);
  assert.equal((await f.bot('POST',id+'/questions',{question:'要原盒嗎？',options:['是','散裝可']})).status,200);
  const {questions:[q]} = await (await f.human('GET',humanTask(id))).json();
  assert.equal((await f.human('POST',humanTask(id)+'/questions/'+q.id+'/answer',{answer:'是'})).status,200);
  assert.equal(f.deliveries[1].body.event,'pricedesk.question.answered');
  assert.equal(f.deliveries[1].body.questionId,q.id);
  assert.equal((await (await f.bot('GET',id)).json()).questions[0].answer,'是');
  const draft = {fields:product,evidence:{keyword:'型號'},searches:[{source:'momo',keyword:'1530-1',count:3}]};
  assert.equal((await f.bot('PUT',id+'/draft',draft)).status,200);
  assert.deepEqual((await (await f.human('GET',humanTask(id))).json()).draft,draft);
  assert.equal(f.sql.prepare('SELECT count(*) n FROM tracked_products').get().n,1);
  const created = await f.human('POST','/api/tracked',{...product,name:'3M 紙膠 原盒',grokDraftId:id});
  assert.equal(created.status,201);
  assert.equal(f.sql.prepare("SELECT created_by FROM tracked_products WHERE name='3M 紙膠 原盒'").get().created_by,'emp-0001');
  assert.equal(f.sql.prepare('SELECT status FROM grok_tasks WHERE id=?').get(id).status,'applied');
});

test('刪除單輪候選：18 變 17，拒絕舊 run 與晚到回填，來源下輪可再列出', async t => {
  const f = await setup(t);
  await f.run();
  const {id} = await f.judge();
  const pending = async () => (await (await f.human('GET','/api/pending')).json()).count;
  assert.equal(await pending(),18);
  const url = '/api/tracked/'+f.id+'/candidates/momo/M0';
  assert.equal((await f.human('DELETE',url,{runId:'old'})).status,409);
  const {run_id:runId} = f.sql.prepare("SELECT run_id FROM latest_candidates WHERE listing_key='M0'").get();
  assert.equal((await f.human('DELETE',url,{runId})).status,200);
  assert.equal(await pending(),17);
  assert.equal((await f.bot('PUT',id+'/results/momo/M0',suggestion)).status,404);
  await f.run();
  assert.equal(await pending(),18);
  assert.notEqual(f.sql.prepare("SELECT run_id FROM latest_candidates WHERE listing_key='M0'").get().run_id,runId);
  assert.equal(f.sql.prepare("SELECT count(*) n FROM audit_log WHERE action='candidate.delete'").get().n,1);
});
