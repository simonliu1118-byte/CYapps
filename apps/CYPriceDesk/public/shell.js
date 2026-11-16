// App shell: hash-based page switching, page title, phone navigation drawer and the account menu.
const $ = id => document.getElementById(id);
const PAGES = {'#overview': 'overview', '#tracked': 'tracked', '#auto-query': 'auto-query'};

function showPage() {
  const current = PAGES[location.hash] || 'overview';
  for (const id of Object.values(PAGES)) $(id).hidden = id !== current;
  for (const a of document.querySelectorAll('.nav-page')) {
    const on = PAGES[a.getAttribute('href')] === current;
    a.classList.toggle('active', on);
    if (on) $('page-title').textContent = a.dataset.title;
  }
  closeNav();
  window.scrollTo(0, 0);
}
function closeNav() { $('nav').classList.remove('open'); $('nav-backdrop').classList.remove('open'); }
window.addEventListener('hashchange', showPage);
$('menu-toggle').onclick = () => { $('nav').classList.add('open'); $('nav-backdrop').classList.add('open'); };
$('nav-backdrop').onclick = closeNav;
$('sidebar-audit').addEventListener('click', closeNav);

const setMenu = open => { $('account-popover').hidden = !open; $('account-trigger').setAttribute('aria-expanded', String(open)); };
$('account-trigger').onclick = () => setMenu($('account-popover').hidden);
document.addEventListener('click', e => { if (!$('account').contains(e.target)) setMenu(false); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') setMenu(false); });
showPage();
