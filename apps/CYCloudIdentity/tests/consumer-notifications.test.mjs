import assert from 'node:assert/strict';
import test from 'node:test';
import { fixture, notifySuperAdmin } from './authority-fixture.mjs';

const input = { workspaceId: 'workspace-one', applicationId: 'OTHER_APP',
  type: 'backup_health_failed', eventId: '11111111-1111-4111-8111-111111111111' };
const setup=()=>{const f=fixture();f.env.EMAIL_DAILY_BUDGET='100';return f;};
const now = new Date('2026-10-15T14:00:00Z');
test('notification resolves current protected Super Admin, deduplicates and uses shared Email budgets', async () => {
  const {db, env} = setup(); const messages=[];
  const sender={async send(message) {messages.push(message);return {provider:'test',providerMessageId:'TEST'};}};
  try {
    assert.equal((await notifySuperAdmin(env,input,{now,sender})).status,'delivered');
    assert.equal(messages[0].to,'one@example.test');
    assert.match(messages[0].text,/Other App/);
    assert.doesNotMatch(JSON.stringify(messages),/employee-one|workspace-one|11111111/);
    assert.equal((await notifySuperAdmin(env,input,{now,sender})).status,'already_delivered');
    assert.equal(messages.length,1);
    const next={...input,eventId:'22222222-2222-4222-8222-222222222222'};
    assert.equal((await notifySuperAdmin(env,next,{now,sender})).status,'deferred');
    db.prepare("UPDATE employees SET email_normalized='changed@example.test' WHERE employee_id='employee-one'").run();
    assert.equal((await notifySuperAdmin(env,next,{now:new Date(now.getTime()+3600001),sender})).status,'delivered');
    assert.equal(messages[1].to,'changed@example.test');
    assert.equal(db.prepare('SELECT sent_count,reserved_count FROM email_delivery_budget').get().sent_count,2);
    assert.equal(db.prepare('SELECT reserved_count FROM workspace_email_delivery_budget').get().reserved_count,0);
    assert.doesNotMatch(JSON.stringify(db.prepare('SELECT * FROM consumer_notifications').all()),/@|credential|Session/);
  } finally {db.close();}
});
test('private capability rejects arbitrary content, disabled scopes and unverified recipients; failed delivery remains retryable',async()=>{
  const {db,env}=setup();let sends=0;
  const sender={async send(){sends++;throw new Error('private recipient diagnostics');}};
  try {
    for(const patch of [{to:'attacker@example.test'},{text:'arbitrary'},{type:'other'},{eventId:'bad'}])
      await assert.rejects(notifySuperAdmin(env,{...input,...patch},{now,sender}),/NOTIFICATION_REQUEST_INVALID/);
    db.prepare("UPDATE workspace_applications SET enabled=0 WHERE workspace_id='workspace-one' AND application_id='OTHER_APP'").run();
    await assert.rejects(notifySuperAdmin(env,input,{now,sender}),/NOTIFICATION_AUTHORITY_UNAVAILABLE/);
    db.prepare("UPDATE workspace_applications SET enabled=1 WHERE workspace_id='workspace-one'").run();
    db.prepare("UPDATE employees SET email_verified_at=NULL WHERE employee_id='employee-one'").run();
    await assert.rejects(notifySuperAdmin(env,input,{now,sender}),/NOTIFICATION_AUTHORITY_UNAVAILABLE/);
    db.prepare("UPDATE employees SET email_verified_at='2026-01-01' WHERE employee_id='employee-one'").run();
    await assert.rejects(notifySuperAdmin(env,input,{now,sender}),error=>error.message==='NOTIFICATION_DELIVERY_FAILED');
    assert.equal(sends,1);
    assert.equal(db.prepare('SELECT reserved_count,sent_count FROM email_delivery_budget').get().reserved_count,0);
    assert.equal(db.prepare('SELECT sent_at FROM consumer_notifications').get().sent_at,null);
    const good={async send(){return {provider:'test',providerMessageId:'TEST'};}};
    assert.equal((await notifySuperAdmin(env,input,{now,sender:good})).status,'delivered');
  }finally{db.close();}
});
test('concurrent delivery claims only one slot',async()=>{
  const {db,env}=setup();let release;
  const sender={send(){return new Promise(resolve=>{release=()=>resolve({provider:'test',providerMessageId:'TEST'});});}};
  try {
    let failure;
    const first=notifySuperAdmin(env,input,{now,sender});
    first.catch(error=>{failure=error;});
    while(!release && !failure) await new Promise(resolve=>setImmediate(resolve));
    if(failure) throw failure;
    assert.equal((await notifySuperAdmin(env,input,{now,sender})).status,'deferred');
    release();assert.equal((await first).status,'delivered');
  }finally{db.close();}
});
