/* V0.21.1 keyboard bridge for the Desktop custom date trigger. */

const CY_V0211_KEYBOARD_DESKTOP = '(min-width: 1024px)';

const setupV0211KeyboardBridge = () => {
  if (!window.matchMedia(CY_V0211_KEYBOARD_DESKTOP).matches) return;
  const input = document.querySelector('#txDate');
  const root = input?.nextElementSibling?.classList?.contains('v0211-date-picker') ? input.nextElementSibling : null;
  const trigger = root?.querySelector('.v0211-date-trigger');
  if (!input || !trigger || trigger.dataset.v0211KeyboardBound === '1') return;
  trigger.dataset.v0211KeyboardBound = '1';

  trigger.addEventListener('keydown', event => {
    if (event.isComposing) return;

    if (event.key === 'Enter' && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      event.stopPropagation();
      document.querySelector('#summary')?.focus();
      return;
    }

    if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      event.stopPropagation();
      if (typeof state === 'object' && typeof setEntryKind === 'function') {
        setEntryKind(state.kind === 'expense' ? 'income' : 'expense');
        if (typeof updateEntryKindVisual === 'function') updateEntryKindVisual();
        if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
        if (typeof loadFrequentSummaries === 'function') loadFrequentSummaries();
      }
      trigger.focus();
      return;
    }

    const quickDigit = /^\d$/.test(event.key) && !event.ctrlKey && !event.altKey && !event.metaKey;
    const quickStep = event.ctrlKey && !event.altKey && !event.metaKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown');
    if (!quickDigit && !quickStep) return;

    event.preventDefault();
    event.stopPropagation();
    input.dispatchEvent(new KeyboardEvent('keydown', {
      key: event.key,
      code: event.code,
      ctrlKey: event.ctrlKey,
      altKey: event.altKey,
      metaKey: event.metaKey,
      shiftKey: event.shiftKey,
      bubbles: true,
      cancelable: true
    }));
  });
};

if (document.readyState === 'complete') setTimeout(setupV0211KeyboardBridge, 0);
else window.addEventListener('load', () => setTimeout(setupV0211KeyboardBridge, 0), { once: true });
setTimeout(setupV0211KeyboardBridge, 350);
