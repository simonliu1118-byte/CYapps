const CY_V0215_BUILD3_MOBILE = '(max-width: 767px)';
const CY_V0215_BUILD3_EDGE_GUARD = 24;
const CY_V0215_BUILD3_ACTION_WIDTH = 72;
const CY_V0215_BUILD3_OPEN_THRESHOLD = 34;

let cyV0215Build3OpenRow = null;
let cyV0215Build3Gesture = null;
let cyV0215Build3SuppressClickUntil = 0;

window.addEventListener('load', setupV0215Build3SwipeActions, { once: true });

function setupV0215Build3SwipeActions() {
  const rows = document.querySelector('#transactionRows');
  if (!rows || rows.dataset.v0215Build3SwipeBound === '1') return;
  rows.dataset.v0215Build3SwipeBound = '1';

  rows.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_V0215_BUILD3_MOBILE).matches) return;
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    if (event.target.closest('button, input, select, textarea, a')) return;

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (!row) return;

    if (event.clientX <= CY_V0215_BUILD3_EDGE_GUARD) {
      closeV0215Build3SwipeRow();
      return;
    }

    if (cyV0215Build3OpenRow && cyV0215Build3OpenRow !== row) {
      closeV0215Build3SwipeRow();
    }

    cyV0215Build3Gesture = {
      pointerId: event.pointerId,
      row,
      startX: event.clientX,
      startY: event.clientY,
      startOffset: readV0215Build3OpenOffset(row),
      horizontal: false,
      cancelled: false,
      moved: false
    };

    row.classList.remove('v0215-swipe-animate');
    row.setPointerCapture?.(event.pointerId);
  });

  rows.addEventListener('pointermove', event => {
    const gesture = cyV0215Build3Gesture;
    if (!gesture || gesture.pointerId !== event.pointerId || gesture.cancelled) return;

    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    const absX = Math.abs(dx);
    const absY = Math.abs(dy);

    if (!gesture.horizontal) {
      if (Math.max(absX, absY) < 7) return;
      if (absY > absX) {
        gesture.cancelled = true;
        return;
      }
      gesture.horizontal = true;
    }

    const row = gesture.row;
    let next = clampV0215Build3(
      gesture.startOffset + dx,
      -CY_V0215_BUILD3_ACTION_WIDTH,
      CY_V0215_BUILD3_ACTION_WIDTH
    );

    const edit = row.querySelector('[data-edit-id]');
    const remove = row.querySelector('[data-delete-id]');
    if (next > 0 && (!edit || edit.disabled)) next = 0;
    if (next < 0 && (!remove || remove.disabled)) next = 0;

    gesture.moved = gesture.moved || Math.abs(next - gesture.startOffset) > 7;
    setV0215Build3SwipeOffset(row, next);
    row.dataset.swipeDirection = next > 1 ? 'edit' : next < -1 ? 'delete' : '';
    event.preventDefault();
  });

  const finish = event => {
    const gesture = cyV0215Build3Gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    cyV0215Build3Gesture = null;

    const row = gesture.row;
    row.releasePointerCapture?.(event.pointerId);

    if (gesture.cancelled || !gesture.horizontal) {
      row.classList.add('v0215-swipe-animate');
      return;
    }

    const current = readV0215Build3Offset(row);
    if (gesture.moved) cyV0215Build3SuppressClickUntil = Date.now() + 260;

    if (current >= CY_V0215_BUILD3_OPEN_THRESHOLD) {
      openV0215Build3SwipeRow(row, 'edit');
    } else if (current <= -CY_V0215_BUILD3_OPEN_THRESHOLD) {
      openV0215Build3SwipeRow(row, 'delete');
    } else {
      closeV0215Build3SwipeRow(row);
    }
  };

  rows.addEventListener('pointerup', finish);
  rows.addEventListener('pointercancel', finish);

  rows.addEventListener('click', event => {
    const action = event.target.closest('[data-edit-id], [data-delete-id]');
    if (action) {
      const row = action.closest('tr.ledger-row');
      window.setTimeout(() => closeV0215Build3SwipeRow(row), 0);
      return;
    }

    if (Date.now() < cyV0215Build3SuppressClickUntil) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (row && row === cyV0215Build3OpenRow) {
      event.preventDefault();
      event.stopPropagation();
      closeV0215Build3SwipeRow(row);
    }
  }, true);

  document.addEventListener('pointerdown', event => {
    if (!cyV0215Build3OpenRow) return;
    if (cyV0215Build3OpenRow.contains(event.target)) return;
    closeV0215Build3SwipeRow();
  }, true);

  window.addEventListener('scroll', () => closeV0215Build3SwipeRow(), { passive: true });
}

function openV0215Build3SwipeRow(row, mode) {
  if (!row?.isConnected) return;
  if (cyV0215Build3OpenRow && cyV0215Build3OpenRow !== row) {
    closeV0215Build3SwipeRow(cyV0215Build3OpenRow);
  }

  const action = mode === 'edit'
    ? row.querySelector('[data-edit-id]')
    : row.querySelector('[data-delete-id]');
  if (!action || action.disabled) {
    closeV0215Build3SwipeRow(row);
    return;
  }

  row.classList.add('v0215-swipe-animate');
  row.dataset.swipeOpen = mode;
  row.dataset.swipeDirection = mode;
  setV0215Build3SwipeOffset(
    row,
    mode === 'edit' ? CY_V0215_BUILD3_ACTION_WIDTH : -CY_V0215_BUILD3_ACTION_WIDTH
  );
  cyV0215Build3OpenRow = row;
}

function closeV0215Build3SwipeRow(row = cyV0215Build3OpenRow) {
  if (!row) return;
  row.classList.add('v0215-swipe-animate');
  row.dataset.swipeOpen = '';
  row.dataset.swipeDirection = '';
  setV0215Build3SwipeOffset(row, 0);
  if (cyV0215Build3OpenRow === row) cyV0215Build3OpenRow = null;
}

function readV0215Build3OpenOffset(row) {
  if (row?.dataset.swipeOpen === 'edit') return CY_V0215_BUILD3_ACTION_WIDTH;
  if (row?.dataset.swipeOpen === 'delete') return -CY_V0215_BUILD3_ACTION_WIDTH;
  return 0;
}

function readV0215Build3Offset(row) {
  const value = Number(row?.style.getPropertyValue('--v0215-swipe-x').replace('px', ''));
  return Number.isFinite(value) ? value : 0;
}

function setV0215Build3SwipeOffset(row, value) {
  row?.style.setProperty('--v0215-swipe-x', value + 'px');
}

function clampV0215Build3(value, min, max) {
  return Math.max(min, Math.min(max, value));
}
