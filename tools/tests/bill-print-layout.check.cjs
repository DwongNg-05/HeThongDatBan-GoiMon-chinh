// S4-01 Task 3: kiểm tra bản in khổ 80mm trên trình duyệt thật (CSS bill-print.css + JS bill-print.js thật, không cần web/database).
// Dựng một trang có cùng cấu trúc với Views/ProvisionalBill/_PrintReceipt.cshtml + _PrintLine.cshtml (BillPrintTests kiểm tra
// hai partial vẫn dùng đúng các lớp/thuộc tính dưới đây), gồm tên món rất dài, tên không có dấu cách, ghi chú dài, số tiền lớn,
// nhóm "Đã huỷ – có tính tiền", 3 món và 60 món; rồi in sang PDF với khổ trang do bill-print.js đặt. Kiểm tra:
// - khi in chỉ còn bản in (không thanh điều hướng, không nút); bản in rộng đúng 80mm, không ô nào bị tràn / cắt;
// - cột SL và Thành tiền thẳng hàng ở mọi dòng, số tiền không xuống dòng; tên món và ghi chú dài xuống dòng trong cột Món;
// - nhóm món huỷ có nhãn rõ; hoá đơn nhiều món in liền MỘT trang (không ngắt giữa chừng), khổ 80mm × chiều dài nội dung.
//
// Chuẩn bị (một lần):  npm install --no-save playwright-core
// Chạy:                node tools/tests/bill-print-layout.check.cjs
// Tuỳ chọn: BROWSER=msedge|chrome, CHROME_PATH=đường dẫn trình duyệt, OUT_DIR=thư mục lưu PDF mẫu để xem bằng mắt.
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
let chromium;
try { ({ chromium } = require('playwright-core')); } catch { ({ chromium } = require('playwright')); }

const root = path.resolve(__dirname, '..', '..');
const asset = rel => pathToFileURL(path.join(root, 'src', 'RestaurantManagement.Web', 'wwwroot', rel)).href;
const money = n => n.toLocaleString('en-US').replace(/,/g, '.') + ' đ';
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');

function line(id, name, price, qty, unit, note, charged) {
    return `<tr class="bp-line" data-print-line="${id}" data-amount="${price * qty}"><td class="bp-name">`
        + `<span class="bp-dish" data-print-name>${esc(name)}</span><span class="bp-price" data-print-unit-price>${money(price)}/${unit}</span>`
        + (charged ? '<span class="bp-charged-label" data-print-charged-label>[Đã huỷ – có tính tiền]</span>' : '')
        + (note ? `<span class="bp-note" data-print-note>${esc(note)}</span>` : '')
        + `</td><td class="bp-qty" data-print-quantity>${qty}</td><td class="bp-amount" data-print-amount>${money(price * qty)}</td></tr>`;
}

function fixture(count) {
    const names = ['Phở bò', 'Lẩu thái hải sản đặc biệt nhiều tôm mực nghêu cho bàn 6 người kèm rau nấm và bún tươi', 'Trà đá',
        'Cánhgàchiênnướcmắmsiêucayđặcbiệtkhôngkhoảngtrắngnàocả', 'Bào ngư sốt dầu hào'];
    const prices = [65000, 1299000, 5000, 145000, 9990000], qtys = [1, 2, 12, 3, 99];
    const note = 'Ít cay · Không hành | Ghi chú: khách dị ứng đậu phộng, làm riêng nước chấm, mang ra sau món khai vị và nhớ thêm chanh ớt riêng cho bé';
    let rows = '', items = 0;
    for (let i = 1; i <= count; i++) {
        rows += line(i, names[i % 5], prices[i % 5], qtys[i % 5], 'phần', i % 4 === 0 || i === 2 ? note : null, false);
        items += prices[i % 5] * qtys[i % 5];
    }
    const charged = 150000;
    return `<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="${asset('css/bill-preview.css')}"><link rel="stylesheet" href="${asset('css/bill-print.css')}">
<style>body.admin-shell{display:grid;grid-template-columns:240px 1fr;margin:0}.admin-sidebar{min-height:100vh;background:#b3122e}</style></head>
<body class="admin-shell"><aside class="admin-sidebar"><nav>Sơ đồ bàn</nav></aside><div><header class="workspace-header">Thanh trên</header><main>
<div class="bill-preview"><div class="bill-preview-actions"><a href="/?table=A03" data-preview-back>← Quay lại gọi món</a><button type="button" data-preview-print>In</button></div></div>
<section class="bill-print" data-print-receipt aria-hidden="true"><p class="bp-notice" data-print-notice>TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN</p>
<h1 class="bp-restaurant" data-print-restaurant>Bếp Nhà – chi nhánh có tên rất dài để thử xuống dòng</h1>
<dl class="bp-meta"><div><dt>Bàn</dt><dd data-print-tables>A03, A04</dd></div><div><dt>Bắt đầu phục vụ</dt><dd data-print-service-start>15/10/2026 19:05</dd></div>
<div><dt>Thời điểm in</dt><dd data-print-printed-at>15/10/2026 22:30</dd></div><div><dt>Người in</dt><dd data-print-printed-by>Nguyễn Thị Phương Thảo Ngọc</dd></div></dl>
<table class="bp-items"><colgroup><col /><col class="bp-col-qty" /><col class="bp-col-amount" /></colgroup>
<thead><tr><th scope="col" class="bp-name">Món</th><th scope="col" class="bp-qty">SL</th><th scope="col" class="bp-amount">Thành tiền</th></tr></thead>
<tbody data-print-group="items">${rows}</tbody>
<tbody class="bp-charged" data-print-group="charged-cancelled"><tr class="bp-group-head"><th scope="rowgroup" colspan="3" data-print-group-label>ĐÃ HUỶ – CÓ TÍNH TIỀN</th></tr>
${line(9999, 'Cá kho tộ niêu đất truyền thống ăn kèm cơm cháy', charged, 1, 'niêu', 'khách vội', true)}</tbody></table>
<table class="bp-totals"><tr><th scope="row">Cộng món đã gọi</th><td data-print-items-subtotal>${money(items)}</td></tr>
<tr><th scope="row">Cộng món đã huỷ – có tính tiền</th><td data-print-charged-subtotal>${money(charged)}</td></tr>
<tr class="bp-total"><th scope="row">TỔNG CỘNG</th><td data-print-total="${items + charged}">${money(items + charged)}</td></tr></table>
<p class="bp-foot">Chưa trừ giảm giá. Số tiền chính thức theo hoá đơn khi thanh toán.</p></section>
</main></div><script src="${asset('js/bill-print.js')}"></script></body></html>`;
}

async function launch() {
    if (process.env.CHROME_PATH) return chromium.launch({ executablePath: process.env.CHROME_PATH });
    for (const channel of process.env.BROWSER ? [process.env.BROWSER] : ['msedge', 'chrome', undefined]) {
        try { return await chromium.launch(channel ? { channel } : {}); } catch { /* thử trình duyệt tiếp theo */ }
    }
    throw new Error('Không mở được Edge/Chrome. Cài Microsoft Edge hoặc Google Chrome, hoặc đặt BROWSER / CHROME_PATH.');
}

const MM = 96 / 25.4;
let failures = 0;
const check = (ok, label) => { console.log(`${ok ? 'PASS' : 'FAIL'}: ${label}`); if (!ok) failures++; };

(async () => {
    const browser = await launch();
    const dir = fs.mkdtempSync(path.join(require('node:os').tmpdir(), 'bill-print-'));
    try {
        for (const count of [3, 60]) {
            const file = path.join(dir, `bill-${count}.html`);
            fs.writeFileSync(file, fixture(count));
            const page = await browser.newPage();
            let requests = 0;
            page.on('request', request => { if (!request.url().startsWith('file:')) requests++; });
            await page.goto(pathToFileURL(file).href);
            await page.evaluate(() => { window.print = () => { window.__printed = (window.__printed || 0) + 1; }; });
            await page.click('[data-preview-print]');
            await page.emulateMedia({ media: 'print' });
            const r = await page.evaluate(() => {
                const receipt = document.querySelector('[data-print-receipt]');
                const box = el => el.getBoundingClientRect();
                const lines = el => { const range = document.createRange(); range.selectNodeContents(el); return new Set([...range.getClientRects()].map(x => Math.round(x.top))).size; };
                return {
                    printed: window.__printed || 0,
                    visible: [...document.body.children].filter(e => getComputedStyle(e).display !== 'none').map(e => e.className),
                    buttons: [...document.querySelectorAll('button, a')].filter(e => e.getClientRects().length > 0).length,
                    width: box(receipt).width, height: box(receipt).height,
                    overflow: [...receipt.querySelectorAll('td, th, dd, dt, p, h1')].filter(c => c.scrollWidth > c.clientWidth + 1).map(c => c.textContent.slice(0, 40)),
                    qtyLeft: new Set([...receipt.querySelectorAll('.bp-qty')].map(c => Math.round(box(c).left))).size,
                    amountRight: new Set([...receipt.querySelectorAll('.bp-amount')].map(c => Math.round(box(c).right))).size,
                    amountLines: Math.max(...[...receipt.querySelectorAll('td.bp-amount, .bp-totals td')].map(lines)),
                    longNameLines: lines(receipt.querySelector('[data-print-line="1"] .bp-dish')),
                    noSpaceNameLines: lines(receipt.querySelector('[data-print-line="3"] .bp-dish')),
                    noteLines: Math.max(0, ...[...receipt.querySelectorAll('.bp-note')].map(lines)),
                    groupLabel: receipt.querySelector('[data-print-group-label]')?.textContent,
                    chargedLabel: receipt.querySelector('[data-print-charged-label]')?.textContent,
                    rightEdge: Math.max(...[...receipt.querySelectorAll('*')].map(e => box(e).right)),
                    pageRule: document.getElementById('bill-print-page-size')?.textContent
                };
            });
            const label = `${count} món`;
            check(r.printed === 1, `${label}: nút "In" mở hộp thoại in`);
            check(requests === 0, `${label}: in không gửi yêu cầu nào lên máy chủ`);
            check(r.visible.length === 1 && r.visible[0].includes('bill-print') && r.buttons === 0, `${label}: khi in chỉ còn bản in, không thanh điều hướng, không nút (${r.visible.join(', ')})`);
            check(Math.abs(r.width - 80 * MM) < 1 && r.rightEdge <= 80 * MM + 0.5, `${label}: bản in rộng đúng 80mm, không gì vượt khổ (${(r.width / MM).toFixed(1)}mm)`);
            check(r.overflow.length === 0, `${label}: không ô nào bị tràn / cắt chữ ${r.overflow.join(' | ')}`);
            check(r.qtyLeft === 1 && r.amountRight === 1 && r.amountLines === 1, `${label}: cột SL và Thành tiền thẳng hàng, số tiền không xuống dòng`);
            check(r.longNameLines >= 2 && r.noSpaceNameLines >= 2 && r.noteLines >= 2, `${label}: tên món rất dài, tên không dấu cách và ghi chú dài xuống dòng trong cột Món`);
            check(r.groupLabel === 'ĐÃ HUỶ – CÓ TÍNH TIỀN' && r.chargedLabel === '[Đã huỷ – có tính tiền]', `${label}: nhóm món huỷ có tính tiền in rõ nhãn`);
            const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
            const text = pdf.toString('latin1');
            const pages = (text.match(/\/Type\s*\/Page[^s]/g) || []).length;
            const media = (text.match(/\/MediaBox\s*\[([^\]]+)\]/) || [])[1]?.trim().split(/\s+/).map(Number) || [];
            const [widthMm, heightMm] = [media[2] / 72 * 25.4, media[3] / 72 * 25.4];
            check(pages === 1, `${label}: in liền một dải, một trang duy nhất (${pages} trang)`);
            check(Math.abs(widthMm - 80) < 0.5 && heightMm >= r.height / MM && heightMm <= r.height / MM + 6,
                `${label}: khổ giấy 80mm × ${heightMm.toFixed(0)}mm (theo nội dung ${(r.height / MM).toFixed(0)}mm) — ${r.pageRule}`);
            if (process.env.OUT_DIR) fs.writeFileSync(path.join(process.env.OUT_DIR, `bill-print-${count}.pdf`), pdf);
            await page.close();
        }
    } finally {
        await browser.close();
        fs.rmSync(dir, { recursive: true, force: true });
    }
    console.log(failures ? `${failures} kiểm tra thất bại.` : 'Bản in 80mm: mọi kiểm tra đều đạt.');
    process.exitCode = failures ? 1 : 0;
})().catch(error => { console.error(error); process.exitCode = 1; });
