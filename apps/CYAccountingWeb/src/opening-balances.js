const MAX_REASON_LENGTH = 200;
const AUDIT_LIMIT = 100;

export async function handleOpeningBalanceApi(request, env, principal) {
  if (!env || !env.DB) return null;
  const url = new URL(request.url);

  if (url.pathname === '/api/opening-balances' && request.method === 'GET') {
    const month = String(url.searchParams.get('month') || currentMonth()).trim();
    if (!isMonth(month)) return json({ ok: false, error: '月份格式錯誤。' }, 400);
    const snapshot = await buildOpeningBalanceSnapshot(env.DB, month);
    if (url.searchParams.get('audit') === '1') {
      snapshot.audit = await readAuditEntries(env.DB, month, '', 30);
    }
    return json(snapshot);
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

  // One indexed lookup on a warm month, shared by every device and Excel.
  // Lock state is live metadata, not part of the saved financial read model.
  for (let attempt = 0; attempt < 2; attempt++) {
    const saved = await db.prepare(`SELECT revision,
      (SELECT accounts_json FROM opening_balance_snapshots WHERE month=?) AS accounts_json,
      (SELECT value FROM app_settings WHERE key='locked_through') AS locked_through
      FROM opening_balance_snapshot_state WHERE id=1`).bind(month).first();
    if (saved.accounts_json !== null) {
      return openingSnapshotResult(month, JSON.parse(saved.accounts_json), saved.locked_through);
    }
    const accounts = await calculateOpeningAccounts(db, month);
    // A concurrent financial write also advances revision in the same D1
    // transaction. Never republish an obsolete calculation after invalidation.
    const stored = await db.prepare(`INSERT INTO opening_balance_snapshots(month,accounts_json,created_at)
      SELECT ?,?,? FROM opening_balance_snapshot_state WHERE id=1 AND revision=?
      ON CONFLICT(month) DO UPDATE SET accounts_json=excluded.accounts_json,created_at=excluded.created_at`)
      .bind(month, JSON.stringify(accounts), new Date().toISOString(), saved.revision).run();
    if (Number(stored.meta?.changes) === 1) return openingSnapshotResult(month, accounts, saved.locked_through);
  }
  throw new Error('帳務更新中，請重新載入期初餘額。');
}

async function calculateOpeningAccounts(db, month) {
  const [accountRows, overrideRows] = await Promise.all([
    db.prepare('SELECT name, archived_at FROM accounts ORDER BY CASE WHEN archived_at IS NULL THEN 0 ELSE 1 END, sort_order, id').all(),
    db.prepare('SELECT month, account_name, amount, reason, updated_at, updated_by_employee_no, updated_by_name, updated_by_role FROM opening_balance_overrides WHERE month <= ? ORDER BY account_name, month').bind(month).all()
  ]);

  // Use the same prior anchors for the SQL range and the returned metadata.
  // A second read of the override table could observe a concurrent adjustment.
  const priorMonths = new Map();
  for (const row of overrideRows.results || []) {
    if (row.month < month) priorMonths.set(String(row.account_name), row.month);
  }
  const transactionTotals = await db.prepare(`
    -- Walk distinct account keys with indexed seeks instead of scanning every
    -- historical row just to discover names (including archived/orphan names).
    -- The same covering index bounds each sum at its latest prior anchor.
      WITH RECURSIVE transaction_accounts(account_name) AS (
        SELECT MIN(account_name) FROM transactions
        UNION ALL
        SELECT (SELECT MIN(account_name) FROM transactions
                WHERE account_name > transaction_accounts.account_name)
        FROM transaction_accounts WHERE account_name IS NOT NULL
      )
      SELECT names.account_name,
        (SELECT SUM(CASE WHEN kind = 'income' THEN amount ELSE -amount END)
         FROM transactions AS t
         WHERE t.account_name = names.account_name
           AND substr(t.tx_date, 1, 7) >= COALESCE(anchors.value, '')
           AND substr(t.tx_date, 1, 7) < ?) AS net
      FROM transaction_accounts AS names
      LEFT JOIN json_each(?) AS anchors ON anchors.key = names.account_name
      WHERE names.account_name IS NOT NULL AND EXISTS (
        SELECT 1 FROM transactions AS t
        WHERE t.account_name = names.account_name AND substr(t.tx_date, 1, 7) <= ?
      )
    `).bind(month, JSON.stringify(Object.fromEntries(priorMonths)), month).all();

  const master = new Map();
  for (const row of accountRows.results || []) {
    master.set(String(row.name), { archived: Boolean(row.archived_at) });
  }

  const names = new Set();
  for (const [name, meta] of master) {
    if (!meta.archived) names.add(name);
  }
  for (const row of transactionTotals.results || []) names.add(String(row.account_name || ''));
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

  const netsByAccount = new Map((transactionTotals.results || [])
    .map(row => [String(row.account_name || ''), Number(row.net) || 0]));

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
    const automaticAmount = (previous ? previous.amount : 0) + (netsByAccount.get(name) || 0);
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

  return accounts;
}

function openingSnapshotResult(month, accounts, lockedValue) {
  const lockedThrough = validLockedThrough(lockedValue);
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
  if (!name) return { latestOverrideAmount: 0, nonZeroOverrides: 0, overrideCount: 0 };
  const [latest, counts] = await Promise.all([
    db.prepare('SELECT amount FROM opening_balance_overrides WHERE account_name = ? ORDER BY month DESC LIMIT 1').bind(name).first(),
    db.prepare('SELECT COUNT(*) AS count, SUM(CASE WHEN amount <> 0 THEN 1 ELSE 0 END) AS non_zero FROM opening_balance_overrides WHERE account_name = ?').bind(name).first()
  ]);
  return {
    latestOverrideAmount: latest ? Number(latest.amount) || 0 : 0,
    nonZeroOverrides: Number(counts && counts.non_zero || 0),
    overrideCount: Number(counts && counts.count || 0)
  };
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

    if (amount === Number(item.amount)) continue;

    statements.push(
      db.prepare('INSERT INTO opening_balance_overrides(month, account_name, amount, reason, created_at, updated_at, updated_by_employee_id, updated_by_employee_no, updated_by_name, updated_by_role) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(month, account_name) DO UPDATE SET amount = excluded.amount, reason = excluded.reason, updated_at = excluded.updated_at, updated_by_employee_id = excluded.updated_by_employee_id, updated_by_employee_no = excluded.updated_by_employee_no, updated_by_name = excluded.updated_by_name, updated_by_role = excluded.updated_by_role')
        .bind(month, name, amount, reason, now, now, actor.employeeId, actor.employeeNo, actor.name, actor.role),
      auditStatement(db, month, name, 'set', Number(item.amount), amount, reason, actor, now)
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
  return json({ ok: true, entries: await readAuditEntries(db, month, account, AUDIT_LIMIT) });
}

async function readAuditEntries(db, month, account, limit) {
  let result;
  if (month && account) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE month = ? AND account_name = ? ORDER BY id DESC LIMIT ?').bind(month, account, limit).all();
  } else if (month) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE month = ? ORDER BY id DESC LIMIT ?').bind(month, limit).all();
  } else if (account) {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit WHERE account_name = ? ORDER BY id DESC LIMIT ?').bind(account, limit).all();
  } else {
    result = await db.prepare('SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit ORDER BY id DESC LIMIT ?').bind(limit).all();
  }

  return (result.results || []).map(row => ({
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
  }));
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
