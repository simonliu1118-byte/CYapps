const TRANSFER_SETTING = 'statistics_transfer_category_ids';

export function statisticsPeriod(start, end) {
  const valid = value => /^\d{4}-(0[1-9]|1[0-2])$/.test(value) && value >= '0001-01' && value <= '9998-12';
  if (!valid(start) || !valid(end) || start > end) return null;
  const [year, month] = end.split('-').map(Number);
  return { start, end, from: `${start}-01`, until: `${String(month === 12 ? year + 1 : year).padStart(4, '0')}-${String(month === 12 ? 1 : month + 1).padStart(2, '0')}-01` };
}

export function buildStatistics(rows, transferIds, period) {
  const transfers = new Set(transferIds);
  const cash = { income: 0, expense: 0, net: 0, count: 0 };
  const operating = { income: 0, expense: 0, net: 0, count: 0 };
  const transfer = { income: 0, expense: 0, net: 0, count: 0 };
  const groups = new Map();
  for (const row of rows) {
    const amount = Number(row.amount);
    const count = Number(row.count);
    const isTransfer = transfers.has(Number(row.category_id));
    cash[row.kind] += amount;
    cash.count += count;
    const subtotal = isTransfer ? transfer : operating;
    subtotal[row.kind] += amount;
    subtotal.count += count;
    const key = `${row.kind}:${row.group_id ?? 'unclassified'}`;
    if (!groups.has(key)) groups.set(key, {
      key, kind: row.kind, name: row.group_name || '未分類', amount: 0, operatingAmount: 0, transferAmount: 0, subjects: []
    });
    const group = groups.get(key);
    group.amount += amount;
    group[isTransfer ? 'transferAmount' : 'operatingAmount'] += amount;
    group.subjects.push({ id: row.category_id ?? null, name: row.category_name, amount, count, isTransfer });
  }
  for (const total of [cash, operating, transfer]) total.net = total.income - total.expense;
  return { period, cash, operating, transfer, transferCategoryIds: transferIds, groups: [...groups.values()] };
}

export async function handleStatisticsApi(request, env) {
  const url = new URL(request.url);
  if (url.pathname === '/api/statistics' && request.method === 'GET') {
    const start = url.searchParams.get('start') || '';
    const end = url.searchParams.get('end') || start;
    const period = statisticsPeriod(start, end);
    if (!period) return json({ ok: false, error: '請選擇有效的起訖年月，起月不可晚於訖月。' }, 400);
    // One D1 snapshot: no transaction-page limit, including archived accounts and unmatched historical subjects.
    const [totals, setting] = await env.DB.batch([
      env.DB.prepare(`SELECT t.kind, t.category_name, c.id AS category_id,
          g.id AS group_id, g.name AS group_name, SUM(t.amount) AS amount, COUNT(*) AS count
        FROM transactions t
        LEFT JOIN categories c ON c.kind = t.kind AND c.name = t.category_name
        LEFT JOIN category_groups g ON g.id = c.group_id
        WHERE t.tx_date >= ? AND t.tx_date < ?
        GROUP BY t.kind, t.category_name, c.id, g.id
        ORDER BY t.kind, COALESCE(g.sort_order, 2147483647), g.id, c.sort_order, c.id, t.category_name`)
        .bind(period.from, period.until),
      env.DB.prepare('SELECT value FROM app_settings WHERE key = ?').bind(TRANSFER_SETTING)
    ]);
    return json({ ok: true, ...buildStatistics(totals.results || [], parseTransferIds(setting.results?.[0]?.value), period) });
  }
  if (url.pathname === '/api/settings/statistics') {
    if (request.method === 'GET') {
      const [categories, setting] = await env.DB.batch([
        env.DB.prepare(`SELECT c.id, c.kind, c.name, g.name AS group_name FROM categories c
          JOIN category_groups g ON g.id = c.group_id ORDER BY c.kind, g.sort_order, g.id, c.sort_order, c.id`),
        env.DB.prepare('SELECT value FROM app_settings WHERE key = ?').bind(TRANSFER_SETTING)
      ]);
      return json({ ok: true, categories: categories.results || [], transferCategoryIds: parseTransferIds(setting.results?.[0]?.value) });
    }
    if (request.method === 'PUT') {
      const body = await request.json().catch(() => null);
      const ids = body?.transferCategoryIds;
      if (!Array.isArray(ids) || ids.length > 10000 || ids.some(id => !Number.isSafeInteger(id) || id <= 0) || new Set(ids).size !== ids.length) {
        return json({ ok: false, error: '移轉科目設定格式錯誤。' }, 400);
      }
      // Validate and write atomically against the same category snapshot; a concurrent delete cannot leave stale IDs.
      const value = JSON.stringify([...ids].sort((a, b) => a - b));
      const result = await env.DB.prepare(`INSERT INTO app_settings(key, value)
        SELECT ?, ? WHERE NOT EXISTS (
          SELECT 1 FROM json_each(?) choice LEFT JOIN categories c ON c.id = choice.value WHERE c.id IS NULL
        ) ON CONFLICT(key) DO UPDATE SET value = excluded.value`).bind(TRANSFER_SETTING, value, value).run();
      if (!result.meta?.changes) return json({ ok: false, error: '科目已變更，請重新載入設定。' }, 409);
      return json({ ok: true, transferCategoryIds: JSON.parse(value) });
    }
  }
  return null;
}

function parseTransferIds(value) {
  try {
    const ids = JSON.parse(value || '[]');
    return Array.isArray(ids) ? ids.filter(id => Number.isSafeInteger(id) && id > 0) : [];
  } catch { return []; }
}
function json(data, status = 200) {
  return Response.json(data, { status, headers: { 'cache-control': 'no-store' } });
}
