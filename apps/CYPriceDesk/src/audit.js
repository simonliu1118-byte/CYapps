// Append-only audit log: the statement every write batches with its change, and GET /api/audit.
import {json} from './http.js';
import {PLATFORM_LABEL} from '../public/compare.js';

// The audit row is written in the same batch as its change. With `afterChange` it is only inserted when the
// preceding revision-guarded statement really changed a row.
export const auditStmt = (DB, actor, now, action, entityId, before, after, afterChange = false) => DB.prepare(
  `INSERT INTO audit_log (at,actor_employee_id,actor_name,action,entity_id,before_json,after_json) SELECT ?,?,?,?,?,?,?${afterChange ? ' WHERE changes() > 0' : ''}`
).bind(now, actor.employeeId, actor.displayName, action, entityId, before && JSON.stringify(before), after && JSON.stringify(after));

const VERDICT = {same: '同規格', other_package: '其他包裝', not_same: '不是同一商品'};

function detailOf(action, before, after) {
  const record = after || before;
  if (action.startsWith('grok.')) return ({create:'建立任務',stop:'停止判讀',discard_judge:after.taskId ? '刪除判讀批次' : '清除全部判讀批次',dispatch:after.status === 'sent' ? '派工已送出' : '派工失敗',claim:'領取任務',progress:'平台進度',draft:'回填建檔',results:'回填判讀',questions:'提出問題',answer:'回答問題',review:'採用判讀',expired:'條件已過期',recheck:'要求再查',discard:'捨棄補完',resend:'重送任務'}[action.slice(5)] || action) + ' · Grok';
  if (action==='candidate.clear_pending') return `${after.name} · 清除 ${after.count} 筆待確認候選`;
  if (action==='candidate.delete') return '刪除當輪候選 · '+(PLATFORM_LABEL[record.source]||record.source)+' '+record.listingKey;
  if (action.startsWith('tracked.') && action !== 'tracked.price') return `${record.name}（${record.model}）`;
  if (action === 'tracked.price') return `${record.name} · ${PLATFORM_LABEL[record.platform]}：${before?.myPrice ?? '未設定'} → ${after.myPrice ?? '停止比價'}`;
  if (action.startsWith('manual.')) return `${record.name} · ${PLATFORM_LABEL[record.platform]} · ${record.price}${record.seller ? ` · ${record.seller}` : ''}`;
  if (action === 'judgment.update') return `${record.name} · ${PLATFORM_LABEL[record.source]} · ${VERDICT[record.verdict]} · ${record.title}`;
  if (action === 'judgment.delete') return `${record.name} · ${PLATFORM_LABEL[record.source]} · 恢復 ${record.title}`;
  return [PLATFORM_LABEL[record.source] || record.source, record.model, record.listingKey].filter(Boolean).join(' · ');   // classification.*
}

export async function handleAuditApi(request, env, url) {
  if (url.pathname !== '/api/audit' || request.method !== 'GET') return null;
  const limit = Math.min(Math.max(Math.trunc(Number(url.searchParams.get('limit'))) || 50, 1), 200);
  const rows = (await env.DB.prepare('SELECT * FROM audit_log ORDER BY at DESC, id DESC LIMIT ?').bind(limit).all()).results;
  return json({entries: rows.map(row => ({
    id: row.id, at: row.at, actor: row.actor_name, action: row.action,
    detail: detailOf(row.action, row.before_json && JSON.parse(row.before_json), row.after_json && JSON.parse(row.after_json))
  }))});
}
