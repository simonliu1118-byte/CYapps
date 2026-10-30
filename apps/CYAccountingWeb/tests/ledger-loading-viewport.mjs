import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const ledger = fs.readFileSync(new URL('../public/ledger-tools.js', import.meta.url), 'utf8');
const css = fs.readFileSync(new URL('../public/adaptive-ui.css', import.meta.url), 'utf8');
const app = fs.readFileSync(new URL('../public/app.js', import.meta.url), 'utf8');

const start = ledger.indexOf("  const tableWrap = card.querySelector('.table-wrap');");
const end = ledger.indexOf('  setupLedgerUtilityMenu();', start);
assert.ok(start >= 0 && end > start, 'canonical ledger toolbar must own loading overlay');

class TestNode {
  constructor(tag) { this.tag = tag; this.children = []; this.parentNode = null; this.hidden = false; }
  append(child) { child.parentNode = this; this.children.push(child); }
  before(node) {
    const siblings = this.parentNode.children;
    siblings.splice(siblings.indexOf(this), 0, node);
    node.parentNode = this.parentNode;
  }
  setAttribute(name, value) { this[name] = value; }
}
const card = new TestNode('ledger-card');
const tableWrap = new TestNode('table-wrap');
card.append(tableWrap);
card.querySelector = selector => selector === '.table-wrap' ? tableWrap : null;
vm.runInNewContext(ledger.slice(start, end), {
  card, document: { createElement: tag => new TestNode(tag) }
});
assert.equal(card.children.length, 1, 'viewport must replace old scroll-container position');
const viewport = card.children[0];
assert.equal(viewport.className, 'ledger-table-viewport');
assert.equal(viewport.children[0], tableWrap, 'table remains the single scroll owner');
const overlay = viewport.children[1];
assert.equal(overlay.id, 'ledgerLoadingOverlay');
assert.equal(overlay.parentNode, viewport, 'loading UI must not be inside scrolling content');
assert.notEqual(overlay.parentNode, tableWrap);
assert.ok(overlay.hidden, 'loading UI begins hidden');
assert.match(overlay.innerHTML, /ledger-loading-spinner/);
assert.match(overlay.innerHTML, /載入中…/);

// Desktop layout constrains the overlay to the visible ledger pane, not the
// full scrollHeight of a long month, while preserving its independent scrollbar.
assert.match(css, /html\[data-viewport="desktop"\] \.ledger-card \.ledger-table-viewport \{[^}]*position:\s*relative;[^}]*display:\s*flex;[^}]*flex:\s*1 1 auto;[^}]*min-height:\s*0;[^}]*overflow:\s*hidden;/);
assert.match(css, /html\[data-viewport="desktop"\] \.ledger-card \.ledger-table-viewport > \.table-wrap \{[^}]*overflow-y:\s*auto;/);
assert.match(css, /html\[data-viewport="desktop"\] \.ledger-card \.ledger-table-viewport > \.ledger-loading-overlay:not\(\[hidden\]\) \{[^}]*position:\s*absolute;[^}]*inset:\s*0;[^}]*align-items:\s*center;[^}]*justify-content:\s*center;/);
assert.doesNotMatch(ledger.slice(start, end), /tableWrap\.append\(overlay\)/,
  'overlay must never move with a scrolled table');
assert.match(app, /loadingOverlay\.hidden = !busy/,
  'reuse the one app loading state, without a second loading owner');
assert.match(ledger, /id="ledgerMonthLoading"/, 'retain existing mobile loading status');
assert.match(css, /\.ledger-card\.is-loading \.table-wrap::after/,
  'preserve mobile and tablet loading presentation');

// No per-transaction color dictionary or closure allocation on the hot path.
const row = ledger.slice(ledger.indexOf('function renderLedgerRow('), ledger.indexOf('let cyMarkerPicker'));
assert.doesNotMatch(row, /const markerNames = \{/);
assert.doesNotMatch(row, /const highlightText = \(/);
assert.match(row, /const summaryText = escapeHtml\(tx\.summary \|\| ''\)/);
assert.match(row, /const categoryText = escapeHtml\(tx\.category_name \|\| ''\)/);

console.log('Pinned ledger loading backdrop and lean marker render regression passed.');
