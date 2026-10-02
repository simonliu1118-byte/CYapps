import { currentOpeningUsageForAccount } from './opening-balances.js';

export async function handleAccountLifecycleApi(request, env, principal) {
  if (!env.DB) return null;
  const url = new URL(request.url);

  let match = url.pathname.match(/^\/api\/accounts\/(\d+)\/archive$/);
  if (match && request.method === 'POST') {
    return archiveAccount(Number(match[1]), env.DB);
  }

  match = url.pathname.match(/^\/api\/accounts\/(\d+)\/restore$/);
  if (match && request.method === 'POST') {
    return restoreAccount(Number(match[1]), env.DB);
  }

  match = url.pathname.match(/^\/api\/accounts\/(\d+)\/permanent$/);
  if (match && request.method === 'DELETE') {
    return permanentlyDeleteAccount(Number(match[1]), env.DB, principal);
  }

  return null;
}

async function archiveAccount(id, db) {
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);

  const [account, activeCount] = await Promise.all([
    db.prepare('SELECT id, name, sort_order, is_default, archived_at FROM accounts WHERE id = ?').bind(id).first(),
    db.prepare('SELECT COUNT(*) AS count FROM accounts WHERE archived_at IS NULL').first()
  ]);
  if (!account) return json({ ok: false, error: '找不到帳戶。' }, 404);
  if (account.archived_at) {
    return json({ ok: true, archived: true, account: accountSummary(account) });
  }
  if (Number(activeCount?.count || 0) <= 1) {
    return json({ ok: false, error: '至少需要保留一個可用帳戶。', code: 'LAST_ACTIVE_ACCOUNT' }, 409);
  }

  const replacement = Number(account.is_default) === 1
    ? await db.prepare(
      'SELECT id FROM accounts WHERE id <> ? AND archived_at IS NULL ORDER BY sort_order, id LIMIT 1'
    ).bind(id).first()
    : null;
  const archivedAt = new Date().toISOString();
  const statements = [
    db.prepare('UPDATE accounts SET archived_at = ?, is_default = 0 WHERE id = ?').bind(archivedAt, id)
  ];
  if (replacement) {
    statements.push(
      db.prepare('UPDATE accounts SET is_default = CASE WHEN id = ? THEN 1 ELSE 0 END WHERE archived_at IS NULL')
        .bind(Number(replacement.id))
    );
  }
  await db.batch(statements);
  await normalizeActiveAccountOrder(db);

  return json({
    ok: true,
    archived: true,
    account: {
      id,
      name: String(account.name || ''),
      archived_at: archivedAt
    }
  });
}

async function restoreAccount(id, db) {
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);

  const account = await db.prepare(
    'SELECT id, name, sort_order, is_default, archived_at FROM accounts WHERE id = ?'
  ).bind(id).first();
  if (!account) return json({ ok: false, error: '找不到帳戶。' }, 404);
  if (!account.archived_at) {
    return json({ ok: true, restored: true, account: accountSummary(account) });
  }

  const next = await db.prepare(
    'SELECT COALESCE(MAX(sort_order), -1) + 1 AS next_order FROM accounts WHERE archived_at IS NULL'
  ).first();
  await db.prepare(
    'UPDATE accounts SET archived_at = NULL, sort_order = ?, is_default = 0 WHERE id = ?'
  ).bind(Number(next?.next_order || 0), id).run();

  const activeDefault = await db.prepare(
    'SELECT id FROM accounts WHERE archived_at IS NULL AND is_default = 1 LIMIT 1'
  ).first();
  if (!activeDefault) {
    await db.prepare('UPDATE accounts SET is_default = 1 WHERE id = ?').bind(id).run();
  }

  return json({
    ok: true,
    restored: true,
    account: {
      id,
      name: String(account.name || ''),
      archived_at: null
    }
  });
}

async function permanentlyDeleteAccount(id, db, principal) {
  if (String(principal?.workspaceRole || '') !== 'SUPER_ADMIN') {
    return json({
      ok: false,
      error: '只有超級管理員可以永久刪除帳戶。',
      code: 'SUPER_ADMIN_REQUIRED'
    }, 403);
  }
  if (!validId(id)) return json({ ok: false, error: '帳戶編號錯誤。' }, 400);

  const account = await db.prepare(
    'SELECT id, name, archived_at FROM accounts WHERE id = ?'
  ).bind(id).first();
  if (!account) return json({ ok: false, error: '找不到帳戶。' }, 404);
  if (!account.archived_at) {
    return json({
      ok: false,
      error: '帳戶必須先封存，才能永久刪除。',
      code: 'ACCOUNT_MUST_BE_ARCHIVED'
    }, 409);
  }

  const name = String(account.name || '');
  const [transactions, openingUsage] = await Promise.all([
    db.prepare('SELECT COUNT(*) AS count FROM transactions WHERE account_name = ?').bind(name).first(),
    currentOpeningUsageForAccount(db, name)
  ]);
  const transactionCount = Number(transactions?.count || 0);
  const nonZeroOpeningOverrides = Number(openingUsage?.nonZeroOverrides || 0);
  if (transactionCount > 0 || nonZeroOpeningOverrides > 0) {
    return json({
      ok: false,
      error: '此帳戶仍有歷史記帳或非 0 的期初調整，不能永久刪除。',
      code: 'ACCOUNT_HAS_HISTORY',
      usage: {
        transactions: transactionCount,
        nonZeroOpeningOverrides
      }
    }, 409);
  }

  const result = await db.prepare(
    'DELETE FROM accounts WHERE id = ? AND archived_at IS NOT NULL'
  ).bind(id).run();
  if (!result.meta?.changes) {
    return json({ ok: false, error: '帳戶狀態已變更，請重新整理後再試。' }, 409);
  }

  return json({
    ok: true,
    permanentlyDeleted: true,
    id,
    name
  });
}

async function normalizeActiveAccountOrder(db) {
  const result = await db.prepare(
    'SELECT id FROM accounts WHERE archived_at IS NULL ORDER BY sort_order, id'
  ).all();
  const rows = result.results || [];
  if (!rows.length) return;
  await db.batch(rows.map((row, index) =>
    db.prepare('UPDATE accounts SET sort_order = ? WHERE id = ?').bind(index, row.id)
  ));
}

function accountSummary(account) {
  return {
    id: Number(account.id),
    name: String(account.name || ''),
    archived_at: account.archived_at ? String(account.archived_at) : null
  };
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
