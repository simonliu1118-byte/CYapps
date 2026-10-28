/* CYAccountingWeb backup UI functional module. */

window.addEventListener('load', () => {
  setupBackupSettings();
});

let backupTopology = 'legacy_gcs';
let backupRecoveryPreview = null;
let backupRecoveryGeneration = 0;
let backupRecoveryCommitting = false;

async function setupBackupSettings() {
  const user = await sharedSessionUser();
  if (user?.role !== 'SUPER_ADMIN') return;

  setupMobileBackupInfo();
  void showBackupHealthWarning();

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
  pane.innerHTML = backupSettingsHtml();
  content.append(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('backup');
    await loadBackupStatus();
  });
  pane.querySelector('#backupRunNow')?.addEventListener('click', runBackupNow);
  pane.querySelector('#backupRefreshStatus')?.addEventListener('click', loadBackupStatus);
  pane.querySelector('#backupOpenRecovery')?.addEventListener('click', openBackupRecovery);
  ensureBackupRecoveryDialog();
}

async function sharedSessionUser() {
  if (window.cyaccCurrentUser) return window.cyaccCurrentUser;
  try {
    return window.cyaccSessionPromise ? await window.cyaccSessionPromise : null;
  } catch {
    return null;
  }
}


async function showBackupHealthWarning() {
  let health;
  try { health = (await api('/api/backup/health')).health; }
  catch { health = { status: 'failed', issues: [{ code: 'BACKUP_HEALTH_UNAVAILABLE' }] }; }
  if (health?.status !== 'failed') return;
  let dialog = document.querySelector('#backupHealthWarning');
  if (!dialog) {
    dialog = document.createElement('dialog');
    dialog.id = 'backupHealthWarning';
    dialog.className = 'modal backup-health-dialog';
    dialog.setAttribute('role', 'alertdialog');
    dialog.setAttribute('aria-labelledby', 'backupHealthWarningTitle');
    dialog.setAttribute('aria-describedby', 'backupHealthWarningText');
    dialog.innerHTML = `<div class="modal-header"><h2 id="backupHealthWarningTitle">備份異常警告</h2></div>
      <p id="backupHealthWarningText"></p><p class="backup-health-delivery"></p>
      <div class="backup-health-actions"><button type="button" class="primary">已了解</button></div>`;
    dialog.querySelector('button').addEventListener('click', () => dialog.close());
    document.body.append(dialog);
  }
  const providers = [...new Set((health.issues || []).map(item => item.provider === 'cloudflare_r2' ? 'R2'
    : item.provider === 'google_cloud_storage' ? 'GCS' : '').filter(Boolean))];
  dialog.querySelector('#backupHealthWarningText').textContent = providers.length
    ? `${providers.join('／')} 備份自動檢查未通過，請通知維運人員檢查備份／復原狀態。目前帳本不會自動還原或切換。`
    : '備份自動檢查未完成或結果已逾期，請通知維運人員確認。';
  dialog.querySelector('.backup-health-delivery').textContent = health.emailSent ? '已寄出超級管理員通知信。'
    : health.emailPending ? '通知信尚未寄達，系統將於下次排程重試。' : '';
  if (!dialog.open) dialog.showModal();
}

function setupMobileBackupInfo() {
  const action = document.querySelector('[data-mobile-ledger-action="backup"]');
  if (action) action.hidden = false;
  window.cyOpenMobileBackupInfo = openMobileBackupInfo;
  ensureMobileBackupInfoDialog();
}

function ensureMobileBackupInfoDialog() {
  let dialog = document.querySelector('#mobileBackupInfoDialog');
  if (dialog) return dialog;

  dialog = document.createElement('dialog');
  dialog.id = 'mobileBackupInfoDialog';
  dialog.className = 'modal mobile-utility-dialog mobile-backup-info-dialog';
  dialog.setAttribute('aria-labelledby', 'mobileBackupInfoTitle');
  dialog.innerHTML = `
    <div class="modal-header">
      <div>
        <h2 id="mobileBackupInfoTitle">備份資訊</h2>
      </div>
      <button class="icon-button" type="button" data-close-mobile-backup-info aria-label="關閉">×</button>
    </div>
    <div id="mobileBackupInfoContent" class="mobile-utility-body mobile-backup-info-content" role="status" aria-live="polite">
      <p class="mobile-backup-loading">讀取中…</p>
    </div>`;
  document.body.append(dialog);
  dialog.querySelector('[data-close-mobile-backup-info]')?.addEventListener('click', () => dialog.close());
  return dialog;
}

async function openMobileBackupInfo() {
  if (!window.matchMedia('(max-width: 767px)').matches) return;
  const user = await sharedSessionUser();
  if (user?.role !== 'SUPER_ADMIN') return;

  const dialog = ensureMobileBackupInfoDialog();
  const content = dialog.querySelector('#mobileBackupInfoContent');
  if (content) content.innerHTML = '<p class="mobile-backup-loading">讀取中…</p>';
  if (!dialog.open) dialog.showModal();

  try {
    const data = await api('/api/backup/status');
    if (content) content.innerHTML = mobileBackupInfoHtml(data);
  } catch (error) {
    if (content) content.innerHTML = `<p class="mobile-backup-error">${escapeBackupHtml(error?.message || '無法讀取備份狀態。')}</p>`;
  }
}

function mobileBackupInfoHtml(data) {
  const model = backupUiModel(data);
  const acceptance = phaseCAcceptanceUiModel(data);
  const latest = model.latest;
  const latestTime = model.tiered ? latest?.createdAt : latest?.completedAt;
  const latestDetail = latest
    ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytes(latest.byteSize || 0)}`
    : '尚無有效備份';

  let providerHtml = '';
  if (model.tiered) {
    const copies = Array.isArray(latest?.copies) ? latest.copies : [];
    providerHtml = [
      providerHealthCard('Cloudflare R2', 'Operational backup', model.r2, copies.find(copy => copy.provider === 'cloudflare_r2')),
      providerHealthCard('Google Cloud Storage', model.gcs.role === 'cross_cloud_long_term' ? '跨雲長期備份' : 'Cross-cloud validation', model.gcs, copies.find(copy => copy.provider === 'google_cloud_storage') || model.logicalBackups.flatMap(item => item.copies || []).find(copy => copy.provider === 'google_cloud_storage'))
    ].join('');
  } else {
    providerHtml = providerHealthCard(
      'Google Cloud Storage',
      'Legacy production / rollback path',
      { configured: model.configured, retentionDays: Number(String(model.retention).match(/\d+/)?.[0] || 14) },
      null
    );
  }

  return `
    <div class="mobile-backup-summary">
      <div class="mobile-backup-row"><span>備份狀態</span><strong>${escapeBackupHtml(model.state)}</strong></div>
      <div class="mobile-backup-row"><span>自動排程</span><strong>${escapeBackupHtml(model.schedule)}</strong></div>
      <div class="mobile-backup-row"><span>保留政策</span><strong>${escapeBackupHtml(model.retention)}</strong></div>
      <div class="mobile-backup-row mobile-backup-row-latest">
        <span>最近有效備份</span>
        <strong>${escapeBackupHtml(latestTime ? backupLocalDateTime(latestTime) : '尚無')}</strong>
        <small>${escapeBackupHtml(latestDetail)}</small>
      </div>
    </div>
    ${acceptance.visible ? `<div class="backup-acceptance-strip mobile-backup-acceptance">${phaseCAcceptanceHtml(acceptance)}</div>` : ''}
    <div class="backup-provider-health mobile-backup-provider-health">${providerHtml}</div>`;
}

function backupSettingsHtml() {
  return `
    <div class="backup-heading">
      <div>
        <h3 id="backupHeadingTitle">自動備份</h3>

      </div>
      <div class="backup-heading-actions">
        <button id="backupRunNow" class="primary compact" type="button" disabled>立即備份</button>
        <button id="backupRefreshStatus" class="secondary compact backup-refresh-button" type="button" aria-label="重新整理備份狀態" title="重新整理備份狀態">重新整理</button>
      </div>
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

    <div id="backupAcceptance" class="backup-acceptance-strip" hidden></div>

    <div class="backup-actions-row backup-policy-row">
      <span id="backupRetentionText" class="hint">讀取保留政策中…</span>
    </div>

    <div class="backup-history-block">
      <div class="backup-history-title"><strong>最近 logical backup</strong><span class="hint">一筆 logical backup 對應各 provider copy health</span></div>
      <div class="backup-history-table-wrap">
        <table class="backup-history-table">
          <thead><tr><th>時間</th><th>方式</th><th>備份 ID</th><th>R2</th><th>GCS</th><th>資料</th></tr></thead>
          <tbody id="backupHistoryRows"><tr><td colspan="6" class="empty">讀取中…</td></tr></tbody>
        </table>
      </div>
    </div>

    <section id="backupRecoveryTools" class="backup-recovery-launcher" hidden>
      <div class="backup-recovery-action"><button id="backupOpenRecovery" class="secondary" type="button" aria-haspopup="dialog">還原操作…</button>
      <p id="backupRecoverySummary" class="hint" role="status"></p></div>
      <a class="backup-emergency-link" href="/recovery" target="_blank" rel="noopener">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><path d="M12 3 4 6v6c0 4 8 9 8 9s8-5 8-9V6z"/><path d="m8 12 3 3 5-6"/></svg>
        <span><strong>緊急復原入口</strong><small>正式帳本無法開啟時使用</small></span>
        <span aria-hidden="true">↗</span>
      </a>
    </section>`;
}

function ensureBackupRecoveryDialog() {
  let dialog = document.querySelector('#backupRecoveryDialog');
  if (dialog) return dialog;
  dialog = document.createElement('dialog');
  dialog.id = 'backupRecoveryDialog';
  dialog.className = 'modal backup-recovery-dialog';
  dialog.setAttribute('aria-labelledby', 'backupRecoveryTitle');
  dialog.innerHTML = `
    <div class="modal-header">
      <h2 id="backupRecoveryTitle">還原帳本</h2>
      <button type="button" class="icon-button" data-close-backup-recovery aria-label="關閉還原視窗">×</button>
    </div>
    <div class="backup-recovery-body">
      <p class="backup-recovery-notice">先還原至獨立工作區並核對資料，保留目前帳本與稽核歷史；完成後由維運人員另行切換正式帳本。</p>
      <div id="backupRecoveryMessage" class="dialog-message" role="status" hidden></div>
      <label>備份<select id="backupRecoverySource"></select></label>
      <label>讀取來源<select id="backupRecoveryProvider"><option value="auto">R2 優先，無法讀取時使用 GCS</option><option value="google_cloud_storage">Google Cloud Storage（災難復原）</option><option value="cloudflare_r2">Cloudflare R2</option></select></label>
      <label>還原理由<input id="backupRecoveryReason" type="text" maxlength="200" autocomplete="off"></label>
      <button id="backupRecoveryPreview" class="secondary" type="button" disabled>驗證備份並預覽還原</button>
      <p id="backupRecoveryStatus" class="hint" role="status">正在讀取還原工作區狀態…</p>
      <div id="backupRecoveryConfirm" hidden>
        <label class="backup-recovery-review"><input id="backupRecoveryReviewed" type="checkbox">我已核對備份來源，確認還原至獨立工作區</label>
        <label>輸入「重建獨立帳本」確認<input id="backupRecoveryConfirmation" type="text" autocomplete="off"></label>
        <button id="backupRecoveryCommit" class="primary" type="button" disabled>還原至獨立工作區</button>
      </div>
    </div>
    <div class="backup-recovery-footer"><button class="secondary" type="button" data-close-backup-recovery>關閉</button></div>`;
  document.body.append(dialog);
  dialog.querySelectorAll('[data-close-backup-recovery]').forEach(button => button.addEventListener('click', () => dialog.close()));
  dialog.addEventListener('close', cancelBackupRecoveryPreview);
  dialog.addEventListener('cancel', event => {
    if (backupRecoveryCommitting) event.preventDefault();
    else cancelBackupRecoveryPreview();
  });
  dialog.querySelector('#backupRecoveryPreview').addEventListener('click', previewBackupRecovery);
  dialog.querySelector('#backupRecoveryCommit').addEventListener('click', commitBackupRecovery);
  for (const id of ['backupRecoverySource', 'backupRecoveryProvider', 'backupRecoveryReason']) dialog.querySelector('#' + id).addEventListener('input', cancelBackupRecoveryPreview);
  for (const id of ['backupRecoveryReviewed', 'backupRecoveryConfirmation']) dialog.querySelector('#' + id).addEventListener('input', syncBackupRecoveryConfirmation);
  return dialog;
}

async function openBackupRecovery() {
  if (!window.cyIsDesktopInteractionWorkspace?.()) return;
  const dialog = ensureBackupRecoveryDialog();
  cancelBackupRecoveryPreview();
  document.querySelector('#backupRecoveryReason').value = '';
  document.querySelector('#backupRecoveryProvider').value = 'auto';
  setBackupRecoveryMessage('');
  if (!dialog.open) dialog.showModal();
  document.querySelector('#backupRecoveryPreview').disabled = true;
  if (!await loadBackupStatus()) setBackupRecoveryMessage('無法讀取還原狀態，請關閉後重試。', true);
}

function setBackupRecoveryMessage(message, failed = false) {
  const node = document.querySelector('#backupRecoveryMessage');
  if (!node) return;
  node.textContent = message;
  node.hidden = !message;
  node.style.color = failed ? '#a34540' : '';
}

async function loadBackupStatus() {
  const run = document.querySelector('#backupRunNow');
  if (run) run.disabled = true;
  setBackupMessage('');

  try {
    const data = await api('/api/backup/status');
    renderBackupStatus(data);
    return data;
  } catch (error) {
    setBackupMessage(error.message || '無法讀取備份狀態。', true);
    const state = document.querySelector('#backupConnectionState');
    if (state) state.textContent = '狀態讀取失敗';
    return null;
  }
}

function backupUiModel(data) {
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
      hint: data?.policy?.phase === 'phase_d' ? 'R2 保存每日備份，GCS 保存跨雲長期備份。' : 'R2 與 GCS 每日備份並驗證相同內容。',
      primaryLabel: '備份拓撲',
      state: data?.configured ? '雙 Provider 已啟用' : 'Provider 尚未完整設定',
      account: data?.configured ? 'R2 + GCS 均已完成設定' : '請檢查 R2 binding 與 GCS Secrets',
      schedule: data?.schedule?.localTime || '每日 03:30（台灣時間）',
      retention: `R2 ${Number(r2.retentionDays || 30)} 天｜GCS ${Number(gcs.retentionDays || 14)} 天（${data?.policy?.phase === 'phase_d' ? 'Phase D' : 'Phase C'}）`,
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
      logicalBackups,
      scheduledAttempts: Array.isArray(data?.scheduledAttempts) ? data.scheduledAttempts : []
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

function renderBackupStatus(data) {
  const model = backupUiModel(data);
  backupTopology = model.topology;

  const heading = document.querySelector('#backupHeadingTitle');
  const primaryLabel = document.querySelector('#backupPrimaryLabel');
  const state = document.querySelector('#backupConnectionState');
  const account = document.querySelector('#backupAccountText');
  const schedule = document.querySelector('#backupScheduleText');
  const lastTime = document.querySelector('#backupLastTime');
  const lastDetail = document.querySelector('#backupLastDetail');
  const retention = document.querySelector('#backupRetentionText');
  const run = document.querySelector('#backupRunNow');

  if (heading) heading.textContent = model.heading;
  if (primaryLabel) primaryLabel.textContent = model.primaryLabel;
  if (state) state.textContent = model.state;
  if (account) account.textContent = model.account;
  if (schedule) schedule.textContent = model.schedule;
  if (retention) retention.textContent = model.retention;

  if (model.tiered) {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.createdAt ? backupLocalDateTime(latest.createdAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytes(latest.byteSize || 0)} · Package SHA ${String(latest.packageSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可立即備份並驗證 R2 與 GCS' : '完成兩個 provider 設定後即可測試');
    }
    renderBackupProviderHealth(model, latest);
    renderTieredBackupHistory(model.logicalBackups, model.scheduledAttempts);
    if (data.attemptLogAvailable === false) setBackupMessage('無法讀取自動備份的執行紀錄，以下清單可能不完整。', true);
  } else {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.completedAt ? backupLocalDateTime(latest.completedAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytes(latest.byteSize || 0)} · SHA ${String(latest.fileSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可立即備份並確認 GCS 回讀結果' : '完成 Cloudflare Secrets 後即可測試');
    }
    renderLegacyProviderHealth(model);
    renderBackupHistory(model.recentRuns || []);
  }

  if (window.cyIsDesktopInteractionWorkspace?.()) {
    const latest = model.latest;
    if (lastDetail && latest) lastDetail.textContent = `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytes(latest.byteSize || 0)} · ${latest.status === 'success' ? '驗證成功' : '待確認驗證結果'}`;
    const historyTitle = document.querySelector('.backup-history-title strong');
    const historyHint = document.querySelector('.backup-history-title .hint');
    if (historyTitle) historyTitle.textContent = '最近備份紀錄';
    if (historyHint) historyHint.hidden = true;
  }

  if (run) run.disabled = !model.configured;
  renderPhaseCAcceptance(data);
  renderBackupRecoveryState(data);
}

function renderBackupProviderHealth(model, latest) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  const copies = Array.isArray(latest?.copies) ? latest.copies : [];
  const r2Copy = copies.find(copy => copy.provider === 'cloudflare_r2');
  const gcsCopy = copies.find(copy => copy.provider === 'google_cloud_storage') || model.logicalBackups.flatMap(item => item.copies || []).find(copy => copy.provider === 'google_cloud_storage');

  container.innerHTML = [
    providerHealthCard('Cloudflare R2', 'Operational backup', model.r2, r2Copy),
    providerHealthCard('Google Cloud Storage', model.gcs.role === 'cross_cloud_long_term' ? '跨雲長期備份' : '跨雲雙副本驗證', model.gcs, gcsCopy)
  ].join('');
}

function renderLegacyProviderHealth(model) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  container.innerHTML = providerHealthCard(
    'Google Cloud Storage',
    'Legacy production / rollback path',
    { configured: model.configured, retentionDays: Number(String(model.retention).match(/\d+/)?.[0] || 14) },
    null
  );
}

function providerHealthCard(name, role, provider, copy) {
  const status = copy?.status || (provider?.configured ? 'configured' : 'not_configured');
  const badge = status === 'success' ? '成功' : status === 'failed' ? '失敗' : provider?.configured ? '已設定' : '未設定';
  const badgeClass = status === 'success' ? 'success' : status === 'failed' ? 'failed' : 'neutral';
  const verified = copy?.verifiedAt ? ` · 驗證 ${backupLocalDateTime(copy.verifiedAt)}` : '';
  const detail = copy?.lastError || `${Number(provider?.retentionDays || 0)} 天${verified}`;
  return `<article class="backup-provider-card">
    <div class="backup-provider-card-head"><div><strong>${escapeBackupHtml(name)}</strong><span>${escapeBackupHtml(role)}</span></div><span class="backup-run-badge ${badgeClass}">${badge}</span></div>
    <div class="backup-provider-card-detail">${escapeBackupHtml(detail)}</div>
  </article>`;
}

function renderBackupHistory(runs) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  const items = Array.isArray(runs) ? runs.slice(0, 8) : [];
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無 Cloud Storage 備份執行紀錄。</td></tr>';
    return;
  }
  tbody.innerHTML = items.map(run => {
    const success = run.status === 'success';
    const detail = success ? run.fileName : (run.errorMessage || '備份失敗');
    return `<tr>
      <td>${escapeBackupHtml(backupLocalDateTime(run.completedAt || run.startedAt))}</td>
      <td>${run.trigger === 'scheduled' ? '自動' : '手動'}</td>
      <td class="backup-run-detail" title="${escapeBackupHtml(detail)}">${escapeBackupHtml(run.fileName || '—')}</td>
      <td class="backup-copy-cell">—</td>
      <td class="backup-copy-cell"><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="backup-data-summary">${Number(run.rowCount || 0).toLocaleString()} 筆 · ${run.byteSize ? backupBytes(run.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

function renderTieredBackupHistory(backups, attempts = []) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  const logical = Array.isArray(backups) ? backups : [];
  const items = [...logical, ...attempts.filter(attempt => ['failed', 'pending'].includes(attempt.status)
    && !logical.some(item => item.backupId === attempt.backupId)).map(attempt => ({ ...attempt, attemptOnly: true }))]
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt)).slice(0, 8);
  if (!items.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無備份執行紀錄。</td></tr>';
    return;
  }
  tbody.innerHTML = items.map(item => {
    const slot = item.attemptOnly ? item.slot : attempts.find(attempt => attempt.backupId === item.backupId)?.slot;
    const trigger = slot === 'retry' ? '補跑' : item.attemptOnly || item.trigger === 'scheduled' ? '自動' : '手動';
    if (item.attemptOnly) {
      const detail = item.status === 'pending' ? '執行未完成' : item.errorCode === 'D1_DAILY_LIMIT_EXCEEDED'
        ? 'D1 每日額度已用完' : item.errorCode === 'BACKUP_DATABASE_UNAVAILABLE' ? '無法讀取資料庫' : '備份執行失敗';
      return `<tr><td>${escapeBackupHtml(backupLocalDateTime(item.createdAt))}</td><td>${trigger}</td>
        <td colspan="3" class="backup-run-detail" title="${escapeBackupHtml(item.errorCode || '')}">${escapeBackupHtml(detail)}</td>
        <td class="backup-data-summary">—</td></tr>`;
    }
    const copies = Array.isArray(item.copies) ? item.copies : [];
    const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
    const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
    const title = item.status === 'success'
      ? `Package SHA ${String(item.packageSha256 || '')}`
      : copies.filter(copy => copy.status !== 'success').map(copy => copy.lastError || `${copy.provider} failed`).join(' · ');
    return `<tr>
      <td>${escapeBackupHtml(backupLocalDateTime(item.createdAt))}</td>
      <td>${trigger}</td>
      <td class="backup-run-detail" title="${escapeBackupHtml(title)}">${escapeBackupHtml(item.backupId || '—')}</td>
      <td class="backup-copy-cell">${backupCopyBadgeBase(r2)}</td>
      <td class="backup-copy-cell">${item.gcsExpected === false && !gcs ? '' : backupCopyBadgeBase(gcs)}</td>
      <td class="backup-data-summary">${Number(item.rowCount || 0).toLocaleString()} 筆 · ${backupBytes(item.byteSize || 0)}</td>
    </tr>`;
  }).join('');
}

function backupCopyBadgeBase(copy) {
  if (!copy) return '<span class="backup-run-badge neutral">—</span>';
  const success = copy.status === 'success';
  const failed = copy.status === 'failed';
  const label = success ? '成功' : failed ? '失敗' : '處理中';
  const cls = success ? 'success' : failed ? 'failed' : 'neutral';
  return `<span class="backup-run-badge ${cls}" title="${escapeBackupHtml(copy.lastError || '')}">${label}</span>`;
}

async function runBackupNow() {
  const tiered = backupTopology === 'parallel_dual_provider';
  const target = tiered ? 'R2 + GCS paired backup' : 'Google Cloud Storage 備份';
  if (!await window.cyConfirm({title:'立即備份',message:`現在立即備份到 ${target}？正常每日備份仍會在排程時間自動執行。`,confirmText:'立即備份'})) return;
  const button = document.querySelector('#backupRunNow');
  if (button) button.disabled = true;
  setBackupMessage(tiered
    ? '正在建立單一 BackupSet、寫入 R2 + GCS 並逐一回讀驗證…'
    : '正在建立 data.json／manifest.json、上傳並回讀驗證…');
  try {
    const data = await api('/api/backup/run', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    setBackupMessage(`備份完成：${data.backup?.backupId || data.backup?.fileName || ''}`);
    await loadBackupStatus();
  } catch (error) {
    setBackupMessage(error.message || '測試備份失敗。', true);
    await loadBackupStatus();
  }
}

function setBackupMessage(message, isError = false) {
  const element = document.querySelector('#backupMessage');
  if (!element) return;
  setDialogMessage(element, message || '', isError);
}

function backupLocalDateTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return new Intl.DateTimeFormat('zh-TW', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
  }).format(date);
}

function backupBytes(value) {
  const bytes = Number(value || 0);
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function escapeBackupHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

const PHASE_C_REQUIRED = 14;

function derivePhaseCAcceptanceFromLogicalBackups(backups, required) {
  const scheduled = (Array.isArray(backups) ? backups : []).filter(item => item?.trigger === 'scheduled');
  let count = 0;
  for (const item of scheduled) {
    if (count >= required) break;
    const copies = Array.isArray(item?.copies) ? item.copies : [];
    const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
    const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
    const packageSha = String(item?.packageSha256 || '');
    if (!(r2?.status === 'success' && gcs?.status === 'success' && /^[0-9a-f]{64}$/i.test(packageSha))) break;
    count += 1;
  }
  return count;
}

function phaseCAcceptanceUiModel(data) {
  const tiered = data?.topology === 'parallel_dual_provider' || data?.provider === 'tiered';
  if (!tiered || data?.policy?.phase === 'phase_d') return { visible: false, required: PHASE_C_REQUIRED, count: 0, remaining: PHASE_C_REQUIRED, completed: false };
  const server = data?.phaseCAcceptance || {};
  const required = Math.max(1, Number(server.requiredConsecutiveScheduled || PHASE_C_REQUIRED));
  const fallbackCount = derivePhaseCAcceptanceFromLogicalBackups(data?.logicalBackups, required);
  const count = Math.max(0, Math.min(required, Number.isFinite(Number(server.consecutiveScheduledSuccesses))
    ? Number(server.consecutiveScheduledSuccesses)
    : fallbackCount));
  return {
    visible: true,
    required,
    count,
    remaining: Math.max(0, required - count),
    completed: Boolean(server.completed) || count >= required,
    latestScheduledAt: server.latestScheduledAt || null,
    latestScheduledBackupId: server.latestScheduledBackupId || null
  };
}

function phaseCAcceptanceHtml(model) {
  const state = model.completed ? 'Phase C gate 已完成' : `尚差 ${model.remaining} 次`;
  const latest = model.latestScheduledAt ? `最近排程：${backupLocalDateTime(model.latestScheduledAt)}` : '尚未有 Phase C 排程備份';
  return `
    <div class="backup-acceptance-main"><span>Phase C 排程驗收</span><strong>${model.count} / ${model.required}</strong></div>
    <progress class="backup-acceptance-progress" max="${model.required}" value="${model.count}"></progress>
    <div class="backup-acceptance-detail"><span>${escapeBackupHtml(state)}</span><span>${escapeBackupHtml(latest)}；手動測試不計</span></div>`;
}

function renderPhaseCAcceptance(data) {
  const container = document.querySelector('#backupAcceptance');
  if (!container) return;
  const model = phaseCAcceptanceUiModel(data);
  container.hidden = !model.visible;
  container.innerHTML = model.visible ? phaseCAcceptanceHtml(model) : '';
}

window.phaseCAcceptanceUiModel = phaseCAcceptanceUiModel;
window.mobileBackupInfoHtml = mobileBackupInfoHtml;


function renderBackupRecoveryState(data) {
  const panel=document.querySelector('#backupRecoveryTools');
  if(!panel) return;
  panel.hidden=!window.cyIsDesktopInteractionWorkspace?.()||!data?.policy;
  const source=document.querySelector('#backupRecoverySource');
  const previous=source?.value;
  if(source) { source.innerHTML=(data.logicalBackups||[]).map(row=>`<option value="${escapeBackupHtml(row.backupId)}">${escapeBackupHtml(backupLocalDateTime(row.createdAt))} · ${Number(row.rowCount||0).toLocaleString()} 筆</option>`).join('');if(previous&&[...source.options].some(option=>option.value===previous))source.value=previous; }
  const workspace=data.recoveryWorkspace||{};
  const button=document.querySelector('#backupRecoveryPreview');
  if(button)button.disabled=!workspace.ready||!data.logicalBackups?.length;
  const status=document.querySelector('#backupRecoveryStatus');
  if(status)status.textContent=data.recoveryLatest?.status==='verified'?'獨立帳本已還原，完整資料、稽核及備份政策核對通過；目前正式帳本未切換。':!workspace.configured?'還原工作區採需要時建立，請先由維運人員準備。':!workspace.ready?'還原工作區已有資料，不可覆寫；請由維運人員處理。':'請選擇備份、填寫理由並預覽還原。';
  const summary = document.querySelector('#backupRecoverySummary');
  if (summary && status) summary.textContent = status.textContent;
  if(!workspace.ready)cancelBackupRecoveryPreview();
}
function cancelBackupRecoveryPreview() {
  backupRecoveryGeneration += 1;
  backupRecoveryPreview=null;
  const panel=document.querySelector('#backupRecoveryConfirm');if(panel)panel.hidden=true;
  const reviewed=document.querySelector('#backupRecoveryReviewed');if(reviewed)reviewed.checked=false;
  const text=document.querySelector('#backupRecoveryConfirmation');if(text)text.value='';
  syncBackupRecoveryConfirmation();
}
function syncBackupRecoveryConfirmation() {
  const button=document.querySelector('#backupRecoveryCommit');
  if(button)button.disabled=!backupRecoveryPreview||!document.querySelector('#backupRecoveryReviewed')?.checked||document.querySelector('#backupRecoveryConfirmation')?.value!=='重建獨立帳本';
}
async function previewBackupRecovery() {
  if (backupRecoveryCommitting) return;
  cancelBackupRecoveryPreview();
  const generation = backupRecoveryGeneration;
  const button=document.querySelector('#backupRecoveryPreview');if(button)button.disabled=true;
  setBackupRecoveryMessage('正在驗證備份與獨立工作區…');
  try {
    const data=await api('/api/backup/recovery/preview',{method:'POST',headers:jsonHeaders(),body:JSON.stringify({backupId:document.querySelector('#backupRecoverySource').value,provider:document.querySelector('#backupRecoveryProvider').value,reason:document.querySelector('#backupRecoveryReason').value})},120000);
    if (generation !== backupRecoveryGeneration || !document.querySelector('#backupRecoveryDialog')?.open) return;
    backupRecoveryPreview=data.preview;
    document.querySelector('#backupRecoveryStatus').textContent=`${data.preview.normalization ? '舊版格式轉換及驗證通過，' : '備份驗證通過，'}將還原 ${Number(data.preview.counts.transactions||0).toLocaleString()} 筆記帳至獨立工作區。請完成下方兩項確認；預覽有效 10 分鐘。`;
    document.querySelector('#backupRecoveryConfirm').hidden=false;
    setBackupRecoveryMessage('還原預覽完成，目前帳本未更動。');
  } catch(error) {setBackupRecoveryMessage(error.message,true);} finally {if(button)button.disabled=false;}
}
async function commitBackupRecovery() {
  if(!backupRecoveryPreview || backupRecoveryCommitting)return;
  backupRecoveryCommitting = true;
  const dialog = document.querySelector('#backupRecoveryDialog');
  const fields = dialog.querySelectorAll('input, select');
  fields.forEach(field => { field.disabled = true; });
  document.querySelector('#backupRecoveryPreview').disabled = true;
  const closeButtons = dialog.querySelectorAll('[data-close-backup-recovery]');
  closeButtons.forEach(button => { button.disabled = true; });
  const button=document.querySelector('#backupRecoveryCommit');if(button)button.disabled=true;
  setBackupRecoveryMessage('正在還原至獨立工作區並核對完整資料…');
  try {
    await api('/api/backup/recovery/commit',{method:'POST',headers:jsonHeaders(),body:JSON.stringify({previewId:backupRecoveryPreview.id,reviewed:document.querySelector('#backupRecoveryReviewed').checked,confirmation:document.querySelector('#backupRecoveryConfirmation').value})},120000);
    cancelBackupRecoveryPreview();await loadBackupStatus();setBackupRecoveryMessage('獨立帳本還原及核對完成，目前正式帳本未切換。');
  } catch(error) {cancelBackupRecoveryPreview();await loadBackupStatus();setBackupRecoveryMessage(error.message,true);}
  finally { backupRecoveryCommitting = false; fields.forEach(field => { field.disabled = false; }); closeButtons.forEach(button => { button.disabled = false; }); }
}
