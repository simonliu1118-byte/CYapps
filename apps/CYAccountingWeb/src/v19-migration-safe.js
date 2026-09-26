import {
  DesktopMigrationError,
  analyzeDesktopMigration,
  handleV19MigrationApi as handleBaseMigrationApi
} from './v19-migration.js';

const HISTORY_KEY = 'desktop_migration_history_v1';
const HISTORY_LIMIT = 20;
const TRANSACTION_JSON_CHUNK = 500;
const OPENING_JSON_CHUNK = 1000;
const MAX_SAFE_BATCH_STATEMENTS = 40;

export async function handleV19MigrationApi(request, env, session) {
  const url = new URL(request.url);
  if (url.pathname !== '/api/migration/desktop/commit' || request.method !== 'POST') {
    return handleBaseMigrationApi(request, env, session);
  }

  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以執行桌面帳本移轉。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }
  if (!env?.DB) return json({ ok: false, error: 'D1 尚未綁定。', code: 'DB_NOT_CONFIGURED' }, 503);

  try {
    const body = await request.json().catch(() => null);
    if (body?.confirm !== true) {
      throw new DesktopMigrationError('必須先完成預覽並確認移轉。', 'MIGRATION_CONFIRM_REQUIRED', 400);
    }

    const analysis = await analyzeDesktopMigration(body?.snapshot, env.DB);
    const expectedMode = String(body?.expectedMode || '');
    if (expectedMode && expectedMode !== analysis.plan.mode) {
      throw new DesktopMigrationError('目標帳本狀態已變更，請重新建立移轉預覽。', 'MIGRATION_TARGET_CHANGED', 409);
    }
    if (analysis.plan.alreadyImported) {
      throw new DesktopMigrationError('這個 SQLite 檔案先前已完成移轉。若桌面資料有更新，請重新選擇更新後的資料庫檔。', 'MIGRATION_SOURCE_ALREADY_IMPORTED', 409);
    }
    if (!analysis.plan.canCommit) {
      throw new DesktopMigrationError('仍有資料衝突或驗證錯誤，未寫入任何資料。', 'MIGRATION_CONFLICT', 409);
    }

    const result = await executeDesktopMigrationSafe(analysis, env.DB, session);
    return json({ ok: true, migration: result });
  } catch (error) {
    return migrationErrorResponse(error);
  }
}

export function buildSafeMigrationStatements(analysis, db, session) {
  const { snapshot, target, plan } = analysis;
  const statements = [];

  if (plan.missingAccounts.length) {
    const data = plan.missingAccounts.map(item => ({
      name: item.name,
      sortOrder: plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder,
      isDefault: plan.mode === 'pristine_merge' ? item.isDefault : 0,
      createdAt: item.createdAt
    }));
    statements.push(db.prepare(`
      INSERT INTO accounts(name, sort_order, is_default, created_at)
      SELECT
        json_extract(value, '$.name'),
        CAST(json_extract(value, '$.sortOrder') AS INTEGER),
        CAST(json_extract(value, '$.isDefault') AS INTEGER),
        json_extract(value, '$.createdAt')
      FROM json_each(?)
    `).bind(JSON.stringify(data)));
  }

  if (plan.mode === 'pristine_merge') {
    const accountsJson = JSON.stringify(snapshot.accounts.map(item => ({ name: item.name, sortOrder: item.sortOrder })));
    statements.push(db.prepare(`
      WITH src AS (
        SELECT json_extract(value, '$.name') AS name,
               CAST(json_extract(value, '$.sortOrder') AS INTEGER) AS sort_order
        FROM json_each(?)
      )
      UPDATE accounts
      SET sort_order = (SELECT sort_order FROM src WHERE src.name = accounts.name)
      WHERE EXISTS (SELECT 1 FROM src WHERE src.name = accounts.name)
    `).bind(accountsJson));

    const defaultName = snapshot.accounts.find(item => item.isDefault === 1)?.name;
    if (defaultName) {
      statements.push(db.prepare('UPDATE accounts SET is_default = CASE WHEN name = ? THEN 1 ELSE 0 END').bind(defaultName));
    }
  }

  if (plan.missingGroups.length) {
    const data = plan.missingGroups.map(item => ({
      kind: item.kind,
      name: item.name,
      sortOrder: plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder,
      createdAt: item.createdAt
    }));
    statements.push(db.prepare(`
      INSERT INTO category_groups(kind, name, sort_order, created_at)
      SELECT
        json_extract(value, '$.kind'),
        json_extract(value, '$.name'),
        CAST(json_extract(value, '$.sortOrder') AS INTEGER),
        json_extract(value, '$.createdAt')
      FROM json_each(?)
    `).bind(JSON.stringify(data)));
  }

  if (plan.mode === 'pristine_merge') {
    const groupsJson = JSON.stringify(snapshot.groups.map(item => ({ kind: item.kind, name: item.name, sortOrder: item.sortOrder })));
    statements.push(db.prepare(`
      WITH src AS (
        SELECT json_extract(value, '$.kind') AS kind,
               json_extract(value, '$.name') AS name,
               CAST(json_extract(value, '$.sortOrder') AS INTEGER) AS sort_order
        FROM json_each(?)
      )
      UPDATE category_groups
      SET sort_order = (
        SELECT sort_order FROM src
        WHERE src.kind = category_groups.kind AND src.name = category_groups.name
      )
      WHERE EXISTS (
        SELECT 1 FROM src
        WHERE src.kind = category_groups.kind AND src.name = category_groups.name
      )
    `).bind(groupsJson));
  }

  if (plan.missingCategories.length) {
    const data = plan.missingCategories.map(item => ({
      kind: item.kind,
      groupName: item.groupName,
      name: item.name,
      sortOrder: plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder,
      isFavorite: item.isFavorite,
      createdAt: item.createdAt
    }));
    statements.push(db.prepare(`
      INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
      SELECT
        json_extract(j.value, '$.kind'),
        g.id,
        json_extract(j.value, '$.name'),
        CAST(json_extract(j.value, '$.sortOrder') AS INTEGER),
        CAST(json_extract(j.value, '$.isFavorite') AS INTEGER),
        json_extract(j.value, '$.createdAt')
      FROM json_each(?) AS j
      JOIN category_groups AS g
        ON g.kind = json_extract(j.value, '$.kind')
       AND g.name = json_extract(j.value, '$.groupName')
    `).bind(JSON.stringify(data)));
  }

  if (plan.mode === 'pristine_merge' && snapshot.categories.length) {
    const categoriesJson = JSON.stringify(snapshot.categories.map(item => ({
      kind: item.kind,
      groupName: item.groupName,
      name: item.name,
      sortOrder: item.sortOrder,
      isFavorite: item.isFavorite
    })));
    statements.push(db.prepare(`
      WITH src AS (
        SELECT
          json_extract(value, '$.kind') AS kind,
          json_extract(value, '$.groupName') AS group_name,
          json_extract(value, '$.name') AS name,
          CAST(json_extract(value, '$.sortOrder') AS INTEGER) AS sort_order,
          CAST(json_extract(value, '$.isFavorite') AS INTEGER) AS is_favorite
        FROM json_each(?)
      )
      UPDATE categories
      SET group_id = (
            SELECT g.id
            FROM src JOIN category_groups AS g
              ON g.kind = src.kind AND g.name = src.group_name
            WHERE src.kind = categories.kind AND src.name = categories.name
            LIMIT 1
          ),
          sort_order = (
            SELECT sort_order FROM src
            WHERE src.kind = categories.kind AND src.name = categories.name
          ),
          is_favorite = (
            SELECT is_favorite FROM src
            WHERE src.kind = categories.kind AND src.name = categories.name
          )
      WHERE EXISTS (
        SELECT 1 FROM src
        WHERE src.kind = categories.kind AND src.name = categories.name
      )
    `).bind(categoriesJson));
  }

  for (let start = 0; start < plan.readyTransactions.length; start += TRANSACTION_JSON_CHUNK) {
    const chunk = plan.readyTransactions.slice(start, start + TRANSACTION_JSON_CHUNK);
    statements.push(db.prepare(`
      INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
      SELECT
        json_extract(value, '$.txDate'),
        json_extract(value, '$.accountName'),
        json_extract(value, '$.kind'),
        json_extract(value, '$.categoryName'),
        json_extract(value, '$.summary'),
        CAST(json_extract(value, '$.amount') AS INTEGER),
        json_extract(value, '$.createdAt'),
        json_extract(value, '$.updatedAt')
      FROM json_each(?)
    `).bind(JSON.stringify(chunk)));
  }

  for (let start = 0; start < plan.readyOpeningBalances.length; start += OPENING_JSON_CHUNK) {
    const chunk = plan.readyOpeningBalances.slice(start, start + OPENING_JSON_CHUNK);
    statements.push(db.prepare(`
      INSERT INTO opening_balances(month, account_name, amount, created_at, updated_at)
      SELECT
        json_extract(value, '$.month'),
        json_extract(value, '$.accountName'),
        CAST(json_extract(value, '$.amount') AS INTEGER),
        json_extract(value, '$.createdAt'),
        json_extract(value, '$.updatedAt')
      FROM json_each(?)
    `).bind(JSON.stringify(chunk)));
  }

  if (plan.resultingLockedThrough) {
    statements.push(db.prepare(`
      INSERT INTO app_settings(key, value) VALUES ('locked_through', ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value
    `).bind(plan.resultingLockedThrough));
  }

  const now = new Date().toISOString();
  const historyEntry = {
    sha256: snapshot.source.fileSha256,
    fileName: snapshot.source.fileName,
    schemaVersion: snapshot.source.schemaVersion,
    importedAt: now,
    employeeNo: String(session?.employee_no || ''),
    mode: plan.mode,
    insertedTransactions: plan.readyTransactions.length,
    skippedDuplicateTransactions: plan.duplicateTransactions,
    insertedOpeningBalances: plan.readyOpeningBalances.length
  };
  const history = [historyEntry, ...target.history].slice(0, HISTORY_LIMIT);
  statements.push(db.prepare(`
    INSERT INTO app_settings(key, value) VALUES (?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).bind(HISTORY_KEY, JSON.stringify(history)));

  if (statements.length > MAX_SAFE_BATCH_STATEMENTS) {
    throw new DesktopMigrationError(
      `本次移轉需要 ${statements.length} 個 D1 statements，超過安全上限 ${MAX_SAFE_BATCH_STATEMENTS}。`,
      'MIGRATION_D1_QUERY_LIMIT',
      413
    );
  }

  return { statements, completedAt: now };
}

async function executeDesktopMigrationSafe(analysis, db, session) {
  const { statements, completedAt } = buildSafeMigrationStatements(analysis, db, session);
  try {
    await db.batch(statements);
  } catch (error) {
    console.error('cyaccounting_desktop_migration_write_failed', error instanceof Error ? error.message : String(error));
    throw new DesktopMigrationError('桌面帳本移轉寫入失敗；D1 transaction 已回滾，請重新建立預覽後再試。', 'MIGRATION_WRITE_FAILED', 500);
  }

  const { snapshot, plan } = analysis;
  return {
    sourceSha256: snapshot.source.fileSha256,
    sourceSchemaVersion: snapshot.source.schemaVersion,
    mode: plan.mode,
    insertedAccounts: plan.missingAccounts.length,
    insertedGroups: plan.missingGroups.length,
    insertedCategories: plan.missingCategories.length,
    insertedTransactions: plan.readyTransactions.length,
    skippedDuplicateTransactions: plan.duplicateTransactions,
    insertedOpeningBalances: plan.readyOpeningBalances.length,
    skippedDuplicateOpeningBalances: plan.duplicateOpeningBalances,
    historicalAccountsPreserved: plan.historicalAccounts,
    historicalCategoriesPreserved: plan.historicalCategories,
    lockedThrough: plan.resultingLockedThrough,
    completedAt
  };
}

function migrationErrorResponse(error) {
  if (error instanceof DesktopMigrationError) {
    const body = { ok: false, error: error.message, code: error.code };
    if (error.detail) body.preview = error.detail;
    return json(body, error.status);
  }
  console.error('cyaccounting_desktop_migration_failed', error instanceof Error ? error.message : String(error));
  return json({ ok: false, error: '桌面帳本移轉處理失敗，請稍後再試。', code: 'MIGRATION_FAILED' }, 500);
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
