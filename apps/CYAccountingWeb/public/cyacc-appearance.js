/* CY Web-inspired appearance preference. Presentation only; no identity ownership. */
(() => {
  const key = 'cyacc-ui-theme';
  const modes = ['system', 'light', 'dark'];
  function read() {
    try { const value = localStorage.getItem(key); return modes.includes(value) ? value : 'system'; }
    catch { return 'system'; }
  }
  function render() {
    const selected = read();
    document.documentElement.dataset.cyaccTheme = selected;
    document.querySelectorAll('[data-cyacc-theme-switch]').forEach(slot => {
      slot.replaceChildren();
      const group = document.createElement('div');
      group.className = 'cyacc-theme-switch';
      group.setAttribute('role', 'group');
      group.setAttribute('aria-label', '外觀');
      for (const [value, label] of [['system','系統'], ['light','淺色'], ['dark','深色']]) {
        const button = document.createElement('button');
        button.type = 'button'; button.textContent = label;
        button.dataset.theme = value;
        button.setAttribute('aria-pressed', String(selected === value));
        button.addEventListener('click', () => { try { localStorage.setItem(key, value); } catch {} render(); });
        group.append(button);
      }
      slot.append(group);
    });
  }
  document.documentElement.dataset.cyaccTheme = read();
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', render, { once: true });
  else render();
})();
