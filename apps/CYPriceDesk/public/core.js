export const money = value => value === null || value === undefined ? '—' : new Intl.NumberFormat('zh-TW', {style:'currency',currency:'TWD',maximumFractionDigits:2}).format(value);
export const localTime = value => new Intl.DateTimeFormat('sv-SE', {timeZone:'Asia/Taipei', year:'numeric', month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hourCycle:'h23'}).format(new Date(value)).replace(' ','T');
export const displayTime = value => value ? localTime(value).replace('T',' ') : '尚未查價';
export function validateLink(value) {
  if (!String(value ?? '').trim()) return '';
  try {
    const url = new URL(String(value).trim());
    if (!['https:','http:'].includes(url.protocol) || url.username || url.password) throw new Error();
    url.hash = '';
    return url.href;
  } catch { throw new Error('商品連結請填入有效的 http 或 https 網址'); }
}
