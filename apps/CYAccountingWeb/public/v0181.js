(() => {
  const PHASE_C_REQUIRED = 14;

  const baseBackupSettingsHtml = backupSettingsHtmlV17;
  backupSettingsHtmlV17 = function backupSettingsHtmlV181() {
    return baseBackupSettingsHtml()
      .replace(
        '<div id="backupProviderHealth" class="backup-provider-health"></div>\n\n    <div class="backup-actions-row">',
        '<div id="backupProviderHealth" class="backup-provider-health"></div>\n\n    <div id="backupAcceptance" class="backup-acceptance-strip" hidden></div>\n\n    <div class="backup-actions-row">'
      )
      .replace(
        '<thead><tr><th>時間</th><th>方式</th><th>整體</th><th>備份 ID／錯誤</th><th>R2</th><th>GCS</th><th class="num">資料筆數</th><th class="num">大小</th></tr></thead>',
        '<thead><tr><th>時間</th><th>方式</th><th>備份 ID</th><th>R2</th><th>GCS</th><th>資料</th></tr></thead>'
      )
      .replace('colspan="8" class="empty">讀取中…', 'colspan="6" class="empty">讀取中…');
  };

  const baseRenderBackupStatus = renderBackupStatusV17;
  renderBackupStatusV17 = function renderBackupStatusV181(data) {
    baseRenderBackupStatus(data);
    renderPhaseCAcceptanceV181(data);
  };

  renderTieredBackupHistoryV18 = function renderTieredBackupHistoryV181(backups) {
    const tbody = document.querySelector('#backupHistoryRows');
    if (!tbody) return;
    const items = Array.isArray(backups) ? backups.slice(0, 8) : [];
    if (!items.length) {
      tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無 paired backup 執行紀錄。</td></tr>';
      return;
    }

    tbody.innerHTML = items.map(item => {
      const copies = Array.isArray(item.copies) ? item.copies : [];
      const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
      const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
      const title = item.status === 'success'
        ? `Package SHA ${String(item.packageSha256 || '')}`
        : copies.filter(copy => copy.status !== 'success').map(copy => copy.lastError || `${copy.provider} failed`).join(' · ');
      return `<tr>
        <td>${v17Escape(backupLocalDateTimeV17(item.createdAt))}</td>
        <td>${item.trigger === 'scheduled' ? '自動' : '手動'}</td>
        <td class="backup-run-detail" title="${v17Escape(title)}">${v17Escape(item.backupId || '—')}</td>
        <td class="backup-copy-cell">${copyBadgeV181(r2)}</td>
        <td class="backup-copy-cell">${copyBadgeV181(gcs)}</td>
        <td class="backup-data-summary">${Number(item.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(item.byteSize || 0)}</td>
      </tr>`;
    }).join('');
  };

  renderBackupHistoryV17 = function renderBackupHistoryV181(runs) {
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
        <td>${v17Escape(backupLocalDateTimeV17(run.completedAt || run.startedAt))}</td>
        <td>${run.trigger === 'scheduled' ? '自動' : '手動'}</td>
        <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(run.fileName || '—')}</td>
        <td class="backup-copy-cell">—</td>
        <td class="backup-copy-cell"><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
        <td class="backup-data-summary">${Number(run.rowCount || 0).toLocaleString()} 筆 · ${run.byteSize ? backupBytesV17(run.byteSize) : '—'}</td>
      </tr>`;
    }).join('');
  };

  function copyBadgeV181(copy) {
    if (!copy) return '<span class="backup-run-badge neutral">—</span>';
    const success = copy.status === 'success';
    const failed = copy.status === 'failed';
    const label = success ? '成功' : failed ? '失敗' : '處理中';
    const cls = success ? 'success' : failed ? 'failed' : 'neutral';
    return `<span class="backup-run-badge ${cls}" title="${v17Escape(copy.lastError || '')}">${label}</span>`;
  }

  function deriveAcceptanceFromLogicalBackupsV181(backups, required) {
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

  function phaseCAcceptanceUiModelV181(data) {
    const tiered = data?.topology === 'parallel_dual_provider' || data?.provider === 'tiered';
    if (!tiered) return { visible: false, required: PHASE_C_REQUIRED, count: 0, remaining: PHASE_C_REQUIRED, completed: false };

    const server = data?.phaseCAcceptance || {};
    const required = Math.max(1, Number(server.requiredConsecutiveScheduled || PHASE_C_REQUIRED));
    const fallbackCount = deriveAcceptanceFromLogicalBackupsV181(data?.logicalBackups, required);
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

  function renderPhaseCAcceptanceV181(data) {
    const container = document.querySelector('#backupAcceptance');
    if (!container) return;
    const model = phaseCAcceptanceUiModelV181(data);
    container.hidden = !model.visible;
    if (!model.visible) {
      container.innerHTML = '';
      return;
    }

    const state = model.completed ? 'Phase C gate 已完成' : `尚差 ${model.remaining} 次`;
    const latest = model.latestScheduledAt
      ? `最近排程：${backupLocalDateTimeV17(model.latestScheduledAt)}`
      : '尚未有 Phase C 排程備份';
    container.innerHTML = `
      <div class="backup-acceptance-main">
        <span>Phase C 排程驗收</span>
        <strong>${model.count} / ${model.required}</strong>
      </div>
      <progress class="backup-acceptance-progress" max="${model.required}" value="${model.count}"></progress>
      <div class="backup-acceptance-detail"><span>${v17Escape(state)}</span><span>${v17Escape(latest)}；手動測試不計</span></div>`;
  }

  window.phaseCAcceptanceUiModelV181 = phaseCAcceptanceUiModelV181;
  window.addEventListener('load', () => {
    const version = document.querySelector('.version');
    if (version) version.textContent = 'V0.18.1';
  });
})();
