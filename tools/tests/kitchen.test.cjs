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
    vm.runInNewContext(source, {
        document: { getElementById: id => elements[id], createElement: tag => new Element(tag), querySelector: () => ({ value: 'csrf' }) },
        window: { confirm: () => confirm }, URLSearchParams,
        setTimeout: callback => { timer = callback; },
        fetch: async (url, options) => {
            if (options.method === 'POST') {
                posts++;
                const data = options.body;
                assert.equal(data.get('__RequestVerificationToken'), 'csrf');
                if (conflict) return { ok: false, json: async () => ({ message: 'Món đã được thiết bị khác cập nhật.' }) };
                lines = lines.map(line => String(line.id) === data.get('id') ? { ...line, status: data.get('to'), version: 'new' + posts } : line);
                return { ok: true };
            }
            return { ok: true, json: async () => lines };
        }
    });
    await tick();
    return { elements, cancel: () => { confirm = false; }, allow: () => { confirm = true; },
        conflict: () => { conflict = true; }, posts: () => posts, poll: async () => { await timer(); } };
}
(async () => {
    const s = await session(true);
    const queue = s.elements['kitchen-queue'];
    const ready = s.elements['kitchen-ready'];
    assert.equal(queue.childElementCount, 2);
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
    await queue.children[0].children[2].handlers.click();
    assert.equal(queue.childElementCount, 1);
    assert.equal(ready.childElementCount, 1);
    assert.equal(ready.children[0].children[1].textContent, 'Đã xong');
    await s.poll();
    assert.equal(ready.childElementCount, 1, 'Repeated snapshot does not duplicate ready dish');
    s.conflict();
    await queue.children[0].children[2].handlers.click();
    assert.match(s.elements['kitchen-message'].textContent, /thiết bị khác/);
    const waiter = await session(false);
    assert.equal(waiter.elements['kitchen-queue'].children[0].children.length, 2, 'Waiter has no transition button');
    console.log('PASS: kitchen sequential transitions, isolated line, ready queue, refresh, confirmation, conflict, safe text and waiter view');
})().catch(error => { console.error(error); process.exitCode = 1; });
