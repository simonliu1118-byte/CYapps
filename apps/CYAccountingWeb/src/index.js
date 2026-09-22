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

      if (!env.DB) {
        return json({ ok: false, error: 'D1 尚未綁定。' }, 503);
      }

      if (url.pathname === '/api/bootstrap' && request.method === 'GET') {
        return handleBootstrap(env.DB);
      }

      if (url.pathname === '/api/transactions' && request.method === 'GET') {
        return handleListTransactions(url, env.DB);
      }

      if (url.pathname === '/api/transactions' && request.method === 'POST') {
        return handleCreateTransaction(request, env.DB);
      }

      const match = url.pathname.match(/^\/api\/transactions\/(\d+)$/);
      if (match && request.method === 'DELETE') {
        return handleDeleteTransaction(Number(match[1]), env.DB);
      }

      return json({ ok: false, error: '找不到 API。' }, 404);
    } catch (error) {
      console.error(error);
      return json({ ok: false, error: '伺服器處理失敗。' }, 500);
    }
  }
};

async function handleBootstrap(db) {
  const [accounts, categories] = await Promise.all([
    db.prepare(`SELECT id, name, sort_order, is_default FROM accounts ORDER BY sort_order, id`).all(),
    db.prepare(`
      SELECT c.id, c.kind, c.name, c.sort_order, c.is_favorite,
             g.id AS group_id, g.name AS group_name, g.sort_order AS group_sort_order
      FROM categories c
      JOIN category_groups g ON g.id = c.group_id
      ORDER BY c.kind, g.sort_order, g.id, c.sort_order, c.id
    `).all()
  ]);

  return json({
    ok: true,
    accounts: accounts.results || [],
    categories: categories.results || []
  });
}

async function handleListTransactions(url, db) {
  const month = url.searchParams.get('month') || currentMonth();
  if (!/^\d{4}-\d{2}$/.test(month)) {
    return json({ ok: false, error: '月份格式錯誤。' }, 400);
  }

  const result = await db.prepare(`
    SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at
    FROM transactions
    WHERE substr(tx_date, 1, 7) = ?
    ORDER BY tx_date DESC, id DESC
  `).bind(month).all();

  return json({ ok: true, month, transactions: result.results || [] });
}

async function handleCreateTransaction(request, db) {
  const body = await request.json().catch(() => null);
  if (!body) return json({ ok: false, error: '資料格式錯誤。' }, 400);

  const txDate = String(body.txDate || '').trim();
  const accountName = String(body.accountName || '').trim();
  const kind = String(body.kind || '').trim();
  const categoryName = String(body.categoryName || '').trim();
  const summary = String(body.summary || '').trim();
  const amount = Number(body.amount);

  if (!/^\d{4}-\d{2}-\d{2}$/.test(txDate) || !isRealDate(txDate)) {
    return json({ ok: false, error: '日期格式錯誤。' }, 400);
  }
  if (!accountName) return json({ ok: false, error: '請選擇帳戶。' }, 400);
  if (!['income', 'expense'].includes(kind)) {
    return json({ ok: false, error: '收支類型錯誤。' }, 400);
  }
  if (!categoryName) return json({ ok: false, error: '請選擇科目。' }, 400);
  if (!Number.isInteger(amount) || amount < 1 || amount > MAX_AMOUNT) {
    return json({ ok: false, error: '金額必須為 1～9,999,999 的整數。' }, 400);
  }

  const [account, category] = await Promise.all([
    db.prepare(`SELECT id FROM accounts WHERE name = ?`).bind(accountName).first(),
    db.prepare(`SELECT id FROM categories WHERE kind = ? AND name = ?`).bind(kind, categoryName).first()
  ]);
  if (!account) return json({ ok: false, error: '帳戶不存在。' }, 400);
  if (!category) return json({ ok: false, error: '科目不存在或收支類型不符。' }, 400);

  const now = new Date().toISOString();
  const result = await db.prepare(`
    INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(txDate, accountName, kind, categoryName, summary, amount, now, now).run();

  return json({ ok: true, id: result.meta?.last_row_id ?? null }, 201);
}

async function handleDeleteTransaction(id, db) {
  if (!Number.isInteger(id) || id <= 0) {
    return json({ ok: false, error: '交易編號錯誤。' }, 400);
  }
  const result = await db.prepare(`DELETE FROM transactions WHERE id = ?`).bind(id).run();
  if (!result.meta?.changes) return json({ ok: false, error: '找不到交易。' }, 404);
  return json({ ok: true });
}

function isRealDate(value) {
  const [y, m, d] = value.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
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
