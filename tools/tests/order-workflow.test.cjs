const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/order-workflow.js', 'utf8');
function setup(fetch, kitchen = false) {
    const elements = {}, timers = [];
    function element(tag) { return { tag, children: [], listeners: {}, dataset: {}, value: '', disabled: false, open: false,
        append(child) { this.children.push(child); }, replaceChildren() { this.children = []; },
        addEventListener(event, callback) { this.listeners[event] = callback; },
        showModal() { this.open = true; }, close() { this.open = false; }, reportValidity() {},
        querySelector() { return { value: 'token' }; } }; }
    const document = { getElementById(id) { return elements[id] ??= element(); }, createElement: element };
    document.getElementById('order-workflow').dataset = { kitchen: String(kitchen), snapshot: '/snapshot', cancel: '/cancel', start: '/start', canCancel: 'true', canStart: 'true' };
    vm.runInNewContext(source, { document, fetch, Intl, URLSearchParams, setTimeout(callback, delay) { timers.push({callback,delay}); }, clearTimeout() {} });
    return { elements, timers };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const reply = data => ({ ok: true, headers: { get: () => 'application/json' }, json: async () => data });
const item = (id, status) => ({ id, sessionId: 1, tableCode: 'A01', itemName: 'Cơm', quantity: 2, unitPrice: 25000, lineTotal: 50000, status, sessionStatus: 'Open', chargeWhenCancelled: false });
const snapshot = items => ({ items, sessions: [{ id: 1, subtotal: items.filter(i=>i.status!=='Cancelled').reduce((sum,i)=>sum+i.lineTotal,0) }] });
function all(root) { return [root, ...root.children.flatMap(all)]; }
test('pending cancel enabled, preparing disabled with explanation, reason required and duplicate submit suppressed', async () => {
    let posts = 0, finish;
    const { elements } = setup(async (url, options) => {
        if (options.method === 'POST') { posts++; assert.equal(options.body.get('reason'), 'Mistake'); return new Promise(resolve=>finish=resolve); }
        return reply(snapshot([item(1,'Pending'), item(2,'Preparing')]));
    });
    await flush();
    const buttons = all(elements['order-list']).filter(e=>e.tag==='button');
    assert.equal(buttons[0].disabled, false); assert.equal(buttons[1].disabled, true);
    assert.ok(all(elements['order-list']).some(e=>String(e.textContent).includes('nhân viên không được huỷ')));
    buttons[0].listeners.click();
    const form = elements['order-cancel-form'];
    form.listeners.submit({ preventDefault() {} }); await flush(); assert.equal(posts,0);
    elements['order-reason'].value='Mistake';
    form.listeners.submit({ preventDefault() {} }); form.listeners.submit({ preventDefault() {} });
    assert.equal(posts,1); assert.equal(elements['order-cancel-confirm'].disabled,true);
    finish(reply({message:'Đã huỷ'})); await flush(); assert.equal(elements['order-cancel-dialog'].open,false);
});
test('kitchen polling removes cancelled item and staff subtotal renders zero', async () => {
    let state='Pending';
    const { elements, timers } = setup(async () => reply(snapshot(state==='Cancelled'?[]:[item(1,state)])),true);
    await flush(); assert.ok(all(elements['order-list']).some(e=>e.textContent==='Cơm'));
    assert.equal(timers.at(-1).delay,2000);
    state='Cancelled'; timers.at(-1).callback(); await flush();
    assert.ok(all(elements['order-list']).some(e=>e.textContent==='Hàng đợi bếp trống.'));
    const staff=setup(async()=>reply(snapshot([item(1,'Cancelled')]))); await flush();
    assert.ok(all(staff.elements['order-list']).some(e=>e.textContent==='Tạm tính: 0 ₫'));
});
test('conflict after kitchen starts refreshes disabled cancel button', async () => {
    let state='Pending';
    const { elements }=setup(async (url,options)=> {
        if(options.method==='POST') { state='Preparing';return { ...reply({message:'Món không còn chờ bếp'}),ok:false }; }
        return reply(snapshot([item(1,state)]));
    });
    await flush(); all(elements['order-list']).find(e=>e.tag==='button').listeners.click();
    elements['order-reason'].value='SoldOut';elements['order-cancel-form'].listeners.submit({preventDefault(){}});
    await flush();
    assert.equal(elements['order-message'].textContent,'Món không còn chờ bếp');
    assert.equal(all(elements['order-list']).find(e=>e.tag==='button').disabled,true);
});
