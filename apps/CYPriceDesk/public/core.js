export const normalize = value => String(value ?? '').normalize('NFKC').trim().replace(/\s+/g, ' ').toLowerCase();
export const groupKey = offer => `${normalize(offer.product)}\u001f${normalize(offer.spec)}`;
export const money = value => value === null || value === undefined ? '—' : new Intl.NumberFormat('zh-TW', {style:'currency',currency:'TWD',maximumFractionDigits:2}).format(value);
export const localTime = value => new Intl.DateTimeFormat('sv-SE', {timeZone:'Asia/Taipei', year:'numeric', month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hourCycle:'h23'}).format(new Date(value)).replace(' ','T');
export const displayTime = value => value ? localTime(value).replace('T',' ') : '尚未查價';
export function parseTime(value) {
  const input = String(value ?? '').trim();
  const match = input.match(/^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?$/);
  if (match) {
    const [,y,m,d,h,min,s='0'] = match;
    const dt = new Date(Date.UTC(+y,+m-1,+d,+h,+min,+s));
    if (dt.getUTCFullYear() !== +y || dt.getUTCMonth() !== +m-1 || dt.getUTCDate() !== +d || +h>23 || +min>59 || +s>59) throw new Error('查價時間不正確');
    return new Date(dt.getTime()-8*3600000).toISOString();
  }
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,3})?)?(Z|[+-]\d{2}:\d{2})$/.test(input) || !Number.isFinite(Date.parse(input))) throw new Error('查價時間請使用 YYYY-MM-DD HH:mm');
  const datePart = input.slice(0,10);
  const [y,m,d] = datePart.split('-').map(Number);
  const test = new Date(Date.UTC(y,m-1,d));
  if (test.toISOString().slice(0,10)!==datePart) throw new Error('查價時間不正確');
  return new Date(input).toISOString();
}
function amount(value, name) {
  if (value === '' || value === null || value === undefined) return null;
  let input = String(value).trim().replace(/^(NT\$|TWD|\$)\s*/i, '');
  if (input.includes(',') && !/^\d{1,3}(,\d{3})+(\.\d{1,2})?$/.test(input)) throw new Error(`${name}格式不正確`);
  input = input.replace(/,/g, '');
  if (!/^\d+(\.\d{1,2})?$/.test(input) || +input>1e9) throw new Error(`${name}需為 0 到 1,000,000,000 的數字，最多兩位小數`);
  return +input;
}
export function validateLink(value) {
  if (!String(value ?? '').trim()) return '';
  try {
    const url = new URL(String(value).trim());
    if (!['https:','http:'].includes(url.protocol) || url.username || url.password) throw new Error();
    url.hash = '';
    return url.href;
  } catch { throw new Error('商品連結請填入有效的 http 或 https 網址'); }
}
export function inferPlatform(value) {
  try {
    const host = new URL(value).hostname.toLowerCase();
    const belongs = d => host === d || host.endsWith(`.${d}`);
    if (belongs('shopee.tw') || belongs('shp.ee')) return '蝦皮';
    if (belongs('momoshop.com.tw') || belongs('momo.com.tw')) return 'momo';
    if (belongs('pchome.com.tw')) return 'PChome';
    if (belongs('yahoo.com')) return 'Yahoo';
  } catch {}
  return '';
}
export function validateOffer(raw, now = Date.now()) {
  const product = String(raw.product ?? '').trim();
  const spec = String(raw.spec ?? '').trim();
  const platform = String(raw.platform ?? '').trim();
  const seller = String(raw.seller ?? '').trim();
  const note = String(raw.note ?? '').trim();
  if (!product) throw new Error('請填入商品名稱');
  if (!platform) throw new Error('請填入平台');
  if ([product,spec,platform,seller].some(s=>s.length>200) || note.length>1000) throw new Error('文字過長，欄位請控制在 200 字內，備註 1,000 字內');
  const url = validateLink(raw.url);
  if (url.length>2048) throw new Error('商品連結過長');
  const price = amount(raw.price, '商品價格');
  const shipping = amount(raw.shipping, '運費');
  if (price === null && !url) throw new Error('請填入價格或商品連結');
  const checkedAt = price === null ? null : parseTime(raw.checkedAt || localTime(now));
  if (checkedAt && Date.parse(checkedAt)>now+5*60000) throw new Error('查價時間不能晚於現在');
  return {product,spec,platform,seller,note,url,price,shipping,checkedAt};
}
export function identity(offer) {
  return `${groupKey(offer)}\u001f${offer.url ? `url:${offer.url}` : `seller:${normalize(offer.platform)}:${normalize(offer.seller)}`}`;
}
export function upsertOffers(existing, incoming, uuid = () => crypto.randomUUID()) {
  const result = existing.map(x=>({...x}));
  let added=0,updated=0,skipped=0;
  for (const offer of incoming) {
    const idx = result.findIndex(x=>identity(x)===identity(offer));
    if (idx<0) { result.push({...offer,id:uuid()}); added++; }
    else {
      const previous=result[idx];
      if (previous.checkedAt && (!offer.checkedAt || Date.parse(offer.checkedAt)<Date.parse(previous.checkedAt))) {skipped++;continue;}
      result[idx]={...offer,id:previous.id}; updated++;
    }
  }
  return {offers:result,added,updated,skipped};
}
export function priceValue(offer, basis='price') {
  if (offer.price===null || (basis==='total' && offer.shipping===null)) return null;
  return basis==='total' ? (Math.round(offer.price*100)+Math.round(offer.shipping*100))/100 : offer.price;
}
export function compareOffers(offers, basis='price') {
  const rows=offers.map(x=>({...x,value:priceValue(x,basis)})).sort((a,b)=>(a.value??Infinity)-(b.value??Infinity) || a.platform.localeCompare(b.platform,'zh-TW'));
  const values=rows.filter(x=>x.value!==null).map(x=>x.value);
  const min=values.length ? Math.min(...values) : null;
  const max=values.length ? Math.max(...values) : null;
  return {rows:rows.map(x=>({...x,isLowest:x.value!==null && x.value===min,diff:x.value===null||min===null?null:Math.round((x.value-min)*100)/100,percent:x.value===null||min===null||min===0?null:((x.value-min)/min)*100})),min,max,gap:min===null?null:Math.round((max-min)*100)/100,count:values.length};
}
export function groupsOf(offers) {
  const map=new Map();
  for(const offer of offers) {
    const key=groupKey(offer);
    if(!map.has(key)) map.set(key,{key,product:offer.product,spec:offer.spec,offers:[]});
    map.get(key).offers.push(offer);
  }
  return [...map.values()];
}
// Quoted commas, escaped quotes, and multiline CSV cells are supported.
export function parseDelimited(input) {
  input=String(input).replace(/^\uFEFF/,'');
  if(input.length>1000000) throw new Error('資料過大，請控制在 1 MB 以內');
  const firstLine=input.split(/\r?\n/)[0];
  const delimiter=firstLine.includes('\t')?'\t':',';
  const rows=[];let row=[],cell='',quoted=false,closed=false;
  for(let i=0;i<input.length;i++) {
    const c=input[i];
    if(quoted) {
      if(c==='"'&&input[i+1]==='"') {cell+='"';i++;}
      else if(c==='"') {quoted=false;closed=true;}
      else cell+=c;
    } else if(c==='"') {
      if(cell.trim() || closed) throw new Error('CSV 引號格式不正確');
      cell='';quoted=true;
    } else if(c===delimiter) {row.push(cell);cell='';closed=false;}
    else if(c==='\n' || c==='\r') {
      if(c==='\r'&&input[i+1]==='\n') i++;
      row.push(cell);if(row.some(s=>s.trim())) rows.push(row);row=[];cell='';closed=false;
      if(rows.length>2001) throw new Error('單次最多匯入 2,000 筆');
    } else {if(closed && c.trim()) throw new Error('CSV 引號後只能接分隔符號');cell+=c;}
  }
  if(quoted) throw new Error('CSV 有未關閉的引號');
  row.push(cell);if(row.some(s=>s.trim())) rows.push(row);
  return rows;
}
const columnMap={商品:'product',商品名稱:'product',product:'product',規格:'spec',spec:'spec',平台:'platform',platform:'platform',價格:'price',商品價格:'price',price:'price',運費:'shipping',shipping:'shipping',查價時間:'checkedAt',checkedat:'checkedAt',連結:'url',商品連結:'url',url:'url',賣家:'seller',seller:'seller',備註:'note',note:'note'};
export function previewImport(input,now=Date.now()) {
  const parsed=parseDelimited(input);
  if(parsed.length<2) throw new Error('請包含標題列與至少一筆資料');
  const columns=parsed[0].map(x=>columnMap[normalize(x)]);
  if(!columns.includes('product') || !columns.includes('platform') || (!columns.includes('price')&&!columns.includes('url'))) throw new Error('標題列至少需要「商品、平台、價格」或「商品、平台、連結」');
  const known=columns.filter(Boolean);
  if(new Set(known).size!==known.length) throw new Error('標題列含有重複欄位');
  if(parsed.length>2001) throw new Error('單次最多匯入 2,000 筆');
  const offers=[],errors=[];
  parsed.slice(1).forEach((row,i)=> {
    try {
      if(row.length!==columns.length) throw new Error('欄位數量與標題列不一致');
      const raw={};columns.forEach((key,j)=> {if(key) raw[key]=row[j];});
      offers.push(validateOffer(raw,now));
    } catch(e) {errors.push(`第 ${i+2} 列：${e.message}`);}
  });
  return {offers,errors};
}
export function exportCSV(offers) {
  const keys=['product','spec','platform','price','shipping','checkedAt','url','seller','note'];
  const quote=value=> {let s=String(value??'');if(/^[=+\-@\t\r]/.test(s)) s="'"+s;return '"'+s.replace(/"/g,'""')+'"';};
  return '\uFEFF'+['商品,規格,平台,價格,運費,查價時間,連結,賣家,備註',...offers.map(o=>keys.map(k=>quote(k==='checkedAt'&&o[k]?localTime(o[k]).replace('T',' '):o[k])).join(','))].join('\r\n');
}
