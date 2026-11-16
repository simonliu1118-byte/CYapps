// Auto query page: momo / Feebee Shopee / Feebee Coupang lookups through /api/search, spec parsing and
// manual classification (/api/classifications, shared and audited), and handing a result to the add-record dialog.
import {displayTime, money} from './core.js';
import {classification, facets, filtered, groups, sanitizeAttributes} from './catalog-core.js';

const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const PLATFORM={momo:'momo',shopee:'蝦皮',coupang:'酷澎'};
const SOURCE_TITLE={momo:'momo',shopee:'飛比蝦皮',coupang:'飛比酷澎'};
const FEEBEE_TITLE={shopee:'蝦皮',coupang:'酷澎'};
const DISCLAIMER='飛比收錄的商品級第三方參考價，來源價格更新時間未確認，非即時或逐規格價格。';

let source='momo',query=null,response=null,products=[],rules=new Map(),effective=[],selections={},editing=null,busy=false;

async function api(method,path,body) {
  const res=await fetch(path,{method,headers:body===undefined?{}:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
  const data=await res.json().catch(()=>({}));
  if(res.status===401){location.href='/login';throw new Error(data.error||'尚未登入');}
  if(!res.ok)throw new Error(data.error||'操作失敗，請稍後再試。');
  return data;
}

// ---- products ----
function toProducts(list) {
  return list.map(r=>source==='momo'
    ?{source,key:r.code,title:r.title,price:r.price,url:r.url,seller:'',label:r.code,note:`momo 品號 ${r.code} · ${r.match_label}`,relevance:r.relevance}
    :{source,key:r.url,title:r.title,price:r.price,url:r.url,seller:r.seller||'',label:r.seller||r.merchant,merchant:r.merchant,
      note:(r.merchant+(r.seller?'－'+r.seller:''))+' · 來源價格更新時間未確認 · 規格與入數請核對',relevance:0});
}
function sorted(list) {
  const sort=$('aq-sort').value;
  if(sort==='relevance')return source==='momo'?[...list].sort((a,b)=>b.relevance-a.relevance||(a.price===null)-(b.price===null)||(a.price??0)-(b.price??0)):list;
  const priced=list.filter(p=>p.price!==null).sort((a,b)=>sort==='desc'?b.price-a.price:a.price-b.price);
  return [...priced,...list.filter(p=>p.price===null)];
}

// ---- messages ----
function message(res) {
  const feebee=source!=='momo';
  switch(res.status) {
    case 'blocked': case 'blocked_cooldown': case 'provider_refused':
      return feebee?'飛比限制了這次查詢，未取得可用參考價。':'站方拒絕或限制查詢，已停止重試。請稍後再試。';
    case 'rate_limited':
      return (feebee?'查詢過於頻繁，請稍後再試。':'查詢太密集，請稍後再試。同關鍵字 10 分鐘內會優先使用快取。')+(res.retryAfterSeconds?`（約 ${res.retryAfterSeconds} 秒後可再查）`:'');
    case 'no_match': case 'no_price':
      return feebee?`未找到能可靠配對${FEEBEE_TITLE[source]}商品與價格的飛比資料，不能將不明數字當成參考價。`:'目前沒有符合條件的有效展示價，請更換關鍵字。';
    default:
      return feebee?'暫時無法取得可靠飛比參考價，請直接到飛比搜尋頁核對。':'暫時無法取得價格，請稍後再試。';
  }
}
function setStatus(html,cls='aq-empty'){$('aq-status').innerHTML=`<p class="${cls}">${html}</p>`;}
const checkedText=res=>res.checkedAt?`查價時間：${esc(displayTime(res.checkedAt))}（臺北）${res.fromCache?'，使用 10 分鐘內的共用快取':''}`:'';

// ---- rendering ----
function renderDisclaimer() {
  const box=$('aq-disclaimer');
  box.hidden=source==='momo';
  if(source!=='momo')box.textContent=`${DISCLAIMER}${source==='shopee'?'蝦皮多規格商品無法由此確認逐規格 SKU 售價，需另行核價。':''}不與 momo、其他平台價格統計混算。`;
}
function classify(){effective=products.map(p=>classification(p,rules.get(p.key)));}
function render() {
  const rows=filtered(sorted(effective),selections);
  const feebee=source!=='momo';
  $('aq-results').innerHTML=effective.length?`<div class="table-wrap"><table class="data-table"><thead><tr><th>商品</th><th class="num">${feebee?'商品級參考價':'公開展示價'}</th><th>${feebee?'賣家／來源':'來源'}</th><th>連結</th><th>規格解析</th><th>操作</th></tr></thead><tbody>${rows.map(rowHtml).join('')||'<tr><td colspan="6" class="muted">沒有符合目前篩選的商品。</td></tr>'}</tbody></table></div>`:'';
  $('aq-analysis').hidden=!effective.length;
  if(effective.length){renderFacets();$('aq-count').textContent=`目前顯示 ${rows.length} 筆；已排除 ${effective.filter(x=>x.excluded).length} 筆；待確認 ${effective.filter(x=>!x.ready&&!x.excluded).length} 筆`;renderGroups(rows);}
  const pager=$('aq-pager');
  pager.hidden=source!=='momo'||!response||!['ok','no_match','no_price'].includes(response.status);
  if(!pager.hidden){$('aq-prev').hidden=response.page<=1;$('aq-next').hidden=!response.hasNext;$('aq-page').textContent=`第 ${response.page} 頁（最多 5 頁）`;}
}
const attrText=attrs=>Object.entries(attrs||{}).map(([k,v])=>`${esc(k)}：${esc(v)}`).join('、')||'—';
function rowHtml(p) {
  const sellerText=p.source==='momo'?'momo':p.merchant+(p.seller?'－'+p.seller:'');
  const pack=p.quantity?`${p.quantity} ${esc(p.unit)}`:'未確認';
  return `<tr class="${p.excluded?'is-excluded':''}" data-key="${esc(p.key)}">
    <td data-label="商品">${esc(p.title)}<small>${esc(p.note)}</small></td>
    <td data-label="價格" class="num">${p.price===null?'<span class="muted">未取得</span>':money(p.price)}</td>
    <td data-label="來源">${esc(sellerText)}</td>
    <td data-label="連結"><a href="${esc(p.url)}" target="_blank" rel="noopener noreferrer">${p.source==='momo'?'商品頁':'飛比來源'}</a></td>
    <td data-label="規格解析"><strong>${esc(p.model||'未辨識型號')}</strong> <span class="chip ${p.ready?'success':'warning'}">${esc(p.status)}</span><div class="aq-spec">規格：${attrText(p.attributes)} · 包裝：${pack}</div><div class="aq-unit">${p.unitPrice===null?'單價待確認':`${money(p.unitPrice)}／${esc(p.unit)}`}</div></td>
    <td><div class="row-actions"><button type="button" class="btn small" data-edit="${esc(p.key)}">編輯分類</button><button type="button" class="btn small" data-exclude="${esc(p.key)}">${p.excluded?'取消排除':'不使用'}</button><button type="button" class="btn small" data-add="${esc(p.key)}">加入價格紀錄</button></div></td>
  </tr>`;
}
function renderFacets() {
  const available=facets(effective);
  $('aq-facets').innerHTML=available.length
    ?available.map(f=>`<label class="aq-facet">${esc(f.label)}<select data-facet="${esc(f.key)}" aria-label="${esc(f.label)}篩選"><option value="">全部</option>${f.values.map(v=>`<option value="${esc(v)}" ${selections[f.key]===v?'selected':''}>${esc(v)}</option>`).join('')}</select></label>`).join('')+'<button type="button" class="btn small" id="aq-clear-facets">清除篩選</button>'
    :'<p class="muted">暫無可辨識的分類條件。</p>';
  for(const k of Object.keys(selections))if(!available.some(f=>f.key===k&&f.values.includes(selections[k])))delete selections[k];
}
function renderGroups(rows) {
  const summary=groups(rows.filter(x=>!x.excluded));
  $('aq-group-count').textContent=`${summary.length} 個可比較群組（每單位推算價；不含待確認／已排除）`;
  $('aq-groups').innerHTML=summary.length?summary.map(g=>{
    const st=g.statistics;
    return `<div class="aq-group"><h4>${esc(g.model)} ｜每 ${esc(g.unit)} 價格</h4><div class="muted">${attrText(g.attributes)}</div>
      <div class="table-wrap"><table class="data-table"><thead><tr><th class="num">筆數</th><th class="num">最低價</th><th class="num">最高價</th><th class="num">平均價</th><th class="num">中位數</th><th class="num">樣本標準差</th></tr></thead><tbody><tr><td data-label="筆數" class="num">${st.count}</td><td data-label="最低價" class="num">${money(st.min)}</td><td data-label="最高價" class="num">${money(st.max)}</td><td data-label="平均價" class="num">${money(st.mean)}</td><td data-label="中位數" class="num">${money(st.median)}</td><td data-label="標準差" class="num">${st.stddev===null?'—':money(st.stddev)}</td></tr></tbody></table></div></div>`;
  }).join(''):'<p class="muted">目前沒有具備明確型號、包裝數量與價格的可比群組。</p>';
}

// ---- search ----
async function run(page=1) {
  if(busy)return;
  const keyword=$('aq-keyword').value;
  query={keyword,page};
  busy=true;$('aq-submit').disabled=true;setStatus('查詢中…');$('aq-results').innerHTML='';$('aq-analysis').hidden=true;$('aq-pager').hidden=true;
  response=null;products=[];effective=[];
  const requested=source;
  try {
    const params=new URLSearchParams({source,keyword,page});
    const res=await api('GET','/api/search?'+params);
    const usable=['ok','no_match','no_price'].includes(res.status);
    if(usable&&res.results.length)rules=new Map((await api('GET','/api/classifications?source='+source)).classifications.map(c=>[c.listingKey,c]));
    if(requested!==source)return;
    response=res;products=toProducts(res.results);classify();
    if(res.status==='ok')setStatus(source==='momo'
      ?`第 ${res.page} 頁：momo 回傳 ${res.rawCount} 筆，保留 ${res.results.length} 筆，其中 ${res.results.filter(r=>r.price!==null).length} 筆含價格。推薦商品不一定與關鍵字完全一致，請核對型號與入數。${checkedText(res)}`
      :`取得 ${res.results.length} 筆${FEEBEE_TITLE[source]}商品級參考價。${checkedText(res)}（讀取時間不是來源價格更新時間）${res.sourceUrl?` <a href="${esc(res.sourceUrl)}" target="_blank" rel="noopener noreferrer">查看飛比原始搜尋結果</a>`:''}`,'aq-note');
    else setStatus(esc(message(res)));
    render();
  } catch(error){setStatus(esc(error.message));}
  finally{busy=false;$('aq-submit').disabled=false;}
}

// ---- classification dialog ----
const attributesText=attrs=>Object.entries(attrs||{}).map(([k,v])=>`${k}=${v}`).join('\n');
function parseAttributes(text) {
  const attrs={};
  for(const line of text.split(/\r?\n/).slice(0,12)) {
    if(!line.trim())continue;
    const at=line.indexOf('=');if(at<1)throw new Error('額外規格請使用「名稱=內容」，每行一項');
    const key=line.slice(0,at).trim(),value=line.slice(at+1).trim();
    if(!key||!value)throw new Error('額外規格不可留空');
    attrs[key]=value;
  }
  return sanitizeAttributes(attrs);
}
function openEditor(p) {
  editing=p;const form=$('class-form');
  $('class-source').textContent=`來源：${SOURCE_TITLE[source]}`;$('class-listing').textContent=p.title;
  form.elements.model.value=p.model||'';form.elements.quantity.value=p.quantity??'';form.elements.unit.value=p.unit||'';
  form.elements.attributes.value=attributesText(p.attributes);form.elements.decision.value=p.excluded?'exclude':'include';
  $('class-error').hidden=true;$('class-dialog').showModal();
}
async function saveRule(p,fields) {
  const body={source,listingKey:p.key,...fields};
  await api('PUT','/api/classifications',body);
  rules.set(p.key,body);classify();render();
}
$('class-form').addEventListener('submit',async e=> {
  e.preventDefault();const form=e.target;
  try {
    const model=form.elements.model.value.trim(),quantity=Number(form.elements.quantity.value),unit=form.elements.unit.value.trim(),decision=form.elements.decision.value;
    const attributes=parseAttributes(form.elements.attributes.value);
    if(decision!=='exclude'&&(!model||!unit||!Number.isFinite(quantity)||quantity<=0||quantity>100000))
      throw new Error('要納入單價統計，請確認型號、包裝數量及計價單位；不確定可先標記不使用。');
    await saveRule(editing,{model,quantity:quantity>0?quantity:null,unit,attributes,decision});
    $('class-dialog').close();
  } catch(error){$('class-error').hidden=false;$('class-error').textContent=error.message;}
});
$('class-reset').onclick=async()=> {
  try {
    if(rules.has(editing.key)){await api('DELETE',`/api/classifications?source=${source}&listingKey=${encodeURIComponent(editing.key)}`);rules.delete(editing.key);classify();render();}
    $('class-dialog').close();
  } catch(error){$('class-error').hidden=false;$('class-error').textContent=error.message;}
};
$('class-dialog').addEventListener('click',e=>{if(e.target===$('class-dialog'))$('class-dialog').close();});

// ---- events ----
$('aq-form').addEventListener('submit',e=>{e.preventDefault();run(1);});
$('aq-prev').onclick=()=>run(query.page-1);
$('aq-next').onclick=()=>run(query.page+1);
$('aq-sort').addEventListener('change',render);
for(const b of document.querySelectorAll('[data-source]'))b.onclick=()=> {
  source=b.dataset.source;selections={};response=null;products=[];effective=[];query=null;
  document.querySelectorAll('[data-source]').forEach(x=>{const on=x.dataset.source===source;x.classList.toggle('selected',on);x.setAttribute('aria-pressed',String(on));});
  renderDisclaimer();render();
  setStatus($('aq-keyword').value.trim()?'按「查詢價格」查詢此來源。':'輸入關鍵字，即可查看搜尋結果。');
};
$('aq-results').addEventListener('click',async e=> {
  const button=e.target.closest('button');if(!button)return;
  const key=button.dataset.edit??button.dataset.exclude??button.dataset.add;
  const p=effective.find(x=>x.key===key);if(!p)return;
  if(button.dataset.edit!==undefined)openEditor(p);
  else if(button.dataset.add!==undefined)window.cypdAddOffer({product:p.title,platform:PLATFORM[source],seller:p.seller,url:p.url,price:p.price});
  else {
    try{await saveRule(p,{model:p.model,quantity:p.quantity,unit:p.unit,attributes:p.attributes,decision:p.excluded?'include':'exclude'});}
    catch(error){setStatus(esc(error.message));}
  }
});
$('aq-analysis').addEventListener('change',e=>{if(e.target.dataset.facet){selections[e.target.dataset.facet]=e.target.value;render();}});
$('aq-analysis').addEventListener('click',e=>{if(e.target.id==='aq-clear-facets'){selections={};render();}});
renderDisclaimer();
