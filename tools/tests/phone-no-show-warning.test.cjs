// S3-09 Task 3: cảnh báo số điện thoại nhiều lần không tới trên form tạo đặt bàn (wwwroot/js/phone-no-show-warning.js).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/RestaurantManagement.Web/wwwroot/js/phone-no-show-warning.js', 'utf8');

function setup(respond, initialPhone = '') {
    const elements = {};
    const element = id => (elements[id] ??= { id, hidden: false, value: '', textContent: '', checked: false, dataset: {}, listeners: {},
        addEventListener(event, callback) { this.listeners[event] = callback; } });
    element('phone-no-show').dataset = { phoneHistoryApi: '/api/reservation-alerts/phone-history', phoneInput: 'Phone' };
    element('Phone').value = initialPhone;
    element('phone-no-show-warning').hidden = true;
    element('NoShowCountAcknowledged').value = '0';
    const token = { value: 'csrf-token' };
    const document = { getElementById: element, querySelector: selector => (selector.includes('RequestVerificationToken') ? token : null) };
    const requests = [];
    const timers = [];
    const fetch = (url, options) => {
        const phone = new URLSearchParams(options.body).get('phone');
        requests.push({ url, phone, method: options.method, token: options.headers.RequestVerificationToken });
        return respond(phone, options.signal);
    };
    const window = {};
    vm.runInNewContext(source, { document, fetch, window, URLSearchParams, AbortController, Number, String,
        setTimeout(callback, delay) { timers.push({ callback, delay }); return timers.length; }, clearTimeout() {} });
    return { elements, requests, timers, api: window.PhoneNoShowWarning };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
const json = data => ({ ok: true, json: async () => data });
const history = {
    '0985309200': 0, '0985309202': 2, '0985309203': 3, '0985309205': 5
};
function server(phone) {
    const digits = phone.replace(/[\s.\-()]/g, '').replace(/^\+84/, '0').replace(/^0084/, '0').replace(/^84(?=\d{9}$)/, '0');
    if (!/^0\d{9}$/.test(digits)) return Promise.resolve(json({ valid: false, noShowCount: 0, isWarning: false }));
    const count = history[digits] ?? 0;
    return Promise.resolve(json({ valid: true, normalizedPhone: digits, noShowCount: count, isWarning: count >= 3,
        message: count >= 3 ? `Số điện thoại này có ${count} lần khách không tới trong 90 ngày gần nhất.` : null }));
}

test('0 and 2 no-shows show no warning; 3 and 5 show the warning with the count', async () => {
    const { elements, api, requests } = setup(server);
    const box = elements['phone-no-show-warning'];
    for (const [phone, warned, count] of [['0985309200', false], ['0985309202', false], ['0985309203', true, 3], ['0985309205', true, 5]]) {
        elements.Phone.value = phone;
        await api.check(false);
        assert.equal(box.hidden, !warned, phone);
        if (warned) {
            assert.match(elements['phone-no-show-message'].textContent, new RegExp(`có ${count} lần`));
            assert.equal(elements.NoShowCountAcknowledged.value, String(count));
        } else assert.equal(elements.NoShowCountAcknowledged.value, '0');
    }
    assert.ok(requests.every(r => r.method === 'POST' && r.token === 'csrf-token' && r.url === '/api/reservation-alerts/phone-history'),
        'the phone number goes in a POST body with the anti-forgery token, never in the URL');
});

test('typing waits 350 ms; changing the phone updates the warning and resets the confirmation', async () => {
    const { elements, timers } = setup(server);
    const box = elements['phone-no-show-warning'];
    elements.Phone.value = '0985309203';
    elements.Phone.listeners.input();
    assert.equal(timers.at(-1).delay, 350);
    await timers.at(-1).callback();
    assert.equal(box.hidden, false);
    elements.NoShowWarningConfirmed.checked = true;
    elements.Phone.value = '0985309202';
    await elements.Phone.listeners.change();
    assert.equal(box.hidden, true);
    assert.equal(elements.NoShowWarningConfirmed.checked, false);
    assert.equal(elements.NoShowCountAcknowledged.value, '0');
    elements.Phone.value = '0985309205';
    await elements.Phone.listeners.change();
    assert.equal(box.hidden, false);
    assert.equal(elements.NoShowCountAcknowledged.value, '5');
});

test('equivalent ways of typing give the same result and the field is rewritten in standard form on change', async () => {
    const { elements } = setup(server);
    for (const typed of ['0985 309 203', '+84 985 309 203', '84985309203', '098.530.9203']) {
        elements.Phone.value = typed;
        elements['phone-no-show-warning'].hidden = true;
        await elements.Phone.listeners.change();
        assert.equal(elements['phone-no-show-warning'].hidden, false, typed);
        assert.equal(elements.Phone.value, '0985309203', typed);
        assert.equal(elements.NoShowCountAcknowledged.value, '3');
    }
});

test('a confirmation stays ticked while the count is unchanged but is cleared when the history grows', async () => {
    let count = 3;
    const { elements, api } = setup(() => Promise.resolve(json({ valid: true, normalizedPhone: '0985309203', noShowCount: count, isWarning: true, message: `${count} lần` })),
        '0985309203');
    await flush();
    elements.NoShowWarningConfirmed.checked = true;
    await api.check(false);
    assert.equal(elements.NoShowWarningConfirmed.checked, true);
    count = 4;
    await api.check(false);
    assert.equal(elements.NoShowWarningConfirmed.checked, false);
    assert.equal(elements.NoShowCountAcknowledged.value, '4');
});

test('only the latest lookup is shown, invalid or empty numbers hide the warning, and a failed lookup changes nothing', async () => {
    let releaseSlow;
    const { elements, api } = setup(phone => phone === '0985309205'
        ? new Promise(resolve => { releaseSlow = () => resolve(server(phone)); })
        : phone === '0985309299' ? Promise.resolve({ ok: false, status: 503 }) : server(phone));
    const box = elements['phone-no-show-warning'];
    elements.Phone.value = '0985309205';
    const slow = api.check(false);
    elements.Phone.value = '0985309202';
    await api.check(false);
    releaseSlow();
    await slow;
    await flush();
    assert.equal(box.hidden, true, 'the slow answer for the old number is ignored');
    elements.Phone.value = '0985309203';
    await api.check(false);
    assert.equal(box.hidden, false);
    elements.Phone.value = '0985309299';
    await api.check(false);
    assert.equal(box.hidden, false, 'a failed lookup keeps the last state; the server checks again on submit');
    elements.Phone.value = '12345';
    await api.check(false);
    assert.equal(box.hidden, true);
    elements.Phone.value = '0985309203';
    await api.check(false);
    elements.Phone.value = '';
    await api.check(false);
    assert.equal(box.hidden, true);
});

test('a phone already filled in (form shown again) is checked on load', async () => {
    const { elements, requests } = setup(server, '0985309203');
    await flush();
    assert.equal(requests.length, 1);
    assert.equal(elements['phone-no-show-warning'].hidden, false);
});
