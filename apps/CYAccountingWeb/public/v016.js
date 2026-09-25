window.addEventListener('load', () => {
  const version = document.querySelector('.version');
  if (version) version.textContent = 'V0.17.0';
  setupBackupSettingsV17();
});

async function setupBackupSettingsV17() {
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
  if (!nav || !content || document.querySelector('[data-settings-tab="backup"]')) return;

  const tab = document.createElement('button');
  tab.type = 'button';
  tab.className = 'settings-tab';
  tab.dataset.settingsTab = 'backup';
  tab.textContent = '備份／復原';
  nav.append(tab);

  const pane = document.createElement('section');
  pane.className = 'settings-pane backup-settings-pane';
  pane.dataset.settingsPane = 'backup';
  pane.innerHTML = backupSettingsHtmlV17();
  content.append(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('backup');
    await loadBackupStatusV17();
  });
  pane.querySelector('#backupRunNow')?.addEventListener('click', runBackupNowV17);
  pane.querySelector('#backupRefreshStatus')?.addEventListener('click', loadBackupStatusV17);
}

function backupSettingsHtmlV17() {
  return `
    <div class="backup-heading">
      <div>
        <h3>Google Cloud Storage 自動備份</h3>
        <p class="hint">Cloudflare D1 是正式資料來源；Cloud Storage 只作異地／災難復原備份。</p>
      </div>
      <button id="backupRefreshStatus" class="secondary compact" type="button">重新整理</button>
    </div>

    <div id="backupMessage" class="dialog-message"></div>

    <div class="backup-status-grid">
      <article class="backup-status-card">
        <span class="backup-card-label">Cloud Storage</span>
        <strong id="backupConnectionState">讀取中…</strong>
        <span id="backupAccountText" class="hint">—</span>
      </article>
      <article class="backup-status-card">
        <span class="backup-card-label">自動排程</span>
        <strong id="backupScheduleText">每日 03:30</strong>
        <span class="hint">台灣時間；無須人工按備份</span>
      </article>
      <article class="backup-status-card">
        <span class="backup-card-label">最近有效備份</span>
        <strong id="backupLastTime">尚無</strong>
        <span id="backupLastDetail" class="hint">—</span>
      </article>
    </div>

    <div class="backup-actions-row">
      <button id="backupRunNow" class="primary" type="button" disabled>立即執行測試備份</button>
      <span id="backupRetentionText" class="hint">保留最近 14 天備份</span>
    </div>

    <div class="backup-security-note">
      <strong>安全設計</strong>
      <span>Service Account 私鑰只存在 Cloudflare Secret，不寫入 D1；Worker 每次需要時換取短效 access token。備份上傳後會立即從 Cloud Storage 回讀並比對 SHA-256，通過才記為成功。</span>
    </div>

    <div class="backup-history-block">
      <div class="backup-history-title"><strong>最近執行紀錄</strong><span class="hint">成功／失敗最多顯示 8 筆</span></div>
      <div class="backup-history-table-wrap">
        <table class="backup-history-table">
          <thead><tr><th>時間</th><th>方式</th><th>狀態</th><th>檔名／錯誤</th><th class="num">資料筆數</th><th class="num">大小</th></tr></thead>
          <tbody id="backupHistoryRows"><tr><td colspan="6" class="empty">讀取中…</td></tr></tbody>
        </table>
      </div>
    </div>

    <div class="backup-restore-note">
      <strong>復原</strong>
      <span>V0.17 先完成 Cloud Storage 自動備份。復原功能尚未開放；後續只允許 <code>SUPER_ADMIN</code> 使用，並在覆蓋 D1 前再次驗證備份格式、版本與完整性。</span>
    </div>`;
}

async function loadBackupStatusV17() {
  const run = document.querySelector('#backupRunNow');
  if (run) run.disabled = true;
  setBackupMessageV17('');

  try {
    const data = await api('/api/backup/status');
    renderBackupStatusV17(data);
  } catch (error) {
    setBackupMessageV17(error.message || '無法讀取備份狀態。', true);
    const state = document.querySelector('#backupConnectionState');
    if (state) state.textContent = '狀態讀取失敗';
  }
}

function renderBackupStatusV17(data) {
  const configured = Boolean(data.configured);
  const state = document.querySelector('#backupConnectionState');
  const account = document.querySelector('#backupAccountText');
  const schedule = document.querySelector('#backupScheduleText');
  const lastTime = document.querySelector('#backupLastTime');
  const lastDetail = document.querySelector('#backupLastDetail');
  const retention = document.querySelector('#backupRetentionText');
  const run = document.querySelector('#backupRunNow');

  if (state) state.textContent = configured ? '已完成設定' : '尚未完成 Cloudflare Secrets';
  if (account) {
    account.textContent = configured
      ? `${data.bucketName || '—'} · ${data.serviceAccountEmail || '—'}`
      : '需要 GCS_BUCKET_NAME、GCS_SERVICE_ACCOUNT_EMAIL、GCS_PRIVATE_KEY';
  }
  if (schedule) schedule.textContent = data.schedule?.localTime || '每日 03:30（台灣時間）';
  if (retention) retention.textContent = `保留最近 ${Number(data.retentionDays || 14)} 天備份`;

  const latest = data.latestSuccess;
  if (lastTime) lastTime.textContent = latest?.completedAt ? backupLocalDateTimeV17(latest.completedAt) : '尚無';
  if (lastDetail) {
    lastDetail.textContent = latest
      ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(latest.byteSize || 0)} · SHA ${String(latest.fileSha256 || '').slice(0, 10)}…`
      : (configured ? '可先執行一次測試備份確認權限與 Bucket 設定' : '完成 Cloudflare Secrets 後即可測試');
  }

  if (run) run.disabled = !configured;
  renderBackupHistoryV17(data.recentRuns || []);
}

function renderBackupHistoryV17(runs) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!runs.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無 Cloud Storage 備份執行紀錄。</td></tr>';
    return;
  }

  tbody.innerHTML = runs.map(run => {
    const success = run.status === 'success';
    const detail = success ? run.fileName : (run.errorMessage || '備份失敗');
    return `<tr>
      <td>${v17Escape(backupLocalDateTimeV17(run.completedAt || run.startedAt))}</td>
      <td>${run.trigger === 'scheduled' ? '自動' : '手動測試'}</td>
      <td><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(detail)}</td>
      <td class="num">${Number(run.rowCount || 0).toLocaleString()}</td>
      <td class="num">${run.byteSize ? backupBytesV17(run.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

async function runBackupNowV17() {
  if (!confirm('現在立即執行一次 Google Cloud Storage 測試備份？\n正常每日備份仍會在排程時間自動執行。')) return;
  const button = document.querySelector('#backupRunNow');
  if (button) button.disabled = true;
  setBackupMessageV17('正在建立、上傳並回讀驗證備份…');
  try {
    const data = await api('/api/backup/run', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    setBackupMessageV17(`備份完成：${data.backup?.fileName || ''}`);
    await loadBackupStatusV17();
  } catch (error) {
    setBackupMessageV17(error.message || '測試備份失敗。', true);
    await loadBackupStatusV17();
  }
}

function setBackupMessageV17(message, isError = false) {
  const element = document.querySelector('#backupMessage');
  if (!element) return;
  setDialogMessage(element, message || '', isError);
}

function backupLocalDateTimeV17(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return new Intl.DateTimeFormat('zh-TW', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
  }).format(date);
}

function backupBytesV17(value) {
  const bytes = Number(value || 0);
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function v17Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
