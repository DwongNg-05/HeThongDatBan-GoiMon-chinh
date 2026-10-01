const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../src/RestaurantManagement.Web/wwwroot/js/table-map-sync.js'), 'utf8');

function setup() {
    const events = {}, timers = new Map();
    let sequence = 0, interval, calls = 0, result = { cursor: '1', tables: [], syncedAtUtc: '2026-10-02T01:00:00Z' };
    let fail = false;
    const count = { textContent: '2' }, notice = { hidden: true }, clock = {};
    const cards = ['A01', 'A02'].map(code => {
        const classes = new Set(['status-available']);
        return { dataset: { tableCode: code, status: 'Available' }, label: {}, classes,
            classList: { add: (...values) => values.forEach(x => classes.add(x)), remove: (...values) => values.forEach(x => classes.delete(x)) },
            querySelector() { return this.label; }, setAttribute(name, value) { this[name] = value; },
            closest() { return area; }, offsetWidth: 100 };
    });
    const area = { querySelector: () => count, querySelectorAll: () => cards };
    const root = { dispatchEvent() {}, dataset: { cursor: '0', loadFailed: 'false', changesUrl: '/api/table-map/changes' }, querySelector: selector => selector === '.map-sync-notice' ? notice : clock, querySelectorAll: () => cards };
    const document = { hidden: false, querySelector: () => root, addEventListener: (name, callback) => events[name] = callback };
    const navigator = { onLine: true };
    const window = { scrollY: 700, addEventListener: (name, callback) => events[name] = callback };
    const context = { document, navigator, window, Map, Array, Date, Number, Object, AbortController, encodeURIComponent, CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        setInterval: callback => { interval = callback; return 1; }, clearInterval() {},
        setTimeout: callback => { timers.set(++sequence, callback); return sequence; }, clearTimeout: id => timers.delete(id),
        fetch: async () => { calls++; if (fail) throw Error('offline'); return { ok: true, status: 200, json: async () => result }; } };
    vm.runInNewContext(script, context);
    return { cards, count, notice, clock, window, document, navigator, timers, events, tick: () => interval(), get calls() { return calls; }, set result(value) { result = value; }, set fail(value) { fail = value; } };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const update = (code, status) => ({ code, status, capacity: 4 });

test('successive status changes restart highlighting without retaining either animation class', async () => {
    const ui = setup(); await flush();
    ui.result = { cursor: '2', tables: [update('A01', 'Serving')], syncedAtUtc: '2026-10-02T01:00:02Z' };
    ui.tick(); await flush();
    ui.result = { cursor: '3', tables: [update('A01', 'Cleaning')], syncedAtUtc: '2026-10-02T01:00:04Z' };
    ui.tick(); await flush();
    assert(!ui.cards[0].classes.has('table-just-changed'));
    assert(ui.cards[0].classes.has('table-just-changed-again'));
    assert.equal(ui.count.textContent, '1');
    for (const timer of ui.timers.values()) timer();
    assert(!ui.cards[0].classes.has('table-just-changed-again'));
});

test('only changed cards update; area count, labels, highlight and scroll are preserved', async () => {
    const ui = setup(); await flush();
    ui.result = { cursor: '2', tables: [update('A01', 'Serving')], syncedAtUtc: '2026-10-02T01:00:02Z' };
    ui.tick(); await flush();
    assert.equal(ui.cards[0].label.textContent, 'Đang phục vụ');
    assert(ui.cards[0].classes.has('status-serving'));
    assert(ui.cards[0].classes.has('table-just-changed'));
    assert.equal(ui.cards[1].dataset.status, 'Available');
    assert.equal(ui.cards[1].label.textContent, undefined);
    assert.equal(ui.count.textContent, '1');
    assert.equal(ui.window.scrollY, 700);
    for (const timer of ui.timers.values()) timer();
    assert(!ui.cards[0].classes.has('table-just-changed'));
});
test('multiple changes use last server state without replacing table elements', async () => {
    const ui = setup(); await flush(); const original = ui.cards[0];
    ui.result = { cursor: '4', tables: [update('A01', 'Reserved'), update('A02', 'Cleaning')], syncedAtUtc: '2026-10-02T01:00:04Z' };
    ui.tick(); await flush();
    assert.equal(ui.cards[0], original);
    assert.equal(ui.cards[0].label.textContent, 'Đã đặt trước');
    assert.equal(ui.cards[1].label.textContent, 'Đang dọn');
    assert.equal(ui.count.textContent, '0');
});
test('offline warning preserves last synchronization time; online automatically recovers', async () => {
    const ui = setup(); await flush(); const time = ui.clock.dateTime;
    ui.navigator.onLine = false; ui.events.offline(); ui.tick(); await flush();
    assert(!ui.notice.hidden); assert.match(ui.notice.textContent, /có thể đã cũ/);
    assert.equal(ui.clock.dateTime, time);
    ui.navigator.onLine = true; ui.events.online(); await flush();
    assert(ui.notice.hidden);
});
test('hidden tab stops fetching and returning triggers immediate synchronization', async () => {
    const ui = setup(); await flush(); const before = ui.calls;
    ui.document.hidden = true; ui.events.visibilitychange(); ui.tick(); await flush();
    assert.equal(ui.calls, before);
    ui.document.hidden = false; ui.events.visibilitychange(); await flush();
    assert.equal(ui.calls, before + 1);
});
test('request failure warns and subsequent poll recovers', async () => {
    const ui = setup(); await flush();
    ui.fail = true; ui.tick(); await flush(); assert(!ui.notice.hidden);
    ui.fail = false; ui.tick(); await flush(); assert(ui.notice.hidden);
});
