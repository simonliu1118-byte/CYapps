// Tracked products page: list, detail (own price history, same-spec comparison, other packages, pending / excluded
// listings), manual judgments and "check now". All data comes from /api/tracked (audited on the server).
import {displayTime, money} from './core.js';
import {sanitizeAttributes} from './catalog-core.js';

const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const SOURCE={momo:'momo',shopee:'飛比蝦皮',coupang:'飛比酷澎'};
const STATUS={ok:'完成',no_match:'無符合結果',no_price:'無價格',skipped:'未設定',rate_limited:'查詢間隔未到，1 小時後重試',blocked_cooldown:'冷卻中，1 小時後重試',blocked:'被限制，1 小時後重試',provider_refused:'被拒絕，1 小時後重試',network_error:'連線失敗，1 小時後重試'};
const JUDGMENT_PATH=id=>`/api/tracked/${id}/judgments`;

let products=[],filter='all',detail=null,pendingJudge=null,editing=null,busy=false,toastTimer;

async function api(method,path,body) {
  const res=await fetch(path,{method,headers:body===undefined?{}:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
  const data=await res.json().catch(()=>({}));
  if(res.status===401){location.href='/login';throw new Error(data.error||'尚未登入');}
  if(!res.ok){const error=new Error(data.error||'操作失敗，請稍後再試。');error.status=res.status;throw error;}
  return data;
}
function toast(message) {
  clearTimeout(toastTimer);$('toast').textContent=message;$('toast').classList.add('visible');
  toastTimer=setTimeout(()=>$('toast').classList.remove('visible'),4200);
}
const attrText=attrs=>Object.entries(attrs||{}).map(([k,v])=>`${esc(k)}：${esc(v)}`).join('、')||'—';
const attrLines=attrs=>Object.entries(attrs||{}).map(([k,v])=>`${k}=${v}`).join('\n');
const packText=p=>`${p.packageQuantity} ${esc(p.packageUnit)}`;
const when=value=>value?esc(displayTime(value)):'—';
const percent=(mine,base)=>mine===null||base===null||base===0?null:(mine-base)/base*100;
const percentText=value=>value===null?'—':`<span class="${value>0?'pos':value<0?'neg':''}">${value>0?'+':''}${value.toFixed(1)}%</span>`;
const stateChip=p=>p.active?'<span class="chip success">啟用</span>':'<span class="chip">停用</span>';

// ---- list ----
async function loadList() {
  try {products=(await api('GET','/api/tracked')).products;renderList();}
  catch(error){$('tr-list').innerHTML=`<div class="notice error">${esc(error.message)}</div>`;}
}
function renderList() {
  const q=$('tr-search').value.trim().toLocaleLowerCase();
  const rows=products.filter(p=>(!q||`${p.name} ${p.model}`.toLocaleLowerCase().includes(q))&&(filter==='all'||(filter==='pending'?p.pendingCount>0:!p.active)));
  if(!rows.length){
    $('tr-list').innerHTML=`<div class="empty"><strong>${products.length?'沒有符合條件的追蹤商品':'尚無追蹤商品'}</strong><p>${products.length?'換個搜尋詞或篩選條件。':'新增追蹤商品後，系統會自動分配時間定期查價。'}</p></div>`;
    return;
  }
  $('tr-list').innerHTML=`<div class="table-wrap"><table class="data-table"><thead><tr><th>商品</th><th>規格／包裝</th><th class="num">自己的售價</th><th class="num">同規格最低價</th><th class="num">同規格中位數</th><th class="num">售價相對最低價</th><th class="num">待確認</th><th>上次查價</th><th>下次查價</th><th>狀態</th></tr></thead><tbody>${rows.map(p=>`<tr class="clickable" data-id="${esc(p.id)}" tabindex="0">
    <td data-label="商品"><strong>${esc(p.name)}</strong><small>型號 ${esc(p.model)}</small></td>
    <td data-label="規格／包裝">${attrText(p.attributes)}<small>${packText(p)}</small></td>
    <td data-label="自己的售價" class="num">${p.myPrice===null?'—':money(p.myPrice)}</td>
    <td data-label="同規格最低價" class="num">${p.sameMin===null?'—':money(p.sameMin)}<small>${p.sameCount} 筆</small></td>
    <td data-label="同規格中位數" class="num">${p.sameMedian===null?'—':money(p.sameMedian)}</td>
    <td data-label="相對最低價" class="num">${percentText(percent(p.myPrice,p.sameMin))}</td>
    <td data-label="待確認" class="num">${p.pendingCount?`<span class="chip warning">${p.pendingCount}</span>`:'0'}</td>
    <td data-label="上次查價">${when(p.lastRunAt)}</td>
    <td data-label="下次查價">${p.active?when(p.nextRunAt):'—'}</td>
    <td data-label="狀態">${stateChip(p)}</td></tr>`).join('')}</tbody></table></div>`;
}
$('tr-search').addEventListener('input',renderList);
for(const b of document.querySelectorAll('[data-tr-filter]'))b.onclick=()=>{
  filter=b.dataset.trFilter;
  document.querySelectorAll('[data-tr-filter]').forEach(x=>x.classList.toggle('selected',x===b));renderList();
};
$('tr-list').addEventListener('click',e=>{const row=e.target.closest('tr[data-id]');if(row)openDetail(row.dataset.id);});
$('tr-list').addEventListener('keydown',e=>{if(e.key==='Enter'){const row=e.target.closest('tr[data-id]');if(row)openDetail(row.dataset.id);}});

// ---- detail ----
async function openDetail(id) {
  try {detail=await api('GET',`/api/tracked/${id}`);}
  catch(error){toast(error.message);return;}
  $('tracked-list-view').hidden=true;$('tracked-detail-view').hidden=false;renderDetail();window.scrollTo(0,0);
}
function backToList() {detail=null;$('tracked-detail-view').hidden=true;$('tracked-list-view').hidden=false;loadList();}
const listingRow=(c,actions)=>`<tr class="${c.group==='excluded'?'muted-row':''}">
  <td data-label="來源">${esc(SOURCE[c.source])}</td>
  <td data-label="商品"><a href="${esc(c.url)}" target="_blank" rel="noopener noreferrer">${esc(c.title)}</a><small>${esc(c.seller)}</small></td>
  <td data-label="價格" class="num">${c.price===null?'—':money(c.price)}</td>
  <td data-label="說明">${c.group==='other'?`${c.parsed.quantity} ${esc(c.parsed.unit)}`:''}${c.parsed.reason?` <span class="chip ${c.group==='pending'?'warning':'info'}">${esc(c.parsed.reason)}</span>`:''}</td>
  <td><div class="row-actions">${actions}</div></td></tr>`;
const judgeButtons=c=>[['same','同規格'],['other','其他包裝'],['not_same','不是同一商品']].filter(([k])=>!(k==='same'&&c.group==='same')&&!(k==='other'&&c.group==='other'))
  .map(([k,label])=>`<button type="button" class="btn small" data-judge="${k}" data-source="${esc(c.source)}" data-key="${esc(c.listingKey)}">${label}</button>`).join('');
const listingTable=(rows,actions)=>`<div class="table-wrap"><table class="data-table"><thead><tr><th>來源</th><th>商品／賣家</th><th class="num">價格</th><th>說明</th><th>操作</th></tr></thead><tbody>${rows.map(c=>listingRow(c,actions(c))).join('')}</tbody></table></div>`;

function renderDetail() {
  const {product:p,priceChanges,candidates,history,lastRun}=detail;
  const by=group=>candidates.filter(c=>c.group===group);
  const same=by('same'),other=by('other'),pending=by('pending'),excluded=by('excluded'),unrelated=by('unrelated').length;
  const samePriced=same.filter(c=>c.price!==null).sort((a,b)=>a.price-b.price);
  const lowest=samePriced[0];
  const bySource=['momo','shopee','coupang'].filter(s=>same.some(c=>c.source===s));
  const packages=new Map();
  for(const c of other.filter(c=>c.price!==null)){const k=`${c.parsed.quantity} ${c.parsed.unit}`;packages.set(k,[...(packages.get(k)||[]),c.price]);}
  $('tracked-detail-view').innerHTML=`
  <div class="detail-head"><div class="toolbar-group"><button class="back" id="tr-back" type="button">← 追蹤商品</button><h2>${esc(p.name)}</h2>${stateChip(p)}</div>
    <div class="toolbar-group"><button class="btn" id="tr-edit" type="button">編輯</button><button class="btn primary" id="tr-run" type="button">立即查價</button></div></div>
  <section class="panel"><div class="kv">
    <div><span>型號</span>${esc(p.model)}</div><div><span>規格屬性</span>${attrText(p.attributes)}</div><div><span>包裝</span>${packText(p)}</div>
    <div><span>自己的商品連結</span>${p.myUrl?`<a href="${esc(p.myUrl)}" target="_blank" rel="noopener noreferrer">開啟</a>`:'—'}</div>
    <div><span>momo 關鍵字</span>${esc(p.momoKeyword)||'—'}</div><div><span>飛比關鍵字</span>${esc(p.feebeeKeyword)||'—'}</div>
    <div><span>上次查價</span>${when(p.lastRunAt)}${lastRun?`<small class="muted run-status">momo：${esc(STATUS[lastRun.momoStatus]||lastRun.momoStatus)}；飛比：${esc(STATUS[lastRun.feebeeStatus]||lastRun.feebeeStatus)}</small>`:''}</div>
    <div><span>下次查價</span>${p.active?when(p.nextRunAt):'已停用'}</div></div></section>
  <section class="panel"><div class="panel-head"><h2>自己的售價 <small>${p.myPrice===null?'未設定':money(p.myPrice)}</small></h2><button class="btn" id="tr-price" type="button">修改售價</button></div>
    ${priceChanges.length?`<div class="table-wrap"><table class="data-table"><thead><tr><th>時間</th><th>操作者</th><th class="num">改前</th><th class="num">改後</th><th>備註</th></tr></thead><tbody>${priceChanges.map(c=>`<tr><td data-label="時間">${when(c.at)}</td><td data-label="操作者">${esc(c.actorName)}</td><td data-label="改前" class="num">${c.oldPrice===null?'—':money(c.oldPrice)}</td><td data-label="改後" class="num">${money(c.newPrice)}</td><td data-label="備註">${esc(c.note)||'—'}</td></tr>`).join('')}</tbody></table></div>`:'<p class="muted">尚無售價紀錄。</p>'}</section>
  <section class="panel"><div class="panel-head"><h2>同規格 <small>${same.length} 筆${lowest?` · 總最低價 ${money(lowest.price)}（${esc(SOURCE[lowest.source])}）`:''}${lowest&&p.myPrice!==null?` · 自己的售價相對最低價 ${percentText(percent(p.myPrice,lowest.price))}`:''}</small></h2></div>
    ${bySource.length?bySource.map(s=>`<h3 class="sub-title">${esc(SOURCE[s])}${s==='momo'?'':'（第三方商品級參考價，非即時或逐規格價格）'}</h3>${listingTable(same.filter(c=>c.source===s).sort((a,b)=>(a.price??Infinity)-(b.price??Infinity)),judgeButtons)}`).join(''):'<p class="muted">最近一次查價沒有同規格商品。</p>'}
    <h3 class="sub-title">歷次同規格價格</h3>
    ${history.length?`<div class="table-wrap"><table class="data-table"><thead><tr><th>查價時間</th><th class="num">筆數</th><th class="num">最低價</th><th class="num">中位數</th><th class="num">最高價</th></tr></thead><tbody>${history.map(h=>`<tr><td data-label="查價時間">${when(h.at)}</td><td data-label="筆數" class="num">${h.count}</td><td data-label="最低價" class="num">${money(h.min)}</td><td data-label="中位數" class="num">${money(h.median)}</td><td data-label="最高價" class="num">${money(h.max)}</td></tr>`).join('')}</tbody></table></div>`:'<p class="muted">尚無歷史資料。</p>'}</section>
  <section class="panel"><div class="panel-head"><h2>其他包裝 <small>${other.length} 筆，僅供參考，不與自己的售價比較</small></h2></div>
    ${other.length?`${packages.size?`<div class="table-wrap"><table class="data-table"><thead><tr><th>包裝</th><th class="num">筆數</th><th class="num">價格範圍</th></tr></thead><tbody>${[...packages].map(([k,list])=>`<tr><td data-label="包裝">${esc(k)}</td><td data-label="筆數" class="num">${list.length}</td><td data-label="價格範圍" class="num">${money(Math.min(...list))}${list.length>1?` – ${money(Math.max(...list))}`:''}</td></tr>`).join('')}</tbody></table></div>`:''}<h3 class="sub-title">明細</h3>${listingTable(other,judgeButtons)}`:'<p class="muted">最近一次查價沒有其他包裝。</p>'}</section>
  <section class="panel"><div class="panel-head"><h2>待確認 <small>${pending.length} 筆，程式無法判斷，請人工確認</small></h2></div>
    ${pending.length?listingTable(pending,judgeButtons):'<p class="muted">沒有待確認的商品。</p>'}
    ${unrelated?`<p class="footnote">另有 ${unrelated} 筆標題不含型號，已略過不顯示。</p>`:''}</section>
  <section class="panel"><div class="panel-head"><h2>已排除 <small>${excluded.length} 筆，之後查價不再顯示</small></h2></div>
    ${excluded.length?listingTable(excluded,c=>`<button type="button" class="btn small" data-restore="1" data-source="${esc(c.source)}" data-key="${esc(c.listingKey)}">恢復</button>`):'<p class="muted">沒有已排除的商品。</p>'}</section>`;
}
async function refreshDetail() {detail=await api('GET',`/api/tracked/${detail.product.id}`);renderDetail();}

async function judge(source,listingKey,verdict,extra={}) {
  const result=await api('PUT',JUDGMENT_PATH(detail.product.id),{source,listingKey,verdict,...extra});
  await refreshDetail();return result;
}
$('tracked-detail-view').addEventListener('click',async e=>{
  const button=e.target.closest('button');if(!button||busy)return;
  try {
    if(button.id==='tr-back')backToList();
    else if(button.id==='tr-edit')openEdit(detail.product);
    else if(button.id==='tr-price')openPrice();
    else if(button.id==='tr-run')await runNow(button);
    else if(button.dataset.restore){await api('DELETE',`${JUDGMENT_PATH(detail.product.id)}?source=${button.dataset.source}&listingKey=${encodeURIComponent(button.dataset.key)}`);await refreshDetail();toast('已恢復');}
    else if(button.dataset.judge==='other'){pendingJudge={source:button.dataset.source,key:button.dataset.key};$('package-form').reset();$('package-error').hidden=true;$('package-dialog').showModal();}
    else if(button.dataset.judge){await judge(button.dataset.source,button.dataset.key,button.dataset.judge);toast('已記錄判斷，之後查價自動套用');}
  } catch(error){toast(error.message);}
});
async function runNow(button) {
  busy=true;button.disabled=true;button.textContent='查價中…';
  try {
    const r=await api('POST',`/api/tracked/${detail.product.id}/run`);
    await refreshDetail();
    toast(`查價完成。momo：${STATUS[r.momoStatus]||r.momoStatus}；飛比：${STATUS[r.feebeeStatus]||r.feebeeStatus}`);
  } finally {busy=false;if($('tr-run')){$('tr-run').disabled=false;$('tr-run').textContent='立即查價';}}
}
$('package-form').addEventListener('submit',async e=>{
  e.preventDefault();$('package-error').hidden=true;const f=e.target.elements;
  try {await judge(pendingJudge.source,pendingJudge.key,'other_package',{packageQuantity:Number(f.quantity.value),packageUnit:f.unit.value});$('package-dialog').close();toast('已記錄判斷，之後查價自動套用');}
  catch(error){$('package-error').hidden=false;$('package-error').textContent=error.message;}
});

// ---- add / edit ----
function parseAttributes(text) {
  const attrs={};
  for(const line of text.split(/\r?\n/).slice(0,12)) {
    if(!line.trim())continue;
    const at=line.indexOf('=');if(at<1||!line.slice(at+1).trim())throw new Error('規格屬性請使用「名稱=內容」，每行一項');
    attrs[line.slice(0,at).trim()]=line.slice(at+1).trim();
  }
  return sanitizeAttributes(attrs);
}
function openEdit(p) {
  editing=p;const f=$('tracked-form');f.reset();$('tracked-error').hidden=true;
  $('tracked-title').textContent=p?'編輯追蹤商品':'新增追蹤商品';$('tr-price-field').hidden=Boolean(p);
  if(p){const e=f.elements;e.name.value=p.name;e.model.value=p.model;e.attributes.value=attrLines(p.attributes);e.packageQuantity.value=p.packageQuantity;e.packageUnit.value=p.packageUnit;e.myUrl.value=p.myUrl;e.momoKeyword.value=p.momoKeyword;e.feebeeKeyword.value=p.feebeeKeyword;e.active.checked=p.active;}
  $('tracked-dialog').showModal();
}
$('tr-add').onclick=()=>openEdit(null);
$('tracked-form').addEventListener('submit',async e=>{
  e.preventDefault();$('tracked-error').hidden=true;const f=e.target.elements;
  try {
    const body={name:f.name.value,model:f.model.value,attributes:parseAttributes(f.attributes.value),packageQuantity:Number(f.packageQuantity.value),packageUnit:f.packageUnit.value,myUrl:f.myUrl.value,momoKeyword:f.momoKeyword.value,feebeeKeyword:f.feebeeKeyword.value,active:f.active.checked};
    if(editing){await api('PUT',`/api/tracked/${editing.id}`,{...body,revision:editing.revision});await refreshDetail();}
    else{body.myPrice=f.myPrice.value;await api('POST','/api/tracked',body);await loadList();}
    $('tracked-dialog').close();toast(editing?'已更新':'已新增，系統會自動安排查價時間');
  } catch(error){
    $('tracked-error').hidden=false;$('tracked-error').textContent=error.message;
    if(error.status===409&&detail)await refreshDetail().catch(()=>{});
  }
});

// ---- own selling price ----
function openPrice() {$('price-form').reset();$('price-error').hidden=true;$('price-form').elements.price.value=detail.product.myPrice??'';$('price-dialog').showModal();}
$('price-form').addEventListener('submit',async e=>{
  e.preventDefault();$('price-error').hidden=true;const f=e.target.elements;
  try {await api('PUT',`/api/tracked/${detail.product.id}/price`,{price:f.price.value,note:f.note.value,revision:detail.product.revision});await refreshDetail();$('price-dialog').close();toast('售價已更新');}
  catch(error){$('price-error').hidden=false;$('price-error').textContent=error.message;if(error.status===409)await refreshDetail().catch(()=>{});}
});

window.addEventListener('hashchange',()=>{if(location.hash==='#tracked'&&!detail)loadList();});
if(location.hash==='#tracked')loadList();
