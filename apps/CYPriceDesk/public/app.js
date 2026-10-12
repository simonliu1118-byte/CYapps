import {groupsOf,groupKey,compareOffers,money,displayTime,localTime,validateOffer,inferPlatform,previewImport,upsertOffers,exportCSV} from './core.js';

const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const icon=(name)=>`<svg viewBox="0 0 24 24" aria-hidden="true">${({box:'<path d="m3 7 9-4 9 4v10l-9 4-9-4V7Zm0 0 9 4 9-4M12 11v10M7 5l10 5"/>',tag:'<path d="M3 3h8l10 10-8 8L3 11V3Z"/><circle cx="7.5" cy="7.5" r="1"/>',layers:'<path d="m12 3 10 5-10 5L2 8l10-5Zm-10 9 10 5 10-5M2 16l10 5 10-5"/>',clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',edit:'<path d="m16 3 5 5-12 12H4v-5L16 3Zm-2 2 5 5"/>',trash:'<path d="M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7"/>',link:'<path d="M14 3h7v7m0-7L10 14M10 3H3v18h18v-7"/>',bars:'<path d="M4 21h16M7 17V9m5 8V3m5 14v-5"/>'})[name]||''}</svg>`;
const STORAGE_KEY='price-desk:v1';
let offers=[],demoMode=false,demoOffers=[],basis='price',editingId=null,deletingId=null,importData=null,storageBlocked=false,toastTimer,lastStoredRaw=null;
try {
  const raw=localStorage.getItem(STORAGE_KEY);lastStoredRaw=raw;
  if(raw) {
    const saved=JSON.parse(raw);
    if(saved.version!==1 || !Array.isArray(saved.offers)) throw new Error('不支援的資料格式');
    offers=saved.offers.map(o=> {
      if(typeof o.id!=='string' || !o.id) throw new Error('資料識別碼有誤');
      return {...validateOffer(o),id:o.id};
    });
    if(new Set(offers.map(o=>o.id)).size!==offers.length) throw new Error('資料識別碼重複');
  }
} catch(e) {
  storageBlocked=true;
  $('storage-warning').hidden=false;
  $('storage-warning').textContent='此瀏覽器的資料目前無法讀取或儲存。請先匯出可讀資料，或改用允許本機儲存的瀏覽器；原始資料不會被覆寫。';
}
const currentOffers=()=>demoMode?demoOffers:offers;
function toast(message) {
  clearTimeout(toastTimer);$('toast').textContent=message;$('toast').classList.add('visible');
  toastTimer=setTimeout(()=>$('toast').classList.remove('visible'),4200);
}
function persist(next) {
  if(demoMode){demoOffers=next;render();return true;}
  if(storageBlocked){toast('此瀏覽器目前無法儲存，請更換瀏覽器或啟用本機儲存。');return false;}
  try {
    if(localStorage.getItem(STORAGE_KEY)!==lastStoredRaw){toast('另一個分頁已更新資料，請重新整理，避免覆蓋較新的紀錄。');return false;}
    const raw=JSON.stringify({version:1,offers:next});localStorage.setItem(STORAGE_KEY,raw);lastStoredRaw=raw;offers=next;
    $('save-status').textContent='已儲存於此瀏覽器';render();return true;
  } catch(e) {toast('儲存失敗，可能已達瀏覽器容量限制。本次變更尚未儲存。');return false;}
}
function sampleOffers() {
  const ago=m=>new Date(Date.now()-m*60000).toISOString();
  return [
    {product:'無線降噪耳機',spec:'A1／黑色／公司貨',platform:'蝦皮',seller:'範例品牌商城',price:2490,shipping:0,checkedAt:ago(30),note:'示意資料，非即時行情'},
    {product:'無線降噪耳機',spec:'A1／黑色／公司貨',platform:'momo',seller:'範例購物中心',price:2690,shipping:0,checkedAt:ago(45),note:'示意資料，非即時行情'},
    {product:'無線降噪耳機',spec:'A1／黑色／公司貨',platform:'PChome',seller:'範例 24h 商城',price:2790,shipping:null,checkedAt:ago(90),note:'示意資料，運費待確認'},
    {product:'USB-C 快充充電器',spec:'65W／單入',platform:'蝦皮',seller:'範例配件館',price:690,shipping:60,checkedAt:ago(80),note:'示意資料，非即時行情'},
    {product:'USB-C 快充充電器',spec:'65W／單入',platform:'momo',seller:'範例購物中心',price:729,shipping:0,checkedAt:ago(110),note:'示意資料，非即時行情'},
    {product:'桌上型空氣清淨機',spec:'P2／白色／單入',platform:'momo',seller:'範例家電館',price:3290,shipping:0,checkedAt:ago(60),note:'示意資料，非即時行情'},
    {product:'桌上型空氣清淨機',spec:'P2／白色／單入',platform:'蝦皮',seller:'範例家電商城',price:3490,shipping:0,checkedAt:ago(120),note:'示意資料，非即時行情'},
  ].map((x,i)=>({...x,url:'',id:`demo-${i}`}));
}
function renderStats(data) {
  const groups=groupsOf(data),priced=data.filter(o=>o.price!==null);
  const platforms=new Set(data.map(x=>x.platform));
  const latest=priced.length?new Date(Math.max(...priced.map(o=>Date.parse(o.checkedAt)))):null;
  const summary=[
    ['指定商品',groups.length,'依商品名稱與規格分組','box'],
    ['有效報價',priced.length,`${data.filter(o=>o.price===null).length} 筆待查價 · 共 ${data.length} 筆紀錄`,'tag'],
    ['比較平台',platforms.size,'蝦皮、momo 與其他通路','layers'],
    ['最近查價',latest?localTime(latest).slice(11):'—',latest?`${localTime(latest).slice(0,10)} · 臺北時間`:'新增價格後顯示查價時間','clock']
  ];
  $('stats').innerHTML=summary.map(([title,val,sub,ico],i)=>`<div class="stat-card ${i===0?'featured':''}"><div class="stat-top">${title}<span class="stat-icon">${icon(ico)}</span></div><div class="stat-value ${i===3?'time-value':''}">${val}</div><div class="stat-sub">${sub}</div></div>`).join('');
}
function renderRow(row,max) {
  const markClass=row.platform==='蝦皮'?'shopee':row.platform.toLowerCase()==='momo'?'momo':row.platform==='PChome'?'pchome':'';
  const mark=row.platform==='蝦皮'?'蝦':row.platform.toLowerCase()==='momo'?'m':row.platform==='PChome'?'P':row.platform.slice(0,1);
  const time=row.checkedAt?localTime(row.checkedAt):null;
  const old=row.checkedAt && Date.now()-Date.parse(row.checkedAt)>7*86400000;
  const width=row.value===null?0:max===0?100:Math.max(4,Math.round(row.value/max*100));
  return `<tr class="${row.isLowest?'lowest':''}" data-offer-id="${esc(row.id)}">
    <td><div class="platform-cell"><span class="platform-mark ${markClass}">${esc(mark)}</span><div><span class="platform-name">${esc(row.platform)}</span><span class="seller">${esc(row.seller||'未填賣家')}</span></div></div>${row.note?`<p class="row-note">${esc(row.note)}</p>`:''}</td>
    <td>${row.value===null?`<span class="pending">${row.price===null?'待查價':'運費未知，未列入比較'}</span>`:`<div class="amount-line"><strong class="price-amount">${money(row.value)}</strong>${row.isLowest?'<span class="lowest-tag">最低價</span>':''}</div><div class="price-track" aria-hidden="true"><i style="width:${width}%"></i></div>`}</td>
    <td>${row.diff===null?'—':row.diff===0?'<span class="same-price">基準價格</span>':`<span class="diff-number">+${money(row.diff)}</span>${row.percent===null?'':`<span class="percent">+${row.percent.toFixed(1)}%</span>`}`}</td>
    <td><span class="shipping-text">${row.shipping===null?'待確認':row.shipping===0?'免運':money(row.shipping)}</span></td>
    <td><div class="time-label">${time?`<span class="time-date">${time.slice(0,10)}</span>${time.slice(11)}`:'尚未查價'}${old?'<span class="stale">超過 7 天，建議更新</span>':''}</div></td>
    <td><div class="row-actions">${row.url?`<a href="${esc(row.url)}" target="_blank" rel="noopener noreferrer" aria-label="開啟 ${esc(row.product)} 的 ${esc(row.platform)} 商品連結" title="開啟商品連結">${icon('link')}</a>`:''}<button data-edit="${esc(row.id)}" aria-label="編輯 ${esc(row.product)} 的 ${esc(row.platform)} 查價" title="編輯查價">${icon('edit')}</button><button data-delete="${esc(row.id)}" aria-label="刪除 ${esc(row.product)} 的 ${esc(row.platform)} 查價" title="刪除查價">${icon('trash')}</button></div></td>
  </tr>`;
}
function render() {
  const data=currentOffers();renderStats(data);
  $('demo-banner').hidden=!demoMode;$('demo-btn').hidden=demoMode;$('export-btn').disabled=demoMode||offers.length===0;
  const selected=$('platform-filter').value;
  $('platform-filter').innerHTML='<option value="">所有平台</option>'+[...new Set(data.map(o=>o.platform))].sort().map(p=>`<option value="${esc(p)}">${esc(p)}</option>`).join('');
  $('platform-filter').value=[...new Set(data.map(o=>o.platform))].includes(selected)?selected:'';
  $('product-suggestions').innerHTML=[...new Set(offers.map(x=>x.product))].map(x=>`<option value="${esc(x)}"></option>`).join('');
  const query=$('search').value.toLocaleLowerCase().trim(),platform=$('platform-filter').value;
  const groups=groupsOf(data).filter(g=>(!query||[g.product,g.spec,...g.offers.flatMap(o=>[o.platform,o.seller,o.note])].some(s=>s.toLocaleLowerCase().includes(query)))&&(!platform||g.offers.some(o=>o.platform===platform)));
  $('group-count').textContent=groups.length;
  if(!groups.length) {
    $('groups').innerHTML=`<div class="empty"><div class="empty-art">${icon('bars')}</div><h3>${data.length?'沒有符合條件的商品':'你的第一筆比價，從這裡開始'}</h3><p>${data.length?'試著換個搜尋詞，或顯示所有平台。':'輸入商品連結與價格，把蝦皮、momo 等平台放在一起比較。最低價與價差會自動幫你算好。'}</p><button class="button ${data.length?'secondary':'primary'}" id="empty-action">${data.length?'清除篩選':'＋ 新增第一筆查價'}</button>${data.length?'':'<button class="button ghost" id="empty-demo">先看看範例 →</button>'}</div>`;
    $('empty-action').onclick=()=>data.length?clearFilters():openOffer();
    if($('empty-demo')) $('empty-demo').onclick=loadDemo;
    return;
  }
  $('groups').innerHTML=groups.map(g=> {
    const comparison=compareOffers(g.offers,basis);
    const visibleRows=comparison.rows.filter(o=>!platform||o.platform===platform);
    const pending=g.offers.filter(o=>o.price===null).length;
    const unknownShipping=basis==='total'?g.offers.filter(o=>o.price!==null&&o.shipping===null).length:0;
    return `<article class="group"><div class="group-heading"><div class="product-header"><div class="product-icon">${icon('box')}</div><div><h3>${esc(g.product)}</h3><div class="product-meta"><span class="spec-tag">${esc(g.spec||'未指定規格')}</span><span>${g.offers.length} 筆紀錄 · ${new Set(g.offers.map(o=>o.platform)).size} 個平台</span></div></div></div><div class="summary-values"><div><span>${basis==='total'?'最低含運總價':'最低商品價格'}</span><strong>${money(comparison.min)}</strong></div><div class="spread"><span>最高與最低價差</span><strong>${money(comparison.gap)}</strong></div></div></div>
      <table class="offer-table"><thead><tr><th scope="col">平台／賣家</th><th scope="col">${basis==='total'?'含運總價':'商品價格'}</th><th scope="col">與最低價差</th><th scope="col">運費</th><th scope="col">查價時間</th><th scope="col">操作</th></tr></thead><tbody>${visibleRows.map(row=>renderRow(row,comparison.max)).join('')}</tbody></table>
      <p class="group-footnote">${!g.spec?'未指定規格，請確認型號、容量與組合相同。 ':''}${platform?'最低價與價差仍以此商品全部平台為基準。 ':''}${pending?`${pending} 筆待查價未列入比較。 `:''}${unknownShipping?`${unknownShipping} 筆運費未知，未列入含運比較。 `:''}${basis==='price'?'以記錄的商品價格比較，不含運費。':'含運總價＝商品價格＋運費。'} 優惠券與會員條件請記在備註；價格以查價時間為準。</p>
    </article>`;
  }).join('');
  $('groups').querySelectorAll('[data-edit]').forEach(b=>b.onclick=()=>openOffer(b.dataset.edit));
  $('groups').querySelectorAll('[data-delete]').forEach(b=>b.onclick=()=>openDelete(b.dataset.delete));
}
function clearFilters(){ $('search').value='';$('platform-filter').value='';render(); }
function loadDemo(){demoMode=true;demoOffers=sampleOffers();clearFilters();$('save-status').textContent='範例操作不會寫入你的資料';}
function exitDemo(){demoMode=false;clearFilters();$('save-status').textContent='資料儲存在此瀏覽器';}
function openOffer(id=null) {
  if(!id && demoMode) exitDemo();
  editingId=id;$('offer-form').reset();$('offer-error').hidden=true;
  $('offer-dialog-title').textContent=id?'編輯查價':'新增查價';
  if(id) {
    const record=currentOffers().find(x=>x.id===id);
    for(const [k,value] of Object.entries(record)) if($('offer-form').elements[k]) $('offer-form').elements[k].value=k==='checkedAt'?(value?localTime(value):localTime(Date.now())):value??'';
  } else $('time-input').value=localTime(Date.now());
  $('offer-dialog').showModal();
}
$('offer-form').addEventListener('submit',e=> {
  e.preventDefault();$('offer-error').hidden=true;
  try {
    const offer=validateOffer(Object.fromEntries(new FormData(e.target)));
    let next,notification;
    if(editingId) {
      const duplicate=currentOffers().find(x=>x.id!==editingId && groupKey(x)===groupKey(offer) && offer.url && x.url===offer.url);
      if(duplicate) throw new Error('此商品規格已有相同連結，請改為編輯那筆查價。');
      next=currentOffers().map(o=>o.id===editingId?{...offer,id:editingId}:o);notification='查價已更新';
    } else {
      const result=upsertOffers(currentOffers(),[offer]);
      if(result.skipped) throw new Error('此連結已有較新的查價時間，請更新時間或編輯原紀錄。');
      next=result.offers;notification=result.updated?'相同連結的查價已更新':'查價已新增';
    }
    if(persist(next)) {$('offer-dialog').close();toast(demoMode?`${notification}（範例操作）`:notification);}
  } catch(error){$('offer-error').hidden=false;$('offer-error').textContent=error.message;}
});
$('url-input').addEventListener('input',()=> { const found=inferPlatform($('url-input').value);if(found)$('platform-input').value=found; });
$('price-input').addEventListener('input',()=> {if(!$('time-input').value)$('time-input').value=localTime(Date.now());});
$('set-now').onclick=()=>{$('time-input').value=localTime(Date.now());};
function openDelete(id){deletingId=id;const record=currentOffers().find(x=>x.id===id);$('delete-description').textContent=`${record.product} · ${record.platform}${record.seller?` · ${record.seller}`:''}。刪除後會立即更新比價結果。`;$('delete-dialog').showModal();}
$('confirm-delete').onclick=()=> {if(persist(currentOffers().filter(x=>x.id!==deletingId))){$('delete-dialog').close();toast('查價已刪除');}};
function openImport(){if(demoMode)exitDemo();$('paste-input').value='';$('csv-file').value='';resetImport();$('import-dialog').showModal();}
function resetImport(){importData=null;$('confirm-import').disabled=true;$('import-preview').innerHTML='';$('import-error').hidden=true;}
$('paste-input').addEventListener('input',resetImport);
$('sample-csv').onclick=()=> {
  $('paste-input').value=`商品,規格,平台,價格,運費,查價時間,連結,賣家,備註\n範例商品,同型號單入,蝦皮,990,60,${localTime(Date.now()).replace('T',' ')},,範例賣家,示意資料請替換\n範例商品,同型號單入,momo,1090,0,${localTime(Date.now()).replace('T',' ')},,範例通路,示意資料請替換`;
  resetImport();
};
$('csv-file').onchange=async e=> {
  const file=e.target.files[0];if(!file)return;
  resetImport();
  try{if(file.size>1000000)throw new Error('CSV 檔案請控制在 1 MB 以內');$('paste-input').value=await file.text();toast('檔案已讀取，請預覽資料');}
  catch(error){$('import-error').hidden=false;$('import-error').textContent=error.message;}
};
$('preview-btn').onclick=()=> {
  resetImport();
  try {
    const result=previewImport($('paste-input').value);
    if(result.errors.length) throw new Error(result.errors.slice(0,8).join('\n')+(result.errors.length>8?`\n另有 ${result.errors.length-8} 列錯誤`:''));
    const preview=upsertOffers(offers,result.offers);
    importData=result.offers;
    $('import-preview').innerHTML=`<div class="import-success"><strong>已驗證 ${result.offers.length} 筆資料</strong><br>預計新增 ${preview.added} 筆、更新 ${preview.updated} 筆、略過 ${preview.skipped} 筆舊資料。<ul class="preview-list">${result.offers.slice(0,4).map(o=>`<li>${esc(o.product)}${o.spec?`／${esc(o.spec)}`:''} · ${esc(o.platform)} · ${o.price===null?'待查價':money(o.price)}</li>`).join('')}</ul>${result.offers.length>4?`另有 ${result.offers.length-4} 筆。`:''}</div>`;
    $('confirm-import').disabled=false;
  }catch(error){$('import-error').hidden=false;$('import-error').textContent=error.message;}
};
$('confirm-import').onclick=()=> {
  if(!importData)return;
  const result=upsertOffers(offers,importData);
  if(persist(result.offers)){$('import-dialog').close();toast(`已新增 ${result.added} 筆、更新 ${result.updated} 筆${result.skipped?`；略過 ${result.skipped} 筆舊資料`:''}`);}
};
$('export-btn').onclick=()=> {
  if(demoMode||!offers.length)return;
  const blob=new Blob([exportCSV(offers)],{type:'text/csv;charset=utf-8;'}),url=URL.createObjectURL(blob);
  const a=document.createElement('a');a.href=url;a.download=`價格觀測室-${localTime(Date.now()).slice(0,10)}.csv`;a.click();setTimeout(()=>URL.revokeObjectURL(url),10000);toast('已匯出查價資料');
};
for(const b of document.querySelectorAll('[data-close]'))b.onclick=()=>$(b.dataset.close).close();
for(const b of document.querySelectorAll('[data-basis]'))b.onclick=()=>{basis=b.dataset.basis;document.querySelectorAll('[data-basis]').forEach(x=>{const on=x.dataset.basis===basis;x.classList.toggle('selected',on);x.setAttribute('aria-pressed',String(on));});render();};
$('search').addEventListener('input',render);$('platform-filter').addEventListener('change',render);
$('add-btn').onclick=()=>openOffer();$('import-btn').onclick=openImport;$('sidebar-import').onclick=openImport;$('demo-btn').onclick=loadDemo;$('exit-demo').onclick=exitDemo;
window.addEventListener('storage',e=>{if(e.key===STORAGE_KEY)toast('另一個分頁已更新資料，請重新整理後再編輯。');});
render();
