// S4-01 Task 3: nút "In" và bản in khổ 80mm (wwwroot/js/bill-print.js). Chạy: node --test tools/tests/bill-print.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = 'src/RestaurantManagement.Web/wwwroot/js/bill-print.js';
const source = fs.readFileSync(path, 'utf8');
const billPrint = require('../../' + path);

test('chiều dài trang = chiều dài bản in (làm tròn lên) + 4mm dư, tối thiểu 40mm', () => {
    assert.equal(billPrint.pageHeightMm(96 / 25.4 * 100), 104);          // đúng 100mm nội dung
    assert.equal(billPrint.pageHeightMm(96 / 25.4 * 100 + 1), 105);      // hơn một chút → làm tròn lên
    assert.equal(billPrint.pageHeightMm(3507), 932);                      // 40 món
    assert.equal(billPrint.pageHeightMm(0), 40);
    assert.equal(billPrint.pageHeightMm(Number.NaN), 40);
    assert.equal(billPrint.pageRule(661.6), '@page { size: 80mm 180mm; margin: 0; }');
});

function fakePage() {
    const listeners = {};
    const on = target => (name, callback) => { (target.listeners[name] ??= []).push(callback); };
    const receipt = { parentElement: null, dataset: {}, classes: new Set(), measured: 0,
        classList: { add(c) { receipt.classes.add(c); }, remove(c) { receipt.classes.delete(c); } },
        getBoundingClientRect() { receipt.measured++; return { height: receipt.classes.has('bill-print-measuring') ? 661.6 : 0 }; } };
    const button = { listeners: {} }; button.addEventListener = on(button);
    const head = { children: [], appendChild(child) { this.children.push(child); } };
    const body = { children: [], appendChild(child) { this.children.push(child); child.parentElement = body; } };
    const document = {
        head, body,
        querySelector: selector => (selector === '[data-print-receipt]' ? receipt : null),
        querySelectorAll: selector => (selector === '[data-preview-print]' ? [button] : []),
        createElement: tag => ({ tag, textContent: '' })
    };
    const window = { document, printed: 0, listeners, print() { this.printed++; }, addEventListener(name, callback) { (listeners[name] ??= []).push(callback); } };
    return { window, document, receipt, button, head, body, listeners };
}

test('đưa bản in ra dưới <body>, đặt khổ 80mm × chiều dài nội dung, nút "In" mở hộp thoại in', () => {
    const page = fakePage();
    vm.runInNewContext(source, { window: page.window, globalThis: page.window, Number, Math, String });
    assert.equal(page.receipt.parentElement, page.body, 'bản in nằm ngay dưới <body> để CSS in chỉ giữ lại nó');
    const style = page.head.children[0];
    assert.equal(style.id, 'bill-print-page-size');
    assert.equal(style.textContent, '@page { size: 80mm 180mm; margin: 0; }');
    assert.equal(page.receipt.dataset.printHeightMm, '180');
    assert.equal(page.receipt.classes.size, 0, 'lớp đo được gỡ ngay sau khi đo');

    page.receipt.getBoundingClientRect = () => ({ height: page.receipt.classes.has('bill-print-measuring') ? 3507 : 0 });
    page.button.listeners.click[0]();
    assert.equal(page.window.printed, 1, 'bấm "In" mở hộp thoại in');
    assert.equal(style.textContent, '@page { size: 80mm 932mm; margin: 0; }', 'đo lại ngay trước khi in (thêm món → dài hơn)');

    page.receipt.getBoundingClientRect = () => ({ height: 1000 });
    page.listeners.beforeprint[0]();
    assert.match(style.textContent, /size: 80mm \d+mm/, 'Ctrl+P cũng đo lại (beforeprint)');
});

test('không có bản in thì không làm gì', () => {
    const page = fakePage();
    page.document.querySelector = () => null;
    vm.runInNewContext(source, { window: page.window, globalThis: page.window, Number, Math, String });
    assert.equal(page.head.children.length, 0);
});

test('in chỉ gọi window.print(): không gửi yêu cầu nào lên máy chủ nên không đổi trạng thái bàn / đơn gọi món', () => {
    for (const pattern of [/fetch\s*\(/, /XMLHttpRequest/, /\.submit\s*\(/, /sendBeacon/, /location\s*=/, /localStorage/])
        assert.doesNotMatch(source, pattern);
    assert.match(source, /root\.print\(\)/);
});
