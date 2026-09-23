const MAX_AMOUNT = 9_999_999;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!url.pathname.startsWith('/api/')) {
      return env.ASSETS.fetch(request);
    }

    try {
      if (url.pathname === '/api/health' && request.method === 'GET') {
        return json({ ok: true, service: 'CYAccounting Web', database: Boolean(env.DB) });
      }
      if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。' }, 503);

      if (url.pathname === '/api/bootstrap' && request.method === 'GET') return handleBootstrap(env.DB);
      if (url.pathname === '/api/transactions' && request.method === 'GET') return handleListTransactions(url, env.DB);
      if (url.pathname === '/api/transactions' && request.method === 'POST') return handleCreateTransaction(request, env.DB);
      if (url.pathname === '/api/summaries/frequent' && request.method === 'GET') return handleFrequentSummaries(url, env.DB);

      let match = url.pathname.match(/^\/api\/transactions\/(\d+)$/);
      if (match && request.method === 'PUT') return handleUpdateTransaction(Number(match[1]), request, env.DB);
      if (match && request.method === 'DELETE') return handleDeleteTransaction(Number(match[1]), env.DB);

      if (url.pathname === '/api/accounts' && request.method === 'POST') return handleCreateAccount(request, env.DB);
      match = url.pathname.match(/^\/api\/accounts\/(\d+)$/);
      if (match && request.method === 'PUT') return handleRenameAccount(Number(match[1]), request, env.DB);
      if (match && request.method === 'DELETE') return handleDeleteAccount(Number(match[1]), env.DB);
      match = url.pathname.match(/^\/api\/accounts\/(\d+)\/default$/);
      if (match && request.method === 'POST') return handleDefaultAccount(Number(match[1]), env.DB);

      if (url.pathname === '/api/category-groups' && request.method === 'POST') return handleCreateGroup(request, env.DB);
      match = url.pathname.match(/^\/api\/category-groups\/(\d+)$/);
      if (match && request.method === 'PUT') return handleRenameGroup(Number(match[1]), request, env.DB);
      if (match && request.method === 'DELETE') return handleDeleteGroup(Number(match[1]), env.DB);

      if (url.pathname === '/api/categories' && request.method === 'POST') return handleCreateCategory(request, env.DB);
      match = url.pathname.match(/^\/api\/categories\/(\d+)\/favorite$/);
      if (match && request.method === 'PUT') return handleSetCategoryFavorite(Number(match[1]), request, env.DB);
      match = url.pathname.match(/^\/api\/categories\/(\d+)$/);
      if (match && request.method === 'PUT') return handleRenameCategory(Number(match[1]), request, env.DB);
      if (match && request.method === 'DELETE') return handleDeleteCategory(Number(match[1]), env.DB);

      if (url.pathname === '/api/opening-balances' && request.method === 'GET') return handleGetOpeningBalances(url, env.DB);
      if (url.pathname === '/api/opening-balances' && request.method === 'PUT') return handleSetOpeningBalances(request, env.DB);

      if (url.pathname === '/api/settings/lock' && request.method === 'GET') {
        return json({ ok: true, lockedThrough: await getLockedThrough(env.DB) });
      }
      if (url.pathname === '/api/settings/lock' && request.method === 'PUT') return handleSetLock(request, env.DB);

      return json({ ok: false, error: '找不到 API。' }, 404);
    } catch (error) {
      console.error(error);
      return json({ ok: false, error: '伺服器處理失敗。' }, 500);
    }
  }
};

async function handleBootstrap(db) {
  const [accounts, groups, categories, lockedThrough] = await Promise.all([
    db.prepare('SELECT id, name, sort_order, is_default FROM accounts ORDER BY sort_order, id').all(),
    db.prepare('SELECT id, kind, name, sort_order FROM category_groups ORDER BY kind, sort_order, id').all(),
    db.prepare(`
      SELECT c.id, c.kind, c.name, c.sort_order, c.is_favorite,
             g.id AS group_id, g.name AS group_name, g.sort_order AS group_sort_order
      FROM categories c
      JOIN category_groups g ON g.id = c.group_id
      ORDER BY c.kind, g.sort_order, g.id, c.sort_order, c.id
    `).all(),
    getLockedThrough(db)
  ]);
  return json({
    ok: true,
    accounts: accounts.results || [],
    groups: groups.results || [],
    categories: categories.results || [],
    lockedThrough
  });
}

async function handleListTransactions(url, db) {
  const month = url.searchParams.get('month') || currentMonth();
  if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
  const [result, lockedThrough] = await Promise.all([
    db.prepare(`
      SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at
      FROM transactions
      WHERE substr(tx_date, 1, 7) = ?
      ORDER BY tx_date DESC, created_at DESC, id DESC
    `).bind(month).all(),
    getLockedThrough(db)
  ]);
  return json({ ok: true, month, locked: isMonthLocked(month, lockedThrough), lockedThrough, transactions: result.results || [] });
}

async function handleCreateTransaction(request, db) {
  const body = await bodyJson(request);
  if (!body) return json({ ok: false, error: '資料格式錯誤。' }, 400);
  const values = normalizeTransactionBody(body);
  const validation = await validateTransaction(values, db);
  if (validation) return validation;
  const lockedThrough = await getLockedThrough(db);
  if (isMonthLocked(values.txDate.slice(0, 7), lockedThrough)) {
    return json({ ok: false, error: `${values.txDate.slice(0, 7)} 已鎖定，無法新增資料。` }, 409);
  }
  const now = new Date().toISOString();
  const result = await db.prepare(`
    INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(values.txDate, values.accountName, values.kind, values.categoryName, values.summary, values.amount, now, now).run();
  return json({ ok: true, id: result.meta?.last_row_id ?? null }, 201);
}

async function handleUpdateTransaction(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '交易編號錯誤。' }, 400);
  const old = await db.prepare('SELECT * FROM transactions WHERE id = ?').bind(id).first();
  if (!old) return json({ ok: false, error: '找不到交易。' }, 404);
  const lockedThrough = await getLockedThrough(db);
  const oldMonth = String(old.tx_date).slice(0, 7);
  if (isMonthLocked(oldMonth, lockedThrough)) return json({ ok: false, error: `${oldMonth} 已鎖定，無法編輯。` }, 409);

  const body = await bodyJson(request);
  if (!body) return json({ ok: false, error: '資料格式錯誤。' }, 400);
  const values = normalizeTransactionBody({ ...body, kind: old.kind });
  const validation = await validateTransaction(values, db);
  if (validation) return validation;
  const newMonth = values.txDate.slice(0, 7);
  if (isMonthLocked(newMonth, lockedThrough)) return json({ ok: false, error: `${newMonth} 已鎖定，無法移入。` }, 409);

  await db.prepare(`
    UPDATE transactions
    SET tx_date = ?, account_name = ?, category_name = ?, summary = ?, amount = ?, updated_at = ?
    WHERE id = ?
  `).bind(values.txDate, values.accountName, values.categoryName, values.summary, values.amount, new Date().toISOString(), id).run();
  return json({ ok: true });
}

async function handleDeleteTransaction(id, db) {
  if (!validId(id)) return json({ ok: false, error: '交易編號錯誤。' }, 400);
  const row = await db.prepare('SELECT tx_date FROM transactions WHERE id = ?').bind(id).first();
  if (!row) return json({ ok: false, error: '找不到交易。' }, 404);
  const month = String(row.tx_date).slice(0, 7);
  if (isMonthLocked(month, await getLockedThrough(db))) return json({ ok: false, error: `${month} 已鎖定，無法刪除。` }, 409);
  await db.prepare('DELETE FROM transactions WHERE id = ?').bind(id).run();
  return json({ ok: true });
}

async function handleFrequentSummaries(url, db) {
  const kind = String(url.searchParams.get('kind') || '').trim();
  const account = normalizeName(url.searchParams.get('account'));
  const category = normalizeName(url.searchParams.get('category'));
  if (!['income', 'expense'].includes(kind) || !account || !category) {
    return json({ ok: false, error: '常用摘要查詢條件不完整。' }, 400);
  }
  const result = await db.prepare(`
    SELECT summary, tx_date, created_at, id
    FROM transactions
    WHERE kind = ? AND account_name = ? AND category_name = ?
    ORDER BY tx_date DESC, created_at DESC, id DESC
    LIMIT 100
  `).bind(kind, account, category).all();

  const stats = new Map();
  for (const [rank, row] of (result.results || []).entries()) {
    const summary = String(row.summary || '').trim();
    if (!summary) continue;
    const item = stats.get(summary) || { count: 0, latestRank: rank };
    item.count += 1;
    stats.set(summary, item);
  }
  const summaries = [...stats.entries()]
    .filter(([, meta]) => meta.count >= 3)
    .sort((a, b) => b[1].count - a[1].count || a[1].latestRank - b[1].latestRank)
    .slice(0, 10)
    .map(([summary]) => summary);
  return json({ ok: true, summaries });
}

async function handleCreateAccount(request, db) {
  const body = await bodyJson(request);
  const name = normalizeName(body?.name);
  if (!name) return json({ ok: false, error: '帳戶名稱不可空白。' }, 400);
  if (await db.prepare('SELECT 1 FROM accounts WHERE name = ?').bind(name).first()) return json({ ok: false, error: '帳戶名稱已存在。' }, 409);
  const row = await db.prepare('SELECT COALESCE(MAX(sort_order), -1) + 1 AS next_order FROM accounts').first();
  const result = await db.prepare('INSERT INTO accounts(name, sort_order, is_default, created_at) VALUES (?, ?, 0, ?)')
    .bind(name, Number(row?.next_order || 0), new Date().toISOString()).run();
  return json({ ok: true, id: result.meta?.last_row_id ?? null }, 201);
}

async function handleRenameAccount(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);
  if (!await db.prepare('SELECT 1 FROM accounts WHERE id = ?').bind(id).first()) return json({ ok: false, error: '找不到帳戶。' }, 404);
  const body = await bodyJson(request);
  const name = normalizeName(body?.name);
  if (!name) return json({ ok: false, error: '帳戶名稱不可空白。' }, 400);
  if (await db.prepare('SELECT 1 FROM accounts WHERE name = ? AND id <> ?').bind(name, id).first()) return json({ ok: false, error: '帳戶名稱已存在。' }, 409);
  await db.prepare('UPDATE accounts SET name = ? WHERE id = ?').bind(name, id).run();
  return json({ ok: true });
}

async function handleDeleteAccount(id, db) {
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);
  const [countRow, account] = await Promise.all([
    db.prepare('SELECT COUNT(*) AS count FROM accounts').first(),
    db.prepare('SELECT id, is_default FROM accounts WHERE id = ?').bind(id).first()
  ]);
  if (!account) return json({ ok: false, error: '找不到帳戶。' }, 404);
  if (Number(countRow?.count || 0) <= 1) return json({ ok: false, error: '帳戶至少需要保留一個。' }, 409);
  const replacement = await db.prepare('SELECT id FROM accounts WHERE id <> ? ORDER BY sort_order, id LIMIT 1').bind(id).first();
  const statements = [db.prepare('DELETE FROM accounts WHERE id = ?').bind(id)];
  if (Number(account.is_default) === 1 && replacement) {
    statements.push(db.prepare('UPDATE accounts SET is_default = CASE WHEN id = ? THEN 1 ELSE 0 END').bind(replacement.id));
  }
  await db.batch(statements);
  await normalizeAccountOrder(db);
  return json({ ok: true });
}

async function handleDefaultAccount(id, db) {
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);
  if (!await db.prepare('SELECT 1 FROM accounts WHERE id = ?').bind(id).first()) return json({ ok: false, error: '找不到帳戶。' }, 404);
  await db.prepare('UPDATE accounts SET is_default = CASE WHEN id = ? THEN 1 ELSE 0 END').bind(id).run();
  return json({ ok: true });
}

async function handleCreateGroup(request, db) {
  const body = await bodyJson(request);
  const kind = String(body?.kind || '');
  const name = normalizeName(body?.name);
  if (!['income', 'expense'].includes(kind)) return json({ ok: false, error: '收支類型錯誤。' }, 400);
  if (!name) return json({ ok: false, error: '大分類名稱不可空白。' }, 400);
  if (await db.prepare('SELECT 1 FROM category_groups WHERE kind = ? AND name = ?').bind(kind, name).first()) return json({ ok: false, error: '大分類名稱已存在。' }, 409);
  const order = await db.prepare('SELECT COALESCE(MAX(sort_order), -1) + 1 AS next_order FROM category_groups WHERE kind = ?').bind(kind).first();
  const result = await db.prepare('INSERT INTO category_groups(kind, name, sort_order, created_at) VALUES (?, ?, ?, ?)')
    .bind(kind, name, Number(order?.next_order || 0), new Date().toISOString()).run();
  return json({ ok: true, id: result.meta?.last_row_id ?? null }, 201);
}

async function handleRenameGroup(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '大分類編號錯誤。' }, 400);
  const group = await db.prepare('SELECT kind FROM category_groups WHERE id = ?').bind(id).first();
  if (!group) return json({ ok: false, error: '找不到大分類。' }, 404);
  const body = await bodyJson(request);
  const name = normalizeName(body?.name);
  if (!name) return json({ ok: false, error: '大分類名稱不可空白。' }, 400);
  if (await db.prepare('SELECT 1 FROM category_groups WHERE kind = ? AND name = ? AND id <> ?').bind(group.kind, name, id).first()) return json({ ok: false, error: '大分類名稱已存在。' }, 409);
  await db.prepare('UPDATE category_groups SET name = ? WHERE id = ?').bind(name, id).run();
  return json({ ok: true });
}

async function handleDeleteGroup(id, db) {
  if (!validId(id)) return json({ ok: false, error: '大分類編號錯誤。' }, 400);
  if (!await db.prepare('SELECT 1 FROM category_groups WHERE id = ?').bind(id).first()) return json({ ok: false, error: '找不到大分類。' }, 404);
  const count = await db.prepare('SELECT COUNT(*) AS count FROM categories WHERE group_id = ?').bind(id).first();
  if (Number(count?.count || 0) > 0) return json({ ok: false, error: '此大分類仍包含科目，請先刪除科目。' }, 409);
  await db.prepare('DELETE FROM category_groups WHERE id = ?').bind(id).run();
  return json({ ok: true });
}

async function handleCreateCategory(request, db) {
  const body = await bodyJson(request);
  const kind = String(body?.kind || '');
  const groupId = Number(body?.groupId);
  const name = normalizeName(body?.name);
  if (!['income', 'expense'].includes(kind)) return json({ ok: false, error: '收支類型錯誤。' }, 400);
  if (!validId(groupId) || !name) return json({ ok: false, error: '科目資料不完整。' }, 400);
  const group = await db.prepare('SELECT kind FROM category_groups WHERE id = ?').bind(groupId).first();
  if (!group || group.kind !== kind) return json({ ok: false, error: '大分類不存在或收支類型不符。' }, 400);
  if (await db.prepare('SELECT 1 FROM categories WHERE kind = ? AND name = ?').bind(kind, name).first()) return json({ ok: false, error: '科目名稱已存在。' }, 409);
  const order = await db.prepare('SELECT COALESCE(MAX(sort_order), -1) + 1 AS next_order FROM categories WHERE kind = ? AND group_id = ?').bind(kind, groupId).first();
  const result = await db.prepare('INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at) VALUES (?, ?, ?, ?, 0, ?)')
    .bind(kind, groupId, name, Number(order?.next_order || 0), new Date().toISOString()).run();
  return json({ ok: true, id: result.meta?.last_row_id ?? null }, 201);
}

async function handleRenameCategory(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '科目編號錯誤。' }, 400);
  const category = await db.prepare('SELECT kind FROM categories WHERE id = ?').bind(id).first();
  if (!category) return json({ ok: false, error: '找不到科目。' }, 404);
  const body = await bodyJson(request);
  const name = normalizeName(body?.name);
  if (!name) return json({ ok: false, error: '科目名稱不可空白。' }, 400);
  if (await db.prepare('SELECT 1 FROM categories WHERE kind = ? AND name = ? AND id <> ?').bind(category.kind, name, id).first()) return json({ ok: false, error: '科目名稱已存在。' }, 409);
  await db.prepare('UPDATE categories SET name = ? WHERE id = ?').bind(name, id).run();
  return json({ ok: true });
}

async function handleSetCategoryFavorite(id, request, db) {
  if (!validId(id)) return json({ ok: false, error: '科目編號錯誤。' }, 400);
  const category = await db.prepare('SELECT kind, is_favorite FROM categories WHERE id = ?').bind(id).first();
  if (!category) return json({ ok: false, error: '找不到科目。' }, 404);
  const body = await bodyJson(request);
  if (!body || typeof body.favorite !== 'boolean') return json({ ok: false, error: '常用科目設定格式錯誤。' }, 400);
  if (body.favorite && Number(category.is_favorite) !== 1) {
    const count = await db.prepare('SELECT COUNT(*) AS count FROM categories WHERE kind = ? AND is_favorite = 1').bind(category.kind).first();
    if (Number(count?.count || 0) >= 10) return json({ ok: false, error: '常用科目最多設定 10 個。' }, 409);
  }
  await db.prepare('UPDATE categories SET is_favorite = ? WHERE id = ?').bind(body.favorite ? 1 : 0, id).run();
  return json({ ok: true, favorite: body.favorite });
}

async function handleDeleteCategory(id, db) {
  if (!validId(id)) return json({ ok: false, error: '科目編號錯誤。' }, 400);
  const result = await db.prepare('DELETE FROM categories WHERE id = ?').bind(id).run();
  if (!result.meta?.changes) return json({ ok: false, error: '找不到科目。' }, 404);
  return json({ ok: true });
}

async function handleGetOpeningBalances(url, db) {
  const month = url.searchParams.get('month') || currentMonth();
  if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
  const [accounts, txNames, openings, lockedThrough] = await Promise.all([
    db.prepare('SELECT name FROM accounts ORDER BY sort_order, id').all(),
    db.prepare('SELECT DISTINCT account_name AS name FROM transactions WHERE substr(tx_date, 1, 7) = ?').bind(month).all(),
    db.prepare('SELECT account_name, amount FROM opening_balances WHERE month = ?').bind(month).all(),
    getLockedThrough(db)
  ]);
  const current = new Set((accounts.results || []).map(row => row.name));
  const amounts = new Map((openings.results || []).map(row => [row.account_name, Number(row.amount)]));
  const names = [...current];
  const historical = new Set([...(txNames.results || []).map(row => row.name), ...(openings.results || []).map(row => row.account_name)]);
  for (const name of [...historical].sort((a, b) => String(a).localeCompare(String(b), 'zh-Hant'))) {
    if (!current.has(name)) names.push(name);
  }
  return json({
    ok: true,
    month,
    locked: isMonthLocked(month, lockedThrough),
    accounts: names.map(name => ({ name, isCurrent: current.has(name), amount: amounts.has(name) ? amounts.get(name) : null }))
  });
}

async function handleSetOpeningBalances(request, db) {
  const body = await bodyJson(request);
  const month = String(body?.month || '').trim();
  if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
  if (isMonthLocked(month, await getLockedThrough(db))) return json({ ok: false, error: `${month} 已鎖定，無法修改期初餘額。` }, 409);
  const values = body?.values;
  if (!values || typeof values !== 'object' || Array.isArray(values)) return json({ ok: false, error: '期初餘額資料格式錯誤。' }, 400);
  const now = new Date().toISOString();
  const statements = [];
  for (const [rawName, rawAmount] of Object.entries(values)) {
    const name = normalizeName(rawName);
    if (!name) continue;
    if (rawAmount === null || rawAmount === '') {
      statements.push(db.prepare('DELETE FROM opening_balances WHERE month = ? AND account_name = ?').bind(month, name));
      continue;
    }
    const amount = Number(rawAmount);
    if (!Number.isSafeInteger(amount)) return json({ ok: false, error: `${name} 的期初餘額必須是整數。` }, 400);
    statements.push(db.prepare(`
      INSERT INTO opening_balances(month, account_name, amount, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(month, account_name) DO UPDATE SET amount = excluded.amount, updated_at = excluded.updated_at
    `).bind(month, name, amount, now, now));
  }
  if (statements.length) await db.batch(statements);
  return json({ ok: true });
}

async function handleSetLock(request, db) {
  const body = await bodyJson(request);
  const lockedThrough = String(body?.lockedThrough || '').trim();
  if (lockedThrough && !isMonth(lockedThrough)) return json({ ok: false, error: '鎖帳月份格式錯誤。' }, 400);
  await db.prepare(`
    INSERT INTO app_settings(key, value) VALUES ('locked_through', ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).bind(lockedThrough).run();
  return json({ ok: true, lockedThrough: lockedThrough || null });
}

async function validateTransaction(values, db) {
  if (!isDate(values.txDate)) return json({ ok: false, error: '日期格式或日期內容不正確。' }, 400);
  if (!values.accountName) return json({ ok: false, error: '尚未選擇帳戶。' }, 400);
  if (!['income', 'expense'].includes(values.kind)) return json({ ok: false, error: '收支類型錯誤。' }, 400);
  if (!values.categoryName) return json({ ok: false, error: '尚未選擇科目。' }, 400);
  if (!Number.isInteger(values.amount) || values.amount < 1) return json({ ok: false, error: '金額必須大於 0。' }, 400);
  if (values.amount > MAX_AMOUNT) return json({ ok: false, error: '金額最多 7 位數。' }, 400);
  const [account, category] = await Promise.all([
    db.prepare('SELECT id FROM accounts WHERE name = ?').bind(values.accountName).first(),
    db.prepare('SELECT id FROM categories WHERE kind = ? AND name = ?').bind(values.kind, values.categoryName).first()
  ]);
  if (!account) return json({ ok: false, error: '帳戶不存在。' }, 400);
  if (!category) return json({ ok: false, error: '科目不存在或收支類型不符。' }, 400);
  return null;
}

function normalizeTransactionBody(body) {
  return {
    txDate: String(body?.txDate || '').trim(),
    accountName: normalizeName(body?.accountName),
    kind: String(body?.kind || '').trim(),
    categoryName: normalizeName(body?.categoryName),
    summary: String(body?.summary || '').trim(),
    amount: Number(body?.amount)
  };
}

async function normalizeAccountOrder(db) {
  const result = await db.prepare('SELECT id FROM accounts ORDER BY sort_order, id').all();
  const rows = result.results || [];
  if (!rows.length) return;
  await db.batch(rows.map((row, index) => db.prepare('UPDATE accounts SET sort_order = ? WHERE id = ?').bind(index, row.id)));
}

async function getLockedThrough(db) {
  const row = await db.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first();
  const value = String(row?.value || '').trim();
  return isMonth(value) ? value : null;
}

function isMonthLocked(month, lockedThrough) {
  return Boolean(lockedThrough && isMonth(month) && month <= lockedThrough);
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}

function isDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(value || ''))) return false;
  const [y, m, d] = value.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

function validId(value) {
  return Number.isInteger(value) && value > 0;
}

function normalizeName(value) {
  return String(value ?? '').trim().replace(/\s+/g, ' ');
}

async function bodyJson(request) {
  return request.json().catch(() => null);
}

function currentMonth() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store'
    }
  });
}