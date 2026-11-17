// App shell: hash-based page switching (a hash like #tracked/<id> belongs to the #tracked page), page title,
// phone navigation drawer and the account menu.
const $ = id => document.getElementById(id);
const PAGES = ['search', 'tracked', 'pending', 'audit'];

function showPage() {
  const current = PAGES.find(id => location.hash.split('/')[0] === `#${id}`) || 'search';
  for (const id of PAGES) $(id).hidden = id !== current;
  for (const a of document.querySelectorAll('.nav-page')) {
    const on = a.getAttribute('href') === `#${current}`;
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

const setMenu = open => { $('account-popover').hidden = !open; $('account-trigger').setAttribute('aria-expanded', String(open)); };
$('account-trigger').onclick = () => setMenu($('account-popover').hidden);
document.addEventListener('click', e => { if (!$('account').contains(e.target)) setMenu(false); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') setMenu(false); });
showPage();
