const CY_V21_BUILD12_VERSION = 'V0.21.0 Build 12';
const CY_V21_BUILD12_DESKTOP = '(min-width: 1024px)';
const CY_V21_BUILD12_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];

ensureV21Build12Stylesheet();
syncV21Build12Version();
syncV21Build12Copy();

window.addEventListener('load', () => {
  syncV21Build12Version();
  syncV21Build12Copy();
  setupV21Build12DesktopMonthPicker();
});

function ensureV21Build12Stylesheet() {
  if (document.querySelector('link[href="/v021b12.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b12.css';
  document.head.appendChild(link);
}

function syncV21Build12Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD12_VERSION;
}

function syncV21Build12Copy() {
  const summary = document.querySelector('#summary');
  const editSummary = document.querySelector('#editSummary');
  if (summary) summary.placeholder = '最多20個字';
  if (editSummary) editSummary.placeholder = '最多20個字';
}

function setupV21Build12DesktopMonthPicker() {
  const media = window.matchMedia(CY_V21_BUILD12_DESKTOP);
  const syncMode = () => {
    const root = document.querySelector('#ledgerMonthPickerCustom');
    if (root) {
      root.hidden = !media.matches;
      if (!media.matches) closeV21Build12MonthPicker(root);
    }
    if (media.matches) ensureV21Build12MonthPicker();
  };

  if (typeof media.addEventListener === 'function') media.addEventListener('change', syncMode);
  else media.addListener?.(syncMode);
  syncMode();
}

function ensureV21Build12MonthPicker() {
  const input = document.querySelector('#monthFilter');
  const slot = document.querySelector('#ledgerMonthSlot');
  if (!input || !slot) {
    setTimeout(ensureV21Build12MonthPicker, 60);
    return;
  }

  let root = document.querySelector('#ledgerMonthPickerCustom');
  if (root) {
    root.hidden = false;
    syncV21Build12MonthPickerLabel(root, input);
    return;
  }

  root = document.createElement('div');
  root.id = 'ledgerMonthPickerCustom';
  root.className = 'v21-month-picker-custom';
  root.innerHTML = `
    <button id="ledgerMonthPickerTrigger" class="v21-month-picker-trigger" type="button" aria-haspopup="dialog" aria-expanded="false">
      <span id="ledgerMonthPickerLabel">—</span><span class="v21-month-picker-caret" aria-hidden="true">▾</span>
    </button>
    <div id="ledgerMonthPickerPopover" class="v21-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="v21-month-picker-head">
        <button type="button" class="v21-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button id="ledgerMonthPickerYearButton" type="button" class="v21-month-picker-year" aria-label="切換年份選擇"></button>
        <button type="button" class="v21-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div id="ledgerMonthPickerGrid" class="v21-month-picker-grid"></div>
    </div>`;
  slot.append(root);

  const trigger = root.querySelector('#ledgerMonthPickerTrigger');
  const popover = root.querySelector('#ledgerMonthPickerPopover');
  const yearButton = root.querySelector('#ledgerMonthPickerYearButton');
  const grid = root.querySelector('#ledgerMonthPickerGrid');
  let view = 'months';
  let displayYear = v21Build12ReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = v21Build12ReadMonth(input);
    if (view === 'months') {
      yearButton.textContent = String(displayYear);
      yearButton.title = '選擇年份';
      grid.className = 'v21-month-picker-grid month-view';
      grid.innerHTML = CY_V21_BUILD12_MONTHS.map((label, index) => {
        const month = index + 1;
        const active = selected.year === displayYear && selected.month === month;
        return `<button type="button" class="v21-month-choice${active ? ' active' : ''}" data-picker-month="${month}" aria-pressed="${active ? 'true' : 'false'}">${label}</button>`;
      }).join('');
      return;
    }

    yearButton.textContent = `${yearStart}–${yearStart + 11}`;
    yearButton.title = '返回月份選擇';
    grid.className = 'v21-month-picker-grid year-view';
    grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year => {
      const active = year === selected.year;
      const current = year === new Date().getFullYear();
      return `<button type="button" class="v21-year-choice${active ? ' active' : ''}${current ? ' current' : ''}" data-picker-year="${year}" aria-pressed="${active ? 'true' : 'false'}">${year}</button>`;
    }).join('');
  };

  const open = () => {
    const selected = v21Build12ReadMonth(input);
    displayYear = selected.year;
    yearStart = displayYear - 5;
    view = 'months';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!window.matchMedia(CY_V21_BUILD12_DESKTOP).matches) return;
    if (popover.hidden) open(); else close(false);
  });

  root.querySelectorAll('[data-picker-nav]').forEach(button => {
    button.addEventListener('click', () => {
      const delta = Number(button.dataset.pickerNav) || 0;
      if (view === 'months') displayYear += delta;
      else yearStart += delta * 12;
      render();
    });
  });

  yearButton.addEventListener('click', () => {
    if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else {
      view = 'months';
    }
    render();
  });

  grid.addEventListener('click', event => {
    const monthButton = event.target.closest('[data-picker-month]');
    if (monthButton) {
      const month = Number(monthButton.dataset.pickerMonth);
      if (month >= 1 && month <= 12) {
        input.value = `${displayYear}-${String(month).padStart(2, '0')}`;
        input.dispatchEvent(new Event('change', { bubbles: true }));
        syncV21Build12MonthPickerLabel(root, input);
        close(true);
      }
      return;
    }

    const yearButtonChoice = event.target.closest('[data-picker-year]');
    if (yearButtonChoice) {
      displayYear = Number(yearButtonChoice.dataset.pickerYear) || displayYear;
      view = 'months';
      render();
    }
  });

  input.addEventListener('change', () => {
    const selected = v21Build12ReadMonth(input);
    displayYear = selected.year;
    syncV21Build12MonthPickerLabel(root, input);
    if (!popover.hidden) render();
  });

  document.addEventListener('pointerdown', event => {
    if (popover.hidden || root.contains(event.target)) return;
    close(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || popover.hidden) return;
    close(true);
  });

  root._v21Build12Close = close;
  syncV21Build12MonthPickerLabel(root, input);
}

function syncV21Build12MonthPickerLabel(root, input) {
  const label = root?.querySelector('#ledgerMonthPickerLabel');
  if (!label || !input) return;
  const selected = v21Build12ReadMonth(input);
  label.textContent = `${selected.year}年${String(selected.month).padStart(2, '0')}月`;
}

function closeV21Build12MonthPicker(root) {
  const popover = root?.querySelector('#ledgerMonthPickerPopover');
  const trigger = root?.querySelector('#ledgerMonthPickerTrigger');
  if (popover) popover.hidden = true;
  trigger?.setAttribute('aria-expanded', 'false');
}

function v21Build12ReadMonth(input) {
  const value = String(input?.value || '');
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (match) return { year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}
