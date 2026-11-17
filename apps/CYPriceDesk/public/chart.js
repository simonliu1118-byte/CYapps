// One native-SVG trend chart (WO-1.4.0 §2.7): per platform the same-spec lowest price (solid) and median (dashed),
// our own price as a thick step line, manual comparisons as dots; toggles, 3 months / 1 year / all, hover or tap tooltip.
import {PLATFORMS, PLATFORM_COLOR, PLATFORM_LABEL, dayNumber, valueAt} from './compare.js';
import {money} from './core.js';
import {esc} from './ui.js';

const RANGE_DAYS = {'3m': 92, '1y': 366, all: null};
const RANGE_LABEL = {'3m': '3 個月', '1y': '1 年', all: '全部'};
const KIND_LABEL = {min: '最低價', median: '中位數', mine: '我的售價'};
const SVG = 'http://www.w3.org/2000/svg';
const MARGIN = {l: 58, r: 14, t: 12, b: 28};

const dayFrom = n => new Date(n * 86400000).toISOString().slice(0, 10);

function niceTicks(lo, hi, count = 4) {
  const raw = (hi - lo) / count, pow = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map(m => m * pow).find(s => s >= raw);
  const ticks = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi + 1e-9; v += step) ticks.push(Math.round(v * 100) / 100);
  return ticks;
}

// state: {range, on} kept by the caller across re-renders; on = Set of enabled line ids (null = defaults).
export function createChart(root, data, today, state) {
  const lines = PLATFORMS.flatMap(p => ['min', 'median', 'mine'].map(kind => ({id: `${p}:${kind}`, platform: p, kind, points: data.platforms[p][kind]})))
    .filter(l => l.points.length);
  const hasManual = data.manual.length > 0;
  state.range ||= '3m';
  state.on ||= new Set([...lines.filter(l => l.kind !== 'median').map(l => l.id), ...(hasManual ? ['manual'] : [])]);

  root.innerHTML = `<div class="chart-controls"><div class="chart-toggles"></div><div class="segmented" role="group" aria-label="時間範圍">${Object.entries(RANGE_LABEL).map(([k, label]) => `<button type="button" data-range="${k}">${label}</button>`).join('')}</div></div>
    <div class="chart-plot"><svg role="img" aria-label="價格走勢圖"></svg><div class="chart-tip" hidden></div></div><p class="chart-empty muted" hidden>目前沒有可顯示的資料：查價後才會有市場價格，調整售價後才會有售價線。</p>`;
  const toggles = root.querySelector('.chart-toggles'), plot = root.querySelector('.chart-plot'), svg = root.querySelector('svg'), tip = root.querySelector('.chart-tip'), empty = root.querySelector('.chart-empty');

  toggles.innerHTML = PLATFORMS.map(p => `<div class="chart-group" style="--c:${PLATFORM_COLOR[p]}"><strong>${PLATFORM_LABEL[p]}</strong>${lines.filter(l => l.platform === p).map(l =>
    `<button type="button" class="line-toggle" data-line="${l.id}" aria-pressed="${state.on.has(l.id)}"><i class="sw sw-${l.kind}"></i>${KIND_LABEL[l.kind]}</button>`).join('') || '<span class="muted">尚無資料</span>'}</div>`).join('')
    + (hasManual ? `<div class="chart-group"><strong>其他</strong><button type="button" class="line-toggle" data-line="manual" aria-pressed="${state.on.has('manual')}"><i class="sw sw-dot"></i>手動比價</button></div>` : '');
  const syncButtons = () => {
    for (const b of root.querySelectorAll('[data-range]')) b.classList.toggle('selected', b.dataset.range === state.range);
    for (const b of toggles.querySelectorAll('[data-line]')) b.setAttribute('aria-pressed', String(state.on.has(b.dataset.line)));
  };

  let width = 0, view = null;
  const el = (name, attrs, parent = svg) => {
    const node = document.createElementNS(SVG, name);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
    parent.appendChild(node);
    return node;
  };

  function draw() {
    syncButtons();
    tip.hidden = true;
    svg.replaceChildren();
    const end = dayNumber(today);
    const dayNums = [...lines.flatMap(l => l.points.map(p => dayNumber(p.day))), ...data.manual.map(m => dayNumber(m.day))];
    const days = RANGE_DAYS[state.range];
    const start = days ? end - days : Math.min(end - 1, ...dayNums);
    const clamp = n => n >= start && n <= end;
    const shown = lines.filter(l => state.on.has(l.id)).map(l => {
      let points = l.points.map(p => ({n: dayNumber(p.day), day: p.day, value: p.value})).filter(p => clamp(p.n));
      if (l.kind === 'mine') {   // a step line starts at the price in force when the range begins
        const before = [...l.points].reverse().find(p => dayNumber(p.day) < start);
        if (before) points = [{n: start, day: dayFrom(start), value: before.value}, ...points];
      }
      return {...l, pts: points};
    }).filter(l => l.pts.length);
    const dots = state.on.has('manual') ? data.manual.map(m => ({...m, n: dayNumber(m.day)})).filter(m => clamp(m.n)) : [];
    const values = [...shown.flatMap(l => l.pts.map(p => p.value)).filter(v => v !== null), ...dots.map(d => d.price)];
    empty.hidden = values.length > 0;
    svg.toggleAttribute('hidden', values.length === 0);
    if (!values.length) { view = null; return; }

    width = Math.max(280, plot.clientWidth);
    const height = width < 520 ? 240 : 300;
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.setAttribute('width', width);
    svg.setAttribute('height', height);
    let lo = Math.min(...values), hi = Math.max(...values);
    const pad = (hi - lo || hi * 0.1 || 1) * 0.12;
    lo = Math.max(0, lo - pad); hi += pad;
    const x = n => MARGIN.l + (end === start ? 0 : (n - start) / (end - start)) * (width - MARGIN.l - MARGIN.r);
    const y = v => MARGIN.t + (1 - (v - lo) / (hi - lo)) * (height - MARGIN.t - MARGIN.b);

    for (const t of niceTicks(lo, hi)) {
      el('line', {x1: MARGIN.l, x2: width - MARGIN.r, y1: y(t), y2: y(t), class: 'grid'});
      el('text', {x: MARGIN.l - 8, y: y(t) + 4, 'text-anchor': 'end', class: 'axis'}).textContent = `$${t.toLocaleString('en-US')}`;
    }
    const xCount = width < 520 ? 4 : 6, long = end - start > 120;
    for (let i = 0; i <= xCount; i++) {
      const n = Math.round(start + (end - start) * i / xCount), d = dayFrom(n);
      el('text', {x: x(n), y: height - 8, 'text-anchor': i === 0 ? 'start' : i === xCount ? 'end' : 'middle', class: 'axis'}).textContent = long ? d.slice(0, 7).replace('-', '/') : `${+d.slice(5, 7)}/${+d.slice(8)}`;
    }

    const order = {mine: 0, median: 1, min: 2};
    for (const l of [...shown].sort((a, b) => order[a.kind] - order[b.kind])) {
      const color = PLATFORM_COLOR[l.platform];
      if (l.kind === 'mine') {
        // A blank price (stopped comparing) lifts the pen until the next price.
        let d = '', prev = null;
        for (const p of l.pts) {
          if (prev !== null) d += `H${x(p.n)}`;
          if (p.value !== null) d += prev !== null ? `V${y(p.value)}` : `M${x(p.n)} ${y(p.value)}`;
          prev = p.value;
        }
        if (prev !== null) d += `H${x(end)}`;
        if (d) el('path', {d, fill: 'none', stroke: color, 'stroke-width': 5, 'stroke-opacity': .45, 'stroke-linejoin': 'round'});
      } else {
        el('polyline', {points: l.pts.map(p => `${x(p.n)},${y(p.value)}`).join(' '), fill: 'none', stroke: color, 'stroke-width': 2, ...(l.kind === 'median' ? {'stroke-dasharray': '6 4'} : {})});
        if (l.kind === 'min') for (const p of l.pts) el('circle', {cx: x(p.n), cy: y(p.value), r: 3, fill: color});
      }
    }
    for (const m of dots) el('circle', {cx: x(m.n), cy: y(m.price), r: 5.5, fill: PLATFORM_COLOR[m.platform], stroke: '#fff', 'stroke-width': 1.5});

    const guide = el('line', {y1: MARGIN.t, y2: height - MARGIN.b, class: 'guide', visibility: 'hidden'});
    view = {start, end, x, shown, dots, guide, height, eventDays: [...new Set([...shown.flatMap(l => l.pts.map(p => p.n)), ...dots.map(d => d.n)])]};
    const overlay = el('rect', {x: MARGIN.l, y: MARGIN.t, width: width - MARGIN.l - MARGIN.r, height: height - MARGIN.t - MARGIN.b, fill: 'transparent', class: 'overlay'});
    overlay.addEventListener('pointermove', e => hover(e));
    overlay.addEventListener('pointerdown', e => hover(e));
    overlay.addEventListener('pointerleave', e => { if (e.pointerType === 'mouse') hideTip(); });
  }

  function hideTip() { tip.hidden = true; if (view) view.guide.setAttribute('visibility', 'hidden'); }

  // Snaps to the nearest day that has data in a visible line, then lists every visible line's value on that day.
  function hover(e) {
    if (!view || !view.eventDays.length) return;
    const box = svg.getBoundingClientRect();
    const px = e.clientX - box.left;
    const target = view.start + (px - MARGIN.l) / (width - MARGIN.l - MARGIN.r) * (view.end - view.start);
    const n = view.eventDays.reduce((best, d) => Math.abs(d - target) < Math.abs(best - target) ? d : best);
    const day = dayFrom(n);
    const rows = view.shown.map(l => ({l, hit: valueAt(l.points, day)})).filter(r => r.hit && r.hit.value !== null).map(({l, hit}) => {
      const stale = l.kind !== 'mine' && hit.day !== day ? `<small>（${esc(hit.day.slice(5).replace('-', '/'))}）</small>` : '';
      return `<div class="tt-row" style="--c:${PLATFORM_COLOR[l.platform]}"><i class="sw sw-${l.kind}"></i><span>${PLATFORM_LABEL[l.platform]} ${KIND_LABEL[l.kind]}</span><b>${money(hit.value)}</b>${stale}</div>`;
    });
    for (const d of view.dots.filter(d => d.day === day)) {
      rows.push(`<div class="tt-row" style="--c:${PLATFORM_COLOR[d.platform]}"><i class="sw sw-dot"></i><span>${PLATFORM_LABEL[d.platform]} 手動比價${d.seller ? `（${esc(d.seller)}）` : ''}</span><b>${money(d.price)}</b></div>`);
    }
    view.guide.setAttribute('x1', view.x(n));
    view.guide.setAttribute('x2', view.x(n));
    view.guide.setAttribute('visibility', 'visible');
    tip.innerHTML = `<div class="tt-date">${esc(day)}</div>${rows.join('')}`;
    tip.hidden = false;
    const w = tip.offsetWidth, left = view.x(n) + 12 + w > width ? view.x(n) - 12 - w : view.x(n) + 12;
    tip.style.left = `${Math.max(0, left)}px`;
    tip.style.top = `${MARGIN.t + 4}px`;
  }

  root.addEventListener('click', e => {
    const range = e.target.closest('[data-range]'), toggle = e.target.closest('[data-line]');
    if (range) state.range = range.dataset.range;
    else if (toggle) state.on[state.on.has(toggle.dataset.line) ? 'delete' : 'add'](toggle.dataset.line);
    else return;
    draw();
  });
  new ResizeObserver(() => { if (plot.clientWidth !== width && plot.clientWidth > 0) draw(); }).observe(plot);
  draw();
}
