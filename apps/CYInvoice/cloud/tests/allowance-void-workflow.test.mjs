import assert from "node:assert/strict";
import {DatabaseSync} from "node:sqlite";
import {readFileSync,readdirSync} from "node:fs";
// Synthetic SQLite only; no remote D1 and no AMEGO network activity.
const db=new DatabaseSync(":memory:");
const migrations=readdirSync("migrations").filter(x=>/^\d{4}_.*\.sql$/.test(x)).sort();
for(const file of migrations.filter(x=>x<"0014"))
 db.exec(readFileSync("migrations/"+file,"utf8"));
const ws="synthetic_ws";
const devA="dev_11111111-1111-4111-8111-111111111111";
const devB="dev_22222222-2222-4222-8222-222222222222";
db.prepare("INSERT INTO workspaces(workspace_id,display_name) VALUES (?,?)")
 .run(ws,"Synthetic");
for(const dev of [devA,devB])
 db.prepare("INSERT INTO devices(device_id,workspace_id,display_name,status,employee_authority_state) "+
 "VALUES (?,?,'Synthetic','active','cloud')").run(dev,ws);
db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql","utf8"));
for(const file of migrations.filter(x=>x>="0014"))
 db.exec(readFileSync("migrations/"+file,"utf8"));
for(const file of ["0015_work_items.sql","0017_company_settings.sql",
 "0019_allowance_void_workflow.sql"])
 db.exec(readFileSync("drafts/"+file,"utf8"));
db.prepare("INSERT INTO company_settings(workspace_id,seller_invoice,amego_app_key_ciphertext,"+
 "revision,changed_by_employee_id,changed_by_device_id,changed_at) "+
 "VALUES (?,?,?,1,'cyid_manager',?,?)")
 .run(ws,"12345678","SYNTHETIC_PLACEHOLDER_NOT_A_SECRET",devA,
 "2026-10-15T01:00:00.000Z");
const item="wi_synthetic_allowance_void_01";
const time="2026-10-15T01:00:00.000Z",hash="a".repeat(64);
function work(id=item,reference=hash){
 db.prepare("INSERT INTO work_items(work_item_id,workspace_id,opened_by_device_id,"+
 "updated_by_device_id,environment,kind,reference_hash,status,revision,actor_id,"+
 "created_at,updated_at) "+
 "VALUES (?,?,?,?,'test','ALLOWANCE_VOID',?,'open',1,'cyid_user',?,?)")
 .run(id,ws,devA,devA,reference,time,time);
}
work();
const request="INSERT INTO allowance_void_requests(work_item_id,workspace_id,"+
 "seller_invoice,company_settings_revision,environment,allowance_number,"+
 "original_invoice_number,reason,applicant_employee_id,applicant_device_id,"+
 "reference_hash,created_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)";
const req=[item,ws,"12345678",1,"test","3821061800001","AB12345678",
 "Synthetic return","cyid_user",devA,hash,time];
assert.throws(()=>db.prepare(request).run(...req.slice(0,9),devB,...req.slice(10)),
 /ALLOWANCE_VOID_SOURCE_MISMATCH/);
db.prepare(request).run(...req);
assert.throws(()=>db.prepare("UPDATE allowance_void_requests SET reason='x'").run(),
 /ALLOWANCE_VOID_REQUEST_IMMUTABLE/);
assert.throws(()=>db.prepare("DELETE FROM allowance_void_requests").run(),
 /ALLOWANCE_VOID_REQUEST_IMMUTABLE/);
const other="wi_synthetic_allowance_void_02";
work(other,"b".repeat(64));
assert.throws(()=>db.prepare(request).run(other,ws,"12345678",1,"test",
 "3821061800001","AB12345678","other","cyid_user",devA,"b".repeat(64),time),
 /UNIQUE|constraint/i,"A second Work Item cannot cancel the same number");
db.prepare("UPDATE work_items SET status='review',revision=2 WHERE work_item_id=?")
 .run(item);
const approval="INSERT INTO allowance_void_approvals(approval_id,work_item_id,"+
 "workspace_id,expected_revision,official_snapshot_sha256,official_checked_at,"+
 "approved_by_employee_id,approved_by_device_id,approved_at,valid_until_utc) "+
 "VALUES (?,?,?,?,?,?,?,?,?,?)";
const granted="2026-10-15T02:00:00.000Z",expires="2026-10-15T02:15:00.000Z";
const appr=["ava_synthetic_01",item,ws,2,"c".repeat(64),granted,
 "cyid_manager",devB,granted,expires];
assert.throws(()=>db.prepare(approval).run(...appr.slice(0,3),1,...appr.slice(4)),
 /ALLOWANCE_VOID_APPROVAL_SOURCE_CHANGED/);
db.prepare(approval).run(...appr);
assert.throws(()=>db.prepare("UPDATE allowance_void_approvals SET valid_until_utc='2099-01-01'").run(),
 /ALLOWANCE_VOID_AUDIT_APPEND_ONLY/);
assert.throws(()=>db.prepare("DELETE FROM allowance_void_approvals").run(),
 /ALLOWANCE_VOID_AUDIT_APPEND_ONLY/);
const claim="INSERT INTO allowance_void_send_claims(work_item_id,workspace_id,"+
 "approval_id,allowance_number,expected_revision,claimed_by_employee_id,"+
 "claimed_by_device_id,claimed_at) VALUES (?,?,?,?,?,?,?,?)";
const attempt=[item,ws,"ava_synthetic_01","3821061800001",2,
 "cyid_manager",devB,"2026-10-15T02:01:00.000Z"];
assert.throws(()=>db.prepare(claim).run(...attempt.slice(0,3),"OTHER",
 ...attempt.slice(4)),/ALLOWANCE_VOID_SEND_NOT_AUTHORIZED/);
assert.throws(()=>db.prepare(claim).run(...attempt.slice(0,7),
 "2026-10-15T02:20:00.000Z"),/ALLOWANCE_VOID_SEND_NOT_AUTHORIZED/);
db.prepare(claim).run(...attempt);
assert.throws(()=>db.prepare(claim).run(...attempt),/UNIQUE|constraint/i);
assert.throws(()=>db.prepare("DELETE FROM allowance_void_send_claims").run(),
 /ALLOWANCE_VOID_SEND_ONCE/);
assert.throws(()=>db.prepare("UPDATE allowance_void_send_claims SET claimed_at='2099-01-01'").run(),
 /ALLOWANCE_VOID_SEND_ONCE/);
assert.throws(()=>db.prepare("UPDATE work_items SET status='closed' WHERE work_item_id=?")
 .run(item),/ALLOWANCE_VOID_OFFICIAL_PROOF_REQUIRED/);
const check="INSERT INTO allowance_void_official_checks(check_id,work_item_id,"+
 "workspace_id,allowance_number,official_type,official_upload_status,"+
 "official_snapshot_sha256,checked_at,checked_by_employee_id,checked_by_device_id) "+
 "VALUES (?,?,?,?,?,?,?,?,?,?)";
function observation(id,kind,status,device=devB){
 return [id,item,ws,"3821061800001",kind,status,"d".repeat(64),
 "2026-10-15T02:10:00.000Z","cyid_manager",device];
}
db.prepare(check).run(...observation("avc_pending","D0501",31));
assert.throws(()=>db.prepare("UPDATE work_items SET status='closed' WHERE work_item_id=?")
 .run(item),/ALLOWANCE_VOID_OFFICIAL_PROOF_REQUIRED/);
db.prepare(check).run(...observation("avc_complete","D0501",99));
assert.equal(db.prepare("UPDATE work_items SET status='closed' WHERE work_item_id=?")
 .run(item).changes,1);
assert.throws(()=>db.prepare("DELETE FROM allowance_void_official_checks").run(),
 /ALLOWANCE_VOID_AUDIT_APPEND_ONLY/);
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(),[]);
db.close();
console.log("D1 0019 synthetic: request, review, append-only grant, one-shot claim and verified closure passed");
