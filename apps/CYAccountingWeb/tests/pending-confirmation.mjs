import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import core from '../src/index.js';
import app from '../src/app.js';
import {accountingFixture,session} from './support/backup-fixtures.mjs';
import {buildBackupSet,validateBackupSetBytes} from '../src/gcs-backup.js';
import {verify} from './support/restore-fixture.mjs';

const db=accountingFixture(),env={DB:db},context={session};
const request=(path,method='GET',body,headers={})=>new Request('https://acc.example.test'+path,{method,
  headers:{...(body===undefined?{}:{'content-type':'application/json',origin:'https://acc.example.test'}),...headers},
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
const send=(path,method,body)=>core.fetch(request(path,method,body),env,context);
const category=db.db.prepare("SELECT name FROM categories WHERE kind='expense' ORDER BY id LIMIT 1").get().name;
const base={txDate:'2026-11-01',accountName:'現金',kind:'expense',categoryName:category,summary:'發票待補',amount:30,pendingConfirmation:true};
const create=async values=>{
  const response=await send('/api/transactions','POST',{...base,...values});
  assert.equal(response.status,201,JSON.stringify(await response.clone().json()));
  return (await response.json()).transaction;
};
const confirmation=(row,extra={})=>({categoryName:category,summary:'核對完成',expectedUpdatedAt:row.updated_at,...extra});
try {
  const row=await create();
  assert.equal(row.pending_confirmation,1);assert.equal(row.summary,'(待確認)發票待補');
  assert.equal((await create({summary:'(待確認)(待確認)測試'})).summary,'(待確認)測試');
  const ordinary=await create({pendingConfirmation:false,summary:'(待確認)只是文字'});
  assert.equal(ordinary.pending_confirmation,0,'text and historical pink alone do not create workflow state');
  assert.equal((await send(`/api/transactions/${row.id}/marker`,'PATCH',{markerColor:'pink'})).status,400);
  assert.equal((await send(`/api/transactions/${row.id}`,'PUT',{...base,pendingConfirmation:false})).status,409,'normal edit cannot bypass confirmation');
  assert.equal((await send(`/api/transactions/${row.id}/confirm`,'POST',confirmation(row,{amount:1}))).status,400);
  assert.equal((await send(`/api/transactions/${row.id}/confirm`,'POST',confirmation(row,{summary:''}))).status,400);
  assert.equal((await send(`/api/transactions/${row.id}/confirm`,'POST',confirmation(row,{categoryName:'TEST_NONEXISTENT'}))).status,400);
  db.db.prepare("UPDATE app_settings SET value='2026-11' WHERE key='locked_through'").run();
  assert.equal((await send(`/api/transactions/${row.id}`,'PUT',base)).status,409,'ordinary locked-month editing remains blocked');
  const response=await send(`/api/transactions/${row.id}/confirm`,'POST',confirmation(row,{summary:'(待確認)已核對'}));
  assert.equal(response.status,200);
  const confirmed=(await response.json()).transaction;
  assert.equal(confirmed.pending_confirmation,0);assert.equal(confirmed.summary,'已核對');
  for (const field of ['id','tx_date','account_name','kind','amount','created_at']) assert.equal(confirmed[field],row[field]);
  const audit=db.db.prepare('SELECT * FROM transaction_confirmation_audit WHERE transaction_id=?').get(row.id);
  assert.equal(audit.previous_summary,row.summary);assert.equal(audit.summary,'已核對');
  assert.equal(JSON.parse(audit.actor_json).employeeNo,session.employee_no);
  assert.equal((await send(`/api/transactions/${row.id}/confirm`,'POST',confirmation(row))).status,409);
  assert.equal((await send(`/api/transactions/${row.id}`,'PUT',base)).status,409,'confirmed entries return to ordinary locks');
  assert.throws(()=>db.db.prepare('DELETE FROM transaction_confirmation_audit WHERE id=?').run(audit.id),/append-only/);

  db.db.prepare("UPDATE app_settings SET value='' WHERE key='locked_through'").run();
  const concurrent=await create();
  const outcomes=await Promise.all([send(`/api/transactions/${concurrent.id}/confirm`,'POST',confirmation(concurrent)),send(`/api/transactions/${concurrent.id}/confirm`,'POST',confirmation(concurrent))]);
  assert.deepEqual(outcomes.map(r=>r.status).sort(),[200,409]);
  assert.equal(db.db.prepare('SELECT COUNT(*) n FROM transaction_confirmation_audit WHERE transaction_id=?').get(concurrent.id).n,1);
  const failed=await create();
  db.db.exec("CREATE TRIGGER test_confirmation_rollback BEFORE UPDATE ON transactions WHEN NEW.pending_confirmation=0 BEGIN SELECT RAISE(ABORT,'TEST_ROLLBACK'); END");
  assert.equal((await send(`/api/transactions/${failed.id}/confirm`,'POST',confirmation(failed))).status,500);
  assert.equal(db.db.prepare('SELECT pending_confirmation FROM transactions WHERE id=?').get(failed.id).pending_confirmation,1);
  assert.equal(db.db.prepare('SELECT COUNT(*) n FROM transaction_confirmation_audit WHERE transaction_id=?').get(failed.id).n,0);
  db.db.exec('DROP TRIGGER test_confirmation_rollback');

  const insert=db.db.prepare(`INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,pending_confirmation,created_at,updated_at) VALUES(?,'現金','expense',?,?,1,?,'TEST','TEST')`);
  db.db.exec('BEGIN');
  for(let i=0;i<10000;i++)insert.run('2026-01-01',category,'TEST_NORMAL',0);
  for(let i=0;i<113;i++)insert.run(i<10?'2021-04-01':'2026-11-01',category,'(待確認)TEST_'+i,1);
  db.db.exec('COMMIT');
  const queries=[],prepare=db.prepare.bind(db);
  db.prepare=sql=>{
    const statement=prepare(sql),all=statement.all;
    statement.all=async()=>{queries.push({sql,plan:db.db.prepare('EXPLAIN QUERY PLAN '+sql).all(...statement.args)});return all();};
    return statement;
  };
  let page=await (await send('/api/transactions/pending-confirmation')).json();
  const rows=[...page.transactions];
  assert.equal(rows.length,50);assert.ok(page.nextCursor);
  while(page.nextCursor){page=await(await send('/api/transactions/pending-confirmation?cursor='+encodeURIComponent(page.nextCursor))).json();rows.push(...page.transactions);}
  const expected=db.db.prepare('SELECT id FROM transactions WHERE pending_confirmation=1 ORDER BY tx_date DESC,id DESC').all().map(row=>row.id);
  assert.deepEqual(rows.map(row=>row.id),expected);assert.equal(new Set(rows.map(row=>row.id)).size,rows.length);
  assert.ok(rows.some(row=>row.tx_date.startsWith('2021')),'pending search has no three-year/date restriction');
  assert.equal(queries.length,Math.ceil(rows.length/50),'one query per page, no count or monthly loops');
  for (const query of queries){assert.ok(query.plan.some(row=>row.detail.includes('idx_transactions_pending_confirmation')));assert.doesNotMatch(query.sql,/\b(?:COUNT|OFFSET)\b/i);}
  assert.equal((await send('/api/transactions/pending-confirmation?cursor=bad')).status,400);

  const token='cyid_'+'a'.repeat(64),cookie={cookie:`cyaccounting_session=${token}`};
  let role='USER';
  const authorized={...env,CYID_WORKSPACE_ID:'workspace-ci-placeholder',CYID_APPLICATION_ID:'CYACC_CI',IDENTITY:{async fetch(){return Response.json({principal:{workspaceId:'workspace-ci-placeholder',employeeId:'employee-ci',employeeNo:'0123',displayName:'CI User',workspaceRole:role,isIdentityAdmin:false,emailVerified:true,isWorkspaceSuperAdmin:role==='SUPER_ADMIN',credentialVersion:1,employeeRevision:1},session:{expiresAt:new Date(Date.now()+60000).toISOString()}});}}};
  assert.equal((await app.fetch(request('/api/transactions/pending-confirmation'),authorized)).status,401);
  assert.equal((await app.fetch(request('/api/transactions/pending-confirmation','GET',undefined,cookie),authorized)).status,200);
  assert.equal((await app.fetch(request(`/api/transactions/${failed.id}/confirm`,'POST',confirmation(failed),cookie),authorized)).status,403);
  role='ADMIN';
  assert.equal((await app.fetch(request(`/api/transactions/${failed.id}/confirm`,'POST',confirmation(failed),cookie),authorized)).status,200);
  assert.equal(JSON.parse(db.db.prepare('SELECT actor_json FROM transaction_confirmation_audit WHERE transaction_id=?').get(failed.id).actor_json).employeeNo,'0123');

  const backup=await buildBackupSet(db),validated=await validateBackupSetBytes(backup.manifestBytes,backup.dataBytes,backup.backupId);
  assert.ok(validated.data.transactionConfirmationAudit.length>=3);
  assert.ok(validated.data.transactions.some(row=>row.pendingConfirmation===1));
  const proof=await verify(validated);assert.equal(proof.fullRowRoundtrip,true);
  const old=structuredClone(validated);old.manifest.schemaVersion=15;delete old.data.transactionConfirmationAudit;
  old.data.transactions.forEach(row=>{delete row.pendingConfirmation;});
  assert.equal((await verify(old)).fullRowRoundtrip,true,'old schema 15 remains restorable, with no inferred pending state');
} finally {db.close();}

const ledger=fs.readFileSync(new URL('../public/ledger-tools.js',import.meta.url),'utf8');
const frontend=fs.readFileSync(new URL('../public/app.js',import.meta.url),'utf8');
const helper=frontend.slice(frontend.indexOf('function pendingSummaryBody('),frontend.indexOf('function manualTransactionValues('));
const renderer=vm.runInNewContext(helper+ledger.slice(ledger.indexOf('function renderLedgerRow('),ledger.indexOf('let cyMarkerPicker'))+'\nrenderLedgerRow',{
  window:{cyLedgerBalanceBreakdowns:new Map()},document:{body:{dataset:{}}},isLocked:()=>true,escapeHtml:value=>String(value).replaceAll('<','&lt;'),splitLedgerAccountName:name=>[name],ledgerAccountVisual:()=>({slot:1,background:'#fff',foreground:'#000'}),money:String});
const html=renderer({id:1,tx_date:'2021-04-01',account_name:'TEST',kind:'expense',category_name:'TEST',summary:'(待確認)<TEST>',amount:1,pending_confirmation:1},null);
assert.match(html,/\(待確認\)<\/span><button[^>]+data-confirm-transaction="1"/);
assert.match(html,/data-marker-color="pink"/);assert.match(html,/&lt;TEST>/);
assert.match(html,/data-edit-id="1"[^>]*disabled/,'confirmation does not unlock normal edit');
const menu=ledger.slice(ledger.indexOf('function openTransactionMarkerPicker('));
assert.doesNotMatch(menu,/\['pink', '螢光粉'\]/);
console.log('Pending confirmation: indexed all-date paging, locked narrow confirmation, audit/concurrency/rollback, authority, backup compatibility and escaped inline action passed.');
