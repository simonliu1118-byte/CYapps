// Small helpers shared by the page modules: element lookup, escaping, API calls, toast, dialogs, platform tag, nav badge.
import {PLATFORM_COLOR, PLATFORM_LABEL} from './compare.js';

export const $ = id => document.getElementById(id);
export const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));

export async function api(method, path, body) {
  const res = await fetch(path, {method, headers: body === undefined ? {} : {'content-type': 'application/json'}, body: body === undefined ? undefined : JSON.stringify(body)});
  const data = await res.json().catch(() => ({}));
  if (res.status === 401) { location.href = '/'; throw new Error(data.error || '尚未登入'); }
  if (!res.ok) { const error = new Error(data.error || '操作失敗，請稍後再試。'); error.status = res.status; throw error; }
  return data;
}

let toastTimer;
export function toast(message) {
  clearTimeout(toastTimer);
  $('toast').textContent = message;
  $('toast').classList.add('visible');
  toastTimer = setTimeout(() => $('toast').classList.remove('visible'), 4200);
}

export const tag = platform => `<span class="src-tag" style="--c:${PLATFORM_COLOR[platform] || '#8a8ea2'}">${esc(PLATFORM_LABEL[platform] || platform)}</span>`;

export function setBadge(count) {
  $('pending-badge').hidden = !count;
  $('pending-badge').textContent = count > 99 ? '99+' : String(count);
}

export function showError(id, error) { $(id).hidden = false; $(id).textContent = error.message; }

// Dialog chrome shared by every dialog: × / cancel buttons (data-close).
for (const button of document.querySelectorAll('[data-close]')) button.addEventListener('click', () => $(button.dataset.close).close());
