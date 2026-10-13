import test from 'node:test';
import assert from 'node:assert/strict';
import {validateOffer,groupKey,compareOffers,upsertOffers,previewImport,exportCSV,parseTime,inferPlatform} from '../public/core.js';
const now=Date.parse('2026-10-10T10:30:00Z');
const base={product:'耳機',spec:'A1 黑色',platform:'蝦皮',price:990,shipping:0,checkedAt:'2026-10-10 18:00',url:'https://shopee.tw/product/1',seller:'官方店'};
test('臺北時間、連結平台與輸入檢查',()=>{
  const offer=validateOffer(base,now);
  assert.equal(offer.checkedAt,'2026-10-10T10:00:00.000Z');
  assert.equal(inferPlatform('https://www.momoshop.com.tw/goods/GoodsDetail.jsp?i_code=1'),'momo');
  assert.equal(inferPlatform('https://shopee.tw.evil.example/x'),'');
  assert.throws(()=>validateOffer({...base,url:'javascript:alert(1)'},now));
  assert.throws(()=>validateOffer({...base,price:-1},now));
  assert.throws(()=>validateOffer({...base,checkedAt:'2026-10-10 23:00'},now));
  assert.throws(()=>parseTime('2026-02-30 15:00'));
  assert.equal(validateOffer({...base,price:'NT$1,299.50'},now).price,1299.5);
  assert.throws(()=>validateOffer({...base,price:'1,23'},now));
});
test('同規格分組、並列最低價與零元價格',()=>{
  assert.notEqual(groupKey(base),groupKey({...base,spec:'A2'}));
  assert.equal(groupKey(base),groupKey({...base,product:' 耳機 ',spec:'a1   黑色'}));
  const cmp=compareOffers([{...base,price:990},{...base,platform:'momo',price:1090},{...base,platform:'PChome',price:990},{...base,price:null}]);
  assert.equal(cmp.min,990);assert.equal(cmp.gap,100);
  assert.equal(cmp.rows.filter(o=>o.isLowest).length,2);
  assert.equal(cmp.rows.find(o=>o.platform==='momo').diff,100);
  assert.equal(cmp.rows.at(-1).value,null);
  const zero=compareOffers([{...base,price:0},{...base,price:10}]);
  assert.equal(zero.min,0);assert.equal(zero.rows[1].percent,null);
});
test('含運比較排除未知運費，並以分為單位加總',()=>{
  const cmp=compareOffers([{...base,price:690,shipping:60},{...base,price:729,shipping:0,platform:'momo'},{...base,price:600,shipping:null}], 'total');
  assert.equal(cmp.min,729);assert.equal(cmp.gap,21);assert.equal(cmp.count,2);
  assert.equal(compareOffers([{...base,price:.1,shipping:.2}],'total').min,.3);
  assert.equal(compareOffers([{...base,price:null}]).min,null);
});
test('相同商品連結更新最新報價，舊資料不覆蓋，規格分開',()=>{
  const one={...validateOffer(base,now),id:'stable'};
  const old=validateOffer({...base,price:800,checkedAt:'2026-10-10 17:00'},now);
  assert.equal(upsertOffers([one],[old]).skipped,1);
  assert.equal(upsertOffers([one],[old]).offers[0].price,990);
  const newer=validateOffer({...base,price:950,checkedAt:'2026-10-10 18:20'},now);
  const result=upsertOffers([one],[newer]);assert.equal(result.updated,1);assert.equal(result.offers[0].id,'stable');
  assert.equal(one.price,990);
  assert.equal(upsertOffers([one],[{...newer,spec:'白色'}],()=> 'new').added,1);
});
test('CSV／TSV、引號逗號換行、錯誤列與匯出往返',()=>{
  const csv='商品,規格,平台,價格,運費,查價時間,連結,賣家,備註\n"耳機, A1",黑色,蝦皮,"1,299",0,2026-10-10 18:00,https://shopee.tw/product/1,官方店,"折扣\n活動"';
  const data=previewImport(csv,now);assert.deepEqual(data.errors,[]);assert.equal(data.offers[0].price,1299);assert.equal(data.offers[0].note,'折扣\n活動');
  const exported=previewImport(exportCSV(data.offers),now);assert.deepEqual(exported.offers,data.offers);
  assert.equal(previewImport('商品\t平台\t價格\n耳機\tmomo\t99',now).offers[0].price,99);
  assert.equal(previewImport('商品,平台,價格\n耳機,momo,-10',now).errors.length,1);
  assert.throws(()=>previewImport('商品,平台,價格\n"耳機,momo,99',now));
  const pending=previewImport('商品,平台,連結\n耳機,蝦皮,https://shopee.tw/product/2',now);assert.equal(pending.offers[0].price,null);
});
