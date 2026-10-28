/*
 * CYACC appearance preference: use CY Web's exact mode names, cookie semantics
 * and segmented interaction. This module owns appearance only, never identity.
 */
(() => {
  const cookieKey = 'cyweb-ui-theme';
  const options = [
    { value: 'system', label: '系統' },
    { value: 'light', label: '淺色' },
    { value: 'dark', label: '深色' }
  ];
  const systemDark = window.matchMedia('(prefers-color-scheme: dark)');
  function readPreference() {
    try {
      const entry = document.cookie.split('; ').find(item => item.startsWith(cookieKey + '='));
      const value = entry?.slice(cookieKey.length + 1);
      return value === 'light' || value === 'dark' ? value : 'system';
    } catch { return 'system'; }
  }
  let preference = readPreference();

  function applyPreference() {
    const root = document.documentElement;
    if (preference === 'system') delete root.dataset.cyTheme;
    else root.dataset.cyTheme = preference;
    root.toggleAttribute('data-cy-dark', preference === 'dark' || (preference === 'system' && systemDark.matches));
  }
  function setPreference(value) {
    if (!options.some(option => option.value === value)) return;
    preference = value;
    try {
      const secure = location.protocol === 'https:' ? '; Secure' : '';
      document.cookie = value === 'system'
        ? cookieKey + '=; Path=/; Max-Age=0; SameSite=Lax' + secure
        : cookieKey + '=' + value + '; Path=/; Max-Age=31536000; SameSite=Lax' + secure;
    } catch { /* Theme still works when cookies are disabled. */ }
    applyPreference();
    renderControls();
  }

  const icons = {
    system: '<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>',
    light: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42m11.3 11.3 1.42 1.42M2 12h2m16 0h2M4.93 19.07l1.42-1.42m11.3-11.3 1.42-1.42"/>',
    dark: '<path d="M20.9 13a9 9 0 0 1-9.9-9.9A9 9 0 1 0 20.9 13Z"/>'
  };
  function renderControls() {
    document.querySelectorAll('[data-cyacc-theme-switch]').forEach(slot => {
      slot.replaceChildren();
      const group = document.createElement('div');
      group.className = 'cy-segmented compact';
      group.setAttribute('role', 'group');
      group.setAttribute('aria-label', '外觀');
      for (const option of options) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = option.label;
        button.setAttribute('aria-pressed', String(preference === option.value));
        button.addEventListener('click', () => setPreference(option.value));
        group.append(button);
      }
      slot.append(group);
    });
    document.querySelectorAll('[data-cyacc-theme-toggle]').forEach(button => {
      const index = options.findIndex(option => option.value === preference);
      const next = options[(index + 1) % options.length];
      const label = '外觀：' + options[index].label + '（按一下改為' + next.label + '）';
      button.setAttribute('aria-label', label);
      button.title = label;
      button.innerHTML = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + icons[preference] + '</svg>';
      button.onclick = () => setPreference(next.value);
    });
  }
  applyPreference();
  systemDark.addEventListener?.('change', applyPreference);
  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', renderControls, { once: true });
  else renderControls();
})();
