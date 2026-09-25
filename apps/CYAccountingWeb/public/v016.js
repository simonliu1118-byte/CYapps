window.addEventListener('load', () => {
  setupBackupSettingsV16();
});

async function setupBackupSettingsV16() {
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
  pane.innerHTML = backupSettingsHtmlV16();
  content.append(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('backup');
    await loadBackupStatusV16();
  });
  pane.querySelector('#backupConnectGoogle')?.addEventListener('click', connectGoogleDriveV16);
  pane.querySelector('#backupRunNow')?.addEventListener('click', runBackupNowV16);
  pane.querySelector('#backupRefreshStatus')?.addEventListener('click', loadBackupStatusV16);

  const params = new URLSearchParams(location.search);
  if (params.has('backup')) {
    els.settingsButton?.click();
    setSettingsTab('backup');
    await loadBackupStatusV16();
    const result = params.get('backup');
    const detail = params.get('detail') || '';
    if (result === 'connected') {
      setBackupMessageV16(detail === 'failed'
        ? 'Google Drive 已連結，但首次驗證備份失敗；請查看下方最近執行紀錄。'
        : 'Google Drive 已連結，首次驗證備份已執行。', detail === 'failed');
    } else if (result === 'error') {
      setBackupMessageV16('Google Drive 連結未完成，請重新嘗試。', true);
    }
    history.replaceState(null, '', location.pathname + location.hash);
  }
}

function backupSettingsHtmlV16() {
  return `
    <div class="backup-heading">
      <div>
        <h3>Google Drive 自動備份</h3>
        <p class="hint">Cloudflare D1 是正式資料來源；Google Drive 只作異地／災難復原備份。</p>
      </div>
      <button id="backupRefreshStatus" class="secondary compact" type="button">重新整理</button>
    </div>

    <div id="backupMessage" class="dialog-message"></div>

    <div class="backup-status-grid">
      <article class="backup-status-card">
        <span class="backup-card-label">Google Drive</span>
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
      <button id="backupConnectGoogle" class="primary" type="button" disabled>連結 Google Drive</button>
      <button id="backupRunNow" class="secondary" type="button" disabled>立即執行測試備份</button>
      <span id="backupRetentionText" class="hint">保留最近 30 份有效備份</span>
    </div>

    <div class="backup-security-note">
      <strong>安全設計</strong>
      <span>refresh token 會以 Cloudflare Secret 提供的 AES-256-GCM 金鑰加密後才寫入 D1；備份上傳後會立即從 Drive 回讀並比對 SHA-256，通過才記為成功。</span>
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
      <span>V0.16 先建立並驗證自動備份。復原功能尚未開放；後續只會允許 <code>SUPER_ADMIN</code> 使用，並在覆蓋 D1 前再次驗證備份格式、版本與完整性。</span>
    </div>`;
}

async function loadBackupStatusV16() {
  const connect = document.querySelector('#backupConnectGoogle');
  const run = document.querySelector('#backupRunNow');
  if (connect) connect.disabled = true;
  if (run) run.disabled = true;
  setBackupMessageV16('');

  try {
    const data = await api('/api/backup/status');
    renderBackupStatusV16(data);
  } catch (error) {
    setBackupMessageV16(error.message || '無法讀取備份狀態。', true);
    const state = document.querySelector('#backupConnectionState');
    if (state) state.textContent = '狀態讀取失敗';
  }
}

function renderBackupStatusV16(data) {
  const configured = Boolean(data.configured);
  const connected = Boolean(data.connected);
  const state = document.querySelector('#backupConnectionState');
  const account = document.querySelector('#backupAccountText');
  const schedule = document.querySelector('#backupScheduleText');
  const lastTime = document.querySelector('#backupLastTime');
  const lastDetail = document.querySelector('#backupLastDetail');
  const retention = document.querySelector('#backupRetentionText');
  const connect = document.querySelector('#backupConnectGoogle');
  const run = document.querySelector('#backupRunNow');

  if (state) state.textContent = !configured ? '尚未完成 Cloudflare 設定' : connected ? '已連結' : '等待 Google 授權';
  if (account) {
    const label = [data.account?.name, data.account?.email].filter(Boolean).join(' · ');
    account.textContent = label || (!configured ? '需要先設定 OAuth Client 與 token 加密金鑰' : '尚未連結 Google 帳號');
  }
  if (schedule) schedule.textContent = data.schedule?.localTime || '每日 03:30（台灣時間）';
  if (retention) retention.textContent = `保留最近 ${Number(data.retention || 30)} 份有效備份`;

  const latest = data.latestSuccess;
  if (lastTime) lastTime.textContent = latest?.completedAt ? backupLocalDateTimeV16(latest.completedAt) : '尚無';
  if (lastDetail) {
    lastDetail.textContent = latest
      ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV16(latest.byteSize || 0)} · SHA ${String(latest.fileSha256 || '').slice(0, 10)}…`
      : '完成 Google Drive 連結後會自動建立第一份驗證備份';
  }

  if (connect) {
    connect.disabled = !configured;
    connect.textContent = connected ? '重新授權 Google Drive' : '連結 Google Drive';
  }
  if (run) run.disabled = !configured || !connected;

  renderBackupHistoryV16(data.recentRuns || []);
}

function renderBackupHistoryV16(runs) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!runs.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無備份執行紀錄。</td></tr>';
    return;
  }

  tbody.innerHTML = runs.map(run => {
    const success = run.status === 'success';
    const detail = success ? run.fileName : (run.errorMessage || '備份失敗');
    return `<tr>
      <td>${v16Escape(backupLocalDateTimeV16(run.completedAt || run.startedAt))}</td>
      <td>${run.trigger === 'scheduled' ? '自動' : '手動測試'}</td>
      <td><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="backup-run-detail" title="${v16Escape(detail)}">${v16Escape(detail)}</td>
      <td class="num">${Number(run.rowCount || 0).toLocaleString()}</td>
      <td class="num">${run.byteSize ? backupBytesV16(run.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

async function connectGoogleDriveV16() {
  const button = document.querySelector('#backupConnectGoogle');
  if (button) button.disabled = true;
  setBackupMessageV16('準備 Google 授權…');
  try {
    const data = await api('/api/backup/google/oauth/start', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    if (!data.authUrl) throw new Error('系統沒有回傳 Google 授權網址。');
    location.assign(data.authUrl);
  } catch (error) {
    setBackupMessageV16(error.message || '無法開始 Google Drive 授權。', true);
    await loadBackupStatusV16();
  }
}

async function runBackupNowV16() {
  if (!confirm('現在立即執行一次 Google Drive 測試備份？\n正常每日備份仍會在排程時間自動執行。')) return;
  const button = document.querySelector('#backupRunNow');
  if (button) button.disabled = true;
  setBackupMessageV16('正在建立、上傳並回讀驗證備份…');
  try {
    const data = await api('/api/backup/run', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    setBackupMessageV16(`備份完成：${data.backup?.fileName || ''}`);
    await loadBackupStatusV16();
  } catch (error) {
    setBackupMessageV16(error.message || '測試備份失敗。', true);
    await loadBackupStatusV16();
  }
}

function setBackupMessageV16(message, isError = false) {
  const element = document.querySelector('#backupMessage');
  if (!element) return;
  setDialogMessage(element, message || '', isError);
}

function backupLocalDateTimeV16(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return new Intl.DateTimeFormat('zh-TW', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
  }).format(date);
}

function backupBytesV16(value) {
  const bytes = Number(value || 0);
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function v16Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
