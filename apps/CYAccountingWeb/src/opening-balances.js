const MAX_REASON_LENGTH = 200;
const AUDIT_LIMIT = 100;

export async function handleOpeningBalanceApi(request, env, principal) {
  if (!env || !env.DB) return null;
  const url = new URL(request.url);

  if (url.pathname === '/api/opening-balances' && request.method === 'GET') {
    const month = String(url.searchParams.get('month') || currentMonth()).trim();
    if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
    return json(await buildOpeningBalanceSnapshot(env.DB, month));
  }

  if (url.pathname === '/api/opening-balance-overrides' && request.method === 'PUT') {
    return setOpeningBalanceOverrides(request, env.DB, principal);
  }

  if (url.pathname === '/api/opening-balance-audit' && request.method === 'GET') {
    return getOpeningBalanceAudit(url, env.DB);
  }

  return null;
}

export async function buildOpeningBalanceSnapshot(db, month) {
  if (!isMonth(month)) throw new Error('INVALID_MONTH');

  const [accountRows, transactionNames, overrideRows, transactionMonths, lockedRow] = await Promise.all([
    db.prepare('SELECT name, archived_at FROM accounts ORDER BY CASE WHEN archived_at IS NULL THEN 0 ELSE 1 END, sort_order, id').all(),
    db.prepare('SELECT DISTINCT account_name AS name FROM transactions WHERE substr(tx_date, 1, 7) <= ?').bind(month).all(),
    db.prepare('SELECT month, account_name, amount, reason, updated_at, updated_by_employee_no, updated_by_name, updated_by_role FROM opening_balance_overrides WHERE month <= ? ORDER BY account_name, month').bind(month).all(),
    db.prepare("SELECT account_name, substr(tx_date, 1, 7) AS month, SUM(CASE WHEN kind = 'income' THEN amount ELSE -amount END) AS net FROM transactions WHERE substr(tx_date, 1, 7) < ? GROUP BY account_name, substr(tx_date, 1, 7) ORDER BY account_name, month").bind(month).all(),
    db.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first()
  ]);

  const master = new Map();
  for (const row of accountRows.results || []) {
    master.set(String(row.name), { archived: Boolean(row.archived_at) });
  }

  const names = new Set();
  for (const [name, meta] of master) {
    if (!meta.archived) names.add(name);
  }
  for (const row of transactionNames.results || []) names.add(String(row.name || ''));
  for (const row of overrideRows.results || []) names.add(String(row.account_name || ''));
  names.delete('');

  const overridesByAccount = new Map();
  for (const row of overrideRows.results || []) {
    const name = String(row.account_name || '');
    if (!overridesByAccount.has(name)) overridesByAccount.set(name, []);
    overridesByAccount.get(name).push({
      month: String(row.month),
      amount: Number(row.amount) || 0,
      reason: String(row.reason || ''),
      updatedAt: String(row.updated_at || ''),
      updatedByEmployeeNo: String(row.updated_by_employee_no || ''),
      updatedByName: String(row.updated_by_name || ''),
      updatedByRole: String(row.updated_by_role || '')
    });
  }

  const netsByAccount = new Map();
  for (const row of transactionMonths.results || []) {
    const name = String(row.account_name || '');
    if (!netsByAccount.has(name)) netsByAccount.set(name, []);
    netsByAccount.get(name).push({ month: String(row.month), net: Number(row.net) || 0 });
  }

  const accounts = [...names].sort((left, right) => {
    const l = master.get(left);
    const r = master.get(right);
    const lRank = l && !l.archived ? 0 : 1;
    const rRank = r && !r.archived ? 0 : 1;
    return lRank - rRank || String(left).localeCompare(String(right), 'zh-Hant');
  }).map(name => {
    const allOverrides = overridesByAccount.get(name) || [];
    const exact = allOverrides.find(item => item.month === month) || null;
    const previous = [...allOverrides].reverse().find(item => item.month < month) || null;
    const automaticAmount = calculateFromAnchor(previous, netsByAccount.get(name) || [], month);
    const meta = master.get(name);
    return {
      name,
      isCurrent: Boolean(meta && !meta.archived),
      isArchived: Boolean(meta && meta.archived),
      amount: exact ? exact.amount : automaticAmount,
      automaticAmount,
      source: exact ? 'override' : 'automatic',
      overrideAmount: exact ? exact.amount : null,
      overrideReason: exact ? exact.reason : '',
      overrideUpdatedAt: exact ? exact.updatedAt : null,
      overrideUpdatedByEmployeeNo: exact ? exact.updatedByEmployeeNo : '',
      overrideUpdatedByName: exact ? exact.updatedByName : '',
      overrideUpdatedByRole: exact ? exact.updatedByRole : '',
      automaticAnchorMonth: previous ? previous.month : null
    };
  });

  const lockedThrough = validLockedThrough(lockedRow && lockedRow.value);
  return {
    ok: true,
    month,
    locked: Boolean(lockedThrough && month <= lockedThrough),
    lockedThrough,
    mode: 'automatic',
    accounts
  };
}

export async function currentOpeningUsageForAccount(db, accountName) {
  const name = String(accountName || '').trim();
  if (!name) return { nonZeroOverrides: 0 };
  const row = await db.prepare('SELECT COUNT(*) AS count FROM opening_balance_overrides WHERE account_name = ? AND amount <> 0').bind(name).first();
  return { nonZeroOverrides: Number(row && row.count || 0) };
}

async function setOpeningBalanceOverrides(request, db, principal) {
  const body = await bodyJson(request);
  const month = String(body && body.month || '').trim();
  if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
  if (await isLockedMonth(db, month)) return json({ ok: false, error: month + ' 已鎖定，無法調整期初餘額。' }, 409);

  const values = body && body.values;
  if (!values || typeof values !== 'object' || Array.isArray(values)) {
    return json({ ok: false, error: '期初餘額調整資料格式錯誤。' }, 400);
  }

  const reason = String(body && body.reason || '').trim();
  if (!reason) return json({ ok: false, error: '手動調整期初餘額必須填寫理由。' }, 400);
  if (Array.from(reason).length > MAX_REASON_LENGTH) {
    return json({ ok: false, error: '調整理由不可超過 ' + MAX_REASON_LENGTH + ' 個字。' }, 400);
  }

  const snapshot = await buildOpeningBalanceSnapshot(db, month);
  const known = new Map(snapshot.accounts.map(item => [item.name, item]));
  const actor = auditActor(principal);
  const now = new Date().toISOString();
  const statements = [];
  const changed = [];

  for (const [rawName, rawAmount] of Object.entries(values)) {
    const name = String(rawName || '').trim();
    const item = known.get(name);
    if (!item) return json({ ok: false, error: '找不到帳戶「' + name + '」。' }, 404);

    const amount = Number(rawAmount);
    if (!Number.isSafeInteger(amount)) {
      return json({ ok: false, error: name + ' 的期初餘額必須是整數。' }, 400);
    }
    if (amount === Number(item.amount)) continue;

    const previousOverride = item.overrideAmount === null ? null : Number(item.overrideAmount);
    if (amount === Number(item.automaticAmount)) {
      if (previousOverride === null) continue;
      statements.push(
        db.prepare('DELETE FROM opening_balance_overrides WHERE month = ? AND account_name = ?').bind(month, name),
        auditStatement(db, month, name, 'clear', previousOverride, Number(item.automaticAmount), reason, actor, now)
      );
      changed.push({ name, action: 'clear', amount: Number(item.automaticAmount) });
      continue;
    }

    statements.push(
      db.prepare('INSERT INTO opening_balance_overrides(month, account_name, amount, reason, created_at, updated_at, updated_by_employee_id, updated_by_employee_no, updated_by_name, updated_by_role) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(month, account_name) DO UPDATE SET amount = excluded.amount, reason = excluded.reason, updated_at = excluded.updated_at, updated_by_employee_id = excluded.updated_by_employee_id, updated_by_employee_no = excluded.updated_by_employee_no, updated_by_name = excluded.updated_by_name, updated_by_role = excluded.updated_by_role')
        .bind(month, name, amount, reason, now, now, actor.employeeId, actor.employeeNo, actor.name, actor.role),
      auditStatement(db, month, name, 'set', previousOverride, amount, reason, actor, now)
    );
    changed.push({ name, action: 'set', amount });
  }

  if (statements.length) await db.batch(statements);
  const result = await buildOpeningBalanceSnapshot(db, month);
  return json(Object.assign({}, result, { changed }));
}

async function getOpeningBalanceAudit(url, db) {
  const month = String(url.searchParams.get('month') || '').trim();
  const account = String(url.searchParams.get('account') || '').trim();
  if (month && !isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);

  let result;
  if (month && account) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE month = ? AND account_name = ? ORDER BY id DESC LIMIT ?').bind(month, account, AUDIT_LIMIT).all();
  } else if (month) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE month = ? ORDER BY id DESC LIMIT ?').bind(month, AUDIT_LIMIT).all();
  } else if (account) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE account_name = ? ORDER BY id DESC LIMIT ?').bind(account, AUDIT_LIMIT).all();
  } else {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit ORDER BY id DESC LIMIT ?').bind(AUDIT_LIMIT).all();
  }

  return json({
    ok: true,
    entries: (result.results || []).map(row => ({
      id: Number(row.id),
      month: String(row.month),
      accountName: String(row.account_name),
      action: String(row.action),
      previousAmount: row.previous_amount === null ? null : Number(row.previous_amount),
      newAmount: row.new_amount === null ? null : Number(row.new_amount),
      reason: String(row.reason || ''),
      actorEmployeeNo: String(row.actor_employee_no || ''),
      actorName: String(row.actor_name || ''),
      actorRole: String(row.actor_role || ''),
      createdAt: String(row.created_at || '')
    }))
  });
}

function calculateFromAnchor(anchor, monthlyNets, targetMonth) {
  let amount = anchor ? Number(anchor.amount) || 0 : 0;
  const startMonth = anchor ? anchor.month : '';
  for (const item of monthlyNets) {
    if (item.month >= targetMonth) continue;
    if (startMonth && item.month < startMonth) continue;
    amount += Number(item.net) || 0;
  }
  return amount;
}

function auditStatement(db, month, name, action, previousAmount, newAmount, reason, actor, now) {
  return db.prepare('INSERT INTO opening_balance_audit(month, account_name, action, previous_amount, new_amount, reason, actor_employee_id, actor_employee_no, actor_name, actor_role, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')
    .bind(month, name, action, previousAmount, newAmount, reason, actor.employeeId, actor.employeeNo, actor.name, actor.role, now);
}

function auditActor(principal) {
  return {
    employeeId: String(principal && principal.employeeId || ''),
    employeeNo: String(principal && principal.employeeNo || ''),
    name: String(principal && principal.displayName || ''),
    role: String(principal && principal.workspaceRole || '')
  };
}

async function isLockedMonth(db, month) {
  const row = await db.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first();
  const lockedThrough = validLockedThrough(row && row.value);
  return Boolean(lockedThrough && month <= lockedThrough);
}

function validLockedThrough(value) {
  const month = String(value || '').trim();
  return isMonth(month) ? month : null;
}

async function bodyJson(request) {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

function currentMonth() {
  return new Date().toISOString().slice(0, 7);
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
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
