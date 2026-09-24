const QUICK_ENTRY_DEFAULTS = Object.freeze({
  basis: 'tx_date',
  recentCount: 100,
  minCount: 3
});

export async function handleV11Api(request, env) {
  const url = new URL(request.url);
  if (!env.DB) return null;

  if (url.pathname === '/api/settings/quick-entry') {
    if (request.method === 'GET') return handleGetQuickEntrySettings(env.DB);
    if (request.method === 'PUT') return handleSetQuickEntrySettings(request, env.DB);
  }

  if (url.pathname === '/api/summaries/frequent' && request.method === 'GET') {
    return handleFrequentSummaries(url, env.DB);
  }

  let match = url.pathname.match(/^\/api\/accounts\/(\d+)\/move$/);
  if (match && request.method === 'PUT') return handleMoveAccount(Number(match[1]), request, env.DB);

  match = url.pathname.match(/^\/api\/category-groups\/(\d+)\/move$/);
  if (match && request.method === 'PUT') return handleMoveGroup(Number(match[1]), request, env.DB);

  match = url.pathname.match(/^\/api\/categories\/(\d+)\/move$/);
  if (match && request.method === 'PUT') return handleMoveCategory(Number(match[1]), request, env.DB);

  return null;
}

async function handleGetQuickEntrySettings(db) {
  return json({ ok: true, settings: await getQuickEntrySettings(db) });
}

async function handleSetQuickEntrySettings(request, db) {
  const body = await request.json().catch(() => null);
  if (!body) return json({ ok: false, error: '設定資料格式錯誤。' }, 400);

  const basis = String(body.basis || '');
  const recentCount = Number(body.recentCount);
  const minCount = Number(body.minCount);

  if (!['tx_date', 'created_at'].includes(basis)) {
    return json({ ok: false, error: '常用摘要統計依據錯誤。' }, 400);
  }
  if (!Number.isInteger(recentCount) || recentCount < 10 || recentCount > 1000) {
    return json({ ok: false, error: '最近筆數必須為 10～1000。' }, 400);
  }
  if (!Number.isInteger(minCount) || minCount < 2 || minCount > 20 || minCount > recentCount) {
    return json({ ok: false, error: '最低出現次數必須為 2～20，且不可大於最近筆數。' }, 400);
  }

  await db.batch([
    upsertSetting(db, 'frequent_summary_basis', basis),
    upsertSetting(db, 'frequent_summary_recent_count', String(recentCount)),
    upsertSetting(db, 'frequent_summary_min_count', String(minCount))
  ]);

  return json({ ok: true, settings: { basis, recentCount, minCount } });
}

async function getQuickEntrySettings(db) {
  const result = await db.prepare(`
    SELECT key, value
    FROM app_settings
    WHERE key IN ('frequent_summary_basis', 'frequent_summary_recent_count', 'frequent_summary_min_count')
  `).all();
  const values = new Map((result.results || []).map(row => [String(row.key), String(row.value)]));

  const basis = values.get('frequent_summary_basis');
  const recent = Number(values.get('frequent_summary_recent_count'));
  const minimum = Number(values.get('frequent_summary_min_count'));

  return {
    basis: ['tx_date', 'created_at'].includes(basis) ? basis : QUICK_ENTRY_DEFAULTS.basis,
    recentCount: Number.isInteger(recent) && recent >= 10 && recent <= 1000 ? recent : QUICK_ENTRY_DEFAULTS.recentCount,
    minCount: Number.isInteger(minimum) && minimum >= 2 && minimum <= 20 ? minimum : QUICK_ENTRY_DEFAULTS.minCount
  };
}

async function handleFrequentSummaries(url, db) {
  const kind = String(url.searchParams.get('kind') || '').trim();
  const account = normalizeName(url.searchParams.get('account'));
  const category = normalizeName(url.searchParams.get('category'));
  if (!['income', 'expense'].includes(kind) || !account || !category) {
    return json({ ok: false, error: '常用摘要查詢條件不完整。' }, 400);
  }

  const settings = await getQuickEntrySettings(db);
  const order = settings.basis === 'created_at'
    ? 'created_at DESC, id DESC'
    : 'tx_date DESC, created_at DESC, id DESC';
  const result = await db.prepare(`
    SELECT summary, tx_date, created_at, id
    FROM transactions
    WHERE kind = ? AND account_name = ? AND category_name = ?
    ORDER BY ${order}
    LIMIT ?
  `).bind(kind, account, category, settings.recentCount).all();

  const stats = new Map();
  for (const [rank, row] of (result.results || []).entries()) {
    const summary = String(row.summary || '').trim();
    if (!summary) continue;
    const item = stats.get(summary) || { count: 0, latestRank: rank };
    item.count += 1;
    stats.set(summary, item);
  }

  const summaries = [...stats.entries()]
    .filter(([, meta]) => meta.count >= settings.minCount)
    .sort((a, b) => b[1].count - a[1].count || a[1].latestRank - b[1].latestRank)
    .slice(0, 10)
    .map(([summary]) => summary);

  return json({ ok: true, summaries, settings });
}

async function handleMoveAccount(id, request, db) {
  const direction = await directionFromRequest(request);
  if (!direction) return json({ ok: false, error: '排序方向錯誤。' }, 400);
  const row = await db.prepare('SELECT id FROM accounts WHERE id = ?').bind(id).first();
  if (!row) return json({ ok: false, error: '找不到帳戶。' }, 404);
  await moveWithin(db, 'accounts', '1 = 1', [], id, direction);
  return json({ ok: true });
}

async function handleMoveGroup(id, request, db) {
  const direction = await directionFromRequest(request);
  if (!direction) return json({ ok: false, error: '排序方向錯誤。' }, 400);
  const row = await db.prepare('SELECT id, kind FROM category_groups WHERE id = ?').bind(id).first();
  if (!row) return json({ ok: false, error: '找不到大分類。' }, 404);
  await moveWithin(db, 'category_groups', 'kind = ?', [String(row.kind)], id, direction);
  return json({ ok: true });
}

async function handleMoveCategory(id, request, db) {
  const direction = await directionFromRequest(request);
  if (!direction) return json({ ok: false, error: '排序方向錯誤。' }, 400);
  const row = await db.prepare('SELECT id, group_id FROM categories WHERE id = ?').bind(id).first();
  if (!row) return json({ ok: false, error: '找不到科目。' }, 404);
  await moveWithin(db, 'categories', 'group_id = ?', [Number(row.group_id)], id, direction);
  return json({ ok: true });
}

async function moveWithin(db, table, whereSql, whereBindings, id, direction) {
  const allowed = new Set(['accounts', 'category_groups', 'categories']);
  if (!allowed.has(table)) throw new Error('unsupported_order_table');

  const list = await db.prepare(`
    SELECT id, sort_order
    FROM ${table}
    WHERE ${whereSql}
    ORDER BY sort_order, id
  `).bind(...whereBindings).all();
  const ids = (list.results || []).map(row => Number(row.id));
  const index = ids.indexOf(Number(id));
  if (index < 0) return;
  const targetIndex = direction === 'up' ? index - 1 : index + 1;
  if (targetIndex < 0 || targetIndex >= ids.length) return;

  await db.batch(ids.map((rowId, order) =>
    db.prepare(`UPDATE ${table} SET sort_order = ? WHERE id = ?`).bind(order, rowId)
  ));

  const targetId = ids[targetIndex];
  await db.batch([
    db.prepare(`UPDATE ${table} SET sort_order = ? WHERE id = ?`).bind(targetIndex, id),
    db.prepare(`UPDATE ${table} SET sort_order = ? WHERE id = ?`).bind(index, targetId)
  ]);
}

async function directionFromRequest(request) {
  const body = await request.json().catch(() => null);
  const direction = String(body?.direction || '');
  return direction === 'up' || direction === 'down' ? direction : null;
}

function upsertSetting(db, key, value) {
  return db.prepare(`
    INSERT INTO app_settings(key, value) VALUES (?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).bind(key, value);
}

function normalizeName(value) {
  const name = String(value ?? '').trim();
  return name ? name.slice(0, 60) : '';
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
