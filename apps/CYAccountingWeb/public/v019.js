(() => {
  const MAX_SQLITE_BYTES = 20 * 1024 * 1024;
  const LIMITS = { accounts: 200, groups: 500, categories: 2000, transactions: 10000, openingBalances: 5000 };
  const REQUIRED_TABLES = ['meta', 'accounts', 'category_groups', 'categories', 'transactions', 'opening_balances', 'app_settings'];
  const SQLJS_SCRIPT = '/vendor/sqljs/sql-wasm.js';
  const SQLJS_WASM = '/vendor/sqljs/sql-wasm.wasm';

  const migrationState = {
    file: null,
    snapshot: null,
    preview: null,
    loadingSqlJs: null,
    committed: false
  };

  window.addEventListener('load', async () => {
    const version = document.querySelector('.version');
    if (version) version.textContent = 'V0.19.0';
    const role = await currentRoleV19();
    if (role === 'SUPER_ADMIN') installMigrationSettingsV19();
  });

  async function currentRoleV19() {
    try {
      const response = await fetch('/api/auth/me', { cache: 'no-store' });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) return '';
      return String(data?.user?.role || '');
    } catch {
      return '';
    }
  }

  function installMigrationSettingsV19() {
    if (document.querySelector('[data-settings-tab="migration"]')) return;
    const nav = document.querySelector('.settings-nav');
    const content = document.querySelector('.settings-content');
    if (!nav || !content || typeof setSettingsTab !== 'function') return;

    const tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'settings-tab';
    tab.dataset.settingsTab = 'migration';
    tab.textContent = '資料移轉';
    nav.appendChild(tab);

    const pane = document.createElement('section');
    pane.className = 'settings-pane migration-pane-v19';
    pane.dataset.settingsPane = 'migration';
    pane.innerHTML = migrationPaneHtmlV19();
    content.insertBefore(pane, document.querySelector('#settingsMessage'));

    if (typeof els === 'object' && Array.isArray(els.settingsTabs) && Array.isArray(els.settingsPanes)) {
      els.settingsTabs.push(tab);
      els.settingsPanes.push(pane);
    }
    tab.addEventListener('click', () => setSettingsTab('migration'));
    bindMigrationPaneV19(pane);
  }

  function migrationPaneHtmlV19() {
    return `
      <div class="migration-heading-v19">
        <div><h3>CYAccounting 桌面帳本移轉</h3><p class="hint">將既有桌面版 SQLite 帳本安全合併到目前 Web 帳本。只有超級管理員可執行。</p></div>
        <span class="migration-local-badge-v19">SQLite 本機解析</span>
      </div>
      <div class="migration-warning-v19">
        <strong>選檔前請先關閉桌面版 CYAccounting。</strong>
        <span>桌面版使用 SQLite WAL；若程式仍開啟，單獨讀取 <code>Data/CYaccounting.db</code> 可能尚未包含 WAL 中的最新資料。也可以選擇最近完成且已驗證的桌面備份檔。</span>
      </div>
      <div class="migration-source-v19">
        <label><span>SQLite 帳本</span><input id="desktopMigrationFileV19" type="file" accept=".db,.sqlite,.sqlite3,application/vnd.sqlite3,application/x-sqlite3"></label>
        <button id="desktopMigrationInspectV19" class="primary compact" type="button" disabled>解析並建立預覽</button>
      </div>
      <p class="hint migration-privacy-v19">原始 SQLite 檔只在你的瀏覽器中解析，不會上傳到伺服器；Worker 只接收解析後的帳戶、科目、交易與期初餘額資料。</p>
      <div id="desktopMigrationMessageV19" class="dialog-message"></div>
      <div id="desktopMigrationSourceV19" class="migration-source-summary-v19 hidden"></div>
      <div id="desktopMigrationPreviewV19" class="migration-preview-v19 hidden"></div>
      <div class="migration-actions-v19">
        <button id="desktopMigrationCommitV19" class="primary" type="button" disabled>確認執行移轉</button>
      </div>`;
  }

  function bindMigrationPaneV19(pane) {
    const fileInput = pane.querySelector('#desktopMigrationFileV19');
    const inspect = pane.querySelector('#desktopMigrationInspectV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    fileInput?.addEventListener('change', () => {
      migrationState.file = fileInput.files?.[0] || null;
      migrationState.snapshot = null;
      migrationState.preview = null;
      migrationState.committed = false;
      resetMigrationPreviewV19(pane);
      if (!migrationState.file) return;
      if (migrationState.file.size > MAX_SQLITE_BYTES) {
        setMigrationMessageV19(pane, 'SQLite 檔案不可超過 20 MB。', true);
        inspect.disabled = true;
        return;
      }
      inspect.disabled = false;
      setMigrationMessageV19(pane, `已選擇 ${migrationState.file.name}（${bytesV19(migrationState.file.size)}）。`);
    });
    inspect?.addEventListener('click', () => inspectDesktopSqliteV19(pane));
    commit?.addEventListener('click', () => commitDesktopMigrationV19(pane));
  }

  async function inspectDesktopSqliteV19(pane) {
    const file = migrationState.file;
    if (!file) return;
    setMigrationBusyV19(pane, true);
    resetMigrationPreviewV19(pane, false);
    setMigrationMessageV19(pane, '正在本機檢查 SQLite 完整性與資料內容…');
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const sha256 = await sha256V19(bytes);
      const snapshot = await readDesktopSqliteV19(bytes, file, sha256);
      migrationState.snapshot = snapshot;
      renderSourceSummaryV19(pane, snapshot);
      setMigrationMessageV19(pane, 'SQLite 本機解析完成，正在比對 Web 帳本…');
      const data = await jsonFetchV19('/api/migration/desktop/preview', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ snapshot })
      });
      migrationState.preview = data;
      renderMigrationPreviewV19(pane, data);
      setMigrationMessageV19(pane, data.plan?.canCommit ? '預覽完成。確認內容後即可執行移轉。' : '預覽完成，但目前有衝突或已移轉紀錄，不能提交。', !data.plan?.canCommit);
    } catch (error) {
      migrationState.snapshot = null;
      migrationState.preview = null;
      renderMigrationErrorV19(pane, error);
    } finally {
      setMigrationBusyV19(pane, false);
    }
  }

  async function readDesktopSqliteV19(bytes, file, sha256) {
    const SQL = await loadSqlJsV19();
    let db;
    try {
      db = new SQL.Database(bytes);
      const integrity = scalarV19(db, 'PRAGMA integrity_check');
      if (String(integrity || '').toLowerCase() !== 'ok') throw new Error(`SQLite 完整性檢查失敗：${integrity || 'unknown'}`);
      const foreignKeys = rowsV19(db, 'PRAGMA foreign_key_check');
      if (foreignKeys.length) throw new Error('SQLite 關聯完整性檢查失敗，請先修復桌面帳本。');

      const tables = new Set(rowsV19(db, "SELECT name FROM sqlite_master WHERE type='table'").map(row => String(row.name || '')));
      const missing = REQUIRED_TABLES.filter(name => !tables.has(name));
      if (missing.length) throw new Error(`不是支援的 CYAccounting 帳本；缺少資料表：${missing.join('、')}`);

      const schemaVersion = Number(scalarV19(db, "SELECT value FROM meta WHERE key='schema_version' LIMIT 1"));
      if (![1, 2].includes(schemaVersion)) throw new Error(`不支援的 CYAccounting SQLite schema version：${schemaVersion || 'unknown'}`);

      enforceCountV19(db, 'accounts', LIMITS.accounts, '帳戶');
      enforceCountV19(db, 'category_groups', LIMITS.groups, '大分類');
      enforceCountV19(db, 'categories', LIMITS.categories, '科目');
      enforceCountV19(db, 'transactions', LIMITS.transactions, '交易');
      enforceCountV19(db, 'opening_balances', LIMITS.openingBalances, '期初餘額');

      const categoryColumns = new Set(rowsV19(db, 'PRAGMA table_info(categories)').map(row => String(row.name || '')));
      const favoriteSelect = categoryColumns.has('is_favorite') ? 'c.is_favorite AS is_favorite' : '0 AS is_favorite';

      const accounts = rowsV19(db, 'SELECT name, sort_order, is_default, created_at FROM accounts ORDER BY sort_order, id').map(row => ({
        name: row.name, sortOrder: Number(row.sort_order), isDefault: Number(row.is_default), createdAt: row.created_at
      }));
      const groups = rowsV19(db, 'SELECT kind, name, sort_order, created_at FROM category_groups ORDER BY kind, sort_order, id').map(row => ({
        kind: row.kind, name: row.name, sortOrder: Number(row.sort_order), createdAt: row.created_at
      }));
      const categories = rowsV19(db, `
        SELECT c.kind, g.name AS group_name, c.name, c.sort_order, ${favoriteSelect}, c.created_at
        FROM categories c JOIN category_groups g ON g.id = c.group_id
        ORDER BY c.kind, g.sort_order, c.sort_order, c.id
      `).map(row => ({
        kind: row.kind, groupName: row.group_name, name: row.name, sortOrder: Number(row.sort_order),
        isFavorite: Number(row.is_favorite || 0), createdAt: row.created_at
      }));
      const transactions = rowsV19(db, `
        SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at
        FROM transactions ORDER BY id
      `).map(row => ({
        sourceId: Number(row.id), txDate: row.tx_date, accountName: row.account_name, kind: row.kind,
        categoryName: row.category_name, summary: row.summary || '', amount: Number(row.amount),
        createdAt: row.created_at, updatedAt: row.updated_at
      }));
      const openingBalances = rowsV19(db, `
        SELECT month, account_name, amount, created_at, updated_at
        FROM opening_balances ORDER BY month, account_name
      `).map(row => ({
        month: row.month, accountName: row.account_name, amount: Number(row.amount),
        createdAt: row.created_at, updatedAt: row.updated_at
      }));
      const lockedThrough = String(scalarV19(db, "SELECT value FROM app_settings WHERE key='locked_through' LIMIT 1") || '').trim() || null;

      return {
        source: { schemaVersion, fileName: file.name, fileSize: file.size, fileSha256: sha256 },
        accounts, groups, categories, transactions, openingBalances, lockedThrough
      };
    } catch (error) {
      if (error?.message?.includes('file is not a database')) throw new Error('選取的檔案不是有效的 SQLite 資料庫。');
      throw error;
    } finally {
      try { db?.close(); } catch { /* no-op */ }
    }
  }

  function enforceCountV19(db, table, max, label) {
    const count = Number(scalarV19(db, `SELECT COUNT(*) FROM ${table}`) || 0);
    if (count > max) throw new Error(`${label}共有 ${count.toLocaleString()} 筆，超過單次移轉上限 ${max.toLocaleString()} 筆。`);
  }

  function rowsV19(db, sql) {
    const result = db.exec(sql)?.[0];
    if (!result) return [];
    return (result.values || []).map(values => Object.fromEntries(result.columns.map((column, index) => [column, values[index]])));
  }

  function scalarV19(db, sql) {
    const result = db.exec(sql)?.[0];
    return result?.values?.[0]?.[0] ?? null;
  }

  async function loadSqlJsV19() {
    if (window.SQL && typeof window.SQL.Database === 'function') return window.SQL;
    if (!migrationState.loadingSqlJs) {
      migrationState.loadingSqlJs = new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${SQLJS_SCRIPT}"]`);
        if (existing) {
          existing.addEventListener('load', initialize, { once: true });
          existing.addEventListener('error', () => reject(new Error('SQLite 解析元件載入失敗。')), { once: true });
          return;
        }
        const script = document.createElement('script');
        script.src = SQLJS_SCRIPT;
        script.onload = initialize;
        script.onerror = () => reject(new Error('SQLite 解析元件載入失敗。'));
        document.head.appendChild(script);

        async function initialize() {
          try {
            if (typeof window.initSqlJs !== 'function') throw new Error('SQLite 解析元件初始化失敗。');
            const SQL = await window.initSqlJs({ locateFile: () => SQLJS_WASM });
            window.SQL = SQL;
            resolve(SQL);
          } catch (error) {
            reject(error);
          }
        }
      });
    }
    return migrationState.loadingSqlJs;
  }

  function renderSourceSummaryV19(pane, snapshot) {
    const target = pane.querySelector('#desktopMigrationSourceV19');
    if (!target) return;
    target.classList.remove('hidden');
    target.innerHTML = `
      <div><span>檔案</span><strong>${escapeV19(snapshot.source.fileName)}</strong></div>
      <div><span>SQLite schema</span><strong>v${snapshot.source.schemaVersion}</strong></div>
      <div><span>SHA-256</span><code title="${escapeV19(snapshot.source.fileSha256)}">${escapeV19(snapshot.source.fileSha256.slice(0, 16))}…</code></div>
      <div><span>資料</span><strong>${snapshot.transactions.length.toLocaleString()} 筆交易</strong></div>`;
  }

  function renderMigrationPreviewV19(pane, data) {
    const box = pane.querySelector('#desktopMigrationPreviewV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (!box) return;
    const plan = data.plan || {};
    const source = data.source || {};
    const target = data.target || {};
    box.classList.remove('hidden');
    box.innerHTML = `
      <div class="migration-plan-head-v19">
        <div><span>移轉模式</span><strong>${plan.mode === 'pristine_merge' ? '空白 Web 帳本初始化合併' : '既有 Web 帳本保守合併'}</strong></div>
        <span class="migration-status-v19 ${plan.canCommit ? 'ok' : 'blocked'}">${plan.canCommit ? '可執行' : '暫停'}</span>
      </div>
      <div class="migration-grid-v19">
        ${statV19('帳戶', plan.accounts?.insert, `沿用 ${plan.accounts?.reuse || 0}`)}
        ${statV19('大分類', plan.groups?.insert, `沿用 ${plan.groups?.reuse || 0}`)}
        ${statV19('科目', plan.categories?.insert, `沿用 ${plan.categories?.reuse || 0} · 調整 ${plan.categories?.realign || 0}`)}
        ${statV19('交易', plan.transactions?.insert, `重複略過 ${plan.transactions?.duplicate || 0}`)}
        ${statV19('期初餘額', plan.openingBalances?.insert, `重複略過 ${plan.openingBalances?.duplicate || 0}`)}
        ${statV19('鎖帳至', plan.resultingLockedThrough || '—', `Web 原本 ${target.lockedThrough || '未鎖帳'}`, false)}
      </div>
      ${listBlockV19('阻擋衝突', plan.conflicts, 'error')}
      ${listBlockV19('注意事項', plan.warnings, 'warn')}
      <p class="hint">來源：${Number(source.counts?.accounts || 0)} 帳戶、${Number(source.counts?.categories || 0)} 科目、${Number(source.counts?.transactions || 0).toLocaleString()} 交易。Web 目前共有 ${Number(target.transactionCount || 0).toLocaleString()} 筆交易。</p>`;
    if (commit) commit.disabled = !plan.canCommit || migrationState.committed;
  }

  function statV19(label, value, detail, numeric = true) {
    const shown = numeric && Number.isFinite(Number(value)) ? Number(value).toLocaleString() : escapeV19(value ?? '—');
    return `<div class="migration-stat-v19"><span>${escapeV19(label)}</span><strong>${shown}</strong><small>${escapeV19(detail || '')}</small></div>`;
  }

  function listBlockV19(title, items, kind) {
    const values = Array.isArray(items) ? items : [];
    if (!values.length) return '';
    return `<div class="migration-list-v19 ${kind}"><strong>${escapeV19(title)}</strong><ul>${values.slice(0, 50).map(item => `<li>${escapeV19(item)}</li>`).join('')}</ul></div>`;
  }

  async function commitDesktopMigrationV19(pane) {
    const snapshot = migrationState.snapshot;
    const preview = migrationState.preview;
    if (!snapshot || !preview?.plan?.canCommit || migrationState.committed) return;
    const tx = Number(preview.plan.transactions?.insert || 0);
    const accounts = Number(preview.plan.accounts?.insert || 0);
    const categories = Number(preview.plan.categories?.insert || 0);
    const opening = Number(preview.plan.openingBalances?.insert || 0);
    const message = `確定執行桌面帳本移轉？\n\n將新增：\n- ${accounts} 個帳戶\n- ${categories} 個科目\n- ${tx} 筆交易\n- ${opening} 筆期初餘額\n\n既有 Web 交易不會被刪除。`;
    if (!confirm(message)) return;

    setMigrationBusyV19(pane, true);
    setMigrationMessageV19(pane, '正在寫入 D1；此步驟失敗會整批回滾…');
    try {
      const data = await jsonFetchV19('/api/migration/desktop/commit', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ snapshot, confirm: true, expectedMode: preview.plan.mode })
      });
      migrationState.committed = true;
      const commit = pane.querySelector('#desktopMigrationCommitV19');
      if (commit) commit.disabled = true;
      const result = data.migration || {};
      setMigrationMessageV19(pane, `移轉完成：新增 ${Number(result.insertedTransactions || 0).toLocaleString()} 筆交易，略過 ${Number(result.skippedDuplicateTransactions || 0).toLocaleString()} 筆既有重複資料。`);
      if (typeof refreshBootstrap === 'function') await refreshBootstrap();
      if (typeof loadTransactions === 'function') await loadTransactions();
      renderMigrationPreviewV19(pane, { ...preview, plan: { ...preview.plan, canCommit: false, warnings: [...(preview.plan.warnings || []), '本次移轉已完成；如來源資料之後有新增內容，請重新選取更新後的 SQLite 檔。'] } });
    } catch (error) {
      setMigrationMessageV19(pane, error.message || '移轉失敗。', true);
      if (error.preview) renderMigrationPreviewV19(pane, error.preview);
    } finally {
      setMigrationBusyV19(pane, false);
    }
  }

  async function jsonFetchV19(url, options) {
    const response = await fetch(url, options);
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.ok === false) {
      const error = new Error(data.error || `HTTP ${response.status}`);
      error.code = data.code;
      error.preview = data.preview;
      throw error;
    }
    return data;
  }

  function renderMigrationErrorV19(pane, error) {
    setMigrationMessageV19(pane, error?.message || 'SQLite 解析失敗。', true);
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (commit) commit.disabled = true;
  }

  function resetMigrationPreviewV19(pane, clearMessage = true) {
    pane.querySelector('#desktopMigrationSourceV19')?.classList.add('hidden');
    pane.querySelector('#desktopMigrationPreviewV19')?.classList.add('hidden');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (commit) commit.disabled = true;
    if (clearMessage) setMigrationMessageV19(pane, '');
  }

  function setMigrationBusyV19(pane, busy) {
    const inspect = pane.querySelector('#desktopMigrationInspectV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    const file = pane.querySelector('#desktopMigrationFileV19');
    if (inspect) inspect.disabled = busy || !migrationState.file;
    if (commit) commit.disabled = busy || !migrationState.preview?.plan?.canCommit || migrationState.committed;
    if (file) file.disabled = busy;
  }

  function setMigrationMessageV19(pane, text, isError = false) {
    const element = pane.querySelector('#desktopMigrationMessageV19');
    if (!element) return;
    element.textContent = text || '';
    element.classList.toggle('error', Boolean(isError));
  }

  async function sha256V19(bytes) {
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    return Array.from(digest, value => value.toString(16).padStart(2, '0')).join('');
  }

  function bytesV19(value) {
    const bytes = Number(value || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  function escapeV19(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }
})();
