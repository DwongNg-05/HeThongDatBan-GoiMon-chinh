const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/daily-reservations.js', 'utf8');
function setup(fetch, followToday = false) {
    const elements = {};
    function element() {
        return { hidden: false, children: [], dataset: {}, listeners: {}, textContent: '', value: '',
            append(child) { this.children.push(child); }, replaceChildren() { this.children = []; },
            addEventListener(event, callback) { this.listeners[event] = callback; }, setAttribute() {} };
    }
    const document = { getElementById(id) { return elements[id] ??= element(); }, createElement: element, addEventListener() {} };
    document.getElementById('daily-reservations').dataset = { url: '/Reservations/Daily', date: '2026-10-02', followToday: String(followToday) };
    elements.timers = [];
    vm.runInNewContext(source, { document, fetch, AbortController, Intl, Date,
        setTimeout(callback, delay) { elements.timers.push({ callback, delay }); return elements.timers.length; }, clearTimeout() {} });
    return elements;
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const response = reservations => ({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ reservations }) });
test('loading then all seven safe text fields, masked phone and unassigned table', async () => {
    let finish;
    const elements = setup(() => new Promise(resolve => { finish = resolve; }));
    assert.equal(elements['reservation-loading'].hidden, false);
    assert.equal(elements['reservation-results'].hidden, true);
    finish(response([{ code: 'ABC123', customerName: '<script>bad</script>', phone: '090****123', guestCount: 2,
        timeSlot: '08:00–09:30', tableName: 'Chưa xếp bàn', status: 'Chờ xác nhận' }]));
    await flush();
    assert.deepEqual(elements['reservation-rows'].children[0].children.map(cell => cell.textContent),
        ['ABC123', '<script>bad</script>', '090****123', 2, '08:00–09:30', 'Chưa xếp bàn', 'Chờ xác nhận']);
    assert.equal(elements['reservation-results'].hidden, false);
    assert.equal(elements['reservation-loading'].hidden, true);
});
test('empty day state', async () => {
    const elements = setup(async () => response([]));
    await flush();
    assert.equal(elements['reservation-empty'].hidden, false);
    assert.equal(elements['reservation-results'].hidden, true);
});
test('failed load and retry recover, with selected date preserved', async () => {
    let attempts = 0;
    const elements = setup(async url => {
        assert.equal(url, '/Reservations/Daily?date=2026-10-02');
        if (++attempts === 1) throw new Error('offline');
        return response([]);
    });
    await flush();
    assert.equal(elements['reservation-error'].hidden, false);
    elements['reservation-retry'].listeners.click();
    await flush();
    assert.equal(attempts, 2);
    assert.equal(elements['reservation-error'].hidden, true);
    assert.equal(elements['reservation-empty'].hidden, false);
});
test('expired login returning HTML shows retry error', async () => {
    const elements = setup(async () => ({ ok: true, headers: { get: () => 'text/html' } }));
    await flush();
    assert.equal(elements['reservation-error'].hidden, false);
});
test('each status change refreshes the current day, empty filtered message and clear filter', async () => {
    const urls = [];
    const elements = setup(async url => { urls.push(url); return response([]); });
    await flush();
    for (const status of ['Pending', 'Confirmed', 'Cancelled', 'NoShow']) {
        elements['reservation-status'].value = status;
        elements['reservation-status'].listeners.change();
        await flush();
        assert.equal(urls.at(-1), `/Reservations/Daily?date=2026-10-02&status=${status}`);
        assert.equal(elements['reservation-empty'].hidden, false);
        assert.match(elements['reservation-empty'].textContent, /phù hợp/);
    }
    elements['reservation-clear-filter'].listeners.click();
    await flush();
    assert.equal(elements['reservation-status'].value, '');
    assert.equal(urls.at(-1), '/Reservations/Daily?date=2026-10-02');
    assert.equal(elements['reservation-empty'].textContent, 'Chưa có lượt đặt bàn trong ngày này.');
});
test('retry keeps active status and date', async () => {
    const urls = [];
    let failed = false;
    const elements = setup(async url => {
        urls.push(url);
        if (url.includes('status=Confirmed') && !failed) { failed = true; throw new Error('offline'); }
        return response([]);
    });
    await flush();
    elements['reservation-status'].value = 'Confirmed';
    elements['reservation-status'].listeners.change();
    await flush();
    assert.equal(elements['reservation-error'].hidden, false);
    elements['reservation-retry'].listeners.click();
    await flush();
    assert.equal(urls.at(-1), '/Reservations/Daily?date=2026-10-02&status=Confirmed');
    assert.equal(elements['reservation-error'].hidden, true);
});
test('rapid filter changes ignore stale response', async () => {
    const requests = [];
    const elements = setup((url, options) => new Promise(resolve => requests.push({ url, options, resolve })));
    elements['reservation-status'].value = 'Pending';
    elements['reservation-status'].listeners.change();
    elements['reservation-status'].value = 'NoShow';
    elements['reservation-status'].listeners.change();
    assert.equal(requests[1].options.signal.aborted, true);
    requests[2].resolve(response([]));
    await flush();
    requests[1].resolve(response([{ code: 'STALE' }]));
    await flush();
    assert.equal(elements['reservation-rows'].children.length, 0);
    assert.equal(elements['reservation-empty'].hidden, false);
});
test('upcoming rows form first highlighted group with no duplicates and all fields', async () => {
    const make = (code, isUpcoming) => ({ code, isUpcoming, customerName: 'Khách', phone: '090****123', guestCount: 2,
        timeSlot: '15:00–16:30', tableName: 'A01', status: 'Đã xác nhận' });
    const elements = setup(async () => response([make('SOON01', true), make('SOON02', true), make('OTHER1', false)]));
    await flush();
    const rows = elements['reservation-rows'].children;
    assert.equal(rows.length, 5);
    assert.equal(rows[0].children[0].textContent, 'Sắp đến trong 30 phút tới');
    assert.equal(rows[1].className, 'table-warning');
    assert.equal(rows[2].className, 'table-warning');
    assert.equal(rows[3].children[0].textContent, 'Các lượt đặt còn lại');
    assert.deepEqual([rows[1], rows[2], rows[4]].map(row => row.children[0].textContent), ['SOON01', 'SOON02', 'OTHER1']);
    assert.ok([rows[1], rows[2], rows[4]].every(row => row.children.length === 7));
});
test('30 second refresh recomputes group and keeps filter without clearing visible rows', async () => {
    const urls = [];
    let upcoming = true;
    const elements = setup(async url => {
        urls.push(url);
        return response([{ code: 'SOON01', isUpcoming: upcoming }]);
    });
    await flush();
    elements['reservation-status'].value = 'Confirmed';
    elements['reservation-status'].listeners.change();
    await flush();
    const timer = elements.timers.at(-1);
    assert.equal(timer.delay, 30000);
    upcoming = false;
    timer.callback();
    assert.equal(elements['reservation-loading'].hidden, true);
    await flush();
    assert.equal(urls.at(-1), '/Reservations/Daily?date=2026-10-02&status=Confirmed');
    assert.equal(elements['reservation-status'].value, 'Confirmed');
    assert.equal(elements['reservation-rows'].children.length, 1);
    assert.equal(elements['reservation-rows'].children[0].className, undefined);
});
test('today mode follows server midnight and keeps filter, explicit past day remains fixed', async () => {
    const urls = [];
    let date = '2026-10-02';
    const elements = setup(async url => {
        urls.push(url);
        return { ...response([]), json: async () => ({ date, reservations: [], evaluatedAtUtc: date === '2026-10-02' ? '2026-10-02T16:59:00Z' : '2026-10-02T17:00:00Z' }) };
    }, true);
    await flush();
    elements['reservation-status'].value = 'Pending';
    elements['reservation-status'].listeners.change();
    await flush();
    date = '2026-10-03';
    elements.timers.at(-1).callback();
    await flush();
    assert.equal(urls.at(-1), '/Reservations/Daily?&status=Pending');
    assert.equal(elements['reservation-date'].value, '2026-10-03');
    assert.equal(elements['viewing-date'].textContent, '03/10/2026');
    assert.equal(elements['reservation-status'].value, 'Pending');
    elements['reservation-date'].value = '2026-10-01';
    elements['reservation-date-form'].listeners.submit({ preventDefault() {} });
    await flush();
    assert.equal(urls.at(-1), '/Reservations/Daily?date=2026-10-01&status=Pending');
});
