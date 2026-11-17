// Replaces globalThis.fetch with a fake of the Vercel fetcher: verifies the HMAC headers like fetcher/app.py
// and answers with fixture JSON. No real network access.
import {signBody} from '../../src/fetcher-client.js';
import {FETCHER_SECRET} from './env.js';

const CHECKED_AT = '2026-11-16T02:00:00+00:00';

export const momoFixture = (keyword, page = 1) => ({
  status: 'ok', raw_count: 20, checked_at: CHECKED_AT,
  results: [
    {title: '3M 1530-0 通氣膠帶 半吋 12入', code: '14011391', price: 259, relevance: 3, match_label: '完全符合', url: 'https://www.momoshop.com.tw/product/14011391'},
    {title: '3M Nexcare 1530-1 紙膠帶 6捲', code: 'TP00013930000165', price: 190, relevance: 2, match_label: '型號相關', url: 'https://www.momoshop.com.tw/goods/GoodsDetail.jsp?i_code=TP00013930000165'},
    {title: '醫療膠帶 12入（推薦）', code: '1234570', price: null, relevance: 0, match_label: 'momo 推薦', url: 'https://www.momoshop.com.tw/product/1234570'}
  ].map(r => ({...r, title: page > 1 ? `${r.title} 第${page}頁` : r.title}))
});

export const feebeeFixture = keyword => ({
  status: 'ok', checked_at: CHECKED_AT, source_url: 'https://feebee.com.tw/s/' + encodeURIComponent(keyword) + '/',
  markets: {
    shopee: {status: 'ok', results: [
      {title: '3M 1530-1 紙膠帶 12捲', price: 300, url: 'https://feebee.com.tw/p/shopee-1', source: '飛比／蝦皮商城', market: 'shopee', merchant: '蝦皮商城', seller: '示範藥局', shop_type: 'mall'},
      {title: '3M 1530-1 紙膠帶 6入', price: 230, url: 'https://feebee.com.tw/p/shopee-2', source: '飛比／蝦皮購物', market: 'shopee', merchant: '蝦皮購物', seller: '示範生活', shop_type: 'marketplace'}
    ]},
    coupang: {status: 'ok', results: [
      {title: '3M 1530-1 紙膠帶 1個', price: 175, url: 'https://feebee.com.tw/p/coupang-1', source: '飛比／Coupang 酷澎', market: 'coupang', merchant: 'Coupang 酷澎', seller: '', shop_type: 'coupang'}
    ]}
  }
});

export function installFakeFetcher() {
  const original = globalThis.fetch;
  const fake = {
    calls: [],
    respond: null, // optional override: ({path, payload}) => JSON
    restore: () => { globalThis.fetch = original; }
  };
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(typeof input === 'string' ? input : input.url);
    const timestamp = init.headers['x-cypd-timestamp'];
    const valid = init.headers['x-cypd-signature'] === await signBody(FETCHER_SECRET, timestamp, init.body);
    const payload = JSON.parse(init.body);
    fake.calls.push({path: url.pathname, payload, valid, host: url.host});
    if (!valid) return new Response('{}', {status: 401});
    const data = fake.respond?.({path: url.pathname, payload})
      ?? (url.pathname === '/v1/momo' ? momoFixture(payload.keyword, payload.page) : feebeeFixture(payload.keyword));
    return new Response(JSON.stringify(data), {status: 200, headers: {'content-type': 'application/json'}});
  };
  return fake;
}
