window.addEventListener('load', () => {
  polishV091ConfirmationSidebar();
});

function polishV091ConfirmationSidebar() {
  const topbar = document.querySelector('.topbar');
  const edgeOpen = document.querySelector('#confirmationEdgeOpen');
  const collapse = document.querySelector('#confirmationDrawerCollapse');
  if (!edgeOpen || !collapse) return;

  const chevronLeft = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M13.5 7.5 9 12l4.5 4.5"></path>
      <path d="M18 7.5 13.5 12l4.5 4.5"></path>
    </svg>`;
  const chevronRight = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="m10.5 7.5 4.5 4.5-4.5 4.5"></path>
      <path d="m6 7.5 4.5 4.5L6 16.5"></path>
    </svg>`;

  const existingBadge = edgeOpen.querySelector('#confirmationEdgeCount');
  edgeOpen.innerHTML = chevronLeft;
  if (existingBadge) edgeOpen.append(existingBadge);
  collapse.innerHTML = chevronRight;

  edgeOpen.title = '展開輸入確認';
  collapse.title = '收合輸入確認';

  const syncBounds = () => {
    if (!topbar) return;
    const height = Math.ceil(topbar.getBoundingClientRect().height);
    document.documentElement.style.setProperty('--cy-confirmation-top', `${height}px`);
  };

  syncBounds();
  window.addEventListener('resize', syncBounds, { passive: true });
}
