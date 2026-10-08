const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

class Element {
    constructor(tag = 'div') { this.tag = tag; this.children = []; this.dataset = {}; this.handlers = {}; }
    append(...items) { this.children.push(...items); }
    replaceChildren() { this.children = []; this.textContent = ''; }
    addEventListener(name, handler) { this.handlers[name] = handler; }
    get childElementCount() { return this.children.length; }
}
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/kitchen.js', 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));
async function session(canEdit) {
    const elements = Object.fromEntries(['kitchen-board', 'kitchen-message', 'kitchen-queue', 'kitchen-ready'].map(id => [id, new Element()]));
    elements['kitchen-board'].dataset.canEdit = String(canEdit);
    let lines = [
        { id: 1, batchId: 1, tableCode: 'A01', itemName: '<img src=x>', quantity: 2, status: 'Pending', version: 'first' },
        { id: 2, batchId: 1, tableCode: 'A01', itemName: 'Canh', quantity: 1, status: 'Pending', version: 'second' }
    ];
    let confirm = true;
    let conflict = false;
    let posts = 0;
    let timer;
    let timerTick;
    let monotonic = 0;
    vm.runInNewContext(source, {
        document: { getElementById: id => elements[id], createElement: tag => new Element(tag), querySelector: () => ({ value: 'csrf' }) },
        window: { confirm: () => confirm }, URLSearchParams,
        setTimeout: callback => { timer = callback; }, setInterval: callback => { timerTick = callback; }, performance: { now: () => monotonic },
        fetch: async (url, options) => {
            if (options.method === 'POST') {
                posts++;
                const data = options.body;
                assert.equal(data.get('__RequestVerificationToken'), 'csrf');
                if (conflict) return { ok: false, json: async () => ({ message: 'Món đã được thiết bị khác cập nhật.' }) };
                if (url === '/Kitchen/CompleteBatch') {
                    lines = lines.map(line => line.batchId === Number(data.get('batchId')) && line.status === 'Preparing'
                        ? { ...line, status: 'Ready', actualCookingMilliseconds: 65000, elapsedCookingMilliseconds: null } : line);
                    return { ok: true, json: async () => ({ message: 'Đã hoàn thành phiếu.' }) };
                }
                lines = lines.map(line => String(line.id) === data.get('id') ? { ...line, status: data.get('to'), version: 'new' + posts,
                    preparingAt: '2026-10-08T00:00:00Z', waitingMilliseconds: 120000,
                    elapsedCookingMilliseconds: data.get('to') === 'Preparing' ? 2000 : null,
                    actualCookingMilliseconds: data.get('to') === 'Ready' ? 65000 : null } : line);
                return { ok: true };
            }
            return { ok: true, json: async () => lines };
        }
    });
    await tick();
    return { elements, cancel: () => { confirm = false; }, allow: () => { confirm = true; },
        conflict: () => { conflict = true; }, posts: () => posts, poll: async () => { await timer(); },
        advanceClock: ms => { monotonic += ms; timerTick(); } };
}
(async () => {
    const s = await session(true);
    const cards = element => element.children.flatMap(section => section.children.filter(child => child.tag === 'article'));
    const queue = { get children() { return cards(s.elements['kitchen-queue']); }, get childElementCount() { return this.children.length; } };
    const ready = s.elements['kitchen-ready'];
    assert.equal(queue.childElementCount, 2);
    assert.equal(s.elements['kitchen-queue'].children[0].children[1].disabled, true, 'Pending blocks batch completion');
    assert.equal(queue.children[0].children[0].textContent.includes('<img src=x>'), true);
    assert.equal(queue.children[0].children[2].textContent, 'Bắt đầu');
    s.cancel();
    await queue.children[0].children[2].handlers.click();
    assert.equal(s.posts(), 0, 'Cancel sends no transition');
    s.allow();
    await queue.children[0].children[2].handlers.click();
    assert.equal(queue.children[0].children[1].textContent, 'Đang chế biến');
    assert.equal(queue.children[1].children[1].textContent, 'Chờ bếp', 'Sibling unchanged');
    assert.equal(queue.children[0].children[2].textContent, 'Xong');
    assert.equal(queue.children[0].children[3].textContent, 'Đã chế biến: 0:02');
    s.advanceClock(3000);
    assert.equal(queue.children[0].children[3].textContent, 'Đã chế biến: 0:05', 'Timer increases without a snapshot or device clock');
    await queue.children[0].children[2].handlers.click();
    assert.equal(queue.childElementCount, 1);
    assert.equal(ready.childElementCount, 1);
    assert.equal(ready.children[0].children[1].textContent, 'Đã xong');
    assert.equal(ready.children[0].children[2].textContent, 'Chế biến thực tế: 1:05');
    s.advanceClock(60000);
    assert.equal(ready.children[0].children[2].textContent, 'Chế biến thực tế: 1:05', 'Ready duration stops growing');
    await s.poll();
    assert.equal(ready.childElementCount, 1, 'Repeated snapshot does not duplicate ready dish');
    s.conflict();
    await queue.children[0].children[2].handlers.click();
    assert.match(s.elements['kitchen-message'].textContent, /thiết bị khác/);
    const waiter = await session(false);
    assert.equal(cards(waiter.elements['kitchen-queue'])[0].children.some(e => e.tag === 'button'), false, 'Waiter has no transition button');
    assert.equal(waiter.elements['kitchen-queue'].children[0].children.some(e => e.tag === 'button'), false, 'Waiter has no batch button');
    const bulk = await session(true);
    await cards(bulk.elements['kitchen-queue'])[0].children[2].handlers.click();
    await cards(bulk.elements['kitchen-queue'])[1].children[2].handlers.click();
    const batchButton = bulk.elements['kitchen-queue'].children[0].children[1];
    assert.equal(batchButton.disabled, false, 'All preparing enables batch completion');
    bulk.cancel(); await batchButton.handlers.click();
    assert.equal(bulk.posts(), 2, 'Cancel batch confirmation sends no request');
    bulk.allow(); await batchButton.handlers.click();
    assert.equal(cards(bulk.elements['kitchen-queue']).length, 0, 'Entire batch leaves queue');
    assert.equal(bulk.elements['kitchen-ready'].childElementCount, 2, 'Entire batch appears ready once');
    console.log('PASS: kitchen sequential transitions, isolated line, ready queue, refresh, confirmation, conflict, safe text and waiter view');
})().catch(error => { console.error(error); process.exitCode = 1; });
