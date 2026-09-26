const MAX_SOURCE_FILE_BYTES = 20 * 1024 * 1024;
const MAX_ACCOUNTS = 200;
const MAX_GROUPS = 500;
const MAX_CATEGORIES = 2000;
const MAX_TRANSACTIONS = 10000;
const MAX_OPENING_BALANCES = 5000;
const MAX_MASTER_NAME_LENGTH = 60;
const MAX_HISTORICAL_NAME_LENGTH = 200;
const MAX_SUMMARY_LENGTH = 1000;
const TRANSACTION_INSERT_CHUNK = 40;
const OPENING_INSERT_CHUNK = 50;
const HISTORY_KEY = 'desktop_migration_history_v1';
const HISTORY_LIMIT = 20;

export async function handleV19MigrationApi(request, env, session) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/migration/desktop/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以執行桌面帳本移轉。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }
  if (!env?.DB) return json({ ok: false, error: 'D1 尚未綁定。', code: 'DB_NOT_CONFIGURED' }, 503);

  if (url.pathname === '/api/migration/desktop/preview' && request.method === 'POST') {
    return previewDesktopMigration(request, env.DB);
  }
  if (url.pathname === '/api/migration/desktop/commit' && request.method === 'POST') {
    return commitDesktopMigrationRequest(request, env.DB, session);
  }
  return json({ ok: false, error: '找不到此資料移轉功能。', code: 'MIGRATION_ROUTE_NOT_FOUND' }, 404);
}

async function previewDesktopMigration(request, db) {
  try {
    const body = await request.json().catch(() => null);
    const analysis = await analyzeDesktopMigration(body?.snapshot, db);
    return json({ ok: true, ...publicAnalysis(analysis) });
  } catch (error) {
    return migrationErrorResponse(error);
  }
}

async function commitDesktopMigrationRequest(request, db, session) {
  try {
    const body = await request.json().catch(() => null);
    if (body?.confirm !== true) {
      throw new DesktopMigrationError('必須先完成預覽並確認移轉。', 'MIGRATION_CONFIRM_REQUIRED', 400);
    }

    const analysis = await analyzeDesktopMigration(body?.snapshot, db);
    const expectedMode = String(body?.expectedMode || '');
    if (expectedMode && expectedMode !== analysis.plan.mode) {
      throw new DesktopMigrationError('目標帳本狀態已變更，請重新建立移轉預覽。', 'MIGRATION_TARGET_CHANGED', 409);
    }
    if (analysis.plan.alreadyImported) {
      throw new DesktopMigrationError('這個 SQLite 檔案先前已完成移轉。若桌面資料有更新，請重新選擇更新後的資料庫檔。', 'MIGRATION_SOURCE_ALREADY_IMPORTED', 409);
    }
    if (!analysis.plan.canCommit) {
      throw new DesktopMigrationError('仍有資料衝突或驗證錯誤，未寫入任何資料。', 'MIGRATION_CONFLICT', 409, publicAnalysis(analysis));
    }

    const result = await executeDesktopMigration(analysis, db, session);
    return json({ ok: true, migration: result });
  } catch (error) {
    return migrationErrorResponse(error);
  }
}

export async function analyzeDesktopMigration(rawSnapshot, db) {
  const snapshot = normalizeDesktopSnapshot(rawSnapshot);
  const target = await loadTargetState(db, snapshot);
  const plan = planDesktopMigration(snapshot, target);
  return { snapshot, target, plan };
}

export function normalizeDesktopSnapshot(raw) {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    throw new DesktopMigrationError('桌面帳本資料格式錯誤。', 'INVALID_MIGRATION_SNAPSHOT', 400);
  }

  const source = raw.source && typeof raw.source === 'object' ? raw.source : {};
  const schemaVersion = integer(source.schemaVersion, '來源 schema version');
  if (![1, 2].includes(schemaVersion)) {
    throw new DesktopMigrationError(`不支援的 CYAccounting SQLite schema version：${schemaVersion}`, 'UNSUPPORTED_DESKTOP_SCHEMA', 400);
  }
  const fileSha256 = String(source.fileSha256 || '').trim().toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(fileSha256)) {
    throw new DesktopMigrationError('來源 SQLite SHA-256 無效。', 'INVALID_SOURCE_DIGEST', 400);
  }
  const fileSize = integer(source.fileSize, '來源檔案大小');
  if (fileSize < 1 || fileSize > MAX_SOURCE_FILE_BYTES) {
    throw new DesktopMigrationError('SQLite 檔案大小超出允許範圍。', 'SOURCE_FILE_TOO_LARGE', 413);
  }
  const fileName = String(source.fileName || '').trim().slice(0, 200);

  const accounts = boundedArray(raw.accounts, MAX_ACCOUNTS, '帳戶').map((value, index) => ({
    name: masterName(value?.name, `帳戶 ${index + 1}`),
    sortOrder: nonNegativeInteger(value?.sortOrder, `帳戶 ${index + 1} 排序`),
    isDefault: booleanInt(value?.isDefault, `帳戶 ${index + 1} 預設旗標`),
    createdAt: timestampText(value?.createdAt)
  }));
  if (!accounts.length) throw new DesktopMigrationError('來源帳本至少需要一個目前帳戶。', 'SOURCE_ACCOUNT_REQUIRED', 400);
  assertUnique(accounts.map(item => item.name), '來源帳戶名稱重複。');
  if (accounts.filter(item => item.isDefault === 1).length !== 1) {
    throw new DesktopMigrationError('來源帳本的預設帳戶設定異常。', 'SOURCE_DEFAULT_ACCOUNT_INVALID', 400);
  }

  const groups = boundedArray(raw.groups, MAX_GROUPS, '大分類').map((value, index) => ({
    kind: kindValue(value?.kind, `大分類 ${index + 1}`),
    name: masterName(value?.name, `大分類 ${index + 1}`),
    sortOrder: nonNegativeInteger(value?.sortOrder, `大分類 ${index + 1} 排序`),
    createdAt: timestampText(value?.createdAt)
  }));
  assertUnique(groups.map(item => groupKey(item.kind, item.name)), '來源大分類名稱重複。');

  const groupKeys = new Set(groups.map(item => groupKey(item.kind, item.name)));
  const categories = boundedArray(raw.categories, MAX_CATEGORIES, '科目').map((value, index) => {
    const item = {
      kind: kindValue(value?.kind, `科目 ${index + 1}`),
      groupName: masterName(value?.groupName, `科目 ${index + 1} 大分類`),
      name: masterName(value?.name, `科目 ${index + 1}`),
      sortOrder: nonNegativeInteger(value?.sortOrder, `科目 ${index + 1} 排序`),
      isFavorite: booleanInt(value?.isFavorite, `科目 ${index + 1} 常用旗標`),
      createdAt: timestampText(value?.createdAt)
    };
    if (!groupKeys.has(groupKey(item.kind, item.groupName))) {
      throw new DesktopMigrationError(`來源科目「${item.name}」找不到對應大分類「${item.groupName}」。`, 'SOURCE_CATEGORY_GROUP_MISSING', 400);
    }
    return item;
  });
  assertUnique(categories.map(item => categoryKey(item.kind, item.name)), '來源科目名稱重複。');

  // Historical transaction names are deliberately not required to exist in the
  // current master tables. CYAccounting allows deleting/renaming master entries
  // while historical rows retain the old name, and the Web model supports the
  // same historical-name semantics.
  const transactions = boundedArray(raw.transactions, MAX_TRANSACTIONS, '交易').map((value, index) => {
    const item = {
      sourceId: positiveInteger(value?.sourceId, `交易 ${index + 1} ID`),
      txDate: dateValue(value?.txDate, `交易 ${index + 1} 日期`),
      accountName: historicalName(value?.accountName, `交易 ${index + 1} 帳戶`),
      kind: kindValue(value?.kind, `交易 ${index + 1}`),
      categoryName: historicalName(value?.categoryName, `交易 ${index + 1} 科目`),
      summary: String(value?.summary || '').trim(),
      amount: integer(value?.amount, `交易 ${index + 1} 金額`),
      createdAt: timestampText(value?.createdAt),
      updatedAt: timestampText(value?.updatedAt)
    };
    if (item.summary.length > MAX_SUMMARY_LENGTH) {
      throw new DesktopMigrationError(`來源交易 ${item.sourceId} 摘要超過 ${MAX_SUMMARY_LENGTH} 字。`, 'SOURCE_SUMMARY_TOO_LONG', 400);
    }
    if (item.amount < 1 || item.amount > 9_999_999) {
      throw new DesktopMigrationError(`來源交易 ${item.sourceId} 金額超出 1～9,999,999。`, 'SOURCE_AMOUNT_OUT_OF_RANGE', 400);
    }
    return item;
  });
  assertUnique(transactions.map(item => String(item.sourceId)), '來源交易 ID 重複。');

  const openingBalances = boundedArray(raw.openingBalances, MAX_OPENING_BALANCES, '期初餘額').map((value, index) => ({
    month: monthValue(value?.month, `期初餘額 ${index + 1} 月份`),
    accountName: historicalName(value?.accountName, `期初餘額 ${index + 1} 帳戶`),
    amount: integer(value?.amount, `期初餘額 ${index + 1} 金額`),
    createdAt: timestampText(value?.createdAt),
    updatedAt: timestampText(value?.updatedAt)
  }));
  assertUnique(openingBalances.map(item => openingKey(item.month, item.accountName)), '來源期初餘額月份／帳戶重複。');

  const lockedThroughRaw = String(raw.lockedThrough || '').trim();
  const lockedThrough = lockedThroughRaw ? monthValue(lockedThroughRaw, '來源鎖帳月份') : null;

  return {
    source: { schemaVersion, fileSha256, fileSize, fileName },
    accounts,
    groups,
    categories,
    transactions,
    openingBalances,
    lockedThrough
  };
}

async function loadTargetState(db, snapshot) {
  const minDate = snapshot.transactions.length
    ? snapshot.transactions.reduce((value, item) => value < item.txDate ? value : item.txDate, snapshot.transactions[0].txDate)
    : null;
  const maxDate = snapshot.transactions.length
    ? snapshot.transactions.reduce((value, item) => value > item.txDate ? value : item.txDate, snapshot.transactions[0].txDate)
    : null;

  const tasks = [
    db.prepare('SELECT name, sort_order, is_default FROM accounts ORDER BY sort_order, id').all(),
    db.prepare('SELECT kind, name, sort_order FROM category_groups ORDER BY kind, sort_order, id').all(),
    db.prepare(`
      SELECT c.kind, c.name, c.sort_order, c.is_favorite, g.name AS group_name
      FROM categories c
      JOIN category_groups g ON g.id = c.group_id
      ORDER BY c.kind, g.sort_order, c.sort_order, c.id
    `).all(),
    db.prepare('SELECT month, account_name, amount FROM opening_balances').all(),
    db.prepare("SELECT key, value FROM app_settings WHERE key IN ('locked_through', ?)").bind(HISTORY_KEY).all(),
    db.prepare('SELECT COUNT(*) AS count FROM transactions').first(),
    db.prepare('SELECT COUNT(*) AS count FROM opening_balances').first()
  ];
  if (minDate && maxDate) {
    tasks.push(db.prepare(`
      SELECT tx_date, account_name, kind, category_name, summary, amount
      FROM transactions
      WHERE tx_date BETWEEN ? AND ?
    `).bind(minDate, maxDate).all());
  } else {
    tasks.push(Promise.resolve({ results: [] }));
  }

  const [accountsResult, groupsResult, categoriesResult, openingResult, settingsResult, txCountRow, openingCountRow, txResult] = await Promise.all(tasks);
  const settings = new Map((settingsResult.results || []).map(row => [String(row.key || ''), String(row.value || '')]));
  const lockedValue = settings.get('locked_through') || '';
  const target = {
    accounts: (accountsResult.results || []).map(row => ({ name: String(row.name), sortOrder: Number(row.sort_order || 0), isDefault: Number(row.is_default || 0) })),
    groups: (groupsResult.results || []).map(row => ({ kind: String(row.kind), name: String(row.name), sortOrder: Number(row.sort_order || 0) })),
    categories: (categoriesResult.results || []).map(row => ({ kind: String(row.kind), name: String(row.name), groupName: String(row.group_name), sortOrder: Number(row.sort_order || 0), isFavorite: Number(row.is_favorite || 0) })),
    openingBalances: (openingResult.results || []).map(row => ({ month: String(row.month), accountName: String(row.account_name), amount: Number(row.amount) })),
    transactions: (txResult.results || []).map(row => ({
      txDate: String(row.tx_date), accountName: String(row.account_name), kind: String(row.kind),
      categoryName: String(row.category_name), summary: String(row.summary || ''), amount: Number(row.amount)
    })),
    transactionCount: Number(txCountRow?.count || 0),
    openingCount: Number(openingCountRow?.count || 0),
    lockedThrough: isMonth(lockedValue) ? lockedValue : null,
    history: parseHistory(settings.get(HISTORY_KEY))
  };
  target.pristineSeed = isPristineSeedTarget(target);
  return target;
}

export function planDesktopMigration(snapshot, target) {
  const conflicts = [];
  const warnings = [];
  const mode = target.pristineSeed ? 'pristine_merge' : 'merge';

  const targetAccounts = new Map(target.accounts.map(item => [normalizeName(item.name), item]));
  const missingAccounts = [];
  const reusedAccounts = [];
  let nextAccountOrder = target.accounts.reduce((max, item) => Math.max(max, Number(item.sortOrder || 0)), -1) + 1;
  for (const source of [...snapshot.accounts].sort(compareSortOrder)) {
    const existing = targetAccounts.get(source.name);
    if (existing) reusedAccounts.push({ source, existing });
    else missingAccounts.push({ ...source, targetSortOrder: nextAccountOrder++ });
  }

  const targetGroups = new Map(target.groups.map(item => [groupKey(item.kind, item.name), item]));
  const missingGroups = [];
  const reusedGroups = [];
  const nextGroupOrder = new Map(['income', 'expense'].map(kind => [
    kind,
    target.groups.filter(item => item.kind === kind).reduce((max, item) => Math.max(max, Number(item.sortOrder || 0)), -1) + 1
  ]));
  for (const source of [...snapshot.groups].sort(compareKindAndSort)) {
    const existing = targetGroups.get(groupKey(source.kind, source.name));
    if (existing) reusedGroups.push({ source, existing });
    else {
      const order = nextGroupOrder.get(source.kind) || 0;
      missingGroups.push({ ...source, targetSortOrder: order });
      nextGroupOrder.set(source.kind, order + 1);
    }
  }

  const targetCategories = new Map(target.categories.map(item => [categoryKey(item.kind, item.name), item]));
  const missingCategories = [];
  const reusedCategories = [];
  const realignCategories = [];
  const nextCategoryOrder = new Map();
  for (const item of target.categories) {
    const key = groupKey(item.kind, item.groupName);
    nextCategoryOrder.set(key, Math.max(nextCategoryOrder.get(key) || 0, Number(item.sortOrder || 0) + 1));
  }
  for (const source of [...snapshot.categories].sort(compareCategorySort)) {
    const existing = targetCategories.get(categoryKey(source.kind, source.name));
    if (existing) {
      if (normalizeName(existing.groupName) !== source.groupName) {
        if (mode === 'pristine_merge') realignCategories.push({ source, existing });
        else conflicts.push(`科目「${source.name}」在 Web 已位於「${existing.groupName}」，來源帳本則位於「${source.groupName}」。`);
      } else {
        reusedCategories.push({ source, existing });
      }
      continue;
    }
    const orderKey = groupKey(source.kind, source.groupName);
    const order = nextCategoryOrder.get(orderKey) || 0;
    missingCategories.push({ ...source, targetSortOrder: order });
    nextCategoryOrder.set(orderKey, order + 1);
  }

  const existingTxCounts = occurrenceMap(target.transactions.map(transactionFingerprint));
  const incomingTxCounts = new Map();
  const readyTransactions = [];
  let duplicateTransactions = 0;
  for (const item of snapshot.transactions) {
    const key = transactionFingerprint(item);
    const occurrence = (incomingTxCounts.get(key) || 0) + 1;
    incomingTxCounts.set(key, occurrence);
    if (occurrence <= (existingTxCounts.get(key) || 0)) duplicateTransactions += 1;
    else readyTransactions.push(item);
  }

  const targetOpening = new Map(target.openingBalances.map(item => [openingKey(item.month, item.accountName), item]));
  const readyOpeningBalances = [];
  let duplicateOpeningBalances = 0;
  for (const item of snapshot.openingBalances) {
    const existing = targetOpening.get(openingKey(item.month, item.accountName));
    if (!existing) readyOpeningBalances.push(item);
    else if (Number(existing.amount) === item.amount) duplicateOpeningBalances += 1;
    else conflicts.push(`期初餘額 ${item.month}／${item.accountName}：Web 為 ${existing.amount}，來源帳本為 ${item.amount}。`);
  }

  const currentSourceAccounts = new Set(snapshot.accounts.map(item => item.name));
  const currentSourceCategories = new Set(snapshot.categories.map(item => categoryKey(item.kind, item.name)));
  const historicalAccounts = new Set();
  const historicalCategories = new Set();
  for (const item of snapshot.transactions) {
    if (!currentSourceAccounts.has(item.accountName)) historicalAccounts.add(item.accountName);
    if (!currentSourceCategories.has(categoryKey(item.kind, item.categoryName))) historicalCategories.add(`${item.kind}:${item.categoryName}`);
  }
  for (const item of snapshot.openingBalances) {
    if (!currentSourceAccounts.has(item.accountName)) historicalAccounts.add(item.accountName);
  }

  const resultingLockedThrough = maxMonth(target.lockedThrough, snapshot.lockedThrough);
  const alreadyImported = target.history.some(item => String(item?.sha256 || '').toLowerCase() === snapshot.source.fileSha256);

  if (mode === 'pristine_merge') {
    warnings.push('Web 目前仍是初始空白帳本；會沿用來源目前帳戶／科目排序、預設帳戶與常用科目設定。');
    warnings.push('Web 初始預設 master 若來源帳本未使用，為避免破壞既有結構不會自動刪除。');
  } else {
    warnings.push('Web 已有既有設定或帳務資料；本次採保守合併，不覆寫既有帳戶／科目排序與預設設定。');
  }
  if (historicalAccounts.size || historicalCategories.size) {
    warnings.push(`來源含歷史 master 名稱：${historicalAccounts.size} 個帳戶、${historicalCategories.size} 個科目；將保留於歷史交易／期初餘額，不重新啟用為目前 master。`);
  }
  if (snapshot.source.schemaVersion === 1) warnings.push('來源為舊 schema v1；來源沒有常用科目旗標，將視為未標記常用。');
  if (alreadyImported) warnings.push('此 SQLite 檔案的 SHA-256 已存在於成功移轉紀錄，不能再次提交。');
  if (snapshot.lockedThrough && resultingLockedThrough !== snapshot.lockedThrough) {
    warnings.push(`Web 既有鎖帳月份較晚，將保留較嚴格的鎖帳至 ${resultingLockedThrough}。`);
  }

  return {
    mode,
    canCommit: conflicts.length === 0 && !alreadyImported,
    alreadyImported,
    conflicts,
    warnings,
    resultingLockedThrough,
    historicalAccounts: historicalAccounts.size,
    historicalCategories: historicalCategories.size,
    missingAccounts,
    reusedAccounts,
    missingGroups,
    reusedGroups,
    missingCategories,
    reusedCategories,
    realignCategories,
    readyTransactions,
    duplicateTransactions,
    readyOpeningBalances,
    duplicateOpeningBalances
  };
}

async function executeDesktopMigration(analysis, db, session) {
  const { snapshot, target, plan } = analysis;
  const now = new Date().toISOString();
  const statements = [];

  for (const item of plan.missingAccounts) {
    statements.push(db.prepare(`
      INSERT INTO accounts(name, sort_order, is_default, created_at)
      VALUES (?, ?, ?, ?)
    `).bind(
      item.name,
      plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder,
      plan.mode === 'pristine_merge' ? item.isDefault : 0,
      item.createdAt
    ));
  }

  if (plan.mode === 'pristine_merge') {
    for (const item of snapshot.accounts) {
      statements.push(db.prepare('UPDATE accounts SET sort_order = ? WHERE name = ?').bind(item.sortOrder, item.name));
    }
    const defaultName = snapshot.accounts.find(item => item.isDefault === 1)?.name;
    if (defaultName) statements.push(db.prepare('UPDATE accounts SET is_default = CASE WHEN name = ? THEN 1 ELSE 0 END').bind(defaultName));
  }

  for (const item of plan.missingGroups) {
    statements.push(db.prepare(`
      INSERT INTO category_groups(kind, name, sort_order, created_at)
      VALUES (?, ?, ?, ?)
    `).bind(item.kind, item.name, plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder, item.createdAt));
  }
  if (plan.mode === 'pristine_merge') {
    for (const item of snapshot.groups) {
      statements.push(db.prepare('UPDATE category_groups SET sort_order = ? WHERE kind = ? AND name = ?')
        .bind(item.sortOrder, item.kind, item.name));
    }
  }

  for (const item of plan.missingCategories) {
    statements.push(db.prepare(`
      INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
      SELECT ?, id, ?, ?, ?, ? FROM category_groups WHERE kind = ? AND name = ? LIMIT 1
    `).bind(
      item.kind,
      item.name,
      plan.mode === 'pristine_merge' ? item.sortOrder : item.targetSortOrder,
      item.isFavorite,
      item.createdAt,
      item.kind,
      item.groupName
    ));
  }
  if (plan.mode === 'pristine_merge') {
    for (const pair of plan.reusedCategories) {
      statements.push(db.prepare(`
        UPDATE categories SET sort_order = ?, is_favorite = ? WHERE kind = ? AND name = ?
      `).bind(pair.source.sortOrder, pair.source.isFavorite, pair.source.kind, pair.source.name));
    }
    for (const pair of plan.realignCategories) {
      statements.push(db.prepare(`
        UPDATE categories
        SET group_id = (SELECT id FROM category_groups WHERE kind = ? AND name = ? LIMIT 1),
            sort_order = ?, is_favorite = ?
        WHERE kind = ? AND name = ?
      `).bind(pair.source.kind, pair.source.groupName, pair.source.sortOrder, pair.source.isFavorite, pair.source.kind, pair.source.name));
    }
  }

  for (let start = 0; start < plan.readyTransactions.length; start += TRANSACTION_INSERT_CHUNK) {
    const chunk = plan.readyTransactions.slice(start, start + TRANSACTION_INSERT_CHUNK);
    const placeholders = chunk.map(() => '(?, ?, ?, ?, ?, ?, ?, ?)').join(', ');
    const values = [];
    for (const item of chunk) {
      values.push(item.txDate, item.accountName, item.kind, item.categoryName, item.summary, item.amount, item.createdAt, item.updatedAt);
    }
    statements.push(db.prepare(`
      INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
      VALUES ${placeholders}
    `).bind(...values));
  }

  for (let start = 0; start < plan.readyOpeningBalances.length; start += OPENING_INSERT_CHUNK) {
    const chunk = plan.readyOpeningBalances.slice(start, start + OPENING_INSERT_CHUNK);
    const placeholders = chunk.map(() => '(?, ?, ?, ?, ?)').join(', ');
    const values = [];
    for (const item of chunk) values.push(item.month, item.accountName, item.amount, item.createdAt, item.updatedAt);
    statements.push(db.prepare(`
      INSERT INTO opening_balances(month, account_name, amount, created_at, updated_at)
      VALUES ${placeholders}
    `).bind(...values));
  }

  if (plan.resultingLockedThrough) {
    statements.push(db.prepare(`
      INSERT INTO app_settings(key, value) VALUES ('locked_through', ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value
    `).bind(plan.resultingLockedThrough));
  }

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

  try {
    await db.batch(statements);
  } catch (error) {
    console.error('cyaccounting_desktop_migration_write_failed', error instanceof Error ? error.message : String(error));
    throw new DesktopMigrationError('桌面帳本移轉寫入失敗；D1 transaction 已回滾，請重新建立預覽後再試。', 'MIGRATION_WRITE_FAILED', 500);
  }

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
    completedAt: now
  };
}

function publicAnalysis(analysis) {
  const { snapshot, target, plan } = analysis;
  return {
    source: {
      fileName: snapshot.source.fileName,
      fileSize: snapshot.source.fileSize,
      fileSha256: snapshot.source.fileSha256,
      schemaVersion: snapshot.source.schemaVersion,
      counts: {
        accounts: snapshot.accounts.length,
        groups: snapshot.groups.length,
        categories: snapshot.categories.length,
        transactions: snapshot.transactions.length,
        openingBalances: snapshot.openingBalances.length
      },
      lockedThrough: snapshot.lockedThrough
    },
    target: {
      transactionCount: target.transactionCount,
      openingCount: target.openingCount,
      lockedThrough: target.lockedThrough,
      pristineSeed: target.pristineSeed
    },
    plan: {
      mode: plan.mode,
      canCommit: plan.canCommit,
      alreadyImported: plan.alreadyImported,
      conflicts: plan.conflicts.slice(0, 50),
      warnings: plan.warnings,
      resultingLockedThrough: plan.resultingLockedThrough,
      historicalAccounts: plan.historicalAccounts,
      historicalCategories: plan.historicalCategories,
      accounts: { insert: plan.missingAccounts.length, reuse: plan.reusedAccounts.length },
      groups: { insert: plan.missingGroups.length, reuse: plan.reusedGroups.length },
      categories: { insert: plan.missingCategories.length, reuse: plan.reusedCategories.length, realign: plan.realignCategories.length },
      transactions: { insert: plan.readyTransactions.length, duplicate: plan.duplicateTransactions },
      openingBalances: { insert: plan.readyOpeningBalances.length, duplicate: plan.duplicateOpeningBalances }
    }
  };
}

function isPristineSeedTarget(target) {
  if (target.transactionCount !== 0 || target.openingCount !== 0 || target.lockedThrough) return false;
  if (target.accounts.length !== 1 || normalizeName(target.accounts[0]?.name) !== '現金') return false;
  const groups = new Set(target.groups.map(item => groupKey(item.kind, item.name)));
  if (groups.size !== 2 || !groups.has(groupKey('income', '收入分類')) || !groups.has(groupKey('expense', '支出分類'))) return false;
  const categories = new Map(target.categories.map(item => [categoryKey(item.kind, item.name), normalizeName(item.groupName)]));
  return categories.size === 2
    && categories.get(categoryKey('income', '一般收入')) === '收入分類'
    && categories.get(categoryKey('expense', '一般支出')) === '支出分類';
}

function parseHistory(value) {
  if (!value) return [];
  try {
    const data = JSON.parse(value);
    return Array.isArray(data) ? data.slice(0, HISTORY_LIMIT).filter(item => item && typeof item === 'object') : [];
  } catch {
    return [];
  }
}

function occurrenceMap(values) {
  const map = new Map();
  for (const value of values) map.set(value, (map.get(value) || 0) + 1);
  return map;
}

function transactionFingerprint(item) {
  return JSON.stringify([
    item.txDate,
    normalizeName(item.accountName),
    item.kind,
    normalizeName(item.categoryName),
    String(item.summary || '').trim(),
    Number(item.amount)
  ]);
}

function compareSortOrder(a, b) {
  return a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'zh-Hant');
}
function compareKindAndSort(a, b) {
  return a.kind.localeCompare(b.kind) || compareSortOrder(a, b);
}
function compareCategorySort(a, b) {
  return a.kind.localeCompare(b.kind) || a.groupName.localeCompare(b.groupName, 'zh-Hant') || compareSortOrder(a, b);
}
function maxMonth(a, b) {
  if (!a) return b || null;
  if (!b) return a || null;
  return a > b ? a : b;
}
function groupKey(kind, name) {
  return `${kind}\u0000${normalizeName(name)}`;
}
function categoryKey(kind, name) {
  return `${kind}\u0000${normalizeName(name)}`;
}
function openingKey(month, accountName) {
  return `${month}\u0000${normalizeName(accountName)}`;
}

function boundedArray(value, max, label) {
  if (!Array.isArray(value)) throw new DesktopMigrationError(`${label}資料格式錯誤。`, 'INVALID_MIGRATION_SNAPSHOT', 400);
  if (value.length > max) throw new DesktopMigrationError(`${label}筆數超過單次移轉上限 ${max.toLocaleString()}。`, 'MIGRATION_TOO_MANY_ROWS', 413);
  return value;
}
function normalizeName(value) {
  return String(value ?? '').trim().replace(/\s+/g, ' ');
}
function masterName(value, label) {
  return constrainedName(value, label, MAX_MASTER_NAME_LENGTH);
}
function historicalName(value, label) {
  return constrainedName(value, label, MAX_HISTORICAL_NAME_LENGTH);
}
function constrainedName(value, label, max) {
  const name = normalizeName(value);
  if (!name || name.length > max) {
    throw new DesktopMigrationError(`${label}名稱不可空白且不可超過 ${max} 字。`, 'INVALID_SOURCE_NAME', 400);
  }
  return name;
}
function timestampText(value) {
  const text = String(value || '').trim();
  if (!text) return new Date().toISOString();
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?(?:Z|[+-]\d{2}:\d{2})?$/.test(text)) {
    throw new DesktopMigrationError('來源時間欄位格式異常。', 'INVALID_SOURCE_TIMESTAMP', 400);
  }
  return text;
}
function kindValue(value, label) {
  const kind = String(value || '').trim();
  if (!['income', 'expense'].includes(kind)) {
    throw new DesktopMigrationError(`${label}的收支類型無效。`, 'INVALID_SOURCE_KIND', 400);
  }
  return kind;
}
function booleanInt(value, label) {
  const number = Number(value);
  if (number !== 0 && number !== 1) throw new DesktopMigrationError(`${label}無效。`, 'INVALID_SOURCE_FLAG', 400);
  return number;
}
function integer(value, label) {
  const number = Number(value);
  if (!Number.isSafeInteger(number)) throw new DesktopMigrationError(`${label}必須是整數。`, 'INVALID_SOURCE_INTEGER', 400);
  return number;
}
function nonNegativeInteger(value, label) {
  const number = integer(value, label);
  if (number < 0) throw new DesktopMigrationError(`${label}不可小於 0。`, 'INVALID_SOURCE_INTEGER', 400);
  return number;
}
function positiveInteger(value, label) {
  const number = integer(value, label);
  if (number < 1) throw new DesktopMigrationError(`${label}必須大於 0。`, 'INVALID_SOURCE_INTEGER', 400);
  return number;
}
function dateValue(value, label) {
  const text = String(value || '').trim();
  if (!isDate(text)) throw new DesktopMigrationError(`${label}格式無效。`, 'INVALID_SOURCE_DATE', 400);
  return text;
}
function monthValue(value, label) {
  const text = String(value || '').trim();
  if (!isMonth(text)) throw new DesktopMigrationError(`${label}格式無效。`, 'INVALID_SOURCE_MONTH', 400);
  return text;
}
function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}
function isDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(value || ''))) return false;
  const [year, month, day] = String(value).split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}
function assertUnique(values, message) {
  if (new Set(values).size !== values.length) throw new DesktopMigrationError(message, 'SOURCE_DUPLICATE_KEY', 400);
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

export class DesktopMigrationError extends Error {
  constructor(message, code, status = 400, detail = null) {
    super(message);
    this.name = 'DesktopMigrationError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
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
