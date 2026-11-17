import test from 'node:test';
import assert from 'node:assert/strict';
import {byAttention, buildChart, compareAll, comparePlatform, gapOf, isWarn, manualCounts, needsAttention, valueAt} from '../public/compare.js';

const TODAY = '2026-11-17';

test('差距%與警示：高於最低價 5% 以上才警示，等於或低於為正常', () => {
  const cases = [
    [105, 100, true], [104.9, 100, false], [100, 100, false], [90, 100, false],
    [320, 259, true], [262.5, 250, true], [null, 100, false], [100, null, false]
  ];
  for (const [mine, min, warn] of cases) assert.equal(isWarn(gapOf(mine, min)), warn, `${mine} vs ${min}`);
  assert.equal(gapOf(100, 0), null);
});

test('手動比價是否計入：30 天內且為三平台才計入', () => {
  const row = (platform, checkedAt) => ({platform, checkedAt});
  const cases = [
    [row('shopee', '2026-11-17'), true], [row('momo', '2026-10-18'), true],   // 30 天
    [row('momo', '2026-10-17'), false],                                        // 31 天，過期
    [row('coupang', '2026-01-01'), false], [row('other', '2026-11-17'), false]
  ];
  for (const [r, counted] of cases) assert.equal(manualCounts(r, TODAY), counted, `${r.platform} ${r.checkedAt}`);
});

test('單一平台比較：同規格價格與有效手動比價合併取最低與中位數；未設定售價只有市場價', () => {
  const manual = [{platform: 'momo', checkedAt: '2026-11-10', price: 240}, {platform: 'momo', checkedAt: '2026-08-01', price: 100}];
  const c = comparePlatform({mine: 320, prices: [259, 275], manual, today: TODAY});
  assert.deepEqual([c.min, c.median, c.count, c.warn], [240, 259, 3, true]);
  assert.equal(Math.round(c.gap * 1000), 333);
  const noPrice = comparePlatform({mine: null, prices: [259], manual: [], today: TODAY});
  assert.deepEqual([noPrice.mine, noPrice.min, noPrice.gap, noPrice.warn], [null, 259, null, false]);
  assert.deepEqual(comparePlatform({mine: 300, prices: [], manual: [], today: TODAY}), {mine: 300, min: null, median: null, count: 0, gap: null, warn: false});
});

test('三平台各自比較，手動比價只計入自己的平台', () => {
  const platforms = compareAll({
    myPrices: {shopee: 300, momo: null, coupang: 200},
    pricesBy: {shopee: [262], momo: [259], coupang: [285]},
    manual: [{platform: 'shopee', checkedAt: TODAY, price: 250}, {platform: 'other', checkedAt: TODAY, price: 1}],
    today: TODAY
  });
  assert.deepEqual([platforms.shopee.min, platforms.momo.min, platforms.coupang.min], [250, 259, 285]);
  assert.deepEqual([platforms.shopee.warn, platforms.momo.warn, platforms.coupang.warn], [true, false, false]);
  assert.equal(needsAttention(platforms), true);
});

test('最需要注意排序：最大正差距由大到小，再待確認筆數，再名稱', () => {
  const items = [
    {name: 'B', attention: 0, pendingCount: 0}, {name: 'A', attention: 0, pendingCount: 0}, {name: 'C', attention: 0, pendingCount: 3},
    {name: 'D', attention: 0.2, pendingCount: 0}, {name: 'E', attention: 0.5, pendingCount: 0}
  ];
  assert.deepEqual(items.sort(byAttention).map(i => i.name), ['E', 'D', 'C', 'A', 'B']);
});

test('走勢圖資料：依 run 與來源算最低與中位數、同日取最後一次；售價階梯；手動比價只留三平台', () => {
  const obs = (runId, source, at, price) => ({runId, source, at, price});
  const chart = buildChart({
    observations: [
      obs('r1', 'momo', '2026-11-01T02:00:00.000Z', 259), obs('r1', 'momo', '2026-11-01T02:00:00.000Z', 275), obs('r1', 'momo', '2026-11-01T02:00:00.000Z', 300),
      obs('r1', 'shopee', '2026-11-01T02:00:00.000Z', 262),
      obs('r2', 'momo', '2026-11-10T02:00:00.000Z', 250), obs('r3', 'momo', '2026-11-10T09:00:00.000Z', 240)   // 同一天，後者為準
    ],
    changes: [
      {platform: 'momo', at: '2026-11-05T02:00:00.000Z', newPrice: 330}, {platform: 'momo', at: '2026-10-01T02:00:00.000Z', newPrice: 320},
      {platform: 'shopee', at: '2026-10-01T02:00:00.000Z', newPrice: 310}
    ],
    manual: [{platform: 'coupang', checkedAt: '2026-11-03', price: 285, seller: 'A'}, {platform: 'other', checkedAt: '2026-11-03', price: 1, seller: 'B'}]
  });
  assert.deepEqual(chart.platforms.momo.min, [{day: '2026-11-01', value: 259}, {day: '2026-11-10', value: 240}]);
  assert.deepEqual(chart.platforms.momo.median, [{day: '2026-11-01', value: 275}, {day: '2026-11-10', value: 240}]);
  assert.deepEqual(chart.platforms.momo.mine, [{day: '2026-10-01', value: 320}, {day: '2026-11-05', value: 330}]);
  assert.deepEqual(chart.platforms.shopee.min, [{day: '2026-11-01', value: 262}]);
  assert.deepEqual(chart.platforms.coupang, {min: [], median: [], mine: []});
  assert.deepEqual(chart.manual, [{platform: 'coupang', day: '2026-11-03', price: 285, seller: 'A'}]);
  assert.equal(valueAt(chart.platforms.momo.mine, '2026-11-04').value, 320);
  assert.equal(valueAt(chart.platforms.momo.mine, '2026-11-05').value, 330);
  assert.equal(valueAt(chart.platforms.momo.mine, '2026-09-30'), null);
});
