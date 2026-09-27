window.addEventListener('load', () => {
  setupMonthlyExcelExport();
});

function setupMonthlyExcelExport() {
  const tools = document.querySelector('.ledger-view-tools');
  if (!tools || document.querySelector('#ledgerExcelExport')) return;

  const button = document.createElement('button');
  button.id = 'ledgerExcelExport';
  button.className = 'secondary compact';
  button.type = 'button';
  button.textContent = '匯出 Excel';
  button.title = '匯出目前月份完整帳簿（.xlsx）';

  const status = document.createElement('span');
  status.id = 'ledgerExcelExportStatus';
  status.className = 'ledger-export-status';
  status.setAttribute('aria-live', 'polite');

  tools.prepend(button, status);
  button.addEventListener('click', downloadMonthlyExcel);
}

async function downloadMonthlyExcel() {
  const button = document.querySelector('#ledgerExcelExport');
  const month = els.monthFilter?.value || '';
  if (!/^\d{4}-\d{2}$/.test(month)) {
    setExportStatus('請先選擇月份。', true);
    return;
  }

  const defaultLabel = '匯出 Excel';
  let failed = false;
  if (button) {
    button.disabled = true;
    button.textContent = '匯出中…';
    button.setAttribute('aria-busy', 'true');
  }
  setExportStatus('正在產生 Excel 檔案…');

  try {
    const response = await fetch(`/api/export/month.xlsx?month=${encodeURIComponent(month)}`, {
      method: 'GET',
      headers: { accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
      cache: 'no-store'
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      throw new Error(data?.error || `匯出失敗（HTTP ${response.status}）。`);
    }

    const blob = await response.blob();
    if (!blob.size) throw new Error('匯出檔案內容為空。');

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `CYAccounting_${month}.xlsx`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    setExportStatus('Excel 已下載。');
  } catch (error) {
    failed = true;
    setExportStatus(error?.message || 'Excel 匯出失敗。', true);
  } finally {
    if (button) {
      button.disabled = false;
      button.removeAttribute('aria-busy');
      button.textContent = failed ? '匯出失敗' : defaultLabel;
      if (failed) {
        setTimeout(() => {
          if (button.textContent === '匯出失敗') button.textContent = defaultLabel;
        }, 2200);
      }
    }
  }
}

function setExportStatus(message, isError = false) {
  const status = document.querySelector('#ledgerExcelExportStatus');
  if (!status) return;
  status.textContent = message || '';
  status.classList.toggle('error', Boolean(message && isError));
}
