/* CYAccountingWeb Excel export UI functional module. */

window.addEventListener('load', () => {
  setupMonthlyExcelExport();
});

function setupMonthlyExcelExport() {
  const button = document.querySelector('#ledgerExcelExport');
  const status = document.querySelector('#ledgerExcelExportStatus');
  if (!button || !status || button.dataset.excelExportBound === '1') return;
  button.dataset.excelExportBound = '1';
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

    const fileName = `CYAccounting_${month}.xlsx`;
    const file = new File([blob], fileName, {
      type: blob.type || 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    });
    const nativeShare = (
      window.matchMedia('(max-width: 767px)').matches
      || document.documentElement.dataset.tabletLayout === 'landscape'
    ) && typeof navigator.share === 'function'
      && (typeof navigator.canShare !== 'function' || navigator.canShare({ files: [file] }));

    if (nativeShare) {
      try {
        await navigator.share({
          files: [file],
          title: `志遠記帳 ${month} Excel`
        });
        setExportStatus('已開啟系統分享。');
        return;
      } catch (shareError) {
        if (shareError?.name === 'AbortError') {
          setExportStatus('已取消分享。');
          return;
        }
      }
    }

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
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