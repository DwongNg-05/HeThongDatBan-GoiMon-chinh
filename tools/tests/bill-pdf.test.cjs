// S4-01 Task 4: nút "Xuất PDF" (wwwroot/js/bill-pdf.js). Chạy: node --test tools/tests/bill-pdf.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = 'src/RestaurantManagement.Web/wwwroot/js/bill-pdf.js';
const source = fs.readFileSync(path, 'utf8');
const billPdf = require('../../' + path);

test('tên tệp lấy từ Content-Disposition của máy chủ', () => {
    assert.equal(billPdf.fileNameFrom('attachment; filename=TamTinh_A03_20261015-2230.pdf; filename*=UTF-8\'\'TamTinh_A03_20261015-2230.pdf', 'x.pdf'),
        'TamTinh_A03_20261015-2230.pdf');
    assert.equal(billPdf.fileNameFrom('attachment; filename="TamTinh_A03-A04_20261015-2230.pdf"', 'x.pdf'), 'TamTinh_A03-A04_20261015-2230.pdf');
    assert.equal(billPdf.fileNameFrom(null, 'TamTinh.pdf'), 'TamTinh.pdf');
    assert.equal(billPdf.fileNameFrom('attachment', 'TamTinh.pdf'), 'TamTinh.pdf');
});

function page(fetchImpl) {
    const element = extra => ({ hidden: false, textContent: '', attributes: {}, listeners: {}, clicked: 0,
        addEventListener(name, callback) { (this.listeners[name] ??= []).push(callback); },
        setAttribute(name, value) { this.attributes[name] = String(value); }, removeAttribute(name) { delete this.attributes[name]; },
        click() { this.clicked++; }, remove() { this.removed = true; }, ...extra });
    const link = element({ href: 'http://localhost/ProvisionalBill/Preview/A03/Pdf', textContent: 'Xuất PDF' });
    const error = element({ hidden: true });
    const created = [];
    const document = {
        querySelector: selector => ({ '[data-preview-pdf]': link, '[data-preview-error]': error }[selector] ?? null),
        createElement: tag => { const node = element({ tag }); created.push(node); return node; },
        body: { appendChild() {} }
    };
    const calls = [];
    const window = {
        document, calls,
        fetch: async (url, options) => { calls.push({ url, options }); return fetchImpl(); },
        URL: { createObjectURL: () => 'blob:pdf', revokeObjectURL() {} },
        setTimeout: () => 0
    };
    vm.runInNewContext(source, { window, globalThis: window });
    const click = async () => {
        let prevented = false;
        await link.listeners.click[0]({ preventDefault() { prevented = true; } });
        return prevented;
    };
    return { link, error, created, calls, click };
}
const headers = values => ({ get: name => values[name] ?? null });

test('bấm "Xuất PDF": tải tệp PDF với đúng tên tệp, không gửi gì ngoài một yêu cầu GET', async () => {
    const p = page(() => ({ ok: true, headers: headers({ 'Content-Type': 'application/pdf', 'Content-Disposition': 'attachment; filename=TamTinh_A03_20261015-2230.pdf' }),
        blob: async () => 'PDF' }));
    assert.equal(await p.click(), true);
    assert.equal(p.calls.length, 1);
    assert.equal(p.calls[0].url, 'http://localhost/ProvisionalBill/Preview/A03/Pdf');
    assert.equal(p.calls[0].options.method, undefined, 'GET (chỉ đọc)');
    assert.equal(p.calls[0].options.headers['X-Requested-With'], 'fetch');
    const anchor = p.created.find(n => n.tag === 'a');
    assert.equal(anchor.download, 'TamTinh_A03_20261015-2230.pdf');
    assert.equal(anchor.href, 'blob:pdf');
    assert.equal(anchor.clicked, 1);
    assert.equal(p.error.hidden, true);
    assert.equal(p.link.textContent, 'Xuất PDF', 'nút trở lại bình thường');
    assert.equal(p.link.attributes['aria-disabled'], undefined);
});

test('máy chủ báo lỗi (bàn không còn khách / không tạo được tệp): hiện thông báo của máy chủ', async () => {
    const p = page(() => ({ ok: false, headers: headers({ 'Content-Type': 'application/json; charset=utf-8' }),
        json: async () => ({ message: 'Bàn A03 hiện không có khách đang phục vụ, chưa có tạm tính.' }) }));
    await p.click();
    assert.equal(p.error.hidden, false);
    assert.equal(p.error.textContent, 'Bàn A03 hiện không có khách đang phục vụ, chưa có tạm tính.');
    assert.equal(p.created.length, 0, 'không tải tệp nào');
});

test('trả về không phải PDF (ví dụ trang đăng nhập khi hết phiên) hoặc mất mạng: thông báo không tạo được tệp', async () => {
    const html = page(() => ({ ok: true, headers: headers({ 'Content-Type': 'text/html' }), json: async () => { throw new SyntaxError('html'); } }));
    await html.click();
    assert.equal(html.error.hidden, false);
    assert.equal(html.error.textContent, billPdf.failureMessage);
    const offline = page(() => { throw new TypeError('Failed to fetch'); });
    await offline.click();
    assert.equal(offline.error.textContent, billPdf.failureMessage);
    assert.equal(offline.link.textContent, 'Xuất PDF');
});

test('thông báo lỗi trùng với máy chủ (BillPdfService.FailureMessage)', () => {
    const service = fs.readFileSync('src/RestaurantManagement.Web/Services/Orders/Pdf/BillPdfService.cs', 'utf8');
    assert.ok(service.includes(`"${billPdf.failureMessage}"`));
    assert.doesNotMatch(source, /method:\s*'(POST|PUT|DELETE)'/);
});
