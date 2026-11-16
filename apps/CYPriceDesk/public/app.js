import {groupsOf,groupKey,compareOffers,money,displayTime,localTime,validateOffer,inferPlatform,previewImport,upsertOffers,exportCSV} from './core.js';

const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const SAVED_TEXT='價格紀錄儲存於雲端，登入者共用';
let offers=[],demoMode=false,demoOffers=[],basis='price',editingId=null,deletingId=null,importData=null,toastTimer;
const currentOffers=()=>demoMode?demoOffers:offers;
// All formal data lives on the server; a 401 means the session is gone, so go back to the login page.
async function api(method,path,body) {
  const response=await fetch(path,{method,headers:body===undefined?{}:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
  const data=await response.json().catch(()=>({}));
  if(response.status===401){location.href='/login';throw new Error(data.error||'尚未登入');}
  if(!response.ok){const error=new Error(data.error||'操作失敗，請稍後再試。');error.status=response.status;throw error;}
  return data;
}
async function reload(){offers=(await api('GET','/api/records')).records;render();}
function toast(message) {
  clearTimeout(toastTimer);$('toast').textContent=message;$('toast').classList.add('visible');
  toastTimer=setTimeout(()=>$('toast').classList.remove('visible'),4200);
}
function demoCommit(next){demoOffers=next;render();}
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
  const priced=data.filter(o=>o.price!==null);
  const latest=priced.length?new Date(Math.max(...priced.map(o=>Date.parse(o.checkedAt)))):null;
  $('stats').textContent=`${groupsOf(data).length} 個商品規格 · ${priced.length} 筆有效報價 · ${data.length-priced.length} 筆待查價 · ${new Set(data.map(x=>x.platform)).size} 個平台 · 最近查價 ${latest?localTime(latest).replace('T',' '):'—'}`;
}
function renderRow(row) {
  const time=row.checkedAt?localTime(row.checkedAt).replace('T',' '):null;
  const old=row.checkedAt && Date.now()-Date.parse(row.checkedAt)>7*86400000;
  return `<tr class="${row.isLowest?'lowest':''}" data-offer-id="${esc(row.id)}">
    <td data-label="平台／賣家"><strong>${esc(row.platform)}</strong> <small>${esc(row.seller||'未填賣家')}</small>${row.note?`<small>${esc(row.note)}</small>`:''}</td>
    <td data-label="${basis==='total'?'含運總價':'商品價格'}" class="num">${row.value===null?`<span class="muted">${row.price===null?'待查價':'運費未知，未列入比較'}</span>`:`<strong>${money(row.value)}</strong>${row.isLowest?' <span class="chip accent">最低價</span>':''}`}</td>
    <td data-label="與最低價差" class="num">${row.diff===null?'—':row.diff===0?'基準價格':`+${money(row.diff)}${row.percent===null?'':` (+${row.percent.toFixed(1)}%)`}`}</td>
    <td data-label="運費" class="num">${row.shipping===null?'待確認':row.shipping===0?'免運':money(row.shipping)}</td>
    <td data-label="查價時間">${time?esc(time):'尚未查價'}${old?' <span class="chip warning">超過 7 天</span>':''}</td>
    <td><div class="row-actions">${row.url?`<a class="btn small" href="${esc(row.url)}" target="_blank" rel="noopener noreferrer" aria-label="開啟 ${esc(row.product)} 的 ${esc(row.platform)} 商品連結">連結</a>`:''}<button class="btn small" data-edit="${esc(row.id)}" aria-label="編輯 ${esc(row.product)} 的 ${esc(row.platform)} 查價">編輯</button><button class="btn small" data-delete="${esc(row.id)}" aria-label="刪除 ${esc(row.product)} 的 ${esc(row.platform)} 查價">刪除</button></div></td>
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
  if(!groups.length) {
    $('groups').innerHTML=`<div class="empty"><strong>${data.length?'沒有符合條件的商品':'尚無價格紀錄'}</strong><p>${data.length?'換個搜尋詞，或顯示所有平台。':'新增第一筆查價，把各平台價格放在一起比較。'}</p><div class="row-actions"><button class="btn ${data.length?'':'primary'}" id="empty-action" type="button">${data.length?'清除篩選':'新增查價'}</button>${data.length?'':'<button class="btn quiet" id="empty-demo" type="button">看看範例</button>'}</div></div>`;
    $('empty-action').onclick=()=>data.length?clearFilters():openOffer();
    if($('empty-demo')) $('empty-demo').onclick=loadDemo;
    return;
  }
  $('groups').innerHTML=`<section class="panel">${groups.map(g=> {
    const comparison=compareOffers(g.offers,basis);
    const visibleRows=comparison.rows.filter(o=>!platform||o.platform===platform);
    const pending=g.offers.filter(o=>o.price===null).length;
    const unknownShipping=basis==='total'?g.offers.filter(o=>o.price!==null&&o.shipping===null).length:0;
    const notes=[!g.spec?'未指定規格，請確認型號、容量與組合相同。':'',platform?'最低價與價差仍以此商品全部平台為基準。':'',pending?`${pending} 筆待查價未列入比較。`:'',unknownShipping?`${unknownShipping} 筆運費未知，未列入含運比較。`:'',basis==='price'?'不含運費。':'含運總價＝商品價格＋運費。'].filter(Boolean).join(' ');
    return `<article class="group"><div class="group-head"><div><h3>${esc(g.product)}</h3><div class="group-meta"><span class="chip">${esc(g.spec||'未指定規格')}</span><span>${g.offers.length} 筆紀錄 · ${new Set(g.offers.map(o=>o.platform)).size} 個平台</span></div></div><div class="group-summary"><div><span>${basis==='total'?'最低含運總價':'最低商品價格'}</span><strong>${money(comparison.min)}</strong></div><div><span>最高與最低價差</span><strong>${money(comparison.gap)}</strong></div></div></div>
      <div class="table-wrap"><table class="data-table"><thead><tr><th scope="col">平台／賣家</th><th scope="col" class="num">${basis==='total'?'含運總價':'商品價格'}</th><th scope="col" class="num">與最低價差</th><th scope="col" class="num">運費</th><th scope="col">查價時間</th><th scope="col">操作</th></tr></thead><tbody>${visibleRows.map(row=>renderRow(row)).join('')}</tbody></table></div>
      <p class="footnote">${notes} 優惠券與會員條件請記在備註；價格以查價時間為準。</p>
    </article>`;
  }).join('')}</section>`;
  $('groups').querySelectorAll('[data-edit]').forEach(b=>b.onclick=()=>openOffer(b.dataset.edit));
  $('groups').querySelectorAll('[data-delete]').forEach(b=>b.onclick=()=>openDelete(b.dataset.delete));
}
function clearFilters(){ $('search').value='';$('platform-filter').value='';render(); }
function loadDemo(){demoMode=true;demoOffers=sampleOffers();clearFilters();$('save-status').textContent='範例操作不會寫入你的資料';}
function exitDemo(){demoMode=false;clearFilters();$('save-status').textContent=SAVED_TEXT;}
function openOffer(id=null,prefill=null) {
  if(!id && demoMode) exitDemo();
  editingId=id;$('offer-form').reset();$('offer-error').hidden=true;
  $('offer-dialog-title').textContent=id?'編輯查價':'新增查價';
  if(id) {
    const record=currentOffers().find(x=>x.id===id);
    for(const [k,value] of Object.entries(record)) if($('offer-form').elements[k]) $('offer-form').elements[k].value=k==='checkedAt'?(value?localTime(value):localTime(Date.now())):value??'';
  } else {
    // Auto query hands over a result: spec is left for the user, the check time is now.
    for(const [k,value] of Object.entries(prefill||{})) if($('offer-form').elements[k]) $('offer-form').elements[k].value=value??'';
    $('time-input').value=localTime(Date.now());
  }
  $('offer-dialog').showModal();
}
window.cypdAddOffer=prefill=>openOffer(null,prefill);
$('offer-form').addEventListener('submit',async e=> {
  e.preventDefault();$('offer-error').hidden=true;
  try {
    const offer=validateOffer(Object.fromEntries(new FormData(e.target)));
    let notification;
    if(demoMode) {
      let next;
      if(editingId) {
        const duplicate=demoOffers.find(x=>x.id!==editingId && groupKey(x)===groupKey(offer) && offer.url && x.url===offer.url);
        if(duplicate) throw new Error('此商品規格已有相同連結，請改為編輯那筆查價。');
        next=demoOffers.map(o=>o.id===editingId?{...offer,id:editingId}:o);notification='查價已更新';
      } else {
        const result=upsertOffers(demoOffers,[offer]);
        if(result.skipped) throw new Error('此連結已有較新的查價時間，請更新時間或編輯原紀錄。');
        next=result.offers;notification=result.updated?'相同連結的查價已更新':'查價已新增';
      }
      demoCommit(next);notification+='（範例操作）';
    } else if(editingId) {
      await api('PUT',`/api/records/${editingId}`,{...offer,revision:offers.find(o=>o.id===editingId).revision});
      await reload();notification='查價已更新';
    } else {
      const {result}=await api('POST','/api/records',offer);
      await reload();notification=result==='created'?'查價已新增':'相同連結的查價已更新';
    }
    $('offer-dialog').close();toast(notification);
  } catch(error){
    $('offer-error').hidden=false;$('offer-error').textContent=error.message;
    if(error.status===409) await reload().catch(()=>{});
  }
});
$('url-input').addEventListener('input',()=> { const found=inferPlatform($('url-input').value);if(found)$('platform-input').value=found; });
$('price-input').addEventListener('input',()=> {if(!$('time-input').value)$('time-input').value=localTime(Date.now());});
$('set-now').onclick=()=>{$('time-input').value=localTime(Date.now());};
function openDelete(id){deletingId=id;const record=currentOffers().find(x=>x.id===id);$('delete-description').textContent=`${record.product} · ${record.platform}${record.seller?` · ${record.seller}`:''}。刪除後會立即更新比價結果。`;$('delete-dialog').showModal();}
$('confirm-delete').onclick=async()=> {
  try {
    if(demoMode) demoCommit(demoOffers.filter(x=>x.id!==deletingId));
    else {await api('DELETE',`/api/records/${deletingId}?revision=${offers.find(x=>x.id===deletingId).revision}`);await reload();}
    toast('查價已刪除');
  } catch(error) {
    toast(error.message);
    if(error.status===409||error.status===404) await reload().catch(()=>{});
  }
  $('delete-dialog').close();
};
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
$('confirm-import').onclick=async()=> {
  if(!importData)return;
  try {
    const result=await api('POST','/api/records/import',{offers:importData});
    await reload();$('import-dialog').close();
    toast(`已新增 ${result.added} 筆、更新 ${result.updated} 筆${result.skipped?`；略過 ${result.skipped} 筆舊資料`:''}`);
  } catch(error){$('import-error').hidden=false;$('import-error').textContent=error.message;}
};
$('export-btn').onclick=()=> {
  if(demoMode||!offers.length)return;
  const blob=new Blob([exportCSV(offers)],{type:'text/csv;charset=utf-8;'}),url=URL.createObjectURL(blob);
  const a=document.createElement('a');a.href=url;a.download=`價格觀測室-${localTime(Date.now()).slice(0,10)}.csv`;a.click();setTimeout(()=>URL.revokeObjectURL(url),10000);toast('已匯出查價資料');
};
for(const b of document.querySelectorAll('[data-close]'))b.onclick=()=>$(b.dataset.close).close();
for(const b of document.querySelectorAll('[data-basis]'))b.onclick=()=>{basis=b.dataset.basis;document.querySelectorAll('[data-basis]').forEach(x=>{const on=x.dataset.basis===basis;x.classList.toggle('selected',on);x.setAttribute('aria-pressed',String(on));});render();};
$('search').addEventListener('input',render);$('platform-filter').addEventListener('change',render);
$('add-btn').onclick=()=>openOffer();$('import-btn').onclick=openImport;$('demo-btn').onclick=loadDemo;$('exit-demo').onclick=exitDemo;
const ACTION_LABELS={'record.create':'新增','record.update':'修改','record.delete':'刪除','record.import':'匯入','classification.update':'分類','classification.delete':'移除分類','tracked.create':'追蹤新增','tracked.update':'追蹤修改','tracked.price':'改售價','tracked.run':'立即查價','judgment.update':'判斷','judgment.delete':'恢復判斷'};
async function openAudit() {
  $('audit-body').textContent='載入中…';$('audit-dialog').showModal();
  try {
    const {entries}=await api('GET','/api/audit?limit=50');
    $('audit-body').innerHTML=entries.length?entries.map(x=>`<div class="audit-item"><span class="audit-action">${esc(ACTION_LABELS[x.action]||x.action)}</span><div><div>${esc(x.detail)}</div><div class="audit-meta">${displayTime(x.at)} · ${esc(x.actor)}</div></div></div>`).join(''):'<p class="muted">尚無操作紀錄。</p>';
  } catch(error){$('audit-body').textContent=error.message;}
}
$('sidebar-audit').onclick=openAudit;
const ready=(async()=> {
  try {
    const me=await api('GET','/api/me');
    const name=me.displayName||me.employeeNo;
    document.querySelectorAll('[data-user-name]').forEach(x=>{x.textContent=name;});
    document.querySelectorAll('[data-user-avatar]').forEach(x=>{x.textContent=[...name][0]||'';});
    await reload();
  } catch(error) {
    $('storage-warning').hidden=false;$('storage-warning').textContent=`無法載入價格紀錄：${error.message}`;
    $('groups').innerHTML='';
  }
})();
