// Pure comparison rules shared by the Worker and the browser (WO-1.4.0 §2.4, §2.7): per-platform comparison of own price
// against the market, attention ordering, manual comparison window, and the data behind the trend chart.
import {calcStats} from './catalog-core.js';
import {localTime} from './core.js';

export const PLATFORMS = ['shopee', 'momo', 'coupang'];
export const PLATFORM_LABEL = {shopee: '蝦皮', momo: 'momo', coupang: '酷澎', other: '其他'};
export const PLATFORM_COLOR = {shopee: '#EE4D2D', momo: '#B5179E', coupang: '#346AFF'};
export const MANUAL_DAYS = 30;
const WARN_PERCENT = 5;

const DAY_MS = 86400000;
export const dayOf = iso => localTime(iso).slice(0, 10);                 // Taipei calendar day of an ISO timestamp
export const dayNumber = day => Date.parse(`${day}T00:00:00Z`) / DAY_MS;  // whole days since the epoch of a YYYY-MM-DD

// A manual comparison counts toward its platform only within the last 30 days; "other" is listed but never counted.
export const manualCounts = (row, today) => PLATFORMS.includes(row.platform) && dayNumber(today) - dayNumber(row.checkedAt) <= MANUAL_DAYS;

// (mine - market lowest) / market lowest, or null when either side is missing.
export const gapOf = (mine, min) => mine === null || mine === undefined || min === null || min === undefined || min <= 0 ? null : (mine - min) / min;
// "+5% or more above the lowest" is judged on the rounded percentage that is shown (one decimal).
export const isWarn = gap => gap !== null && Math.round(gap * 1000) >= WARN_PERCENT * 10;

// prices: same-spec prices from the latest check of this platform; manual: this platform's manual comparisons.
export function comparePlatform({mine, prices, manual, today}) {
  const stats = calcStats([...prices, ...manual.filter(r => manualCounts(r, today)).map(r => r.price)]);
  const gap = gapOf(mine, stats?.min);
  return {mine: mine ?? null, min: stats?.min ?? null, median: stats?.median ?? null, count: stats?.count ?? 0, gap, warn: isWarn(gap)};
}

// myPrices: {shopee, momo, coupang}; pricesBy: {platform: [price]}; manual: all manual comparisons of the product.
export const compareAll = ({myPrices, pricesBy, manual, today}) => Object.fromEntries(PLATFORMS.map(p => [p,
  comparePlatform({mine: myPrices[p], prices: pricesBy[p] || [], manual: manual.filter(r => r.platform === p), today})]));

// Largest positive gap over the three platforms (0 when none is above the market).
export const attentionOf = platforms => Math.max(0, ...PLATFORMS.map(p => platforms[p].gap ?? 0));
export const needsAttention = platforms => PLATFORMS.some(p => platforms[p].warn);
// 最需要注意: largest positive gap first, then more pending candidates, then name.
export const byAttention = (a, b) => b.attention - a.attention || b.pendingCount - a.pendingCount || a.name.localeCompare(b.name, 'zh-TW');

const bySecond = (a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0;
const series = map => [...map].sort(bySecond).map(([day, value]) => ({day, value}));

// observations: same-spec price rows [{runId, source, at, price}]; changes: [{platform, at, newPrice}];
// manual: [{platform, checkedAt, price, seller}]. Days are Taipei dates; the last value of a day wins.
export function buildChart({observations, changes, manual}) {
  const platforms = {};
  for (const platform of PLATFORMS) {
    const runs = new Map();
    for (const o of observations.filter(o => o.source === platform)) {
      const run = runs.get(o.runId) || {at: o.at, prices: []};
      run.prices.push(o.price);
      runs.set(o.runId, run);
    }
    const min = new Map(), median = new Map(), mine = new Map();
    for (const run of [...runs.values()].sort((a, b) => a.at < b.at ? -1 : 1)) {
      const stats = calcStats(run.prices);
      min.set(dayOf(run.at), stats.min);
      median.set(dayOf(run.at), stats.median);
    }
    for (const c of changes.filter(c => c.platform === platform).sort((a, b) => a.at < b.at ? -1 : 1)) mine.set(dayOf(c.at), c.newPrice);
    platforms[platform] = {min: series(min), median: series(median), mine: series(mine)};
  }
  return {
    platforms,
    manual: manual.filter(r => PLATFORMS.includes(r.platform)).map(r => ({platform: r.platform, day: r.checkedAt, price: r.price, seller: r.seller}))
      .sort((a, b) => a.day < b.day ? -1 : a.day > b.day ? 1 : 0)
  };
}

// Last point at or before `day` (carry-forward), or null when the series has not started yet.
export function valueAt(points, day) {
  let found = null;
  for (const p of points) if (p.day <= day) found = p;
  return found;
}
