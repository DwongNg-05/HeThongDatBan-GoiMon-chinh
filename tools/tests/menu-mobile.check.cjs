// S2-01 Task 4: kiểm tra trang thực đơn trên trình duyệt thật ở màn hình điện thoại.
// - Bố cục không tràn ngang ở 360px và 412px; ảnh, tên, mô tả, giá, nhãn "Tạm hết", ô tìm kiếm nằm trong màn hình.
// - Thời gian tải trang (tới sự kiện load) và tải lại sau khi tìm kiếm đều dưới 2 giây.
//
// Chuẩn bị (một lần):  npm install --no-save playwright-core
// Chạy web + 200 món:  dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-200
//                      dotnet run --project src/RestaurantManagement.Web --urls http://localhost:5105
// Chạy kiểm tra:       node tools/tests/menu-mobile.check.cjs
// Tuỳ chọn: BASE_URL=http://localhost:5105  BROWSER=msedge|chrome (mặc định thử Edge rồi Chrome đã cài trên máy)
//           CHROME_PATH=đường dẫn tới chrome.exe/msedge.exe nếu cài ở chỗ khác.
const { chromium } = require('playwright-core');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5105').replace(/\/$/, '');
const BUDGET_MS = 2000;
const VIEWPORTS = [
    { name: '360px', width: 360, height: 740 },
    { name: '412px (điện thoại lớn)', width: 412, height: 915 },
];

async function launch() {
    if (process.env.CHROME_PATH) return chromium.launch({ executablePath: process.env.CHROME_PATH });
    const channels = process.env.BROWSER ? [process.env.BROWSER] : ['msedge', 'chrome'];
    for (const channel of channels) {
        try { return await chromium.launch({ channel }); } catch { /* thử trình duyệt tiếp theo */ }
    }
    throw new Error('Không mở được Edge/Chrome. Cài Microsoft Edge hoặc Google Chrome, hoặc đặt BROWSER.');
}

const layoutCheck = () => {
    const vw = document.documentElement.clientWidth;
    const selectors = '.public-menu-hero, .public-menu-search, .public-menu-search-input, .public-menu-search-button, .public-menu-tabs, .public-menu-group, .public-dish, .public-dish-image, .public-dish-name, .public-dish-desc, .public-dish-price, .public-dish-status, .public-menu-search-status';
    const overflow = [];
    for (const el of document.querySelectorAll(selectors)) {
        const r = el.getBoundingClientRect();
        if (r.width > 0 && (r.left < -0.5 || r.right > vw + 0.5)) overflow.push(`${el.className} [${Math.round(r.left)}..${Math.round(r.right)}]`);
    }
    const input = document.querySelector('.public-menu-search-input')?.getBoundingClientRect();
    const soldOut = document.querySelector('.public-dish[data-sold-out="true"] .public-dish-status');
    return {
        horizontalScroll: document.documentElement.scrollWidth > vw,
        overflow: overflow.slice(0, 5),
        dishes: document.querySelectorAll('.public-dish').length,
        searchUsable: !!input && input.height >= 40 && input.width >= 160,
        soldOutLabel: soldOut ? getComputedStyle(soldOut).opacity === '1' && soldOut.textContent.includes('Tạm hết') : null,
    };
};

const loadTime = () => {
    const nav = performance.getEntriesByType('navigation')[0];
    return Math.round(nav.loadEventEnd - nav.startTime);
};

(async () => {
    const browser = await launch();
    let failed = 0;
    const check = (ok, label) => { console.log(`${ok ? 'PASS' : 'FAIL'}: ${label}`); if (!ok) failed++; };
    try {
        for (const vp of VIEWPORTS) {
            const context = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
            const page = await context.newPage();
            await page.goto(`${BASE_URL}/Menu`, { waitUntil: 'load' }); // lần đầu: làm nóng máy chủ
            await page.goto(`${BASE_URL}/Menu`, { waitUntil: 'load' });
            const ms = await page.evaluate(loadTime);
            const layout = await page.evaluate(layoutCheck);
            check(ms < BUDGET_MS, `[${vp.name}] tải /Menu với ${layout.dishes} món: ${ms} ms (< ${BUDGET_MS} ms)`);
            check(layout.dishes >= 200, `[${vp.name}] hiển thị đủ 200 món (đang có ${layout.dishes}) — nhớ chạy seed-menu-200`);
            check(!layout.horizontalScroll && layout.overflow.length === 0, `[${vp.name}] không tràn ngang${layout.overflow.length ? ': ' + layout.overflow.join('; ') : ''}`);
            check(layout.searchUsable, `[${vp.name}] ô tìm kiếm đủ lớn để chạm và gõ`);
            check(layout.soldOutLabel !== false, `[${vp.name}] nhãn "Tạm hết" rõ ràng, không bị làm mờ`);

            // Tìm kiếm trên điện thoại: gõ từ khoá rồi bấm Tìm.
            await page.fill('#menu-search', 'com rang');
            await Promise.all([page.waitForURL(/\/Menu\?q=/, { waitUntil: 'load' }), page.click('.public-menu-search-button')]);
            const searchMs = await page.evaluate(loadTime);
            const after = await page.evaluate(layoutCheck);
            const names = await page.$$eval('.public-dish-name', els => els.map(e => e.textContent.trim()));
            check(searchMs < BUDGET_MS, `[${vp.name}] tải lại sau khi tìm "com rang": ${searchMs} ms`);
            check(names.length > 0 && names.every(n => n.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/gi, 'd').toLowerCase().includes('com rang')),
                `[${vp.name}] kết quả tìm kiếm đúng (${names.length} món)`);
            check(!after.horizontalScroll && after.overflow.length === 0, `[${vp.name}] trang kết quả không tràn ngang`);

            await page.screenshot({ path: `menu-${vp.width}.png`, fullPage: false });
            await context.close();
        }
    } finally {
        await browser.close();
    }
    console.log(failed === 0 ? 'Tất cả kiểm tra đều đạt. Ảnh chụp: menu-360.png, menu-412.png' : `${failed} kiểm tra không đạt.`);
    process.exitCode = failed === 0 ? 0 : 1;
})().catch(err => { console.error(err.message); process.exitCode = 1; });
