const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../src/RestaurantManagement.Web/wwwroot/js/login-lockout.js'), 'utf8');

test('countdown follows elapsed time even after a background-tab pause, then allows retry', () => {
    let now = 10000;
    let tick;
    let stopped = false;
    const counter = { dataset: { seconds: '900' } };
    const message = {};
    vm.runInNewContext(script, {
        document: { getElementById: id => id === 'lockout-countdown' ? counter : message },
        Date: { now: () => now },
        setInterval: callback => { tick = callback; return 1; },
        clearInterval: () => { stopped = true; }
    });
    assert.equal(counter.textContent, '15:00');
    now += 65000;
    tick();
    assert.equal(counter.textContent, '13:55');
    now += 900000;
    tick();
    assert.equal(counter.textContent, '00:00');
    assert.equal(counter.hidden, true);
    assert.match(message.textContent, /Bạn có thể đăng nhập lại/);
    assert.equal(stopped, true);
});

test('ordinary login form does not start a countdown', () => {
    vm.runInNewContext(script, {
        document: { getElementById: () => null },
        setInterval: () => { throw new Error('Unexpected timer'); }
    });
});
