export async function handleV12Api(request, env) {
  const url = new URL(request.url);
  if (!env.DB) return null;

  let match = url.pathname.match(/^\/api\/categories\/(\d+)\/group$/);
  if (match && request.method === 'PUT') {
    return handleMoveCategoryToGroup(Number(match[1]), request, env.DB);
  }

  if (url.pathname === '/api/settings/lock/step' && request.method === 'PUT') {
    return handleStepLock(request, env.DB);
  }

  return null;
}

async function handleMoveCategoryToGroup(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '科目編號錯誤。' }, 400);
  const body = await request.json().catch(() => null);
  const targetGroupId = Number(body?.groupId);
  if (!validId(targetGroupId)) return json({ ok: false, error: '目標大分類錯誤。' }, 400);

  const [category, target] = await Promise.all([
    db.prepare('SELECT id, kind, group_id, name FROM categories WHERE id = ?').bind(id).first(),
    db.prepare('SELECT id, kind, name FROM category_groups WHERE id = ?').bind(targetGroupId).first()
  ]);
  if (!category) return json({ ok: false, error: '找不到科目。' }, 404);
  if (!target) return json({ ok: false, error: '找不到目標大分類。' }, 404);
  if (String(category.kind) !== String(target.kind)) {
    return json({ ok: false, error: '收入科目與支出科目不可互相移動。' }, 409);
  }

  const oldGroupId = Number(category.group_id);
  if (oldGroupId === targetGroupId) {
    return json({ ok: true, moved: false, groupId: targetGroupId });
  }

  const next = await db.prepare(
    'SELECT COALESCE(MAX(sort_order), -1) + 1 AS next_order FROM categories WHERE group_id = ?'
  ).bind(targetGroupId).first();

  await db.prepare('UPDATE categories SET group_id = ?, sort_order = ? WHERE id = ?')
    .bind(targetGroupId, Number(next?.next_order || 0), id).run();
  await normalizeCategoryOrder(db, oldGroupId);
  await normalizeCategoryOrder(db, targetGroupId);

  return json({
    ok: true,
    moved: true,
    categoryId: id,
    groupId: targetGroupId,
    groupName: String(target.name || '')
  });
}

async function handleStepLock(request, db) {
  const body = await request.json().catch(() => null);
  const direction = String(body?.direction || '');
  if (!['forward', 'backward'].includes(direction)) {
    return json({ ok: false, error: '逐月鎖帳方向錯誤。' }, 400);
  }

  const current = await getLockedThrough(db);
  const earliest = await getEarliestAccountingMonth(db);
  const thisMonth = currentTaipeiMonth();

  if (direction === 'forward') {
    let next;
    if (current) {
      if (current >= thisMonth) {
        return json({ ok: false, error: '目前已鎖帳至本月，逐月操作不能再往未來鎖帳。' }, 409);
      }
      next = shiftMonth(current, 1);
    } else {
      next = earliest && earliest <= thisMonth ? earliest : thisMonth;
    }

    if (!isMonth(next) || next > thisMonth) {
      return json({ ok: false, error: '下一個可鎖帳月份超過本月。' }, 409);
    }
    await setLockedThrough(db, next);
    return json({
      ok: true,
      lockedThrough: next,
      direction,
      message: `已逐月鎖帳至 ${formatMonth(next)}。`
    });
  }

  if (!current) {
    return json({ ok: true, lockedThrough: null, direction, message: '目前未鎖帳。' });
  }

  const previous = shiftMonth(current, -1);
  const nextValue = earliest && current <= earliest ? '' : previous;
  await setLockedThrough(db, nextValue);
  return json({
    ok: true,
    lockedThrough: nextValue || null,
    direction,
    message: nextValue ? `已退回鎖帳至 ${formatMonth(nextValue)}。` : '已退回為未鎖帳。'
  });
}

async function getEarliestAccountingMonth(db) {
  const row = await db.prepare(`
    SELECT MIN(month) AS earliest
    FROM (
      SELECT substr(tx_date, 1, 7) AS month FROM transactions
      UNION ALL
      SELECT month FROM opening_balances
    )
    WHERE month GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]'
  `).first();
  const value = String(row?.earliest || '').trim();
  return isMonth(value) ? value : null;
}

async function getLockedThrough(db) {
  const row = await db.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first();
  const value = String(row?.value || '').trim();
  return isMonth(value) ? value : null;
}

async function setLockedThrough(db, value) {
  await db.prepare(`
    INSERT INTO app_settings(key, value) VALUES ('locked_through', ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).bind(value || '').run();
}

async function normalizeCategoryOrder(db, groupId) {
  const result = await db.prepare('SELECT id FROM categories WHERE group_id = ? ORDER BY sort_order, id')
    .bind(groupId).all();
  const rows = result.results || [];
  if (!rows.length) return;
  await db.batch(rows.map((row, index) =>
    db.prepare('UPDATE categories SET sort_order = ? WHERE id = ?').bind(index, row.id)
  ));
}

function shiftMonth(value, delta) {
  if (!isMonth(value)) return '';
  const [year, month] = value.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1 + delta, 1));
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`;
}

function currentTaipeiMonth() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Taipei',
    year: 'numeric',
    month: '2-digit'
  }).formatToParts(new Date());
  const year = parts.find(part => part.type === 'year')?.value;
  const month = parts.find(part => part.type === 'month')?.value;
  const value = `${year || ''}-${month || ''}`;
  return isMonth(value) ? value : new Date().toISOString().slice(0, 7);
}

function formatMonth(value) {
  const [year, month] = String(value || '').split('-');
  return year && month ? `${year}年${month}月` : '';
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}

function validId(value) {
  return Number.isInteger(value) && value > 0;
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff'
    }
  });
}
