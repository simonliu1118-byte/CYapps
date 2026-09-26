const CY_V21_VERSION = 'V0.21.0';

window.addEventListener('load', () => {
  syncV21Version();
  updateV21KeyboardHint();
});

function syncV21Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_VERSION;
}

function updateV21KeyboardHint() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
}
