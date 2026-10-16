const { test } = require('node:test');
const assert = require('node:assert/strict');
const { startLiveReport, intervalMs, timeoutMs } = require('../src/RestaurantManagement.Web/wwwroot/js/daily-overview.js');

function fixture(query = '?from=2026-10-01&to=2026-10-15', initialState = 'ready') {
    let now = 0, serial = 0;
    const timers = new Map();
    const calls = [];
    let totals = 0, fail = false, pending = false;
    const listeners = {};
    const button = { hidden: true, addEventListener() {} };
    const status = {};
    const from = { value: '2026-10-15', defaultValue: '2026-10-15' };
    const to = { value: '2026-10-15', defaultValue: '2026-10-15' };
    const form = { addEventListener(name, fn) { listeners['form:' + name] = fn; }, checkValidity: () => true, contains: () => false,
        querySelector: selector => selector.includes('from') ? from : to };
    const scroll = { scrollLeft: 175 }, details = { open: true };
    const panel = { current: { dataset: { state: initialState }, total: 0, updatedAt: 0 }, setAttribute() {},
        querySelector(selector) { return selector === '.revenue-scroll' ? scroll : selector === 'details' ? details : this.current; },
        replaceChildren(snapshot) { this.current = snapshot; } };
    const root = { dataset: { enabled: 'true', endpoint: '/Reports/Today/Snapshot' },
        querySelector: selector => ({ '[data-report-panel]': panel, '[data-report-refresh-status]': status, '[data-report-refresh]': button, form })[selector] };
    function schedule(callback, delay, repeat) { const id = ++serial; timers.set(id, { callback, at: now + delay, delay, repeat }); return id; }
    const env = {
        document: { hidden: false, querySelector: () => root, importNode: node => node, addEventListener(name, fn) { listeners[name] = fn; } },
        window: { location: { search: query }, addEventListener(name, fn) { listeners[name] = fn; } },
        AbortController,
        DOMParser: class { parseFromString(html) { return { querySelector: () => JSON.parse(html) }; } },
        setTimeout: (fn, delay) => schedule(fn, delay, false), clearTimeout: id => timers.delete(id),
        setInterval: (fn, delay) => schedule(fn, delay, true), clearInterval: id => timers.delete(id),
        async fetch(url, options) {
            calls.push({ url, options, at: now });
            if (pending) return new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(new Error('Timeout'))));
            if (fail) throw new Error('Offline');
            return { ok: true, status: 200, redirected: false, headers: { get: () => '1' },
                text: async () => JSON.stringify({ dataset: { state: 'ready', from: '2026-10-16', to: '2026-10-16' }, total: totals, updatedAt: now }) };
        }
    };
    const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
    async function advance(ms) {
        const end = now + ms;
        for (;;) {
            const next = [...timers.entries()].filter(([, t]) => t.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
            if (!next) break;
            const [id, timer] = next; now = timer.at;
            if (timer.repeat) timer.at += timer.delay; else timers.delete(id);
            timer.callback(); await flush();
        }
        now = end; await flush();
    }
    const live = startLiveReport(env);
    return { live, panel, status, calls, listeners, scroll, details, from, to, advance, flush,
        setTotals(value) { totals = value; }, setFail(value) { fail = value; }, setPending(value) { pending = value; },
        setFetch(fn) { env.fetch = fn; }, setQuery(value) { env.window.location.search = value; } };
}

test('a committed invoice is displayed within one polling interval (under 60 seconds)', async () => {
    const f = fixture();
    await f.advance(1); f.setTotals(100000); const committedAt = 1;
    await f.advance(intervalMs);
    assert.equal(f.panel.current.total, 100000);
    assert.ok(f.panel.current.updatedAt - committedAt <= 60000);
    assert.equal(f.calls[0].options.cache, 'no-store');
    assert.equal(f.calls[0].options.credentials, 'same-origin');
});
test('multiple invoices use absolute totals without duplicate accumulation', async () => {
    const f = fixture(); f.setTotals(300000); await f.advance(intervalMs);
    f.setTotals(450000); await f.advance(intervalMs); await f.advance(intervalMs);
    assert.equal(f.panel.current.total, 450000);
    assert.equal(f.calls.length, 3);
});
test('refresh keeps the applied range, expanded table and chart scroll position', async () => {
    const f = fixture(); await f.advance(intervalMs);
    assert.equal(f.calls[0].url, '/Reports/Today/Snapshot?from=2026-10-01&to=2026-10-15');
    assert.equal(f.scroll.scrollLeft, 175); assert.equal(f.details.open, true);
});
test('navigation to another range cancels old requests and prevents late results', async () => {
    const old = fixture(); old.setPending(true); old.live.refresh(); old.listeners['form:submit']();
    await old.flush(); assert.equal(old.panel.current.total, 0);
    assert.equal(old.calls[0].options.signal.aborted, true);
    const next = fixture('?from=2026-09-01&to=2026-09-02'); await next.advance(intervalMs);
    assert.equal(next.calls[0].url, '/Reports/Today/Snapshot?from=2026-09-01&to=2026-09-02');
});
test('failure keeps the last successful snapshot and recovers on the next interval', async () => {
    const f = fixture(); f.setTotals(50000); await f.advance(intervalMs);
    const updated = f.panel.current.updatedAt;
    f.setFail(true); await f.advance(intervalMs);
    assert.match(f.status.textContent, /Không thể cập nhật/);
    assert.equal(f.panel.current.updatedAt, updated); assert.equal(f.panel.current.total, 50000);
    f.setFail(false); f.setTotals(80000); await f.advance(intervalMs);
    assert.equal(f.panel.current.total, 80000); assert.equal(f.status.className, 'text-muted');
});
test('a stuck request times out and cannot block the next update', async () => {
    const f = fixture(); f.setPending(true); await f.advance(intervalMs);
    await f.advance(timeoutMs);
    assert.match(f.status.textContent, /Không thể cập nhật/);
    f.setPending(false); f.setTotals(100); await f.advance(intervalMs);
    assert.equal(f.panel.current.total, 100);
});
test('focus/online/visible events update immediately without concurrent requests', async () => {
    const f = fixture(); f.setPending(true); f.listeners.focus(); f.listeners.online();
    assert.equal(f.calls.length, 1);
    await f.advance(timeoutMs); f.setPending(false); f.setTotals(500); f.listeners.visibilitychange(); await f.flush();
    assert.equal(f.panel.current.total, 500);
});
test('today rolls forward without overwriting unsaved date input', async () => {
    const f = fixture(''); await f.advance(intervalMs);
    assert.equal(f.from.value, '2026-10-16');
    f.from.value = '2026-01-01'; await f.advance(intervalMs);
    assert.equal(f.from.value, '2026-01-01');
});
test('initial error retries immediately; an empty report can become populated', async () => {
    const f = fixture('', 'error'); await f.flush();
    assert.equal(f.calls.length, 1); assert.equal(f.panel.current.dataset.state, 'ready');
    f.setTotals(75000); await f.advance(intervalMs); assert.equal(f.panel.current.total, 75000);
});
test('login redirects are never inserted as report HTML', async () => {
    const f = fixture();
    f.setFetch(async () => ({ status: 200, redirected: true }));
    await f.advance(intervalMs);
    assert.match(f.status.textContent, /đăng nhập/); assert.equal(f.panel.current.total, 0);
});
test('expired authorization stops requests until the page is reauthenticated', async () => {
    const f = fixture(); let requests = 0;
    f.setFetch(async () => { requests++; return { status: 401, redirected: false }; });
    await f.advance(intervalMs); f.listeners.focus(); await f.advance(intervalMs * 3);
    assert.equal(requests, 1);
    assert.match(f.status.textContent, /đăng nhập/);
});
