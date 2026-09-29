const CY_V0215_VERSION = 'V0.21.5';
const CY_V0215_MOBILE = '(max-width: 767px)';
let cyV0215ActionPopover = null;
let cyV0215QuickPopover = null;
let cyV0215VersionObserver = null;

ensureV0215Stylesheet();

const runV0215 = () => {
  enforceV0215Version();
  setupV0215MobileEntry();
  setupV0215MobileLedger();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runV0215, 0), { once: true });
} else {
  setTimeout(runV0215, 0);
}
window.addEventListener('load', () => {
  runV0215();
  setTimeout(runV0215, 180);
  setTimeout(runV0215, 520);
  setTimeout(runV0215, 1100);
}, { once: true });

function ensureV0215Stylesheet() {
  if (document.querySelector('link[href="/v0215.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0215.css';
  document.head.appendChild(link);
}

function enforceV0215Version() {
  const version = document.querySelector('.version');
  if (!version) return;
  version.textContent = CY_V0215_VERSION;
  if (!cyV0215VersionObserver) {
    cyV0215VersionObserver = new MutationObserver(() => {
      if (version.textContent !== CY_V0215_VERSION) version.textContent = CY_V0215_VERSION;
    });
    cyV0215VersionObserver.observe(version, { childList: true, subtree: true, characterData: true });
  }
}

function setupV0215MobileEntry() {
  const mobile = window.matchMedia(CY_V0215_MOBILE);
  const grid = document.querySelector('.entry-grid');
  if (!grid) return;

  ensureV0215QuickCell(grid, 'favorite', '常用科目');
  ensureV0215QuickCell(grid, 'summary', '常用摘要');

  const sync = () => {
    const enabled = mobile.matches;
    document.querySelector('#entryAccountChoiceRow')?.classList.toggle('v0215-mobile-hidden', enabled);
    grid.classList.toggle('v0215-mobile-entry-grid', enabled);
    if (!enabled) {
      closeV0215QuickPopover();
      return;
    }
    const date = document.querySelector('#txDate');
    if (date) {
      date.type = 'date';
      date.removeAttribute('inputmode');
    }
  };

  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', sync);
  else mobile.addListener?.(sync);
  sync();

  if (grid.dataset.v0215EntryBound === '1') return;
  grid.dataset.v0215EntryBound = '1';
  grid.addEventListener('click', event => {
    const button = event.target.closest('[data-v0215-quick]');
    if (!button || !mobile.matches) return;
    event.preventDefault();
    const type = button.dataset.v0215Quick;
    if (cyV0215QuickPopover?.dataset.quickType === type && !cyV0215QuickPopover.hidden) {
      closeV0215QuickPopover();
      return;
    }
    openV0215QuickPopover(button, type);
  });

  document.addEventListener('pointerdown', event => {
    if (!cyV0215QuickPopover || cyV0215QuickPopover.hidden) return;
    if (cyV0215QuickPopover.contains(event.target) || event.target.closest('[data-v0215-quick]')) return;
    closeV0215QuickPopover();
  });

  const summarySuggestions = document.querySelector('#summarySuggestions');
  if (summarySuggestions) {
    const observer = new MutationObserver(() => {
      if (cyV0215QuickPopover?.dataset.quickType === 'summary' && !cyV0215QuickPopover.hidden) {
        renderV0215QuickPopover('summary');
      }
    });
    observer.observe(summarySuggestions, { childList: true, subtree: true, characterData: true });
  }
}

function ensureV0215QuickCell(grid, type, label) {
  let cell = grid.querySelector('[data-v0215-quick-cell="' + type + '"]');
  if (cell) return cell;
  cell = document.createElement('div');
  cell.className = 'v0215-quick-cell v0215-quick-' + type;
  cell.dataset.v0215QuickCell = type;
  cell.innerHTML = '<button type="button" class="v0215-quick-trigger" data-v0215-quick="' + type + '">' +
    '<span>' + label + '</span><span aria-hidden="true">▾</span></button>';
  grid.appendChild(cell);
  return cell;
}

function ensureV0215QuickPopover() {
  if (cyV0215QuickPopover?.isConnected) return cyV0215QuickPopover;
  const popover = document.createElement('div');
  popover.id = 'v0215QuickPopover';
  popover.className = 'v0215-quick-popover';
  popover.hidden = true;
  document.body.appendChild(popover);
  popover.addEventListener('click', event => {
    const item = event.target.closest('[data-v0215-quick-value]');
    if (!item) return;
    const type = popover.dataset.quickType;
    const value = item.dataset.v0215QuickValue || '';
    if (type === 'favorite') {
      const select = document.querySelector('#categoryName');
      if (select && [...select.options].some(option => option.value === value)) {
        select.value = value;
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
    } else if (type === 'summary') {
      const input = document.querySelector('#summary');
      if (input) {
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
    }
    closeV0215QuickPopover();
  });
  cyV0215QuickPopover = popover;
  return popover;
}

function openV0215QuickPopover(anchor, type) {
  const popover = ensureV0215QuickPopover();
  popover.dataset.quickType = type;
  renderV0215QuickPopover(type);
  popover.hidden = false;
  positionV0215Popover(anchor, popover);
}

function renderV0215QuickPopover(type) {
  const popover = ensureV0215QuickPopover();
  let values = [];
  if (type === 'favorite' && typeof state === 'object') {
    values = (state.categories || [])
      .filter(item => item.kind === state.kind && Number(item.is_favorite) === 1)
      .map(item => String(item.name || ''))
      .filter(Boolean);
  } else if (type === 'summary') {
    values = [...document.querySelectorAll('#summarySuggestions [data-summary-suggestion]')]
      .map(button => String(button.dataset.summarySuggestion || '').trim())
      .filter(Boolean);
  }
  const unique = [...new Set(values)].slice(0, 12);
  if (!unique.length) {
    popover.innerHTML = '<div class="v0215-quick-empty">目前沒有常用項目</div>';
    return;
  }
  popover.innerHTML = unique.map(value =>
    '<button type="button" data-v0215-quick-value="' + escapeV0215(value) + '">' + escapeV0215(value) + '</button>'
  ).join('');
}

function closeV0215QuickPopover() {
  if (cyV0215QuickPopover) cyV0215QuickPopover.hidden = true;
}

function setupV0215MobileLedger() {
  const rows = document.querySelector('#transactionRows');
  if (!rows) return;
  const mobile = window.matchMedia(CY_V0215_MOBILE);

  const sync = () => decorateV0215LedgerRows(mobile.matches);
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', sync);
  else mobile.addListener?.(sync);

  if (rows.dataset.v0215RowsObserved !== '1') {
    rows.dataset.v0215RowsObserved = '1';
    const observer = new MutationObserver(sync);
    observer.observe(rows, { childList: true, subtree: true });
  }

  if (rows.dataset.v0215ActionBound !== '1') {
    rows.dataset.v0215ActionBound = '1';
    rows.addEventListener('click', event => {
      const trigger = event.target.closest('[data-v0215-row-menu]');
      if (!trigger || !mobile.matches) return;
      event.preventDefault();
      event.stopPropagation();
      openV0215ActionPopover(trigger, trigger.closest('tr'));
    });
  }

  document.addEventListener('pointerdown', event => {
    if (!cyV0215ActionPopover || cyV0215ActionPopover.hidden) return;
    if (cyV0215ActionPopover.contains(event.target) || event.target.closest('[data-v0215-row-menu]')) return;
    closeV0215ActionPopover();
  });

  sync();
}

function decorateV0215LedgerRows(enabled) {
  const rows = document.querySelectorAll('#transactionRows > tr:not(.account-group-row)');
  for (const row of rows) {
    if (row.classList.contains('inline-editing')) continue;
    const cells = row.children;
    if (cells.length < 8) continue;

    if (!enabled) {
      restoreV0215Cell(cells[0]);
      restoreV0215Cell(cells[1]);
      row.classList.remove('v0215-income', 'v0215-expense');
      cells[7].querySelector('[data-v0215-row-menu]')?.remove();
      continue;
    }

    const dateCell = cells[0];
    const accountCell = cells[1];
    const kindCell = cells[2];
    const actionCell = cells[7];

    if (!dateCell.dataset.v0215Original) dateCell.dataset.v0215Original = dateCell.textContent.trim();
    const fullDate = dateCell.dataset.v0215Original;
    const parts = fullDate.split('/');
    if (parts.length === 3) dateCell.textContent = parts.slice(1).join('/');

    if (!accountCell.dataset.v0215Original) accountCell.dataset.v0215Original = accountCell.textContent.trim();
    const account = accountCell.dataset.v0215Original;
    const split = splitAccountV0215(account);
    accountCell.innerHTML = split.map(line => '<span>' + escapeV0215(line) + '</span>').join('');

    const kind = kindCell.textContent.includes('收入') ? 'income' : 'expense';
    row.classList.toggle('v0215-income', kind === 'income');
    row.classList.toggle('v0215-expense', kind === 'expense');

    if (!actionCell.querySelector('[data-v0215-row-menu]')) {
      const trigger = document.createElement('button');
      trigger.type = 'button';
      trigger.className = 'v0215-row-menu-trigger';
      trigger.dataset.v0215RowMenu = '1';
      trigger.setAttribute('aria-label', '交易操作');
      trigger.textContent = '⋯';
      actionCell.appendChild(trigger);
    }
  }
}

function restoreV0215Cell(cell) {
  if (!cell?.dataset.v0215Original) return;
  cell.textContent = cell.dataset.v0215Original;
  delete cell.dataset.v0215Original;
}

function splitAccountV0215(value) {
  const chars = Array.from(String(value || '').trim());
  if (chars.length <= 2) return [chars.join('')];
  const first = Math.floor(chars.length / 2);
  return [chars.slice(0, first).join(''), chars.slice(first).join('')];
}

function ensureV0215ActionPopover() {
  if (cyV0215ActionPopover?.isConnected) return cyV0215ActionPopover;
  const popover = document.createElement('div');
  popover.id = 'v0215ActionPopover';
  popover.className = 'v0215-action-popover';
  popover.hidden = true;
  document.body.appendChild(popover);
  cyV0215ActionPopover = popover;
  return popover;
}

function openV0215ActionPopover(anchor, row) {
  const popover = ensureV0215ActionPopover();
  const edit = row?.querySelector('[data-edit-id]');
  const del = row?.querySelector('[data-delete-id]');
  popover.replaceChildren();

  if (edit && !edit.disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = '編輯';
    button.addEventListener('click', () => {
      closeV0215ActionPopover();
      edit.click();
    }, { once: true });
    popover.appendChild(button);
  }

  if (del && !del.disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'danger';
    button.textContent = '刪除';
    button.addEventListener('click', () => {
      closeV0215ActionPopover();
      del.click();
    }, { once: true });
    popover.appendChild(button);
  }

  if (!popover.children.length) {
    const text = document.createElement('span');
    text.textContent = '此月份已鎖帳';
    popover.appendChild(text);
  }

  popover.hidden = false;
  positionV0215Popover(anchor, popover);
}

function closeV0215ActionPopover() {
  if (cyV0215ActionPopover) cyV0215ActionPopover.hidden = true;
}

function positionV0215Popover(anchor, popover) {
  const rect = anchor.getBoundingClientRect();
  const box = popover.getBoundingClientRect();
  const margin = 8;
  let left = rect.right - box.width;
  left = Math.max(margin, Math.min(left, window.innerWidth - box.width - margin));
  let top = rect.bottom + 6;
  if (top + box.height > window.innerHeight - margin) top = rect.top - box.height - 6;
  popover.style.left = Math.round(left) + 'px';
  popover.style.top = Math.max(margin, Math.round(top)) + 'px';
}

function escapeV0215(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}