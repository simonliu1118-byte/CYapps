(() => {
  const SQLJS_VERSION = '1.14.2';
  const SQLJS_BASE = `https://cdn.jsdelivr.net/npm/sql.js@${SQLJS_VERSION}/dist/`;
  const MAX_FILE_BYTES = 64 * 1024 * 1024;
  const CHUNK_ROWS = 250;
  const state = {
    dataset: null,
    preview: null,
    serverStatus: null,
    busy: false
  };

  window.addEventListener('load', () => {
    const version = document.querySelector('.version');
    if (version) version.textContent = 'V0.19.0';
    setupLegacyMigrationV19();
  });

  async function setupLegacyMigrationV19() {
    let me;
    try {
      const response = await fetch('/api/auth/me', { cache: 'no-store' });
      me = await response.json().catch(() => null);
      if (!response.ok || me?.user?.role !== 'SUPER_ADMIN') return;
    } catch {
      return;
    }

    const nav = document.querySelector('.settings-nav');
    const content = document.querySelector('.settings-content');
    if (!nav || !content || document.querySelector('[data-settings-tab="legacy-migration"]')) return;

    const tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'settings-tab';
    tab.dataset.settingsTab = 'legacy-migration';
    tab.textContent = '舊帳本遷移';
    nav.append(tab);

    const pane = document.createElement('section');
    pane.className = 'settings-pane legacy-migration-pane';
    pane.dataset.settingsPane = 'legacy-migration';
    pane.innerHTML = migrationPaneHtml();
    content.append(pane);

    els.settingsTabs?.push(tab);
    els.settingsPanes?.push(pane);

    tab.addEventListener('click', async () => {
      setSettingsTab('legacy-migration');
      await refreshMigrationStatus();
    });
    pane.querySelector('#legacyMigrationFile')?.addEventListener('change', handleFileSelection);
    pane.querySelector('#legacyMigrationPreview')?.addEventListener('click', previewMigration);
    pane.querySelector('#legacyMigrationStart')?.addEventListener('click', startMigration);
    pane.querySelector('#legacyMigrationAbort')?.addEventListener('click', abortMigration);
    pane.querySelector('#legacyMigrationRefresh')?.addEventListener('click', refreshMigrationStatus);

    await refreshMigrationStatus();
  }

  function migrationPaneHtml() {
    return `
      <div class="legacy-migration-heading">
        <div>
          <h3>CYAccounting 舊帳本遷移</h3>
          <p class="hint">只支援桌面版 <code>CYaccounting.db</code> schema 1 / 2。來源 SQLite 檔只在瀏覽器內唯讀解析，不會修改原檔。</p>
        </div>
        <button id="legacyMigrationRefresh" class="secondary compact" type="button">重新整理狀態</button>
      </div>

      <div id="legacyMigrationServerState" class="migration-server-state"></div>

      <div class="migration-step">
        <strong>1. 選擇舊帳本</strong>
        <div class="migration-file-row">
          <input id="legacyMigrationFile" type="file" accept=".db,.sqlite,.sqlite3,application/vnd.sqlite3,application/x-sqlite3">
          <span id="legacyMigrationFileState" class="hint">尚未選擇檔案</span>
        </div>
        <p class="hint">解析元件固定使用 sql.js ${SQLJS_VERSION}；SQLite 原始 bytes 不會直接送到 CYAccountingWeb Worker。</p>
      </div>

      <div id="legacyMigrationSource" class="migration-step hidden">
        <strong>2. 來源資料檢查</strong>
        <div id="legacyMigrationSummary" class="migration-summary-grid"></div>
        <div id="legacyMigrationSourceMessage" class="dialog-message"></div>
        <button id="legacyMigrationPreview" class="secondary" type="button">檢查 Web 目標狀態</button>
      </div>

      <div id="legacyMigrationPreviewBlock" class="migration-step hidden">
        <strong>3. 遷移預覽</strong>
        <div id="legacyMigrationPreviewMessage" class="dialog-message"></div>
        <div class="migration-confirm-row">
          <label><span>正式開始前請輸入「匯入舊帳本」</span><input id="legacyMigrationConfirmation" type="text" autocomplete="off"></label>
          <button id="legacyMigrationStart" class="primary" type="button" disabled>開始遷移</button>
        </div>
      </div>

      <div id="legacyMigrationProgressBlock" class="migration-step hidden">
        <strong>4. 寫入與核對</strong>
        <div class="migration-progress-line">
          <progress id="legacyMigrationProgress" max="100" value="0"></progress>
          <span id="legacyMigrationProgressText">0%</span>
        </div>
        <div id="legacyMigrationProgressMessage" class="dialog-message"></div>
      </div>

      <div class="migration-safety-note">
        <strong>安全限制</strong>
        <span>V0.19.0 只允許遷移到「空白或系統初始範例」帳本；若 Web D1 已有交易、期初餘額或自訂主檔，會直接阻擋，不提供覆蓋既有正式資料的按鈕。</span>
      </div>

      <div id="legacyMigrationActiveActions" class="migration-active-actions hidden">
        <button id="legacyMigrationAbort" class="secondary danger-outline" type="button">中止目前遷移並回復初始帳本</button>
      </div>`;
  }

  async function refreshMigrationStatus() {
    try {
      const data = await migrationApi('/api/migration/sqlite/status');
      state.serverStatus = data;
      renderServerStatus(data);
    } catch (error) {
      const box = document.querySelector('#legacyMigrationServerState');
      if (box) box.innerHTML = `<span class="migration-state-badge error">狀態讀取失敗</span><span>${escapeText(error.message || '無法讀取遷移狀態。')}</span>`;
    }
  }

  function renderServerStatus(data) {
    const box = document.querySelector('#legacyMigrationServerState');
    const actions = document.querySelector('#legacyMigrationActiveActions');
    if (!box) return;
    const run = data?.run;
    if (data?.active && run) {
      const expected = Number(run.expected?.transactions || 0) + Number(run.expected?.openingBalances || 0);
      const imported = Number(run.imported?.transactions || 0) + Number(run.imported?.openingBalances || 0);
      box.innerHTML = `<span class="migration-state-badge warn">遷移進行中</span><span>${escapeText(run.sourceFileName)} · ${imported.toLocaleString()} / ${expected.toLocaleString()} 筆已寫入</span>`;
      actions?.classList.remove('hidden');
    } else if (run?.status === 'completed') {
      box.innerHTML = `<span class="migration-state-badge ok">最近遷移完成</span><span>${escapeText(run.sourceFileName)} · ${localTime(run.completedAt || run.updatedAt)}</span>`;
      actions?.classList.add('hidden');
    } else if (run?.status === 'aborted') {
      box.innerHTML = '<span class="migration-state-badge neutral">最近遷移已中止</span><span>Web 帳本已回復系統初始資料。</span>';
      actions?.classList.add('hidden');
    } else if (run?.status === 'failed') {
      box.innerHTML = `<span class="migration-state-badge error">最近遷移核對失敗</span><span>${escapeText(run.lastError || '請檢查遷移狀態。')}</span>`;
      actions?.classList.add('hidden');
    } else {
      const ready = data?.target?.ready;
      box.innerHTML = ready
        ? '<span class="migration-state-badge ok">可遷移</span><span>目前 Web 帳本為空白／初始狀態。</span>'
        : '<span class="migration-state-badge neutral">尚未進行</span><span>選取舊帳本後會再次檢查目標狀態。</span>';
      actions?.classList.add('hidden');
    }
  }

  async function handleFileSelection(event) {
    const file = event.target.files?.[0] || null;
    resetSourceState();
    if (!file) return;
    const fileState = document.querySelector('#legacyMigrationFileState');
    if (file.size <= 0 || file.size > MAX_FILE_BYTES) {
      if (fileState) fileState.textContent = '檔案大小不支援（最大 64 MB）';
      return;
    }

    setBusy(true);
    if (fileState) fileState.textContent = '唯讀解析與完整性檢查中…';
    try {
      const dataset = await parseLegacyDatabase(file);
      state.dataset = dataset;
      if (fileState) fileState.textContent = `${file.name} · ${formatBytes(file.size)} · schema ${dataset.manifest.sourceSchemaVersion}`;
      renderSourceSummary(dataset);
      document.querySelector('#legacyMigrationSource')?.classList.remove('hidden');
      setSourceMessage('SQLite integrity_check / foreign_key_check 已通過；來源檔未被修改。');
    } catch (error) {
      state.dataset = null;
      if (fileState) fileState.textContent = file.name;
      setSourceMessage(error.message || '無法讀取此舊帳本。', true);
      document.querySelector('#legacyMigrationSource')?.classList.remove('hidden');
    } finally {
      setBusy(false);
    }
  }

  async function parseLegacyDatabase(file) {
    const buffer = await file.arrayBuffer();
    const fileSha256 = await sha256Bytes(new Uint8Array(buffer));
    const SQL = await loadSqlJs();
    let db;
    try {
      db = new SQL.Database(new Uint8Array(buffer));
      const integrity = scalar(db, 'PRAGMA integrity_check');
      if (String(integrity || '').toLowerCase() !== 'ok') throw new Error(`SQLite 完整性檢查失敗：${integrity || 'unknown'}`);
      if (rows(db, 'PRAGMA foreign_key_check').length) throw new Error('SQLite foreign_key_check 未通過。');

      const tableNames = new Set(rows(db, "SELECT name FROM sqlite_master WHERE type='table'").map(row => String(row.name)));
      const required = ['meta', 'accounts', 'category_groups', 'categories', 'transactions', 'opening_balances', 'app_settings'];
      const missing = required.filter(name => !tableNames.has(name));
      if (missing.length) throw new Error(`不是支援的 CYAccounting 帳本（缺少：${missing.join('、')}）。`);

      const schemaVersion = Number(scalar(db, "SELECT value FROM meta WHERE key='schema_version' LIMIT 1"));
      if (![1, 2].includes(schemaVersion)) throw new Error(`不支援的 CYAccounting SQLite schema：${schemaVersion || 'unknown'}。`);

      const categoryColumns = new Set(rows(db, 'PRAGMA table_info(categories)').map(row => String(row.name)));
      const favoriteExpr = categoryColumns.has('is_favorite') ? 'is_favorite' : '0 AS is_favorite';

      const structure = {
        accounts: rows(db, 'SELECT id,name,sort_order,is_default,created_at FROM accounts ORDER BY id').map(row => ({
          id: Number(row.id), name: String(row.name), sortOrder: Number(row.sort_order), isDefault: Number(row.is_default), createdAt: String(row.created_at)
        })),
        categoryGroups: rows(db, 'SELECT id,kind,name,sort_order,created_at FROM category_groups ORDER BY id').map(row => ({
          id: Number(row.id), kind: String(row.kind), name: String(row.name), sortOrder: Number(row.sort_order), createdAt: String(row.created_at)
        })),
        categories: rows(db, `SELECT id,kind,group_id,name,sort_order,${favoriteExpr},created_at FROM categories ORDER BY id`).map(row => ({
          id: Number(row.id), kind: String(row.kind), groupId: Number(row.group_id), name: String(row.name), sortOrder: Number(row.sort_order), isFavorite: Number(row.is_favorite || 0), createdAt: String(row.created_at)
        })),
        settings: rows(db, `SELECT key,value FROM app_settings WHERE key IN ('locked_through','frequent_summary_basis','frequent_summary_recent_count','frequent_summary_min_count') ORDER BY key`).map(row => ({
          key: String(row.key), value: String(row.value)
        }))
      };

      const transactions = rows(db, `
        SELECT id,tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at
        FROM transactions ORDER BY id
      `).map(row => ({
        id: Number(row.id), txDate: String(row.tx_date), accountName: String(row.account_name), kind: String(row.kind),
        categoryName: String(row.category_name), summary: String(row.summary || '').trim(), amount: Number(row.amount),
        createdAt: String(row.created_at), updatedAt: String(row.updated_at)
      }));

      const openingBalances = rows(db, `
        SELECT month,account_name,amount,created_at,updated_at
        FROM opening_balances ORDER BY month,account_name
      `).map(row => ({
        month: String(row.month), accountName: String(row.account_name), amount: Number(row.amount),
        createdAt: String(row.created_at), updatedAt: String(row.updated_at)
      }));

      const transactionChunks = await makeChunks(transactions);
      const openingBalanceChunks = await makeChunks(openingBalances);
      const structureSha256 = await sha256Text(JSON.stringify(structure));
      const datasetSha256 = await sha256Text(JSON.stringify({
        format: 'CYAccountingLegacyMigration', formatVersion: 1, structureSha256,
        transactionChunks: transactionChunks.map(chunk => chunk.sha256),
        openingBalanceChunks: openingBalanceChunks.map(chunk => chunk.sha256)
      }));

      return {
        manifest: {
          format: 'CYAccountingLegacyMigration',
          formatVersion: 1,
          sourceSchemaVersion: schemaVersion,
          sourceFileName: file.name,
          sourceFileSize: file.size,
          sourceFileSha256: fileSha256,
          structureSha256,
          datasetSha256,
          counts: {
            accounts: structure.accounts.length,
            categoryGroups: structure.categoryGroups.length,
            categories: structure.categories.length,
            transactions: transactions.length,
            openingBalances: openingBalances.length
          },
          transactionChunks: transactionChunks.map(({ index, rows: chunkRows, sha256 }) => ({ index, rowCount: chunkRows.length, sha256 })),
          openingBalanceChunks: openingBalanceChunks.map(({ index, rows: chunkRows, sha256 }) => ({ index, rowCount: chunkRows.length, sha256 }))
        },
        structure,
        transactionChunks,
        openingBalanceChunks
      };
    } catch (error) {
      throw new Error(error?.message || 'SQLite 解析失敗。');
    } finally {
      try { db?.close(); } catch {}
    }
  }

  async function makeChunks(items) {
    const chunks = [];
    for (let start = 0, index = 0; start < items.length; start += CHUNK_ROWS, index += 1) {
      const chunkRows = items.slice(start, start + CHUNK_ROWS);
      chunks.push({ index, rows: chunkRows, sha256: await sha256Text(JSON.stringify(chunkRows)) });
    }
    return chunks;
  }

  async function previewMigration() {
    if (!state.dataset || state.busy) return;
    setBusy(true);
    setPreviewMessage('檢查來源與 Web D1 狀態中…');
    try {
      const data = await migrationApi('/api/migration/sqlite/preview', {
        method: 'POST',
        body: JSON.stringify({ manifest: state.dataset.manifest, structure: state.dataset.structure })
      });
      state.preview = data;
      document.querySelector('#legacyMigrationPreviewBlock')?.classList.remove('hidden');
      const start = document.querySelector('#legacyMigrationStart');
      if (start) {
        start.disabled = !(data.canStart || data.canResume);
        start.textContent = data.canResume ? '繼續遷移' : '開始遷移';
      }
      setPreviewMessage(data.message || (data.canStart ? '可以開始遷移。' : '目前無法遷移。'), !(data.canStart || data.canResume));
      renderServerStatus({ ...state.serverStatus, active: Boolean(data.activeRun), run: data.activeRun || state.serverStatus?.run, target: data.target });
    } catch (error) {
      state.preview = null;
      document.querySelector('#legacyMigrationPreviewBlock')?.classList.remove('hidden');
      setPreviewMessage(error.message || '遷移預覽失敗。', true);
    } finally {
      setBusy(false);
    }
  }

  async function startMigration() {
    if (!state.dataset || !state.preview || state.busy) return;
    const confirmation = String(document.querySelector('#legacyMigrationConfirmation')?.value || '').trim();
    if (confirmation !== '匯入舊帳本') {
      setPreviewMessage('請輸入「匯入舊帳本」後再開始。', true);
      return;
    }

    setBusy(true);
    document.querySelector('#legacyMigrationProgressBlock')?.classList.remove('hidden');
    setProgressMessage('建立遷移作業…');
    try {
      const start = await migrationApi('/api/migration/sqlite/start', {
        method: 'POST',
        body: JSON.stringify({
          manifest: state.dataset.manifest,
          structure: state.dataset.structure,
          confirm: true,
          confirmation
        })
      });
      const runId = start.run?.runId;
      if (!runId) throw new Error('伺服器未回傳遷移作業編號。');

      const queue = [
        ...state.dataset.transactionChunks.map(chunk => ({ ...chunk, kind: 'transactions' })),
        ...state.dataset.openingBalanceChunks.map(chunk => ({ ...chunk, kind: 'opening_balances' }))
      ];
      let completed = 0;
      updateProgress(0, queue.length || 1, '開始寫入…');
      for (const chunk of queue) {
        await migrationApi('/api/migration/sqlite/chunk', {
          method: 'POST',
          body: JSON.stringify({ runId, kind: chunk.kind, index: chunk.index, rows: chunk.rows, sha256: chunk.sha256 })
        });
        completed += 1;
        updateProgress(completed, queue.length || 1, `${chunk.kind === 'transactions' ? '交易' : '期初餘額'}分段 ${chunk.index + 1} 已完成`);
      }

      const finish = await migrationApi('/api/migration/sqlite/finish', {
        method: 'POST',
        body: JSON.stringify({ runId, datasetSha256: state.dataset.manifest.datasetSha256 })
      });
      updateProgress(1, 1, '遷移完成');
      setProgressMessage(`遷移完成：交易 ${Number(finish.counts?.transactions || 0).toLocaleString()} 筆、期初餘額 ${Number(finish.counts?.openingBalances || 0).toLocaleString()} 筆；伺服器端筆數核對已通過。`);
      await refreshMigrationStatus();
      setTimeout(() => location.reload(), 800);
    } catch (error) {
      setProgressMessage(`${error.message || '遷移中斷。'} 已完成的分段會保留；重新選取同一個 .db 後可以續傳。`, true);
      await refreshMigrationStatus();
    } finally {
      setBusy(false);
    }
  }

  async function abortMigration() {
    const runId = state.serverStatus?.run?.runId;
    if (!runId || state.busy) return;
    const phrase = prompt('中止會刪除這次尚未完成的遷移資料，並回復系統初始帳本。\n\n請輸入「中止遷移」：');
    if (phrase !== '中止遷移') return;
    setBusy(true);
    try {
      await migrationApi('/api/migration/sqlite/abort', {
        method: 'POST',
        body: JSON.stringify({ runId, confirm: true, confirmation: phrase })
      });
      setProgressMessage('遷移已中止，Web 帳本已回復系統初始資料。');
      resetSourceState();
      await refreshMigrationStatus();
      location.reload();
    } catch (error) {
      setProgressMessage(error.message || '中止遷移失敗。', true);
    } finally {
      setBusy(false);
    }
  }

  function renderSourceSummary(dataset) {
    const target = document.querySelector('#legacyMigrationSummary');
    if (!target) return;
    const c = dataset.manifest.counts;
    target.innerHTML = [
      ['SQLite schema', dataset.manifest.sourceSchemaVersion],
      ['帳戶', c.accounts],
      ['大分類', c.categoryGroups],
      ['科目', c.categories],
      ['交易', c.transactions],
      ['期初餘額', c.openingBalances]
    ].map(([label, value]) => `<article><span>${escapeText(label)}</span><strong>${Number.isFinite(Number(value)) ? Number(value).toLocaleString() : escapeText(value)}</strong></article>`).join('');
  }

  function resetSourceState() {
    state.dataset = null;
    state.preview = null;
    document.querySelector('#legacyMigrationSource')?.classList.add('hidden');
    document.querySelector('#legacyMigrationPreviewBlock')?.classList.add('hidden');
    document.querySelector('#legacyMigrationProgressBlock')?.classList.add('hidden');
    const confirmation = document.querySelector('#legacyMigrationConfirmation');
    if (confirmation) confirmation.value = '';
    setSourceMessage('');
    setPreviewMessage('');
    setProgressMessage('');
  }

  function setBusy(busy) {
    state.busy = busy;
    for (const id of ['legacyMigrationFile', 'legacyMigrationPreview', 'legacyMigrationStart', 'legacyMigrationAbort', 'legacyMigrationRefresh']) {
      const element = document.querySelector(`#${id}`);
      if (element) element.disabled = busy || (id === 'legacyMigrationStart' && !(state.preview?.canStart || state.preview?.canResume));
    }
  }

  function updateProgress(done, total, text) {
    const progress = document.querySelector('#legacyMigrationProgress');
    const label = document.querySelector('#legacyMigrationProgressText');
    const percent = total > 0 ? Math.max(0, Math.min(100, Math.round(done / total * 100))) : 0;
    if (progress) progress.value = percent;
    if (label) label.textContent = `${percent}%`;
    setProgressMessage(text || '');
  }

  function setSourceMessage(text, error = false) { setBox('#legacyMigrationSourceMessage', text, error); }
  function setPreviewMessage(text, error = false) { setBox('#legacyMigrationPreviewMessage', text, error); }
  function setProgressMessage(text, error = false) { setBox('#legacyMigrationProgressMessage', text, error); }

  function setBox(selector, text, error = false) {
    const element = document.querySelector(selector);
    if (!element) return;
    element.textContent = text || '';
    element.classList.toggle('error', Boolean(error));
  }

  async function migrationApi(path, options = {}) {
    const response = await fetch(path, {
      cache: 'no-store',
      ...options,
      headers: { 'content-type': 'application/json', ...(options.headers || {}) }
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data?.ok === false) throw new Error(data?.error || `HTTP ${response.status}`);
    return data;
  }

  async function loadSqlJs() {
    if (window.__cySqlJsV19) return window.__cySqlJsV19;
    if (!window.initSqlJs) {
      await new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = `${SQLJS_BASE}sql-wasm.js`;
        script.async = true;
        script.crossOrigin = 'anonymous';
        script.onload = resolve;
        script.onerror = () => reject(new Error('無法載入 SQLite 解析元件。'));
        document.head.append(script);
      });
    }
    window.__cySqlJsV19 = await window.initSqlJs({ locateFile: file => `${SQLJS_BASE}${file}` });
    return window.__cySqlJsV19;
  }

  function rows(db, sql) {
    const result = db.exec(sql);
    if (!result.length) return [];
    const first = result[0];
    return first.values.map(values => Object.fromEntries(first.columns.map((column, index) => [column, values[index]])));
  }

  function scalar(db, sql) {
    const result = db.exec(sql);
    return result?.[0]?.values?.[0]?.[0] ?? null;
  }

  async function sha256Text(text) {
    return sha256Bytes(new TextEncoder().encode(text));
  }

  async function sha256Bytes(bytes) {
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
  }

  function formatBytes(value) {
    const bytes = Number(value || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  function localTime(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString('zh-TW', { hour12: false });
  }

  function escapeText(value) {
    return String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
  }
})();
