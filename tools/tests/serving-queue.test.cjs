// Món chờ mang ra (wwwroot/js/serving-queue.js): đồng hồ chờ, cảnh báo quá 5 phút ngay trên danh sách, giữ thứ tự theo lúc hoàn thành,
// gỡ món đã mang ra. Chạy: node --test tools/tests/serving-queue.test.cjs (từ thư mục gốc của repo, không cần cài gói nào).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/serving-queue.js', 'utf8');

const MIN = 60 * 1000;

// ---- DOM giả tối thiểu (đủ cho các thao tác serving-queue.js dùng) ----
const camel = name => name.replace(/-([a-z])/g, (_, c) => c.toUpperCase());
const kebab = name => name.replace(/[A-Z]/g, c => '-' + c.toLowerCase());
class El {
    constructor(tag, attrs = {}, children = []) {
        this.tagName = tag.toUpperCase();
        this.attrs = { ...attrs };
        this.children = [];
        this.parent = null;
        this.text = '';
        this.hidden = 'hidden' in attrs;
        delete this.attrs.hidden;
        this.classes = new Set((attrs.class ?? '').split(/\s+/).filter(Boolean));
        this.listeners = {};
        this.disabled = false;
        children.forEach(child => this.appendChild(child));
        const self = this;
        this.dataset = new Proxy({}, {
            get: (_, key) => self.attrs['data-' + kebab(String(key))],
            set: (_, key, value) => { self.attrs['data-' + kebab(String(key))] = String(value); return true; }
        });
        this.classList = {
            toggle(name, on) { if (on) self.classes.add(name); else self.classes.delete(name); },
            contains: name => self.classes.has(name)
        };
    }
    get textContent() { return this.text + this.children.map(c => c.textContent).join(''); }
    set textContent(value) { this.text = String(value); this.children = []; }
    get firstElementChild() { return this.children[0] ?? null; }
    set className(value) { this.classes = new Set(String(value).split(/\s+/).filter(Boolean)); }
    setAttribute(name, value) { this.attrs[name] = String(value); }
    getAttribute(name) { return this.attrs[name] ?? null; }
    appendChild(child) { child.remove(); child.parent = this; this.children.push(child); return child; }
    insertBefore(child, before) {
        child.remove();
        child.parent = this;
        const index = before ? this.children.indexOf(before) : -1;
        if (index < 0) this.children.push(child); else this.children.splice(index, 0, child);
        return child;
    }
    remove() { if (this.parent) { this.parent.children.splice(this.parent.children.indexOf(this), 1); this.parent = null; } }
    matches(selector) {
        const m = /^(\w+)?(?:\[([\w-]+)(?:="([^"]*)")?\])?$/.exec(selector);
        if (!m) throw new Error('Unsupported selector ' + selector);
        const [, tag, attr, value] = m;
        if (tag && this.tagName !== tag.toUpperCase()) return false;
        if (attr && !(attr in this.attrs)) return false;
        if (attr && value !== undefined && this.attrs[attr] !== value) return false;
        return true;
    }
    *descendants() { for (const child of this.children) { yield child; yield* child.descendants(); } }
    querySelector(selector) { for (const el of this.descendants()) if (el.matches(selector)) return el; return null; }
    querySelectorAll(selector) { return [...this.descendants()].filter(el => el.matches(selector)); }
    closest(selector) { for (let el = this; el; el = el.parent) if (el.matches(selector)) return el; return null; }
    addEventListener(event, callback) { this.listeners[event] = callback; }
    cloneNode() {
        const copy = new El(this.tagName.toLowerCase(), { ...this.attrs, class: [...this.classes].join(' ') }, this.children.map(c => c.cloneNode(true)));
        copy.text = this.text;
        copy.hidden = this.hidden;
        return copy;
    }
}
const h = (tag, attrs, ...children) => new El(tag, attrs ?? {}, children);

/** Một dòng như Views/Serving/Index.cshtml (dòng máy chủ dựng sẵn hoặc mẫu <template> khi item rỗng). */
function rowElement(item) {
    const attrs = item
        ? { 'data-serving-item': String(item.id), 'data-ready-at-utc': item.readyAtUtc, 'data-wait-ms': String(item.waitMs), 'data-overdue': String(item.overdue) }
        : { 'data-serving-item': '', 'data-ready-at-utc': '', 'data-wait-ms': '0', 'data-overdue': 'false' };
    if (item?.overdue) attrs.class = 'table-danger serving-overdue';
    const badge = item?.overdue ? {} : { hidden: '' };
    const row = h('tr', attrs,
        h('td', { 'data-cell': 'position' }), h('td', { 'data-cell': 'table' }), h('td', { 'data-cell': 'name' }),
        h('td', { 'data-cell': 'quantity' }), h('td', { 'data-cell': 'notes' }), h('td', { 'data-cell': 'ready-at' }, h('time')),
        h('td', {}, h('span', { 'data-wait': '' }), h('span', { 'data-overdue-badge': '', ...badge })),
        h('td', {}, h('form', { 'data-serve-form': '', action: item ? `/Serving/Serve/${item.id}` : '' }, h('button'))));
    if (item) row.querySelector('[data-cell="table"]').textContent = item.tableCode;
    return row;
}

function setup({ rows = [], fetch = async () => { throw new Error('offline'); } } = {}) {
    const clock = { now: 1_000_000 };
    const tbody = h('tbody', { 'data-serving-rows': '' }, ...rows.map(rowElement));
    const root = h('div', {
        'data-screen': 'serving-queue', 'data-queue-url': '/Serving/Queue', 'data-serve-url': '/Serving/Serve/',
        'data-refresh-ms': '3000', 'data-overdue-ms': String(5 * MIN)
    },
        h('strong', { 'data-waiting-count': '' }), h('strong', { 'data-overdue-count': '' }),
        h('p', { 'data-serving-empty': '' }), h('table', { 'data-serving-table': '' }, tbody));
    const template = h('template', { id: 'serving-row-template' });
    template.content = h('fragment', {}, rowElement(null));
    const status = h('div', { id: 'serving-status', hidden: '' });
    const notices = h('div', { id: 'serving-notices' });
    const ids = { 'serving-row-template': template, 'serving-status': status, 'serving-notices': notices };
    const timers = [];
    const page = { reloaded: false };
    const document = {
        hidden: false, listeners: {},
        querySelector: selector => (root.matches(selector) ? root : null),
        getElementById: id => ids[id] ?? null,
        createElement: tag => h(tag),
        addEventListener(event, callback) { this.listeners[event] = callback; }
    };
    const context = {
        document, fetch, Date, Number, String, Math, Map, Set, Promise,
        performance: { now: () => clock.now },
        location: { reload() { page.reloaded = true; } },
        FormData: class { constructor(form) { this.form = form; } },
        setInterval(callback, delay) { timers.push({ callback, delay }); return timers.length; },
        setTimeout(callback, delay) { timers.push({ callback, delay, once: true }); return timers.length; }
    };
    context.globalThis = context;
    vm.runInNewContext(source, context);
    const tick = ms => { clock.now += ms; timers.filter(t => t.delay === 1000).forEach(t => t.callback()); };
    const refresh = async () => { timers.filter(t => t.delay === 3000 && !t.once).forEach(t => t.callback()); await flush(); };
    const expire = () => timers.filter(t => t.once).splice(0).forEach(t => t.callback());
    const shown = () => tbody.children.map(row => ({
        id: Number(row.dataset.servingItem),
        position: row.querySelector('[data-cell="position"]').textContent,
        table: row.querySelector('[data-cell="table"]').textContent,
        wait: row.querySelector('[data-wait]').textContent,
        overdue: row.dataset.overdue === 'true' && row.classList.contains('table-danger') && row.classList.contains('serving-overdue')
            && !row.querySelector('[data-overdue-badge]').hidden,
        plain: row.dataset.overdue === 'false' && !row.classList.contains('table-danger') && row.querySelector('[data-overdue-badge]').hidden
    }));
    const noticeTexts = () => notices.children.map(n => n.textContent);
    return { context, root, tbody, status, notices, clock, timers, page, tick, refresh, expire, shown, noticeTexts, logic: context.ServingQueueLogic };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const json = (body, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const item = (id, tableCode, readyAtUtc, waitMs) => ({ id, tableCode, itemName: `Món ${id}`, unit: 'phần', quantity: 1, notes: null, readyAtUtc, readyAt: '18:00', waitMs });

test('pure rules: wait formatting and the strict 5-minute threshold', () => {
    const { logic } = setup();
    assert.equal(logic.formatWait(0), '0:00');
    assert.equal(logic.formatWait(4 * MIN + 59_999), '4:59');
    assert.equal(logic.formatWait(83 * MIN + 4000), '1:23:04');
    assert.equal(logic.isOverdue(5 * MIN - 1, 5 * MIN), false);
    assert.equal(logic.isOverdue(5 * MIN, 5 * MIN), false);
    assert.equal(logic.isOverdue(5 * MIN + 1, 5 * MIN), true);
    assert.equal(logic.waitMs({ waitMs: 1000, receivedAt: 500 }, 400), 1000, 'never counts backwards');
});

test('a dish waiting under 5 minutes is not highlighted', () => {
    const page = setup({ rows: [{ id: 1, tableCode: 'A01', readyAtUtc: '2026-10-11T10:56:00Z', waitMs: 4 * MIN, overdue: false }] });
    page.tick(1000);
    assert.deepEqual(page.shown().map(r => [r.wait, r.plain]), [['4:01', true]]);
    assert.equal(page.root.querySelector('[data-overdue-count]').textContent, '0');
});

test('the highlight turns on in place as soon as a dish passes 5 minutes, without reordering', () => {
    const page = setup({ rows: [
        { id: 7, tableCode: 'A01', readyAtUtc: '2026-10-11T10:55:02Z', waitMs: 4 * MIN + 58_000, overdue: false },
        { id: 3, tableCode: 'B02', readyAtUtc: '2026-10-11T10:59:00Z', waitMs: 1 * MIN, overdue: false }
    ] });
    page.tick(1000);
    assert.deepEqual(page.shown().map(r => [r.id, r.wait, r.plain]), [[7, '4:59', true], [3, '1:01', true]]);
    page.tick(1001);
    assert.deepEqual(page.shown().map(r => [r.id, r.position, r.wait, r.overdue]), [[7, '1', '5:00', true], [3, '2', '1:02', false]]);
    assert.equal(page.root.querySelector('[data-overdue-count]').textContent, '1');
    page.tick(4 * MIN);
    assert.deepEqual(page.shown().map(r => [r.id, r.overdue]), [[7, true], [3, true]], 'second dish also highlighted later, same order');
});

test('a dish already overdue when the page opens stays highlighted', () => {
    const page = setup({ rows: [{ id: 2, tableCode: 'C03', readyAtUtc: '2026-10-11T10:50:00Z', waitMs: 10 * MIN, overdue: true }] });
    assert.deepEqual(page.shown().map(r => [r.wait, r.overdue]), [['10:00', true]]);
});

test('refresh adds new dishes, removes served ones, keeps completion order and recomputes waits', async () => {
    let response;
    const page = setup({
        rows: [
            { id: 1, tableCode: 'A01', readyAtUtc: '2026-10-11T10:54:00Z', waitMs: 6 * MIN, overdue: true },
            { id: 2, tableCode: 'A02', readyAtUtc: '2026-10-11T10:57:00Z', waitMs: 3 * MIN, overdue: false }
        ],
        fetch: async url => { assert.equal(url, '/Serving/Queue'); return json(response); }
    });
    // Món 1 đã được người khác mang ra; món 4 và 3 vừa xong (máy chủ trả lộn xộn); đồng hồ máy chủ: món 2 đã chờ 4:30.
    response = { items: [
        item(4, 'C04', '2026-10-11T10:59:30Z', 0),
        item(2, 'A02', '2026-10-11T10:57:00Z', 4 * MIN + 30_000),
        item(3, 'B03', '2026-10-11T10:59:30Z', 0)
    ] };
    await page.refresh();
    assert.deepEqual(page.shown().map(r => [r.id, r.position, r.table, r.wait]), [[2, '1', 'A02', '4:30'], [3, '2', 'B03', '0:00'], [4, '3', 'C04', '0:00']]);
    assert.ok(page.shown().every(r => r.plain), 'served dish 1 is gone, so no warning is left');
    const added = page.tbody.children[1];
    assert.equal(added.querySelector('[data-serve-form]').getAttribute('action'), '/Serving/Serve/3');
    assert.equal(added.querySelector('[data-cell="name"]').textContent, 'Món 3');
    assert.equal(page.root.querySelector('[data-waiting-count]').textContent, '3');
    page.tick(31_000);
    assert.deepEqual(page.shown().map(r => [r.id, r.wait, r.overdue]), [[2, '5:01', true], [3, '0:31', false], [4, '0:31', false]]);
    // Lần tải sau: danh sách trống → hiện thông báo trống.
    response = { items: [] };
    await page.refresh();
    assert.equal(page.tbody.children.length, 0);
    assert.equal(page.root.querySelector('[data-serving-empty]').hidden, false);
    assert.equal(page.root.querySelector('[data-serving-table]').hidden, true);
});

test('"Đã mang ra" removes the dish at once and a stale refresh does not bring it back', async () => {
    const calls = [];
    const stale = { items: [item(1, 'A01', '2026-10-11T10:54:00Z', 7 * MIN), item(2, 'A02', '2026-10-11T10:58:00Z', 2 * MIN)] };
    const page = setup({
        rows: [
            { id: 1, tableCode: 'A01', readyAtUtc: '2026-10-11T10:54:00Z', waitMs: 6 * MIN, overdue: true },
            { id: 2, tableCode: 'A02', readyAtUtc: '2026-10-11T10:58:00Z', waitMs: 2 * MIN, overdue: false }
        ],
        fetch: async (url, options) => {
            calls.push([url, options?.method ?? 'GET', options?.headers?.['X-Requested-With']]);
            return url === '/Serving/Queue' ? json(stale) : json({ id: 1, served: true, message: 'Đã mang món ra.' });
        }
    });
    const form = page.tbody.children[0].querySelector('[data-serve-form]');
    let prevented = false;
    await page.tbody.listeners.submit({ target: form, preventDefault() { prevented = true; } });
    assert.ok(prevented);
    assert.deepEqual(calls[0], ['/Serving/Serve/1', 'POST', 'XMLHttpRequest']);
    assert.deepEqual(page.shown().map(r => [r.id, r.position]), [[2, '1']]);
    assert.equal(page.root.querySelector('[data-overdue-count]').textContent, '0', 'served dish no longer warned');
    assert.equal(page.status.textContent, 'Đã mang món ra.');
    await page.refresh();
    assert.deepEqual(page.shown().map(r => r.id), [2]);
});

test('a failed serve keeps the dish and re-enables the button', async () => {
    const page = setup({
        rows: [{ id: 5, tableCode: 'A05', readyAtUtc: '2026-10-11T10:58:00Z', waitMs: 2 * MIN, overdue: false }],
        fetch: async () => json({ message: 'Lỗi máy chủ' }, 500)
    });
    const form = page.tbody.children[0].querySelector('[data-serve-form]');
    await page.tbody.listeners.submit({ target: form, preventDefault() {} });
    assert.deepEqual(page.shown().map(r => r.id), [5]);
    assert.equal(form.querySelector('button').disabled, false);
    assert.equal(page.status.hidden, false);
});

test('offline refresh keeps the list and the clock keeps running; expired login reloads to sign in', async () => {
    const page = setup({ rows: [{ id: 9, tableCode: 'B09', readyAtUtc: '2026-10-11T10:56:00Z', waitMs: 4 * MIN + 59_000, overdue: false }] });
    await page.refresh();
    page.tick(2000);
    assert.deepEqual(page.shown().map(r => [r.id, r.wait, r.overdue]), [[9, '5:01', true]]);
    const expired = setup({ fetch: async () => json({}, 401) });
    await expired.refresh();
    assert.equal(expired.page.reloaded, true);
});

// ---- S3-08 Task 1 ----
test('S3-08: a newly completed dish is shown with a notice (table code + dish name) at the next 3-second refresh, within 5 seconds', async () => {
    let items = [];
    const page = setup({ fetch: async () => json({ items }) });
    const refreshTimer = page.timers.find(t => t.delay === 3000 && !t.once);
    assert.ok(refreshTimer && refreshTimer.delay < 5000, 'refresh runs often enough to show a new dish within 5 seconds');
    await page.refresh();
    assert.deepEqual(page.noticeTexts(), [], 'no notice while nothing is ready');
    // Bếp bấm "Đã xong" ngay sau lần tải trước → lần tải kế tiếp (≤ 3 giây sau) đã có món và thông báo.
    items = [{ ...item(8, 'A05', '2026-10-11T11:00:00Z', 200), itemName: 'Phở bò', quantity: 2 }];
    page.clock.now += 3000;
    await page.refresh();
    assert.deepEqual(page.noticeTexts(), ['Món mới xong: Bàn A05 · Phở bò × 2']);
    assert.deepEqual(page.shown().map(r => [r.id, r.table]), [[8, 'A05']]);
    assert.equal(page.tbody.children[0].querySelector('[data-cell="name"]').textContent, 'Phở bò');
    assert.ok(page.tbody.children[0].classList.contains('serving-new'));
    // Lần tải sau vẫn có món đó: không thông báo lại.
    await page.refresh();
    assert.equal(page.noticeTexts().length, 1);
    page.expire();
    assert.deepEqual(page.noticeTexts(), [], 'notice hides itself after a while');
    assert.ok(!page.tbody.children[0].classList.contains('serving-new'));
});

test('S3-08: dishes already on the page are not announced; several new dishes are announced in completion order', async () => {
    let items = [item(1, 'A01', '2026-10-11T10:50:00Z', 10 * MIN)];
    const page = setup({ rows: [{ id: 1, tableCode: 'A01', readyAtUtc: '2026-10-11T10:50:00Z', waitMs: 10 * MIN, overdue: true }], fetch: async () => json({ items }) });
    items = [item(3, 'C03', '2026-10-11T11:00:02Z', 0), items[0], item(2, 'B02', '2026-10-11T11:00:01Z', 1000)];
    await page.refresh();
    assert.deepEqual(page.shown().map(r => r.id), [1, 2, 3], 'list sorted by completion time, earliest first');
    // Thông báo mới nhất ở trên cùng.
    assert.deepEqual(page.noticeTexts(), ['Món mới xong: Bàn C03 · Món 3', 'Món mới xong: Bàn B02 · Món 2']);
    // Món 2 được mang ra ở máy khác → lần tải sau gỡ cả dòng và thông báo của món đó.
    items = items.filter(i => i.id !== 2);
    await page.refresh();
    assert.deepEqual(page.shown().map(r => r.id), [1, 3]);
    assert.deepEqual(page.noticeTexts(), ['Món mới xong: Bàn C03 · Món 3']);
});

test('S3-08: when another waiter already served the dish, the row is removed and a warning names who served it', async () => {
    const page = setup({
        rows: [{ id: 6, tableCode: 'B06', readyAtUtc: '2026-10-11T10:58:00Z', waitMs: 2 * MIN, overdue: false }],
        fetch: async () => json({ id: 6, served: false, alreadyServed: true, message: 'Bàn B06 · Món 6 đã được Nguyễn An mang ra lúc 18:01. Không cần mang lại.' }, 409)
    });
    await page.tbody.listeners.submit({ target: page.tbody.children[0].querySelector('[data-serve-form]'), preventDefault() {} });
    assert.deepEqual(page.shown(), []);
    assert.equal(page.status.textContent, 'Bàn B06 · Món 6 đã được Nguyễn An mang ra lúc 18:01. Không cần mang lại.');
    assert.ok(page.status.classList.contains('alert-warning') && !page.status.classList.contains('alert-success'));
    assert.equal(page.status.hidden, false);
});
