const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function element() {
    return { dataset: {}, hidden: true, textContent: '', listeners: {},
        classList: { add() {}, remove() {}, toggle() {} }, addEventListener(type, fn) { this.listeners[type] = fn; },
        setAttribute() {}, replaceChildren() {}, append() {}, focus() {}, querySelectorAll: () => [] };
}
const ids = new Map();
const byId = id => { if (!ids.has(id)) ids.set(id, element()); return ids.get(id); };
const cards = ['A', 'B'].map(code => {
    const card = element();
    card.dataset = { tableCode: code, status: 'Serving', changedAt: '2026-10-15T01:00:00Z' };
    card.badge = element(); card.label = element();
    card.querySelector = selector => selector === '.table-group-label' ? card.badge : selector === '.table-status-label' ? card.label : null;
    return card;
});
const buttons = cards.map(card => ({ ...element(), dataset: { openDetails: card.dataset.tableCode } }));
const page = element(); page.dataset = { statusApi: '/api/table-status', detailsApi: '/api/table-map' };
byId('details-group-main-link').dataset.orderingUrl = '/Ordering';
byId('details-group-unmerge-link').dataset.unmergeUrl = '/Ordering/Unmerge';
byId('details-bill-link').dataset.billUrl = '/ProvisionalBill/Table/';
let poll;
const group = { label: 'N-11', mainSessionId: 11, mainTableCode: 'A', memberTableCodes: ['A', 'B'], status: 'Open' };
let rows = cards.map(c => ({ code: c.dataset.tableCode, status: 'Serving', statusClass: 'serving', statusLabel: 'Đang phục vụ', capacity: 4, changedAtUtc: '2026-10-15T01:00:00Z', group }));
const selectors = {
    '.map-page': page, '#table-details-overlay': byId('table-details-overlay'),
    '#table-details-dialog': byId('table-details-dialog'),
    '#table-map-connection-label': byId('table-map-connection-label'),
    '#table-map-connection': byId('table-map-connection'), '#table-map-last-update': byId('table-map-last-update')
};
const context = {
    document: { hidden: false, getElementById: byId, addEventListener() {},
        querySelector: selector => selectors[selector] || element(),
        querySelectorAll: selector => selector === '[data-table-code]' ? cards : selector === '[data-open-details]' ? buttons : [] },
    window: { location: { origin: 'http://localhost' }, addEventListener() {},
        setInterval(fn, ms) { assert.equal(ms, 2000); poll = fn; return 1; }, clearInterval() {} },
    fetch: async url => ({ ok: true, json: async () => url === '/api/table-status' ? rows
        : { ...rows.find(r => url.endsWith('/' + r.code)), hasActiveSession: rows[0].status !== 'Cleaning', orderedItems: [] } }),
    EventSource: class { addEventListener() {} close() {} },
    requestAnimationFrame() {}, setTimeout: () => 1, clearTimeout() {}, URL,
    Intl, Date, Number, String, JSON, Map, Array, Object, console, encodeURIComponent
};
const tick = () => new Promise(resolve => setImmediate(resolve));
async function run() {
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../src/RestaurantManagement.Web/wwwroot/js/table-map.js'), 'utf8'), context);
    await tick();
    assert.equal(cards[0].badge.textContent, 'Nhóm N-11');
    assert.equal(cards[1].badge.textContent, 'Nhóm N-11');
    buttons[1].listeners.click(); await tick();
    assert.equal(byId('details-group').hidden, false);
    assert.equal(byId('details-group-members').textContent, 'Các bàn thành viên: A, B');
    const link = new URL(byId('details-group-main-link').href);
    assert.equal(link.searchParams.get('sessionId'), '11');
    assert.equal(link.searchParams.get('tableCode'), 'B', 'Opening the root must retain selected member B as the origin');
    const unmergeLink = new URL(byId('details-group-unmerge-link').href);
    assert.equal(unmergeLink.pathname, '/Ordering/Unmerge');
    assert.equal(unmergeLink.searchParams.get('tableCode'), 'B');
    console.log('PASS: homepage badges and member dialog resolve the shared main session while retaining order origin');

    rows = rows.map(r => ({ ...r, status: 'AwaitingPayment', changedAtUtc: '2026-10-15T02:00:00Z', group: { ...group, status: 'AwaitingPayment' } }));
    poll(); await tick();
    assert.equal(cards[1].dataset.status, 'AwaitingPayment');
    rows = rows.map(r => ({ ...r, status: 'Serving', changedAtUtc: '2026-10-15T01:00:00Z', group }));
    poll(); await tick();
    assert.equal(cards[1].dataset.status, 'Serving', 'Authoritative polling must accept reopening even when the derived timestamp decreases');
    console.log('PASS: homepage retains labels during payment and accepts reopened session state');

    rows = rows.map(r => ({ ...r, status: 'Serving', group: null }));
    poll(); await tick();
    assert.equal(cards[0].badge.hidden, true);
    assert.equal(cards[1].badge.hidden, true);
    assert.equal(cards[0].dataset.status, 'Serving');
    assert.equal(byId('details-group').hidden, true);
    rows = rows.map(r => ({ ...r, group: { ...group, label: 'N-G21' } }));
    poll(); await tick();
    assert.equal(cards[1].badge.textContent, 'Nhóm N-G21');
    assert.equal(byId('details-group-label').textContent, 'Nhóm N-G21');
    console.log('PASS: homepage removes labels on unmerge and refreshes a regrouped label while both tables stay Serving');

    rows = rows.map(r => ({ ...r, status: 'Cleaning', changedAtUtc: '2026-10-15T03:00:00Z', group: null }));
    poll(); await tick();
    assert.equal(cards[0].badge.hidden, true); assert.equal(cards[1].badge.hidden, true);
    assert.equal(byId('details-group').hidden, true);
    console.log('PASS: homepage polling removes both labels and clears the open group detail after checkout');
}
run().catch(error => { console.error(error); process.exitCode = 1; });
