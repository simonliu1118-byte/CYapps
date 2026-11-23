// Browser flow for the V1.4.0 pages against tests/support/local-server.mjs (seeded; fake IDENTITY and fake fetcher, no network):
//   PORT=4173 SEED=1 NO_GAP=1 node tests/support/local-server.mjs &   then   node tests/ui.test.cjs
// With SHOT_DIR=<dir> it also writes the WO-1.4.0 §5 screenshots (1440 and 390 wide).
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

const base = process.env.TEST_BASE_URL || 'http://127.0.0.1:4173';
const SHOT = process.env.SHOT_DIR;
const DESKTOP = {width: 1440, height: 1000}, MOBILE = {width: 390, height: 844};

(async () => {
  const browser = await chromium.launch({headless: true, args: ['--no-sandbox']});
  const page = await (await browser.newContext({viewport: DESKTOP})).newPage();
  const failures = [];
  page.on('pageerror', e => failures.push(e.message));
  page.on('response', r => { if (r.status() >= 500) failures.push(`${r.status()} ${r.url()}`); });
  if (SHOT) fs.mkdirSync(SHOT, {recursive: true});

  // Checks that the page does not scroll sideways at phone width; writes both screenshots when SHOT_DIR is set.
  async function snap(name, {full = true, prep, locator} = {}) {
    for (const [label, size] of [['desktop', DESKTOP], ['mobile', MOBILE]]) {
      await page.setViewportSize(size);
      await page.waitForTimeout(250);
      if (prep) await prep();
      if (label === 'mobile') assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${name}: horizontal page scroll at 390px`);
      if (SHOT) {
        const path = `${SHOT}/${name}-${label}.png`;
        await (locator ? page.locator(locator).screenshot({path}) : page.screenshot({path, fullPage: full}));
      }
    }
    await page.setViewportSize(DESKTOP);
  }
  const nav = href => page.locator(`.nav-page[href="${href}"]`).click();
  const text = selector => page.locator(selector).innerText();
  const rowOf = (selector, hasText) => page.locator(selector, {hasText}).first();

  // ---- sign-in through the portal, 查價 is the default page with exactly four nav items ----
  await page.goto(base);   // no Session: the Worker sends the browser through the stand-in CY Web portal and back signed in
  await page.waitForSelector('#s-form');
  assert.equal(await page.locator('.nav-page').count(), 4);
  assert.equal(await page.locator('#page-title').innerText(), '查價');
  await page.waitForFunction(() => document.getElementById('pending-badge').textContent === '3');
  assert.equal(await page.locator('#s-recent [data-keyword]').count(), 3);
  await snap('search-recent');

  // ---- 查價: one table for all sources, sort, filter ----
  await page.locator('#s-recent [data-keyword]').first().click();
  await page.waitForSelector('#s-body tr[data-i]');
  assert.equal(await page.locator('#s-body tr[data-i]').count(), 10);
  const tags = await page.locator('#s-body .src-tag').allInnerTexts();
  for (const t of ['momo', '蝦皮', '酷澎']) assert(tags.includes(t), t);
  assert((await text('#s-status')).includes('momo 6 筆'));
  assert.equal(await page.locator('#s-source').inputValue(), 'all');
  assert((await rowOf('#s-body tr', '展示品').innerText()).includes('$88'));        // default: price ascending
  await page.locator('[data-sort="price"]').click();
  assert((await page.locator('#s-body tr').first().innerText()).includes('$480'));  // descending
  await page.locator('[data-sort="title"]').click();
  assert.equal(await page.locator('#s-table th[aria-sort="ascending"]').count(), 1);
  await page.locator('[data-sort="price"]').click();
  await page.locator('#s-filter [data-filter="momo"]').click();
  assert.equal(await page.locator('#s-body tr[data-i]').count(), 6);
  await page.locator('#s-filter [data-filter="all"]').click();
  assert.equal(await page.locator('#s-body tr[data-i]').count(), 10);
  assert((await text('#s-summary')).includes('同規格摘要'));
  await snap('search-results');

  // ---- 設定規格, 不使用, 加入追蹤 ----
  await rowOf('#s-body tr', '0.5吋／1吋').locator('[data-act="spec"]').click();
  await page.fill('#class-form [name="model"]', '1530-0'); await page.fill('#class-form [name="quantity"]', '12'); await page.fill('#class-form [name="unit"]', '入');
  await page.locator('#class-form button[type="submit"]').click();
  await page.locator('#class-dialog').waitFor({state: 'hidden'});
  assert((await rowOf('#s-body tr', '0.5吋／1吋').innerText()).includes('人工設定'));
  await rowOf('#s-body tr', '3M 15301').locator('[data-act="exclude"]').click();
  await page.waitForSelector('#s-body tr.is-excluded');
  await page.locator('#s-body tr.is-excluded [data-act="exclude"]').click();
  await page.waitForSelector('#s-body tr.is-excluded', {state: 'detached'});

  await rowOf('#s-body tr', '示範藥局').locator('[data-act="track"]').click();
  const form = '#tracked-form';
  assert.deepEqual(await page.evaluate(() => { const f = document.getElementById('tracked-form').elements; return [f.keyword.value, f.model.value, f.packageQuantity.value, f.packageUnit.value, f.name.value]; }),
    ['3M 1530-0', '1530-0', '12', '入', '3M 1530-0 通氣膠帶 半吋 12入']);
  await page.fill(`${form} [name="price_shopee"]`, '330'); await page.fill(`${form} [name="price_momo"]`, '310');
  await page.locator(`${form} .advanced summary`).click();
  await page.fill(`${form} [name="momoKeyword"]`, 'UI momo 關鍵字');
  await page.locator(`${form} button[type="submit"]`).click();
  await page.locator('#tracked-dialog').waitFor({state: 'hidden'});
  assert((await text('#toast')).includes('已新增追蹤商品'));

  // ---- 追蹤商品 list: three platform columns, 最需要注意 order, filters ----
  await nav('#tracked');
  await page.waitForFunction(() => document.querySelectorAll('#tr-list tr[data-id]').length === 4);
  assert((await page.locator('#tr-list tr[data-id]').first().innerText()).includes('3M 紙膠帶'));   // largest positive gap first
  assert((await rowOf('#tr-list tr', '3M 紙膠帶').innerText()).includes('+25.5%'));
  assert((await page.locator('#tr-list tbody tr').nth(1).innerText()).includes('羅技'));            // pending count breaks the 0-gap tie
  const newRow = await rowOf('#tr-list tr', '通氣膠帶 半吋 12入').innerText();
  assert(newRow.includes('我的 $330.00') && newRow.includes('我的 $310.00'));
  await page.locator('[data-tr-filter="attention"]').click();
  assert.equal(await page.locator('#tr-list tr[data-id]').count(), 1);
  await page.locator('[data-tr-filter="pending"]').click();
  assert.equal(await page.locator('#tr-list tr[data-id]').count(), 2);
  await page.locator('[data-tr-filter="inactive"]').click();
  assert.equal(await page.locator('#tr-list tr[data-id]').count(), 1);
  await page.locator('[data-tr-filter="all"]').click();
  await snap('tracked-list');

  // ---- detail: platform cards, chart toggles, tooltip ----
  await rowOf('#tr-list tr', '3M 紙膠帶').click();
  await page.waitForSelector('#chart-box svg polyline');
  assert.equal(await page.locator('.pcard').count(), 3);
  assert((await page.locator('.pcard').first().innerText()).includes('+25.5%'));
  const lines = () => page.locator('#chart-box svg polyline').count();
  assert.equal(await lines(), 3);                                  // lowest price of each platform
  await page.locator('[data-line="shopee:median"]').click();
  assert.equal(await lines(), 4);
  await snap('detail');
  const overlay = await page.locator('.chart-plot .overlay').boundingBox();
  const hover = () => page.locator('.chart-plot .overlay').hover({position: {x: (overlay.width) * 0.72, y: overlay.height / 2}});
  await hover();
  await page.locator('.chart-tip').waitFor({state: 'visible'});
  const tip = await text('.chart-tip');
  for (const part of ['蝦皮 最低價', '蝦皮 中位數', 'momo 最低價', '酷澎 最低價', '蝦皮 我的售價', 'momo 我的售價']) assert(tip.includes(part), part);
  await snap('chart-tooltip', {locator: '#chart-box', prep: async () => { await page.locator('#chart-box').scrollIntoViewIfNeeded(); const b = await page.locator('.chart-plot .overlay').boundingBox(); await page.mouse.move(b.x + b.width * 0.72, b.y + b.height / 2); }});
  await page.locator('[data-range="1y"]').click();
  assert.equal(await page.locator('#chart-box [data-range="1y"].selected').count(), 1);
  await page.locator('[data-range="3m"]').click();

  // ---- 調整售價 (one platform) ----
  await page.locator('[data-price="momo"]').click();
  await page.fill('#price-form [name="note"]', '介面測試');
  await snap('price-dialog', {full: false, prep: async () => { await page.fill('#price-form [name="price"]', '295'); }});
  await page.locator('#price-form button[type="submit"]').click();
  await page.locator('#price-dialog').waitFor({state: 'hidden'});
  assert((await page.locator('.pcard').nth(1).innerText()).includes('$295.00'));
  assert((await text('#tracked-detail-view')).includes('介面測試'));
  assert.equal(await lines(), 4);                                  // chart toggles survive the refresh

  // ---- 手動比價: add, edit, delete ----
  await page.locator('#manual-add').click();
  const manual = '#manual-form';
  await page.selectOption(`${manual} [name="platform"]`, 'coupang');
  await page.fill(`${manual} [name="seller"]`, 'UI 賣家'); await page.fill(`${manual} [name="price"]`, '260'); await page.fill(`${manual} [name="note"]`, '現場看到');
  await snap('manual-form', {full: false});
  await page.locator(`${manual} button[type="submit"]`).click();
  await page.locator('#manual-dialog').waitFor({state: 'hidden'});
  assert((await rowOf('.panel tr', 'UI 賣家').innerText()).includes('計入'));
  assert((await page.locator('.pcard').nth(2).innerText()).includes('$260.00'));    // lowest for 酷澎 is now the manual price
  await rowOf('.panel tr', 'UI 賣家').locator('[data-manual-edit]').click();
  await page.fill(`${manual} [name="price"]`, '265');
  await page.locator(`${manual} button[type="submit"]`).click();
  await page.locator('#manual-dialog').waitFor({state: 'hidden'});
  assert((await rowOf('.panel tr', 'UI 賣家').innerText()).includes('$265.00'));
  assert((await rowOf('.panel tr', '示範旗艦店').innerText()).includes('已過期，不計入'));
  await rowOf('.panel tr', 'UI 賣家').locator('[data-manual-del]').click();
  await page.locator('#confirm-delete').click();
  await page.waitForFunction(() => !document.getElementById('tracked-detail-view').innerText.includes('UI 賣家'));

  // ---- judging in the detail page updates the nav badge ----
  assert.equal(await page.locator('#pending-badge').innerText(), '3');
  await rowOf('#tracked-detail-view tr', '0.5吋／1吋').locator('[data-judge="same"]').click();
  await page.waitForFunction(() => document.getElementById('pending-badge').textContent === '2');
  assert((await text('[data-tab="pending"]')).includes('1'));

  // ---- 待確認 page: grouped by product, judge in place ----
  await nav('#pending');
  await page.waitForFunction(() => document.querySelectorAll('#pending-list .panel').length === 2);
  assert.equal(await page.locator('#pending-list .panel a[href^="#tracked/"]').count(), 2);
  await snap('pending');
  await rowOf('#pending-list .panel', '羅技').locator('[data-judge="not_same"]').click();
  await page.waitForFunction(() => document.querySelectorAll('#pending-list .panel').length === 1);
  assert.equal(await page.locator('#pending-badge').innerText(), '1');

  // ---- 操作紀錄 ----
  await nav('#audit');
  await page.waitForSelector('#audit-list tr');
  const audit = await text('#audit-list');
  for (const part of ['調整售價', '手動比價新增', '手動比價刪除', '判斷', '追蹤新增', '設定規格']) assert(audit.includes(part), part);
  await snap('audit');

  assert.deepEqual(failures, []);
  console.log(JSON.stringify({passed: true, checks: ['查價合併表格與排序、篩選', '設定規格、不使用、加入追蹤', '追蹤商品列表三平台與注意度排序', '走勢圖切換與浮動框', '調整售價', '手動比價新增修改刪除', '判斷與待確認頁、導覽徽章', '操作紀錄', '390px 無橫向捲動']}, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
