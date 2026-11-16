import test from "node:test";
import assert from "node:assert/strict";
import * as core from "../public/catalog-core.js";
const product=(key,title,price)=>({source:"momo",key,title,price});
test("generic model and pack parsing, no category-specific dictionary",()=>{
  let p=core.parseTitle("3M Micropore 1530-0 半吋 12入");
  assert.equal(p.model,"1530-0");assert.equal(p.quantity,12);assert.equal(p.unit,"入");
  assert.equal(p.attributes["尺寸"],"0.5吋");assert.equal(p.issues.length,0);
  p=core.parseTitle("品牌 X M331 滑鼠 2個");assert.equal(p.model,"M331");assert.equal(p.quantity,2);
});
test("multiple variants and package options are not normalized into a fake unit price",()=>{
  let p=core.parseTitle("1530-0/1530-1 1吋12入／半吋24入");
  assert.ok(p.issues.length);assert.equal(p.quantity,null);
  const c=core.classification(product("TP00013930000165","1530-0 1吋12入／半吋24入",190));
  assert.equal(c.unitPrice,null);assert.equal(c.ready,false);
});
test("groups separate mismatched model, size and packing unit",()=>{
  const xs=[
    product("1","A 1530-0 半吋 6捲",300),
    product("2","B 1530-0 半吋 12捲",480),
    product("3","C 1530-1 半吋 6捲",330),
    product("4","D 1530-0 1吋 6捲",420),
  ].map(p=>core.classification(p));
  const groups=core.groups(xs);
  const g=groups.find(x=>x.model==="1530-0"&&x.attributes["尺寸"]==="0.5吋");
  assert.equal(g.items.length,2);assert.equal(g.statistics.mean,45);assert.equal(g.statistics.median,45);
  assert.equal(g.statistics.stddev,7.07);
});
test("same-price statistics and sample deviation for n=1",()=>{
  assert.equal(core.calcStats([40]).stddev,null);
  assert.deepEqual(core.calcStats([40,45,50]),{count:3,min:40,max:50,mean:45,median:45,stddev:5,values:[40,45,50]});
});
test("manual classification overrides the parsed result and survives price changes",()=>{
  const p=product("9956761","Test M331 黑 2個 多規格任選",699);
  assert.equal(core.classification(p).ready,false);
  const rule={decision:"include",model:"M331",quantity:2,unit:"個",attributes:{"顏色":"黑"}};
  const manual=core.classification(p,rule);
  assert.equal(manual.status,"人工確認");assert.equal(manual.unitPrice,349.5);
  assert.equal(core.classification({...p,price:500},rule).unitPrice,250);
});
test("manual exclude removes the listing from statistics",()=>{
  const p=product("TP00013930000165","3M 1530-0 半吋 12入",190);
  const c=core.classification(p,{decision:"exclude"});
  assert.equal(c.excluded,true);assert.equal(c.status,"不使用");assert.equal(core.groups([c]).length,0);
});
test("dynamic facets generated from real results and manual extra fields",()=>{
  const p=product("1","Test M331 黑 2個",699);
  const rule={decision:"include",model:"M331",quantity:2,unit:"個",attributes:{"顏色":"黑","材質":"塑膠"}};
  const data=[core.classification(p,rule),core.classification(product("2","Test M331 白 1個",399),{
    decision:"include",
    model:"M331",quantity:1,unit:"個",attributes:{"顏色":"白"}
  })];
  const filters=core.facets(data);
  assert.ok(filters.some(f=>f.key==="顏色"&&f.values.length===2));
  assert.ok(filters.some(f=>f.key==="材質"));
  assert.equal(core.filtered(data,{"顏色":"白"}).length,1);
});
test("no SKU proof and no count blocks unit price, not zero",()=>{
  assert.equal(core.classification(product("3","3M 1530-0 盒裝",100)).unitPrice,null);
  assert.equal(core.classification(product("4","品牌 M331",699)).unitPrice,null);
});
