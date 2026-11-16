/* Generic, zero-AI product classification and statistical summaries (moved from the sandbox catalog_core.js).
   Manual classifications are stored in D1 per source and listing key, so the sandbox title-signature
   "stale" check is gone. Shared by the browser and the Worker (validation). */
const normalize = v => String(v ?? "").normalize("NFKC").replace(/\s+/g," ").trim();
const short = v => normalize(v).toUpperCase();
const round = n => Math.round((n + Number.EPSILON)*100)/100;
const UNIT = {卷:"捲",只:"個",件:"入",枚:"片",PCS:"入",PC:"入",套:"組"};
const CATEGORIES = ["入","捲","卷","個","盒","包","組","瓶","罐","片","支","條","顆","袋","套","只"];
const PACK_RE = new RegExp("(\\d+(?:\\.\\d+)?)\\s*("+CATEGORIES.join("|")+")","gi");
const MODEL_RE = /(?<![A-Z0-9])(?:[A-Z]{1,8}[-]?)?\d{2,7}(?:[-][A-Z0-9]{1,8})?(?:[A-Z]{1,5})?(?![A-Z0-9])/g;
function dimensions(title) {
  const n=short(title), found={}, problems=[];
  const rules=[
    {key:"尺寸", regex:/(\d+(?:\.\d+)?)\s*(吋|英吋|INCH(?:ES)?)/g,unit:"吋"},
    {key:"尺寸", regex:/(\d+(?:\.\d+)?)\s*(MM|毫米)/g,unit:"mm"},
    {key:"尺寸", regex:/(\d+(?:\.\d+)?)\s*(CM|公分)/g,unit:"cm"},
    {key:"重量", regex:/(\d+(?:\.\d+)?)\s*(KG|公斤)/g,unit:"kg"},
    {key:"重量", regex:/(\d+(?:\.\d+)?)\s*(G|公克)(?![A-Z])/g,unit:"g"},
    {key:"容量", regex:/(\d+(?:\.\d+)?)\s*(ML|毫升)/g,unit:"mL"},
    {key:"容量", regex:/(\d+(?:\.\d+)?)\s*(L|公升)(?![A-Z])/g,unit:"L"},
    {key:"長度", regex:/(\d+(?:\.\d+)?)\s*(公尺|米)/g,unit:"m"},
  ];
  if (/半\s*吋/.test(n)) found["尺寸"]=["0.5吋"];
  for(const rule of rules) {
    for(const m of n.matchAll(rule.regex)) {
      const value=Number(m[1]); if(!Number.isFinite(value)||value<=0)continue;
      (found[rule.key] ||= []).push(String(value)+rule.unit);
    }
  }
  // Bare uppercase "3M" may be a brand, not a three-metre length; don't infer.
  for(const m of n.matchAll(/(\d+(?:\.\d+)?)\s*(M)(?![A-Z])/g)) {
    if (m[0].includes(" ") && Number(m[1])>0) (found["長度"] ||= []).push(String(Number(m[1]))+"m");
  }
  const attributes={};
  for (const [key,values] of Object.entries(found)) {
    const valuesUnique=[...new Set(values)];
    if (valuesUnique.length===1) attributes[key]=valuesUnique[0];
    else if(valuesUnique.length>1) problems.push(key+"出現多種選項");
  }
  return {attributes,problems};
}
function models(title) {
  const n=short(title), matches=[];
  for (const m of n.matchAll(MODEL_RE)) {
    const candidate=m[0], numeric=(candidate.match(/\d/g)||[]).length;
    const next=n.slice((m.index||0)+candidate.length).trimStart();
    if (numeric<3 || /^(?:入|捲|卷|個|盒|包|組|瓶|罐|片|支|條|顆|袋|G|KG|ML|L|CM|MM|吋|公分|公尺)/.test(next)) continue;
    if (!matches.includes(candidate)) matches.push(candidate);
  }
  return matches;
}
function parseTitle(title) {
  const n=normalize(title), variants=models(n);
  const dimensionsParsed=dimensions(n);
  const packs=[];
  for (const m of short(n).matchAll(PACK_RE)) {
    const count=Number(m[1]), unit=UNIT[m[2]]||m[2];
    if (Number.isFinite(count)&&count>0&&count<=100000) packs.push({quantity:count,unit});
  }
  const uniquePacks=[...new Map(packs.map(x=>[x.quantity+"|"+x.unit,x])).values()];
  const issues=[...dimensionsParsed.problems];
  if (variants.length>1)issues.push("標題包含多個型號");
  if (uniquePacks.length>1)issues.push("標題包含多種包裝／入數");
  if (/任選|多規格|多種|可選|混搭|尺寸任選|規格任選|隨機|價格.*起|[\d入捲個盒包]\/[\d入捲個盒包]/.test(n)) issues.push("可能有多規格或多種商品選項");
  const pack=uniquePacks.length===1?uniquePacks[0]:null;
  const hasExplicit=!!pack && variants.length===1 && !issues.length;
  return {
    model:variants.length===1?variants[0]:"",
    attributes:dimensionsParsed.attributes,
    quantity:pack?.quantity??null,
    unit:pack?.unit??"",
    issues,
    confidence:hasExplicit?"parsed":"needs_review",
  };
}
function sanitizeAttributes(raw) {
  const out={};
  if (!raw || typeof raw!=="object" || Array.isArray(raw))return out;
  for (const [k,v] of Object.entries(raw).slice(0,12)) {
    const key=normalize(k).slice(0,25),value=normalize(v).slice(0,48);
    if(key && value && !/^(?:__proto__|constructor|prototype)$/.test(key)) out[key]=value;
  }
  return out;
}
function classification(product, rule) {
  const parsed=parseTitle(product.title);
  const id=String(product.source||"")+":"+String(product.key||"");
  const data=rule?{
    model:normalize(rule.model),quantity:Number(rule.quantity)||null,
    unit:normalize(rule.unit),attributes:sanitizeAttributes(rule.attributes),
    issues:[],confidence:"manual",
  }:{...parsed};
  const excluded=!!rule && rule.decision==="exclude";
  const p=Number(product.price);
  const ready=!excluded&&!!data.model&&!!data.quantity&&!!data.unit&&
    data.quantity>0&&data.quantity<=100000&&Number.isFinite(p)&&p>0&&!data.issues.length;
  return {
    ...product, id, excluded,
    ...data, ready, unitPrice:ready?round(p/data.quantity):null,
    status:excluded?"不使用":ready?(rule?"人工確認":"程式推算"):"資料待確認"
  };
}
const facetLabel={model:"型號",quantity:"入數／數量",unit:"計價單位"};
function facets(products) {
  const values=new Map();
  for(const p of products.filter(p=>!p.excluded)){
    const raw={model:p.model,quantity:p.quantity,unit:p.unit,...p.attributes};
    for(const [k,v] of Object.entries(raw)){
      if(v===null||v===undefined||v==="")continue;
      if(!values.has(k))values.set(k,new Set());
      values.get(k).add(String(v));
    }
  }
  return [...values.entries()].filter(([,v])=>v.size>0).map(([key,items])=>({
    key,label:facetLabel[key]||key,values:[...items].sort((a,b)=>a.localeCompare(b,"zh-TW",{numeric:true}))
  }));
}
function filtered(products,selections) {
  return products.filter(p=>{
    const record={model:p.model,quantity:p.quantity,unit:p.unit,...p.attributes};
    return Object.entries(selections||{}).every(([k,v])=>!v||String(record[k]??"")===String(v));
  });
}
function calcStats(arr) {
  if(!arr.length)return null;
  const ordered=[...arr].sort((a,b)=>a-b),n=ordered.length;
  const mean=ordered.reduce((s,x)=>s+x,0)/n;
  const median=n%2?ordered[(n-1)/2]:(ordered[n/2-1]+ordered[n/2])/2;
  const variance=n>=2?ordered.reduce((s,x)=>s+(x-mean)**2,0)/(n-1):null;
  return {count:n,min:round(ordered[0]),max:round(ordered[n-1]),
    mean:round(mean),median:round(median),
    stddev:variance===null?null:round(Math.sqrt(variance)),values:ordered};
}
function groups(products) {
  const buckets=new Map();
  for(const p of products) {
    if(!p.ready || p.unitPrice===null)continue;
    const attr=Object.entries(p.attributes||{}).sort(([a],[b])=>a.localeCompare(b));
    const key=JSON.stringify([short(p.model),short(p.unit),attr.map(([k,v])=>[short(k),short(v)])]);
    if(!buckets.has(key))buckets.set(key,{model:p.model,unit:p.unit,attributes:Object.fromEntries(attr),items:[]});
    buckets.get(key).items.push(p);
  }
  return [...buckets.values()].map(g=>({...g,statistics:calcStats(g.items.map(x=>x.unitPrice))}))
    .sort((a,b)=>b.items.length-a.items.length || a.model.localeCompare(b.model,"zh-TW"));
}

export {normalize,parseTitle,classification,facets,filtered,calcStats,groups,sanitizeAttributes};
