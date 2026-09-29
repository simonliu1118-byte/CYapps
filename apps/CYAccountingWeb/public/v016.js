window.addEventListener('load', () => {
  const version = document.querySelector('.version');
  if (version) version.textContent = 'V0.18.0';
  setupBackupSettingsV17();
});

let backupTopologyV18 = 'legacy_gcs';

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
        <h3 id="backupHeadingTitle">自動備份</h3>
        <p id="backupHeadingHint" class="hint">Cloudflare D1 是正式資料來源；正在讀取備份拓撲。</p>
      </div>
      <button id="backupRefreshStatus" class="secondary compact" type="button">重新整理</button>
    </div>

    <div id="backupMessage" class="dialog-message"></div>

    <div class="backup-status-grid">
      <article class="backup-status-card">
        <span id="backupPrimaryLabel" class="backup-card-label">備份拓撲</span>
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

    <div id="backupProviderHealth" class="backup-provider-health"></div>

    <div class="backup-actions-row">
      <button id="backupRunNow" class="primary" type="button" disabled>立即執行測試備份</button>
      <span id="backupRetentionText" class="hint">讀取保留政策中…</span>
    </div>

    <div class="backup-security-note">
      <strong>安全設計</strong>
      <span>D1 只 export 一次，同一份 immutable backup-set bytes 交給各 storage provider；每個 copy 上傳後都必須回讀並通過 SHA-256 / byte size / manifest 驗證才記為成功。</span>
    </div>

    <div class="backup-history-block">
      <div class="backup-history-title"><strong>最近 logical backup</strong><span class="hint">一筆 logical backup 對應各 provider copy health</span></div>
      <div class="backup-history-table-wrap">
        <table class="backup-history-table">
          <thead><tr><th>時間</th><th>方式</th><th>整體</th><th>備份 ID／錯誤</th><th>R2</th><th>GCS</th><th class="num">資料筆數</th><th class="num">大小</th></tr></thead>
          <tbody id="backupHistoryRows"><tr><td colspan="8" class="empty">讀取中…</td></tr></tbody>
        </table>
      </div>
    </div>

    <div class="backup-restore-note">
      <strong>復原</strong>
      <span>復原功能尚未開放；後續只允許 <code>SUPER_ADMIN</code> 使用，採雙重確認並在覆蓋 D1 前再次驗證備份格式、版本與完整性。</span>
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

function backupUiModelV18(data) {
  const tiered = data?.topology === 'parallel_dual_provider' || data?.provider === 'tiered';
  const providers = data?.providers || {};
  const r2 = providers.cloudflare_r2 || {};
  const gcs = providers.google_cloud_storage || {};
  const logicalBackups = Array.isArray(data?.logicalBackups) ? data.logicalBackups : [];
  const latestLogical = logicalBackups.find(item => item?.status === 'success') || logicalBackups[0] || null;

  if (tiered) {
    return {
      tiered: true,
      topology: 'parallel_dual_provider',
      configured: Boolean(data?.configured),
      heading: 'R2 + GCS 分層自動備份',
      hint: 'Cloudflare D1 是正式資料來源；R2 作日常 operational backup，GCS 在 Phase C 同步驗證 cross-cloud copy。',
      primaryLabel: '備份拓撲',
      state: data?.configured ? '雙 Provider 已啟用' : 'Provider 尚未完整設定',
      account: data?.configured ? 'R2 + GCS 均已完成設定' : '請檢查 R2 binding 與 GCS Secrets',
      schedule: data?.schedule?.localTime || '每日 03:30（台灣時間）',
      retention: `R2 ${Number(r2.retentionDays || 30)} 天｜GCS ${Number(gcs.retentionDays || 14)} 天（Phase C）`,
      latest: latestLogical,
      r2: {
        configured: Boolean(r2.configured),
        retentionDays: Number(r2.retentionDays || 30),
        role: String(r2.role || 'operational')
      },
      gcs: {
        configured: Boolean(gcs.configured),
        retentionDays: Number(gcs.retentionDays || 14),
        role: String(gcs.role || 'cross_cloud_validation')
      },
      logicalBackups
    };
  }

  return {
    tiered: false,
    topology: 'legacy_gcs',
    configured: Boolean(data?.configured),
    heading: 'Google Cloud Storage 自動備份',
    hint: 'Cloudflare D1 是正式資料來源；Cloud Storage 作異地／災難復原備份。',
    primaryLabel: 'Cloud Storage',
    state: data?.configured ? '已完成設定' : '尚未完成 Cloudflare Secrets',
    account: data?.configured ? 'Bucket 與專用 Service Account 已設定' : '需要 GCS_BUCKET、GCS_SERVICE_ACCOUNT_JSON',
    schedule: data?.schedule?.localTime || '每日 03:30（台灣時間）',
    retention: `GCS ${Number(data?.retentionDays || 14)} 天`,
    latest: data?.latestSuccess || null,
    recentRuns: Array.isArray(data?.recentRuns) ? data.recentRuns : []
  };
}

function renderBackupStatusV17(data) {
  const model = backupUiModelV18(data);
  backupTopologyV18 = model.topology;

  const heading = document.querySelector('#backupHeadingTitle');
  const hint = document.querySelector('#backupHeadingHint');
  const primaryLabel = document.querySelector('#backupPrimaryLabel');
  const state = document.querySelector('#backupConnectionState');
  const account = document.querySelector('#backupAccountText');
  const schedule = document.querySelector('#backupScheduleText');
  const lastTime = document.querySelector('#backupLastTime');
  const lastDetail = document.querySelector('#backupLastDetail');
  const retention = document.querySelector('#backupRetentionText');
  const run = document.querySelector('#backupRunNow');

  if (heading) heading.textContent = model.heading;
  if (hint) hint.textContent = model.hint;
  if (primaryLabel) primaryLabel.textContent = model.primaryLabel;
  if (state) state.textContent = model.state;
  if (account) account.textContent = model.account;
  if (schedule) schedule.textContent = model.schedule;
  if (retention) retention.textContent = model.retention;

  if (model.tiered) {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.createdAt ? backupLocalDateTimeV17(latest.createdAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(latest.byteSize || 0)} · Package SHA ${String(latest.packageSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可執行一次 paired backup 驗證 R2 + GCS' : '完成兩個 provider 設定後即可測試');
    }
    renderBackupProviderHealthV18(model, latest);
    renderTieredBackupHistoryV18(model.logicalBackups);
  } else {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.completedAt ? backupLocalDateTimeV17(latest.completedAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(latest.byteSize || 0)} · SHA ${String(latest.fileSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可先執行一次測試備份確認 GCS 權限與讀回驗證' : '完成 Cloudflare Secrets 後即可測試');
    }
    renderLegacyProviderHealthV18(model);
    renderBackupHistoryV17(model.recentRuns || []);
  }

  if (run) run.disabled = !model.configured;
}

function renderBackupProviderHealthV18(model, latest) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  const copies = Array.isArray(latest?.copies) ? latest.copies : [];
  const r2Copy = copies.find(copy => copy.provider === 'cloudflare_r2');
  const gcsCopy = copies.find(copy => copy.provider === 'google_cloud_storage');

  container.innerHTML = [
    providerHealthCardV18('Cloudflare R2', 'Operational backup', model.r2, r2Copy),
    providerHealthCardV18('Google Cloud Storage', 'Cross-cloud validation', model.gcs, gcsCopy)
  ].join('');
}

function renderLegacyProviderHealthV18(model) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  container.innerHTML = providerHealthCardV18(
    'Google Cloud Storage',
    'Legacy production / rollback path',
    { configured: model.configured, retentionDays: Number(String(model.retention).match(/\d+/)?.[0] || 14) },
    null
  );
}

function providerHealthCardV18(name, role, provider, copy) {
  const status = copy?.status || (provider?.configured ? 'configured' : 'not_configured');
  const badge = status === 'success' ? '成功' : status === 'failed' ? '失敗' : provider?.configured ? '已設定' : '未設定';
  const badgeClass = status === 'success' ? 'success' : status === 'failed' ? 'failed' : 'neutral';
  const verified = copy?.verifiedAt ? ` · 驗證 ${backupLocalDateTimeV17(copy.verifiedAt)}` : '';
  const detail = copy?.lastError || `${Number(provider?.retentionDays || 0)} 天${verified}`;
  return `<article class="backup-provider-card">
    <div class="backup-provider-card-head"><div><strong>${v17Escape(name)}</strong><span>${v17Escape(role)}</span></div><span class="backup-run-badge ${badgeClass}">${badge}</span></div>
    <div class="backup-provider-card-detail">${v17Escape(detail)}</div>
  </article>`;
}

function renderBackupHistoryV17(runs) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!runs.length) {
    tbody.innerHTML = '<tr><td colspan="8" class="empty">尚無 Cloud Storage 備份執行紀錄。</td></tr>';
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
      <td class="backup-copy-cell">—</td>
      <td class="backup-copy-cell"><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="num">${Number(run.rowCount || 0).toLocaleString()}</td>
      <td class="num">${run.byteSize ? backupBytesV17(run.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

function renderTieredBackupHistoryV18(backups) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!backups.length) {
    tbody.innerHTML = '<tr><td colspan="8" class="empty">尚無 paired backup 執行紀錄。</td></tr>';
    return;
  }

  tbody.innerHTML = backups.map(item => {
    const copies = Array.isArray(item.copies) ? item.copies : [];
    const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
    const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
    const overall = item.status === 'success';
    const failed = copies.find(copy => copy.status !== 'success');
    const detail = failed?.lastError || item.backupId || '—';
    return `<tr>
      <td>${v17Escape(backupLocalDateTimeV17(item.createdAt))}</td>
      <td>${item.trigger === 'scheduled' ? '自動' : '手動測試'}</td>
      <td><span class="backup-run-badge ${overall ? 'success' : 'failed'}">${overall ? '成功' : '部分失敗'}</span></td>
      <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(item.backupId || detail)}</td>
      <td class="backup-copy-cell">${copyBadgeV18(r2)}</td>
      <td class="backup-copy-cell">${copyBadgeV18(gcs)}</td>
      <td class="num">${Number(item.rowCount || 0).toLocaleString()}</td>
      <td class="num">${item.byteSize ? backupBytesV17(item.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

function copyBadgeV18(copy) {
  if (!copy) return '<span class="backup-run-badge neutral">—</span>';
  const success = copy.status === 'success';
  return `<span class="backup-run-badge ${success ? 'success' : 'failed'}" title="${v17Escape(copy.lastError || copy.verifiedAt || '')}">${success ? '成功' : '失敗'}</span>`;
}

async function runBackupNowV17() {
  const tiered = backupTopologyV18 === 'parallel_dual_provider';
  const target = tiered ? 'R2 + GCS paired backup' : 'Google Cloud Storage 測試備份';
  if (!confirm(`現在立即執行一次 ${target}？\n正常每日備份仍會在排程時間自動執行。`)) return;
  const button = document.querySelector('#backupRunNow');
  if (button) button.disabled = true;
  setBackupMessageV17(tiered
    ? '正在建立單一 BackupSet、寫入 R2 + GCS 並逐一回讀驗證…'
    : '正在建立 data.json／manifest.json、上傳並回讀驗證…');
  try {
    const data = await api('/api/backup/run', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    setBackupMessageV17(`備份完成：${data.backup?.backupId || data.backup?.fileName || ''}`);
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
