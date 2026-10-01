const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../src/RestaurantManagement.Web/wwwroot/js/table-details.js'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
const details = (overrides = {}) => ({ code: 'A01', area: 'Tầng một', capacity: 4, status: 'Serving', statusLabel: 'Đang phục vụ', hasActiveSession: true, currentGuestName: 'Khách mẫu', currentGuestPhone: '0900000001', currentGuestCount: 3, serviceStartedAtUtc: '2026-10-02T01:00:00Z', serviceElapsedMinutes: 20, currentSubtotal: 145000, subtotalExplanation: 'Tổng món tính tiền', upcomingReservation: null, upcomingReservationCount: 0, syncedAtUtc: '2026-10-02T01:20:00Z', ...overrides });
function setup() {
    const fields = new Map(), events = {}, dialogEvents = {}, mapEvents = {};
    let tick, result = details(), responseStatus = 200, pendingResponse = null, calls = 0;
    function field(name) {
        if (!fields.has(name)) fields.set(name, { textContent: '', hidden: true, events: {}, addEventListener(type, callback) { this.events[type] = callback; }, focus() { this.focused = true; } });
        return fields.get(name);
    }
    const buttons = ['A01','B01'].map(code => ({ dataset: { openDetails: code }, events: {}, addEventListener(type, callback) { this.events[type] = callback; }, focus() { this.focused = true; } }));
    const map = { dataset: { detailsUrl: '/api/table-map' }, querySelectorAll: () => buttons, addEventListener: (type, callback) => mapEvents[type] = callback };
    const dialog = { open: false,
        querySelector: selector => selector === '#snapshot-details-title' ? field('title') : field(selector.slice('[data-details-'.length, -1)),
        querySelectorAll: () => [], addEventListener: (type, callback) => dialogEvents[type] = callback,
        showModal() { this.open = true; }, close() { this.open = false; dialogEvents.close(); },
        getBoundingClientRect: () => ({ left: 10, right: 100, top: 10, bottom: 100 }) };
    const document = { hidden: false, querySelector: selector => selector === '.snapshot-map' ? map : dialog, addEventListener: (type, callback) => events[type] = callback };
    const context = { document, window: { addEventListener: (type, callback) => events[type] = callback }, Intl, Date, AbortController, encodeURIComponent,
        setInterval: callback => { tick = callback; return 1; }, clearInterval() {}, setTimeout: () => 1, clearTimeout() {},
        fetch: async () => { calls++; if (pendingResponse) return pendingResponse; return { status: responseStatus, ok: responseStatus === 200, json: async () => result }; } };
    vm.runInNewContext(script, context);
    return { field, buttons, dialog, document, events, mapEvents, dialogEvents, open: index => buttons[index ?? 0].events.click(), tick: () => tick(), get calls() { return calls; }, set result(value) { result = value; }, set status(value) { responseStatus = value; }, set pendingResponse(value) { pendingResponse = value; } };
}
test('serving table shows customer, start time and Vietnamese currency; zero is displayed', async () => {
    const ui = setup(); ui.open(); await flush();
    assert(ui.dialog.open); assert.equal(ui.field('guest').textContent, 'Khách mẫu');
    assert.equal(ui.field('phone').textContent, '0900000001');
    assert.match(ui.field('subtotal').textContent, /145\.000/);
    assert.match(ui.field('started').textContent, /8:00:00|08:00:00/);
    ui.result = details({ currentSubtotal: 0 }); ui.tick(); await flush();
    assert.match(ui.field('subtotal').textContent, /^0/);
});
test('nearest reservation and remaining count render for reserved table', async () => {
    const ui = setup(); ui.result = details({ status: 'Reserved', hasActiveSession: false, currentSubtotal: null, upcomingReservation: { customerName: 'Lượt gần nhất', phone: '0900000002', guestCount: 4, startsAtUtc: '2026-10-02T03:00:00Z' }, upcomingReservationCount: 3 });
    ui.open(); await flush();
    assert.equal(ui.field('reservation-name').textContent, 'Lượt gần nhất');
    assert.match(ui.field('reservation-more').textContent, /còn 2 lượt/);
    assert.equal(ui.field('subtotal').textContent, '—');
});
test('open details update after remote status change and remove finished customer', async () => {
    const ui = setup(); ui.open(); await flush();
    ui.result = details({ status: 'Cleaning', statusLabel: 'Đang dọn', hasActiveSession: false, currentGuestName: null, currentSubtotal: null });
    ui.mapEvents['tablemap:changed']({ detail: { code: 'A01' } }); await flush();
    assert.equal(ui.field('status').textContent, 'Đang dọn');
    assert.equal(ui.field('phone').textContent, '');
    assert.equal(ui.field('subtotal').textContent, '—');
    assert(ui.field('reservation-content').hidden);
});
test('close button and backdrop close dialog and restore focus; closed dialog stops polling', async () => {
    const ui = setup(); ui.open(); await flush(); ui.field('close').events.click();
    assert(!ui.dialog.open); assert(ui.buttons[0].focused);
    const calls = ui.calls; ui.tick(); await flush(); assert.equal(ui.calls, calls);
    ui.open(); await flush(); ui.dialogEvents.click({ target: ui.dialog, clientX: 0, clientY: 0 });
    assert(!ui.dialog.open);
});
test('load failure offers retry, recovers and authorization loss clears private data', async () => {
    const ui = setup(); ui.status = 503; ui.open(); await flush();
    assert.match(ui.field('message').textContent, /Không tải được/); assert(!ui.field('retry').hidden);
    ui.status = 200; ui.field('retry').events.click(); await flush(); assert(!ui.field('content').hidden);
    ui.status = 403; ui.tick(); await flush();
    assert(ui.field('content').hidden); assert.equal(ui.field('guest').textContent, ''); assert.equal(ui.field('subtotal').textContent, '');
    const before = ui.calls; ui.tick(); await flush(); assert.equal(ui.calls, before);
});
test('slow response from previously selected table never overwrites new table', async () => {
    const ui = setup(); let resolve;
    ui.pendingResponse = new Promise(done => { resolve = done; });
    ui.open(); ui.pendingResponse = null; ui.result = details({ code: 'B01', currentGuestName: 'Khách B' });
    ui.open(1); await flush();
    resolve({ status: 200, ok: true, json: async () => details() }); await flush();
    assert.equal(ui.field('title').textContent, 'Bàn B01'); assert.equal(ui.field('guest').textContent, 'Khách B');
});
test('hidden tab stops detail polling and returning or reconnecting refreshes', async () => {
    const ui = setup(); ui.open(); await flush(); const calls = ui.calls;
    ui.document.hidden = true; ui.events.visibilitychange(); ui.tick(); await flush(); assert.equal(ui.calls, calls);
    ui.document.hidden = false; ui.events.visibilitychange(); await flush(); assert.equal(ui.calls, calls + 1);
    ui.events.offline(); assert.match(ui.field('message').textContent, /có thể đã cũ/);
    ui.events.online(); await flush(); assert.equal(ui.field('message').textContent, 'Thông tin tự cập nhật khi bảng đang mở.');
});
