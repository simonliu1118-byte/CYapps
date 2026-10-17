import assert from 'node:assert/strict';
import app from '../src/app.js';
import { FixtureD1 } from './support/backup-fixtures.mjs';
const db=new FixtureD1();let role='SUPER_ADMIN';
const env={DB:db,BACKUP_TOPOLOGY:'parallel_dual_provider',CYID_WORKSPACE_ID:'workspace-ci-placeholder',CYID_APPLICATION_ID:'CYACC_CI',IDENTITY:{async fetch(){return Response.json({principal:{workspaceId:'workspace-ci-placeholder',workspaceStatus:'active',workspaceRole:role,employeeId:'TEST_EMPLOYEE',employeeNo:'0123',displayName:'TEST',isIdentityAdmin:false,employeeStatus:'active',appId:'CYACC_CI',appAccessGranted:true,emailVerified:true,isWorkspaceSuperAdmin:role==='SUPER_ADMIN',credentialVersion:1,employeeRevision:1},session:{expiresAt:new Date(Date.now()+3600000).toISOString()}});}}};
const call=(path,method='GET',body,epoch)=>app.fetch(new Request('https://example.invalid'+path,{method,headers:{cookie:'cyaccounting_session=cyid_'+'b'.repeat(64),'content-type':'application/json',...(epoch!==undefined?{'x-cyacc-book-epoch':epoch}:{})},...(body?{body:JSON.stringify(body)}:{})}),env);
assert.equal((await (await call('/api/bootstrap')).json()).accountingEpoch,'');
db.db.exec("INSERT INTO meta VALUES('accounting_epoch','TEST_NEW_BOOK')");
const before=db.db.prepare('SELECT COUNT(*) AS n FROM transactions').get().n;
const transaction={txDate:'2026-01-01',accountName:'現金',kind:'income',categoryName:'一般收入',summary:'TEST',amount:1};
for(const epoch of [undefined,'','TEST_OLD_BOOK']) {
  const response=await call('/api/transactions','POST',transaction,epoch);assert.equal(response.status,409);assert.equal((await response.json()).code,'ACCOUNTING_BOOK_CHANGED');
}
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM transactions').get().n,before);
assert.equal((await call('/api/transactions','POST',transaction,'TEST_NEW_BOOK')).status,201);
assert.equal((await (await call('/api/bootstrap')).json()).accountingEpoch,'TEST_NEW_BOOK');
db.db.exec("INSERT INTO meta VALUES('recovery_hold','1')");
assert.equal((await call('/api/bootstrap')).status,503,'unpromoted workspace cannot become an ordinary book');
assert.equal((await call('/api/transactions','POST',transaction,'TEST_NEW_BOOK')).status,503);
role='ADMIN';assert.equal((await call('/recovery')).status,403,'standalone recovery remains SUPER_ADMIN only');
db.close();
console.log('Recovery book epoch: old page writes rejected, correct epoch accepted, held workspace blocked, original authority unchanged.');
