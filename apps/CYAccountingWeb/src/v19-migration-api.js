const FORMAT = 'CYAccountingLegacyMigration';
const FORMAT_VERSION = 1;
const MAX_SOURCE_FILE_BYTES = 64 * 1024 * 1024;
const MAX_ACCOUNTS = 50;
const MAX_GROUPS = 100;
const MAX_CATEGORIES = 300;
const MAX_TRANSACTIONS = 50_000;
const MAX_OPENING_BALANCES = 10_000;
const MAX_CHUNKS = 500;
const MAX_CHUNK_ROWS = 250;
const MAX_SUMMARY_LENGTH = 100;
const MAX_NAME_LENGTH = 60;
const MAX_SETTING_VALUE_LENGTH = 200;
const MAX_BOUND_PARAMETERS = 100;

const KNOWN_SETTINGS = new Set([
  'locked_through',
  'frequent_summary_basis',
  'frequent_summary_recent_count',
  'frequent_summary_min_count'
]);

export async function handleV19MigrationApi(request, env, session) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/migration/sqlite/')) return null;

  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有 SUPER_ADMIN 可以執行舊帳本遷移。', code: 'MIGRATION_FORBIDDEN' }, 403);
  }
  if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。' }, 503);

  if (url.pathname === '/api/migration/sqlite/status' && request.method === 'GET') return handleStatus(env.DB);
  if (url.pathname === '/api/migration/sqlite/preview' && request.method === 'POST') return handlePreview(request, env.DB);
  if (url.pathname === '/api/migration/sqlite/start' && request.method === 'POST') return handleStart(request, env.DB, session);
  if (url.pathname === '/api/migration/sqlite/chunk' && request.method === 'POST') return handleChunk(request, env.DB);
  if (url.pathname === '/api/migration/sqlite/finish' && request.method === 'POST') return handleFinish(request, env.DB);
  if (url.pathname === '/api/migration/sqlite/abort' && request.method === 'POST') return handleAbort(request, env.DB);
  return json({ ok: false, error: '找不到此遷移功能。', code: 'MIGRATION_ROUTE_NOT_FOUND' }, 404);
}

/**
 * A failed/incomplete migration is also a write lock.  The user must either
 * finish the same run or explicitly abort it before ordinary accounting writes
 * resume.
 */
export async function hasActiveLegacyMigration(db) {
  const row = await db.prepare(`
    SELECT run_id
    FROM legacy_migration_runs
    WHERE status IN ('importing', 'failed')
    ORDER BY created_at DESC
    LIMIT 1
  `).first();
  return row ? String(row.run_id) : null;
}

async function handleStatus(db) {
  const blocking = await loadBlockingRun(db);
  const latest = blocking || await db.prepare(`
    SELECT * FROM legacy_migration_runs
    ORDER BY created_at DESC
    LIMIT 1
  `).first();
  return json({
    ok: true,
    active: Boolean(blocking),
    run: latest ? await runView(db, latest) : null,
    target: await targetState(db)
  });
}

async function handlePreview(request, db) {
  const parsed = await validateMigrationStartPayload(await readJson(request));
  if (!parsed.ok) return errorResponse(parsed, 400);

  const blocking = await loadBlockingRun(db);
  if (blocking) {
    const status = String(blocking.status);
    const same = String(blocking.dataset_sha256) === parsed.manifest.datasetSha256;
    const canResume = status === 'importing' && same;
    return json({
      ok: true,
      canStart: false,
      canResume,
      activeRun: await runView(db, blocking),
      target: await targetState(db),
      source: sourceSummary(parsed.manifest),
      message: status === 'failed'
        ? '上一個遷移作業未通過最終核對；請先中止該作業並回復初始帳本。'
        : canResume
          ? '已有相同舊帳本的遷移作業，可重新選取同一檔案後繼續。'
          : '目前已有另一個舊帳本遷移作業進行中，請先完成或中止該作業。'
    });
  }

  const target = await targetState(db);
  return json({
    ok: true,
    canStart: target.ready,
    canResume: false,
    target,
    source: sourceSummary(parsed.manifest),
    message: target.ready
      ? '來源資料與目標狀態可進行遷移。正式開始後會先移除系統初始範例資料，再寫入舊帳本。'
      : '目前 Web 帳本已含使用者資料或自訂主檔，為避免覆蓋資料，V0.19.0 不允許直接遷移到此 D1。'
  });
}

async function handleStart(request, db, session) {
  const body = await readJson(request);
  const parsed = await validateMigrationStartPayload(body);
  if (!parsed.ok) return errorResponse(parsed, 400);
  if (body?.confirm !== true || String(body?.confirmation || '') !== '匯入舊帳本') {
    return json({ ok: false, error: '請完成舊帳本遷移確認。', code: 'MIGRATION_CONFIRMATION_REQUIRED' }, 400);
  }

  const blocking = await loadBlockingRun(db);
  if (blocking) {
    if (String(blocking.status) === 'failed') {
      return json({ ok: false, error: '上一個遷移作業未通過核對，請先中止並回復初始帳本。', code: 'MIGRATION_FAILED_NEEDS_ABORT' }, 409);
    }
    if (String(blocking.dataset_sha256) !== parsed.manifest.datasetSha256) {
      return json({ ok: false, error: '已有其他舊帳本遷移作業進行中。', code: 'MIGRATION_ALREADY_ACTIVE' }, 409);
    }
    return json({ ok: true, resumed: true, run: await runView(db, blocking) });
  }

  const target = await targetState(db);
  if (!target.ready) {
    return json({ ok: false, error: '目前 Web 帳本不是空白／初始狀態，為避免覆蓋既有資料，已停止遷移。', code: 'MIGRATION_TARGET_NOT_EMPTY', target }, 409);
  }

  const now = new Date().toISOString();
  const runId = `mig_${crypto.randomUUID()}`;
  const { manifest, structure } = parsed;
  const statements = [
    db.prepare('DELETE FROM categories'),
    db.prepare('DELETE FROM category_groups'),
    db.prepare('DELETE FROM accounts'),
    db.prepare("DELETE FROM app_settings WHERE key IN ('locked_through','frequent_summary_basis','frequent_summary_recent_count','frequent_summary_min_count')"),
    db.prepare(`
      INSERT INTO legacy_migration_runs(
        run_id, source_schema_version, source_file_name, source_file_size, source_file_sha256,
        structure_sha256, dataset_sha256,
        expected_accounts, expected_groups, expected_categories, expected_transactions, expected_opening_balances,
        imported_transactions, imported_opening_balances, status,
        employee_id, employee_no, employee_name, created_at, updated_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0, 'importing', ?, ?, ?, ?, ?)
    `).bind(
      runId,
      manifest.sourceSchemaVersion,
      manifest.sourceFileName,
      manifest.sourceFileSize,
      manifest.sourceFileSha256,
      manifest.structureSha256,
      manifest.datasetSha256,
      manifest.counts.accounts,
      manifest.counts.categoryGroups,
      manifest.counts.categories,
      manifest.counts.transactions,
      manifest.counts.openingBalances,
      String(session.employee_id || ''),
      String(session.employee_no || ''),
      String(session.employee_name || ''),
      now,
      now
    )
  ];

  statements.push(...multiInsert(db, 'accounts',
    ['id', 'name', 'sort_order', 'is_default', 'created_at'],
    structure.accounts.map(row => [row.id, row.name, row.sortOrder, row.isDefault, row.createdAt])));
  statements.push(...multiInsert(db, 'category_groups',
    ['id', 'kind', 'name', 'sort_order', 'created_at'],
    structure.categoryGroups.map(row => [row.id, row.kind, row.name, row.sortOrder, row.createdAt])));
  statements.push(...multiInsert(db, 'categories',
    ['id', 'kind', 'group_id', 'name', 'sort_order', 'is_favorite', 'created_at'],
    structure.categories.map(row => [row.id, row.kind, row.groupId, row.name, row.sortOrder, row.isFavorite, row.createdAt])));

  for (const setting of structure.settings) {
    statements.push(db.prepare(`
      INSERT INTO app_settings(key, value) VALUES (?, ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value
    `).bind(setting.key, setting.value));
  }

  const chunkRows = [
    ...manifest.transactionChunks.map(chunk => [runId, 'transactions', chunk.index, chunk.rowCount, chunk.sha256, 'pending']),
    ...manifest.openingBalanceChunks.map(chunk => [runId, 'opening_balances', chunk.index, chunk.rowCount, chunk.sha256, 'pending'])
  ];
  statements.push(...multiInsert(db, 'legacy_migration_chunks',
    ['run_id', 'chunk_kind', 'chunk_index', 'expected_rows', 'expected_sha256', 'status'],
    chunkRows));

  // Keep start within the Free-plan D1 per-invocation query budget as well as
  // the 100-bound-parameter per-query limit enforced by multiInsert().
  if (statements.length > 45) {
    return json({ ok: false, error: '舊帳本主檔結構過大，超過目前安全遷移上限。', code: 'MIGRATION_STRUCTURE_TOO_LARGE' }, 413);
  }

  try {
    await db.batch(statements);
  } catch (error) {
    console.error('cyaccounting_legacy_migration_start_failed', safeError(error));
    return json({ ok: false, error: '無法建立舊帳本遷移作業，尚未開始寫入交易資料。', code: 'MIGRATION_START_FAILED' }, 500);
  }

  const run = await db.prepare('SELECT * FROM legacy_migration_runs WHERE run_id = ?').bind(runId).first();
  return json({ ok: true, resumed: false, run: await runView(db, run) }, 201);
}

async function handleChunk(request, db) {
  const body = await readJson(request);
  const runId = String(body?.runId || '');
  const kind = String(body?.kind || '');
  const index = Number(body?.index);
  const sourceRows = Array.isArray(body?.rows) ? body.rows : null;
  const suppliedSha = String(body?.sha256 || '').toLowerCase();

  if (!/^mig_[0-9a-f-]{36}$/i.test(runId)
      || !['transactions', 'opening_balances'].includes(kind)
      || !Number.isInteger(index) || index < 0
      || !sourceRows || sourceRows.length > MAX_CHUNK_ROWS
      || !isSha256(suppliedSha)) {
    return json({ ok: false, error: '遷移分段資料格式錯誤。', code: 'MIGRATION_CHUNK_INVALID' }, 400);
  }

  const run = await db.prepare("SELECT * FROM legacy_migration_runs WHERE run_id = ? AND status = 'importing'").bind(runId).first();
  if (!run) return json({ ok: false, error: '找不到進行中的遷移作業。', code: 'MIGRATION_RUN_NOT_ACTIVE' }, 409);

  const expected = await db.prepare(`
    SELECT expected_rows, expected_sha256, status
    FROM legacy_migration_chunks
    WHERE run_id = ? AND chunk_kind = ? AND chunk_index = ?
  `).bind(runId, kind, index).first();
  if (!expected) return json({ ok: false, error: '此分段不在遷移清單中。', code: 'MIGRATION_CHUNK_UNEXPECTED' }, 400);

  if (String(expected.status) === 'imported') {
    if (String(expected.expected_sha256) !== suppliedSha || Number(expected.expected_rows) !== sourceRows.length) {
      return json({ ok: false, error: '已完成分段與重新送出的內容不一致。', code: 'MIGRATION_CHUNK_CONFLICT' }, 409);
    }
    return json({ ok: true, duplicate: true, run: await runView(db, run) });
  }

  if (Number(expected.expected_rows) !== sourceRows.length || String(expected.expected_sha256) !== suppliedSha) {
    return json({ ok: false, error: '遷移分段筆數或摘要與預覽不一致。', code: 'MIGRATION_CHUNK_MISMATCH' }, 409);
  }

  const normalized = kind === 'transactions'
    ? validateTransactionRows(sourceRows)
    : validateOpeningBalanceRows(sourceRows);
  if (!normalized.ok) return errorResponse(normalized, 400, 'MIGRATION_CHUNK_DATA_INVALID');

  const actualSha = await sha256Hex(JSON.stringify(normalized.rows));
  if (actualSha !== suppliedSha) {
    return json({ ok: false, error: '遷移分段完整性驗證失敗。', code: 'MIGRATION_CHUNK_DIGEST_MISMATCH' }, 409);
  }

  // Historical rows intentionally use names instead of foreign keys in the
  // desktop and Web schemas.  A deleted/renamed account or category may still
  // be referenced by valid old transactions/opening balances.  Do not require
  // historical names to exist in the current master tables during migration.
  const now = new Date().toISOString();
  const statements = kind === 'transactions'
    ? multiInsert(db, 'transactions',
        ['id', 'tx_date', 'account_name', 'kind', 'category_name', 'summary', 'amount', 'created_at', 'updated_at'],
        normalized.rows.map(row => [row.id, row.txDate, row.accountName, row.kind, row.categoryName, row.summary, row.amount, row.createdAt, row.updatedAt]))
    : multiInsert(db, 'opening_balances',
        ['month', 'account_name', 'amount', 'created_at', 'updated_at'],
        normalized.rows.map(row => [row.month, row.accountName, row.amount, row.createdAt, row.updatedAt]));

  statements.push(
    db.prepare(`
      UPDATE legacy_migration_chunks
      SET status = 'imported', imported_at = ?
      WHERE run_id = ? AND chunk_kind = ? AND chunk_index = ? AND status = 'pending'
    `).bind(now, runId, kind, index),
    db.prepare(`
      UPDATE legacy_migration_runs
      SET ${kind === 'transactions' ? 'imported_transactions' : 'imported_opening_balances'} =
            ${kind === 'transactions' ? 'imported_transactions' : 'imported_opening_balances'} + ?,
          updated_at = ?, last_error = ''
      WHERE run_id = ? AND status = 'importing'
    `).bind(sourceRows.length, now, runId)
  );

  if (statements.length > 45) {
    return json({ ok: false, error: '單次遷移分段過大。', code: 'MIGRATION_CHUNK_TOO_LARGE' }, 413);
  }

  try {
    await db.batch(statements);
  } catch (error) {
    console.error('cyaccounting_legacy_migration_chunk_failed', safeError(error));
    return json({ ok: false, error: '此遷移分段寫入失敗；可重新送出相同分段繼續。', code: 'MIGRATION_CHUNK_WRITE_FAILED' }, 500);
  }

  const updated = await db.prepare('SELECT * FROM legacy_migration_runs WHERE run_id = ?').bind(runId).first();
  return json({ ok: true, duplicate: false, run: await runView(db, updated) });
}

async function handleFinish(request, db) {
  const body = await readJson(request);
  const runId = String(body?.runId || '');
  const datasetSha = String(body?.datasetSha256 || '').toLowerCase();
  if (!/^mig_[0-9a-f-]{36}$/i.test(runId) || !isSha256(datasetSha)) {
    return json({ ok: false, error: '遷移完成資料格式錯誤。', code: 'MIGRATION_FINISH_INVALID' }, 400);
  }

  const run = await db.prepare("SELECT * FROM legacy_migration_runs WHERE run_id = ? AND status = 'importing'").bind(runId).first();
  if (!run) return json({ ok: false, error: '找不到進行中的遷移作業。', code: 'MIGRATION_RUN_NOT_ACTIVE' }, 409);
  if (String(run.dataset_sha256) !== datasetSha) {
    return json({ ok: false, error: '遷移資料摘要與開始時不一致。', code: 'MIGRATION_DATASET_MISMATCH' }, 409);
  }

  const pending = await db.prepare(`
    SELECT COUNT(*) AS count
    FROM legacy_migration_chunks
    WHERE run_id = ? AND status <> 'imported'
  `).bind(runId).first();
  if (Number(pending?.count || 0) !== 0) {
    return json({ ok: false, error: '仍有尚未完成的遷移分段。', code: 'MIGRATION_CHUNKS_PENDING' }, 409);
  }

  const counts = await accountingCounts(db);
  const mismatch = countMismatch(run, counts);
  if (mismatch) {
    const now = new Date().toISOString();
    await db.prepare(`
      UPDATE legacy_migration_runs
      SET status = 'failed', last_error = ?, updated_at = ?
      WHERE run_id = ? AND status = 'importing'
    `).bind(mismatch, now, runId).run();
    return json({ ok: false, error: `遷移後核對失敗：${mismatch}。請中止遷移並回復初始帳本。`, code: 'MIGRATION_RECONCILIATION_FAILED' }, 409);
  }

  const now = new Date().toISOString();
  await db.prepare(`
    UPDATE legacy_migration_runs
    SET status = 'completed', updated_at = ?, completed_at = ?, last_error = ''
    WHERE run_id = ? AND status = 'importing'
  `).bind(now, now, runId).run();
  const completed = await db.prepare('SELECT * FROM legacy_migration_runs WHERE run_id = ?').bind(runId).first();
  return json({ ok: true, completed: true, counts, run: await runView(db, completed) });
}

async function handleAbort(request, db) {
  const body = await readJson(request);
  const runId = String(body?.runId || '');
  if (!/^mig_[0-9a-f-]{36}$/i.test(runId)
      || body?.confirm !== true
      || String(body?.confirmation || '') !== '中止遷移') {
    return json({ ok: false, error: '請完成中止遷移確認。', code: 'MIGRATION_ABORT_CONFIRMATION_REQUIRED' }, 400);
  }

  const run = await db.prepare(`
    SELECT * FROM legacy_migration_runs
    WHERE run_id = ? AND status IN ('importing', 'failed')
  `).bind(runId).first();
  if (!run) return json({ ok: false, error: '找不到可中止的遷移作業。', code: 'MIGRATION_RUN_NOT_ACTIVE' }, 409);

  const now = new Date().toISOString();
  try {
    await db.batch([
      db.prepare('DELETE FROM transactions'),
      db.prepare('DELETE FROM opening_balances'),
      db.prepare('DELETE FROM categories'),
      db.prepare('DELETE FROM category_groups'),
      db.prepare('DELETE FROM accounts'),
      db.prepare("DELETE FROM app_settings WHERE key IN ('locked_through','frequent_summary_basis','frequent_summary_recent_count','frequent_summary_min_count')"),
      db.prepare("INSERT INTO accounts(name, sort_order, is_default, created_at) VALUES ('現金', 0, 1, ?)").bind(now),
      db.prepare("INSERT INTO category_groups(kind, name, sort_order, created_at) VALUES ('income', '收入分類', 0, ?)").bind(now),
      db.prepare("INSERT INTO category_groups(kind, name, sort_order, created_at) VALUES ('expense', '支出分類', 0, ?)").bind(now),
      db.prepare(`
        INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
        SELECT 'income', id, '一般收入', 0, 0, ?
        FROM category_groups WHERE kind = 'income' AND name = '收入分類'
      `).bind(now),
      db.prepare(`
        INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
        SELECT 'expense', id, '一般支出', 0, 0, ?
        FROM category_groups WHERE kind = 'expense' AND name = '支出分類'
      `).bind(now),
      db.prepare(`
        UPDATE legacy_migration_runs
        SET status = 'aborted', updated_at = ?, completed_at = ?, last_error = 'aborted_by_super_admin'
        WHERE run_id = ? AND status IN ('importing', 'failed')
      `).bind(now, now, runId)
    ]);
  } catch (error) {
    console.error('cyaccounting_legacy_migration_abort_failed', safeError(error));
    return json({ ok: false, error: '中止遷移失敗，請勿繼續新增資料並重新嘗試。', code: 'MIGRATION_ABORT_FAILED' }, 500);
  }

  return json({ ok: true, aborted: true, target: await targetState(db) });
}

export async function validateMigrationStartPayload(body) {
  const manifestResult = normalizeManifest(body?.manifest);
  if (!manifestResult.ok) return manifestResult;
  const structureResult = normalizeStructure(body?.structure, manifestResult.value.sourceSchemaVersion);
  if (!structureResult.ok) return structureResult;

  const manifest = manifestResult.value;
  const structure = structureResult.value;
  if (manifest.counts.accounts !== structure.accounts.length
      || manifest.counts.categoryGroups !== structure.categoryGroups.length
      || manifest.counts.categories !== structure.categories.length) {
    return fail('主檔筆數與遷移清單不一致。');
  }

  const refError = validateStructureReferences(structure);
  if (refError) return fail(refError);

  const structureSha = await sha256Hex(JSON.stringify(structure));
  if (structureSha !== manifest.structureSha256) {
    return fail('主檔完整性摘要不一致。', 'MIGRATION_STRUCTURE_DIGEST_MISMATCH');
  }

  const datasetSha = await migrationDatasetDigest(manifest);
  if (datasetSha !== manifest.datasetSha256) {
    return fail('整體資料完整性摘要不一致。', 'MIGRATION_DATASET_DIGEST_MISMATCH');
  }

  return { ok: true, manifest, structure };
}

export async function migrationDatasetDigest(manifest) {
  return sha256Hex(JSON.stringify({
    format: FORMAT,
    formatVersion: FORMAT_VERSION,
    structureSha256: String(manifest.structureSha256 || '').toLowerCase(),
    transactionChunks: (manifest.transactionChunks || []).map(chunk => String(chunk.sha256 || '').toLowerCase()),
    openingBalanceChunks: (manifest.openingBalanceChunks || []).map(chunk => String(chunk.sha256 || '').toLowerCase())
  }));
}

function normalizeManifest(value) {
  if (!value || value.format !== FORMAT || Number(value.formatVersion) !== FORMAT_VERSION) return fail('遷移格式不支援。');
  const sourceSchemaVersion = Number(value.sourceSchemaVersion);
  const sourceFileName = String(value.sourceFileName || '').slice(0, 200);
  const sourceFileSize = Number(value.sourceFileSize);
  const sourceFileSha256 = String(value.sourceFileSha256 || '').toLowerCase();
  const structureSha256 = String(value.structureSha256 || '').toLowerCase();
  const datasetSha256 = String(value.datasetSha256 || '').toLowerCase();
  const counts = {
    accounts: Number(value.counts?.accounts),
    categoryGroups: Number(value.counts?.categoryGroups),
    categories: Number(value.counts?.categories),
    transactions: Number(value.counts?.transactions),
    openingBalances: Number(value.counts?.openingBalances)
  };

  if (![1, 2].includes(sourceSchemaVersion)) return fail('只支援 CYAccounting SQLite schema 1 或 2。');
  if (!sourceFileName || !Number.isInteger(sourceFileSize) || sourceFileSize <= 0 || sourceFileSize > MAX_SOURCE_FILE_BYTES) {
    return fail('來源檔案大小不支援。');
  }
  if (![sourceFileSha256, structureSha256, datasetSha256].every(isSha256)) return fail('來源完整性摘要格式錯誤。');
  if (!validCount(counts.accounts, 1, MAX_ACCOUNTS)
      || !validCount(counts.categoryGroups, 1, MAX_GROUPS)
      || !validCount(counts.categories, 1, MAX_CATEGORIES)
      || !validCount(counts.transactions, 0, MAX_TRANSACTIONS)
      || !validCount(counts.openingBalances, 0, MAX_OPENING_BALANCES)) {
    return fail('舊帳本資料量超過目前安全遷移上限。', 'MIGRATION_SOURCE_TOO_LARGE');
  }

  const txChunks = normalizeChunkManifest(value.transactionChunks, counts.transactions);
  if (!txChunks.ok) return txChunks;
  const obChunks = normalizeChunkManifest(value.openingBalanceChunks, counts.openingBalances);
  if (!obChunks.ok) return obChunks;
  if (txChunks.rows.length + obChunks.rows.length > MAX_CHUNKS) return fail('遷移分段數量過多。');

  return { ok: true, value: {
    format: FORMAT,
    formatVersion: FORMAT_VERSION,
    sourceSchemaVersion,
    sourceFileName,
    sourceFileSize,
    sourceFileSha256,
    structureSha256,
    datasetSha256,
    counts,
    transactionChunks: txChunks.rows,
    openingBalanceChunks: obChunks.rows
  } };
}

function normalizeChunkManifest(value, expectedTotal) {
  const source = Array.isArray(value) ? value : [];
  if (!source.length && expectedTotal !== 0) return fail('遷移分段清單不完整。');
  if (source.length > MAX_CHUNKS) return fail('遷移分段數量過多。');

  let total = 0;
  const seen = new Set();
  const normalized = [];
  for (const item of source) {
    const index = Number(item?.index);
    const rowCount = Number(item?.rowCount);
    const sha256 = String(item?.sha256 || '').toLowerCase();
    if (!Number.isInteger(index) || index < 0 || seen.has(index)
        || !Number.isInteger(rowCount) || rowCount < 1 || rowCount > MAX_CHUNK_ROWS
        || !isSha256(sha256)) return fail('遷移分段清單格式錯誤。');
    seen.add(index);
    total += rowCount;
    normalized.push({ index, rowCount, sha256 });
  }
  normalized.sort((a, b) => a.index - b.index);
  if (normalized.some((item, index) => item.index !== index) || total !== expectedTotal) return fail('遷移分段筆數不一致。');
  return { ok: true, rows: normalized };
}

function normalizeStructure(value, sourceSchemaVersion) {
  if (!value || typeof value !== 'object') return fail('舊帳本主檔資料格式錯誤。');
  const accounts = normalizeAccounts(value.accounts);
  if (!accounts.ok) return accounts;
  const groups = normalizeGroups(value.categoryGroups);
  if (!groups.ok) return groups;
  const categories = normalizeCategories(value.categories, sourceSchemaVersion);
  if (!categories.ok) return categories;
  const settings = normalizeSettings(value.settings);
  if (!settings.ok) return settings;
  return { ok: true, value: {
    accounts: accounts.rows,
    categoryGroups: groups.rows,
    categories: categories.rows,
    settings: settings.rows
  } };
}

function normalizeAccounts(source) {
  if (!Array.isArray(source) || !source.length || source.length > MAX_ACCOUNTS) return fail('帳戶主檔資料量不正確。');
  const ids = new Set();
  const names = new Set();
  let defaults = 0;
  const rows = [];
  for (const item of source) {
    const id = positiveId(item?.id);
    const name = cleanName(item?.name);
    const sortOrder = nonNegativeInt(item?.sortOrder);
    const isDefault = Number(item?.isDefault);
    const createdAt = cleanTimestamp(item?.createdAt);
    if (!id || !name || sortOrder === null || ![0, 1].includes(isDefault) || !createdAt || ids.has(id) || names.has(name)) {
      return fail('帳戶主檔內容不正確。');
    }
    ids.add(id);
    names.add(name);
    defaults += isDefault;
    rows.push({ id, name, sortOrder, isDefault, createdAt });
  }
  if (defaults !== 1) return fail('舊帳本必須且只能有一個預設帳戶。');
  return { ok: true, rows };
}

function normalizeGroups(source) {
  if (!Array.isArray(source) || !source.length || source.length > MAX_GROUPS) return fail('大分類主檔資料量不正確。');
  const ids = new Set();
  const keys = new Set();
  const rows = [];
  for (const item of source) {
    const id = positiveId(item?.id);
    const kind = String(item?.kind || '');
    const name = cleanName(item?.name);
    const sortOrder = nonNegativeInt(item?.sortOrder);
    const createdAt = cleanTimestamp(item?.createdAt);
    const key = `${kind}\u0000${name}`;
    if (!id || !['income', 'expense'].includes(kind) || !name || sortOrder === null || !createdAt || ids.has(id) || keys.has(key)) {
      return fail('大分類主檔內容不正確。');
    }
    ids.add(id);
    keys.add(key);
    rows.push({ id, kind, name, sortOrder, createdAt });
  }
  return { ok: true, rows };
}

function normalizeCategories(source, sourceSchemaVersion) {
  if (!Array.isArray(source) || !source.length || source.length > MAX_CATEGORIES) return fail('科目主檔資料量不正確。');
  const ids = new Set();
  const keys = new Set();
  const rows = [];
  for (const item of source) {
    const id = positiveId(item?.id);
    const kind = String(item?.kind || '');
    const groupId = positiveId(item?.groupId);
    const name = cleanName(item?.name);
    const sortOrder = nonNegativeInt(item?.sortOrder);
    const isFavorite = sourceSchemaVersion >= 2 ? Number(item?.isFavorite || 0) : 0;
    const createdAt = cleanTimestamp(item?.createdAt);
    const key = `${kind}\u0000${name}`;
    if (!id || !groupId || !['income', 'expense'].includes(kind) || !name || sortOrder === null
        || ![0, 1].includes(isFavorite) || !createdAt || ids.has(id) || keys.has(key)) {
      return fail('科目主檔內容不正確。');
    }
    ids.add(id);
    keys.add(key);
    rows.push({ id, kind, groupId, name, sortOrder, isFavorite, createdAt });
  }
  return { ok: true, rows };
}

function normalizeSettings(source) {
  if (!Array.isArray(source)) return fail('帳本設定資料格式錯誤。');
  const seen = new Set();
  const rows = [];
  for (const item of source) {
    const key = String(item?.key || '');
    const value = String(item?.value ?? '');
    if (!KNOWN_SETTINGS.has(key)) continue;
    if (seen.has(key) || value.length > MAX_SETTING_VALUE_LENGTH) return fail('帳本設定資料內容不正確。');
    if (key === 'locked_through' && value && !isMonth(value)) return fail('鎖帳月份格式錯誤。');
    if (key === 'frequent_summary_basis' && !['tx_date', 'created_at'].includes(value)) return fail('常用摘要統計依據錯誤。');
    if (key === 'frequent_summary_recent_count' && (!/^\d+$/.test(value) || Number(value) < 10 || Number(value) > 1000)) {
      return fail('常用摘要最近筆數設定錯誤。');
    }
    if (key === 'frequent_summary_min_count' && (!/^\d+$/.test(value) || Number(value) < 2 || Number(value) > 20)) {
      return fail('常用摘要最低次數設定錯誤。');
    }
    seen.add(key);
    rows.push({ key, value });
  }
  rows.sort((a, b) => a.key.localeCompare(b.key));
  return { ok: true, rows };
}

export function validateTransactionRows(source) {
  const ids = new Set();
  const rows = [];
  if (!Array.isArray(source) || source.length > MAX_CHUNK_ROWS) return fail('交易分段資料量不正確。');
  for (const item of source) {
    const id = positiveId(item?.id);
    const txDate = String(item?.txDate || '');
    const accountName = cleanHistoricalName(item?.accountName);
    const kind = String(item?.kind || '');
    const categoryName = cleanHistoricalName(item?.categoryName);
    const summary = String(item?.summary ?? '').trim();
    const amount = Number(item?.amount);
    const createdAt = cleanTimestamp(item?.createdAt);
    const updatedAt = cleanTimestamp(item?.updatedAt);
    if (!id || ids.has(id) || !isDate(txDate) || !accountName || !['income', 'expense'].includes(kind)
        || !categoryName || summary.length > MAX_SUMMARY_LENGTH
        || !Number.isInteger(amount) || amount < 1 || amount > 9_999_999
        || !createdAt || !updatedAt) return fail('交易資料內容不正確。');
    ids.add(id);
    rows.push({ id, txDate, accountName, kind, categoryName, summary, amount, createdAt, updatedAt });
  }
  return { ok: true, rows };
}

export function validateOpeningBalanceRows(source) {
  const keys = new Set();
  const rows = [];
  if (!Array.isArray(source) || source.length > MAX_CHUNK_ROWS) return fail('期初餘額分段資料量不正確。');
  for (const item of source) {
    const month = String(item?.month || '');
    const accountName = cleanHistoricalName(item?.accountName);
    const amount = Number(item?.amount);
    const createdAt = cleanTimestamp(item?.createdAt);
    const updatedAt = cleanTimestamp(item?.updatedAt);
    const key = `${month}\u0000${accountName}`;
    if (!isMonth(month) || !accountName || !Number.isSafeInteger(amount) || !createdAt || !updatedAt || keys.has(key)) {
      return fail('期初餘額資料內容不正確。');
    }
    keys.add(key);
    rows.push({ month, accountName, amount, createdAt, updatedAt });
  }
  return { ok: true, rows };
}

function validateStructureReferences(structure) {
  const groupById = new Map(structure.categoryGroups.map(row => [row.id, row]));
  for (const category of structure.categories) {
    const group = groupById.get(category.groupId);
    if (!group || group.kind !== category.kind) return '科目所屬大分類關聯不正確。';
  }
  return '';
}

async function targetState(db) {
  const blocking = await loadBlockingRun(db);
  if (blocking) return { ready: false, reason: 'migration_active', activeRunId: String(blocking.run_id) };

  const counts = await accountingCounts(db);
  if (counts.transactions !== 0 || counts.openingBalances !== 0) {
    return { ready: false, reason: 'contains_accounting_data', counts };
  }

  const [accounts, groups, categories] = await Promise.all([
    db.prepare('SELECT name, sort_order, is_default FROM accounts ORDER BY id').all(),
    db.prepare('SELECT kind, name, sort_order FROM category_groups ORDER BY id').all(),
    db.prepare(`
      SELECT c.kind, c.name, c.sort_order, c.is_favorite, g.name AS group_name
      FROM categories c
      JOIN category_groups g ON g.id = c.group_id
      ORDER BY c.id
    `).all()
  ]);
  const a = accounts.results || [];
  const g = groups.results || [];
  const c = categories.results || [];
  const emptyMasters = a.length === 0 && g.length === 0 && c.length === 0;
  const seedOnly = a.length === 1 && a[0].name === '現金' && Number(a[0].is_default) === 1
    && g.length === 2
    && g.some(row => row.kind === 'income' && row.name === '收入分類')
    && g.some(row => row.kind === 'expense' && row.name === '支出分類')
    && c.length === 2
    && c.some(row => row.kind === 'income' && row.name === '一般收入' && row.group_name === '收入分類')
    && c.some(row => row.kind === 'expense' && row.name === '一般支出' && row.group_name === '支出分類');

  if (!emptyMasters && !seedOnly) return { ready: false, reason: 'custom_master_data', counts };
  return { ready: true, reason: seedOnly ? 'seed_only' : 'empty', counts };
}

async function accountingCounts(db) {
  const result = await db.prepare(`
    SELECT 'accounts' AS name, COUNT(*) AS count FROM accounts
    UNION ALL SELECT 'category_groups', COUNT(*) FROM category_groups
    UNION ALL SELECT 'categories', COUNT(*) FROM categories
    UNION ALL SELECT 'transactions', COUNT(*) FROM transactions
    UNION ALL SELECT 'opening_balances', COUNT(*) FROM opening_balances
  `).all();
  const map = new Map((result.results || []).map(row => [String(row.name), Number(row.count || 0)]));
  return {
    accounts: map.get('accounts') || 0,
    categoryGroups: map.get('category_groups') || 0,
    categories: map.get('categories') || 0,
    transactions: map.get('transactions') || 0,
    openingBalances: map.get('opening_balances') || 0
  };
}

function countMismatch(run, counts) {
  const pairs = [
    ['帳戶', Number(run.expected_accounts), counts.accounts],
    ['大分類', Number(run.expected_groups), counts.categoryGroups],
    ['科目', Number(run.expected_categories), counts.categories],
    ['交易', Number(run.expected_transactions), counts.transactions],
    ['期初餘額', Number(run.expected_opening_balances), counts.openingBalances]
  ];
  const bad = pairs.find(([, expected, actual]) => expected !== actual);
  return bad ? `${bad[0]}應為 ${bad[1]} 筆，實際 ${bad[2]} 筆` : '';
}

async function loadBlockingRun(db) {
  return db.prepare(`
    SELECT * FROM legacy_migration_runs
    WHERE status IN ('importing', 'failed')
    ORDER BY created_at DESC
    LIMIT 1
  `).first();
}

async function runView(db, run) {
  const progress = await db.prepare(`
    SELECT chunk_kind,
           COUNT(*) AS total_chunks,
           SUM(CASE WHEN status = 'imported' THEN 1 ELSE 0 END) AS imported_chunks
    FROM legacy_migration_chunks
    WHERE run_id = ?
    GROUP BY chunk_kind
  `).bind(run.run_id).all();
  const chunks = {
    transactions: { total: 0, imported: 0 },
    openingBalances: { total: 0, imported: 0 }
  };
  for (const row of progress.results || []) {
    const target = row.chunk_kind === 'transactions' ? chunks.transactions : chunks.openingBalances;
    target.total = Number(row.total_chunks || 0);
    target.imported = Number(row.imported_chunks || 0);
  }
  return {
    runId: String(run.run_id),
    status: String(run.status),
    sourceSchemaVersion: Number(run.source_schema_version),
    sourceFileName: String(run.source_file_name),
    sourceFileSize: Number(run.source_file_size),
    sourceFileSha256: String(run.source_file_sha256),
    datasetSha256: String(run.dataset_sha256),
    expected: {
      accounts: Number(run.expected_accounts),
      categoryGroups: Number(run.expected_groups),
      categories: Number(run.expected_categories),
      transactions: Number(run.expected_transactions),
      openingBalances: Number(run.expected_opening_balances)
    },
    imported: {
      transactions: Number(run.imported_transactions),
      openingBalances: Number(run.imported_opening_balances)
    },
    chunks,
    createdAt: String(run.created_at),
    updatedAt: String(run.updated_at),
    completedAt: run.completed_at ? String(run.completed_at) : null,
    lastError: String(run.last_error || '')
  };
}

function sourceSummary(manifest) {
  return {
    schemaVersion: manifest.sourceSchemaVersion,
    fileName: manifest.sourceFileName,
    fileSize: manifest.sourceFileSize,
    fileSha256: manifest.sourceFileSha256,
    counts: manifest.counts,
    transactionChunks: manifest.transactionChunks.length,
    openingBalanceChunks: manifest.openingBalanceChunks.length,
    datasetSha256: manifest.datasetSha256
  };
}

function multiInsert(db, table, columns, rows) {
  if (!rows.length) return [];
  const allowed = new Set([
    'accounts',
    'category_groups',
    'categories',
    'transactions',
    'opening_balances',
    'legacy_migration_chunks'
  ]);
  if (!allowed.has(table)) throw new Error('unsupported_migration_table');
  const perStatement = Math.max(1, Math.floor(MAX_BOUND_PARAMETERS / columns.length));
  const statements = [];
  for (let start = 0; start < rows.length; start += perStatement) {
    const chunk = rows.slice(start, start + perStatement);
    const placeholders = chunk.map(() => `(${columns.map(() => '?').join(',')})`).join(',');
    statements.push(
      db.prepare(`INSERT INTO ${table}(${columns.join(',')}) VALUES ${placeholders}`).bind(...chunk.flat())
    );
  }
  return statements;
}

function validCount(value, min, max) {
  return Number.isInteger(value) && value >= min && value <= max;
}

function positiveId(value) {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 && n <= 2_147_483_647 ? n : null;
}

function nonNegativeInt(value) {
  const n = Number(value);
  return Number.isInteger(n) && n >= 0 && n <= 2_147_483_647 ? n : null;
}

function cleanName(value) {
  const text = String(value ?? '');
  if (!text || text.length > MAX_NAME_LENGTH || text !== text.trim()) return '';
  return text;
}

function cleanHistoricalName(value) {
  // Desktop schema stores transaction/opening-balance account/category names as
  // plain text with no FK. Preserve legitimate historical labels while still
  // rejecting empty/unbounded payloads.
  const text = String(value ?? '').trim();
  return text && text.length <= MAX_NAME_LENGTH ? text : '';
}

function cleanTimestamp(value) {
  const text = String(value || '');
  if (!text || text.length > 40 || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/.test(text)) return '';
  return text;
}

function isSha256(value) {
  return /^[0-9a-f]{64}$/i.test(String(value || ''));
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}

function isDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(value || ''))) return false;
  const [y, m, d] = String(value).split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

async function sha256Hex(value) {
  const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : value;
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

function fail(error, code = 'MIGRATION_SOURCE_INVALID') {
  return { ok: false, error, code };
}

function errorResponse(result, status, fallbackCode) {
  return json({
    ok: false,
    error: result.error || '遷移資料格式錯誤。',
    code: result.code || fallbackCode || 'MIGRATION_SOURCE_INVALID'
  }, status);
}

function safeError(error) {
  return error instanceof Error ? error.message : String(error || 'unknown_error');
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
