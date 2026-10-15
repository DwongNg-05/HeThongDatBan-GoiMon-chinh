// Run the production polling script against a small DOM adapter, without a browser dependency.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const events = [];
function card(code) {
    const badge = { hidden: true, textContent: '', title: '' };
    const status = { textContent: 'Đang phục vụ' };
    return {
        dataset: { tableCode: code, status: 'Serving', groupKey: 'null' }, badge,
        querySelector: selector => selector === '.table-group-label' ? badge : status,
        classList: { add() {}, remove() {} }, setAttribute() {}, closest: () => null
    };
}
const cards = ['A', 'B', 'C', 'D'].map(card);
const notice = { hidden: true, textContent: '' };
const root = {
    dataset: { cursor: '0', changesUrl: '/api/table-map/changes' },
    querySelector: selector => selector === '.map-sync-notice' ? notice : null,
    querySelectorAll: () => cards, dispatchEvent: event => events.push(event)
};
const group1 = { label: 'N-11', mainSessionId: 11, mainTableCode: 'A', memberTableCodes: ['A', 'B'], status: 'Open' };
const group2 = { label: 'N-12', mainSessionId: 12, mainTableCode: 'C', memberTableCodes: ['C', 'D'], status: 'Open' };
let payload = { cursor: '0', tables: cards.map((c, i) => ({ code: c.dataset.tableCode, capacity: 4, status: 'Serving', group: i < 2 ? group1 : group2 })) };
let poll;
const context = {
    document: { hidden: false, querySelector: () => root, addEventListener() {} },
    window: { addEventListener() {}, setTimeout: () => 1, clearTimeout() {}, setInterval: fn => { poll = fn; return 1; }, clearInterval() {} },
    fetch: async () => ({ ok: true, status: 200, redirected: false, json: async () => payload }),
    AbortController, CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
    JSON, Date, Number, String, Object, Map, Array, encodeURIComponent
};
const tick = () => new Promise(resolve => setImmediate(resolve));
async function run() {
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../src/RestaurantManagement.Web/wwwroot/js/table-map-sync.js'), 'utf8'), context);
    await tick();
    assert.equal(cards[0].badge.textContent, 'Nhóm N-11');
    assert.equal(cards[1].badge.textContent, cards[0].badge.textContent);
    assert.notEqual(cards[2].badge.textContent, cards[0].badge.textContent);
    assert.equal(cards[0].dataset.status, 'Serving');
    assert.equal(cards[0].badge.hidden, false);
    assert.equal(cards[1].dataset.mainSessionId, '11');
    assert.equal(events.length, 4, 'Group-only updates notify an open detail panel even though status is unchanged');
    assert.match(cards[1].badge.title, /A, B.*Bàn chính: A/);
    console.log('PASS: same-status polling updates both labels, membership and main-session destination');

    const priorEvents = events.length;
    poll(); await tick();
    assert.equal(events.length, priorEvents, 'Unchanged group data does not continually refresh detail panels');
    console.log('PASS: repeated identical polls do not produce redundant change events');

    payload = { cursor: '1', tables: ['A', 'B'].map(code => ({ code, capacity: 4, status: 'AwaitingPayment', group: { ...group1, status: 'AwaitingPayment' } })) };
    poll(); await tick();
    assert.equal(cards[0].badge.textContent, 'Nhóm N-11');
    assert.equal(cards[1].dataset.status, 'AwaitingPayment');
    console.log('PASS: awaiting-payment state retains the shared label');

    payload = { cursor: '2', tables: ['A', 'B'].map(code => ({ code, capacity: 4, status: 'Cleaning', group: null })) };
    poll(); await tick();
    for (const c of cards.slice(0, 2)) {
        assert.equal(c.badge.hidden, true);
        assert.equal(c.badge.textContent, '');
        assert.equal(c.dataset.mainSessionId, '');
        assert.equal(c.dataset.status, 'Cleaning');
    }
    assert.equal(cards[2].badge.hidden, false, 'Other serving groups are unaffected');
    assert.equal(notice.hidden, true);
    console.log('PASS: checkout removes both labels and clears stale main-session links');
}
run().catch(error => { console.error(error); process.exitCode = 1; });
