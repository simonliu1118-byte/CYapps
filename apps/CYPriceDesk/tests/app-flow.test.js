// Exercises the real app event handlers without a browser. Native browser rendering
// and form validation are verified separately on the privately deployed Site.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import * as core from '../public/core.js';
import {randomUUID} from 'node:crypto';

function boot(storage=new Map()) {
  const elements=new Map(),all=[],blobs=[],downloads=[];
  class Element {
    constructor(attrs={}){Object.assign(this,{value:'',textContent:'',hidden:false,disabled:false,open:false,dataset:{},handlers:{},children:[]},attrs);this.classList={add(){},remove(){},toggle(){}};}
    set innerHTML(value){this.html=value;this.children=parse(value);}
    get innerHTML(){return this.html||'';}
    addEventListener(type,callback){this.handlers[type]=callback;}
    setAttribute(){}
    showModal(){this.open=true;}
    close(){this.open=false;}
    reset(){for(const el of Object.values(this.elements||{}))el.value='';}
    querySelectorAll(selector){return this.children.filter(el=>selector==='[data-edit]'?el.dataset.edit:selector==='[data-delete]'?el.dataset.delete:false);}
    click(){if(this.download)downloads.push(this.download);return this.onclick?.();}
    fire(type,event={}){return this.handlers[type]?.({preventDefault(){},target:this,...event});}
  }
  function parse(html){const result=[];for(const tag of html.matchAll(/<\w+\b([^>]*)>/g)){const attrs={dataset:{}};for(const a of tag[1].matchAll(/([\w-]+)="([^"]*)"/g)){if(a[1].startsWith('data-'))attrs.dataset[a[1].slice(5)]=a[2];else attrs[a[1]]=a[2];}const el=new Element(attrs);if(el.id)elements.set(el.id,el);all.push(el);result.push(el);}return result;}
  parse(fs.readFileSync('public/index.html','utf8'));
  const form=elements.get('offer-form');form.elements={};
  for(const el of all)if(el.name)form.elements[el.name]=el;
  const document={getElementById:id=>elements.get(id),querySelectorAll:s=>all.filter(el=>s==='[data-basis]'?el.dataset.basis:s==='[data-close]'?el.dataset.close:false),createElement:()=>new Element()};
  class FormData {constructor(form){this.fields=Object.entries(form.elements).map(([key,e])=>[key,e.value]);}[Symbol.iterator](){return this.fields[Symbol.iterator]();}}
  const URLProxy=class extends URL {};URLProxy.createObjectURL=blob=>{blobs.push(blob);return 'blob:test';};URLProxy.revokeObjectURL=()=>{};
  const context=vm.createContext({...core,document,FormData,Blob,URL:URLProxy,crypto:{randomUUID},window:{addEventListener(){}},Date,Intl,console,setTimeout:()=>1,clearTimeout(){},localStorage:{getItem:k=>storage.get(k)??null,setItem:(k,v)=>storage.set(k,v)}});
  vm.runInContext(fs.readFileSync('public/app.js','utf8').split('\n').slice(1).join('\n'),context);
  const el=id=>elements.get(id);
  const saved=()=>JSON.parse(storage.get('price-desk:v1')||'{"offers":[]}').offers;
  function add(values){el('add-btn').click();for(const [key,val]of Object.entries(values))form.elements[key].value=String(val);form.fire('submit');assert.equal(el('offer-error').hidden,true,el('offer-error').textContent);assert.equal(el('offer-dialog').open,false);}
  function edit(id,values){el('groups').querySelectorAll('[data-edit]').find(e=>e.dataset.edit===id).click();for(const [key,val]of Object.entries(values))form.elements[key].value=String(val);form.fire('submit');}
  return {el,form,add,edit,saved,storage,blobs,downloads,all};
}
test('真實應用程式事件：新增、更新、比較、匯入、刪除、保存與匯出',async()=>{
  const a=boot();
  a.el('empty-demo').click();assert(a.el('groups').innerHTML.includes('最低價'));assert.equal(a.saved().length,0);
  a.el('exit-demo').click();
  a.add({product:'驗證耳機',spec:'A1',platform:'蝦皮',url:'https://shopee.tw/test',seller:'測試店'});
  assert.equal(a.saved()[0].price,null);assert(a.el('groups').innerHTML.includes('待查價'));
  a.edit(a.saved()[0].id,{price:'990',shipping:'60'});
  a.add({product:'驗證耳機',spec:'A1',platform:'momo',price:'1090',shipping:'0'});
  assert(a.el('groups').innerHTML.includes('+$100'));assert(a.el('groups').innerHTML.includes('$990'));
  a.all.find(x=>x.dataset.basis==='total').click();assert(a.el('groups').innerHTML.includes('$1,050'));assert(a.el('groups').innerHTML.includes('+$40'));
  a.el('import-btn').click();
  a.el('paste-input').value='商品,規格,平台,價格\n驗證耳機,A1,momo,-1';a.el('preview-btn').click();assert.equal(a.el('confirm-import').disabled,true);assert.equal(a.saved().length,2);
  a.el('paste-input').value='商品,規格,平台,價格,運費\n驗證耳機,A1,PChome,899,\n驗證耳機,A2,momo,1299,0';a.el('paste-input').fire('input');a.el('preview-btn').click();assert.equal(a.el('confirm-import').disabled,false);a.el('confirm-import').click();
  assert.equal(a.saved().length,4);assert(a.el('groups').innerHTML.includes('運費未知，未列入比較'));assert(a.el('groups').innerHTML.includes('$1,050'));
  a.el('search').value='找不到';a.el('search').fire('input');assert(a.el('groups').innerHTML.includes('沒有符合條件'));
  a.el('empty-action').click();assert(a.el('groups').innerHTML.includes('驗證耳機'));
  a.el('groups').querySelectorAll('[data-delete]')[0].click();a.el('confirm-delete').click();assert.equal(a.saved().length,3);
  const b=boot(a.storage);assert.equal(b.saved().length,3);assert(b.el('groups').innerHTML.includes('驗證耳機'));
  b.el('export-btn').click();assert.equal(b.downloads.length,1);assert((await b.blobs[0].text()).includes('驗證耳機'));
});
test('真實應用程式事件：惡意文字轉義與跨分頁覆蓋保護',()=>{
  const a=boot();a.add({product:'<img src=x onerror=alert(1)>',platform:'momo',price:'99'});
  assert(a.el('groups').innerHTML.includes('&lt;img'));assert(!a.el('groups').innerHTML.includes('<img src=x'));
  const b=boot(a.storage);b.edit(b.saved()[0].id,{price:'88'});
  a.edit(a.saved()[0].id,{price:'77'});assert(a.el('toast').textContent.includes('另一個分頁'));assert.equal(a.saved()[0].price,88);
});
