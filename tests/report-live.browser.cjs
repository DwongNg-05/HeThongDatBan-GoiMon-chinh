const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const { writeFileSync, unlinkSync } = require('node:fs');
const { resolve } = require('node:path');

const url = process.env.RM_REPORT_TEST_URL;
const db = process.env.RM_REPORT_TEST_DB;
const server = process.env.RM_REPORT_TEST_SERVER;
if (!/^RestaurantManagement_Test_[a-f0-9]{32}$/.test(db || '') || !/^http:\/\/127\.0\.0\.1:\d+$/.test(url || ''))
    throw new Error('Browser verification requires an isolated test database and loopback web server.');

function checkout(table) {
    const fixture = resolve('.local/report-checkout.sql');
    writeFileSync(fixture, `
        SET NOCOUNT ON;
        EXEC dbo.usp_OpenSession @TableId=${table},@GuestCount=2,@ActorUserId=2;
        DECLARE @sid bigint=(SELECT MAX(SessionId) FROM dbo.SessionTables WHERE TableId=${table});
        DECLARE @request uniqueidentifier=NEWID();
        EXEC dbo.usp_SubmitOrder @SessionId=@sid,@RequestId=@request,@ItemsJson=N'[{"MenuItemId":1,"Quantity":1}]',@ActorUserId=2;
        DECLARE @item bigint=(SELECT MAX(i.Id) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId=@sid);
        EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Preparing',@ActorUserId=3;
        EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Ready',@ActorUserId=3;
        EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Served',@ActorUserId=2;
        SET @request=NEWID();
        EXEC dbo.usp_Checkout @SessionId=@sid,@RequestId=@request,@Method='Cash',@ActorUserId=4,@CashReceived=1000000;
    `, 'utf8');
    try {
        execFileSync('sqlcmd', ['-S', server, '-d', db, '-E', '-C', '-I', '-b', '-i', fixture], { windowsHide: true, timeout: 15000, stdio: 'pipe' });
    } catch (error) {
        throw new Error('Checkout fixture failed: ' + String(error.stdout || '') + String(error.stderr || ''));
    } finally { unlinkSync(fixture); }
    return Date.now();
}

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        const anon = await context.request.get(url + '/Reports/Today/Snapshot', { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        assert.equal(anon.status(), 401);
        await page.goto(url + '/Account/Login');
        await page.locator('#Identifier').fill('manager');
        await page.locator('#Password').fill(process.env.RM_REPORT_TEST_PASSWORD);
        await page.locator('form').filter({ has: page.locator('#Identifier') }).locator('button[type=submit]').click();
        await page.waitForURL(location => !location.pathname.startsWith('/Account/Login'));
        await page.goto(url + '/Reports/Today');
        await page.getByText('Chưa có dữ liệu', { exact: true }).waitFor();
        await page.locator('[data-report-panel] details').evaluate(e => { e.open = true; });
        const initialStamp = await page.locator('[data-report-updated]').textContent();
        const commitAt = checkout(60);
        await page.waitForFunction(() => document.querySelectorAll('.overview-metrics dd')[1]?.textContent.trim() === '1', null, { timeout: 45000 });
        const latency = Date.now() - commitAt;
        assert.ok(latency <= 60000, `Invoice not visible within 60s: ${latency}ms`);
        assert.equal(await page.getByText('Chưa có dữ liệu', { exact: true }).count(), 0);
        assert.notEqual(await page.locator('[data-report-updated]').textContent(), initialStamp);
        assert.ok(await page.locator('.revenue-bar').evaluateAll(bars => bars.some(b => parseFloat(b.style.height) > 0)));
        assert.equal(await page.locator('[data-report-panel] details').evaluate(e => e.open), true);
        assert.equal((await page.locator('tfoot td').textContent()).replace(/[^0-9]/g, ''), '25000');
        console.log(`PASS: real committed checkout appeared in metrics and chart after ${latency}ms (limit 60000ms).`);
        checkout(59); checkout(58);
        await page.waitForFunction(() => document.querySelectorAll('.overview-metrics dd')[1]?.textContent.trim() === '3', null, { timeout: 45000 });
        assert.equal((await page.locator('.overview-metrics dd').nth(0).textContent()).replace(/[^0-9]/g, ''), '75000');
        console.log('PASS: three consecutive real invoices reconcile without double counting.');
        const from = await page.locator('#report-from').inputValue();
        const today = new Date(from + 'T12:00:00Z');
        const previous = new Date(today.getTime() - 86400000).toISOString().slice(0, 10);
        await page.locator('#report-from').fill(previous);
        await page.locator('#report-to').fill(previous);
        await page.getByRole('button', { name: 'Xem thống kê', exact: true }).click();
        await page.getByText('Chưa có dữ liệu', { exact: true }).waitFor();
        const refreshResponse = page.waitForResponse(r => r.url().includes('/Reports/Today/Snapshot'));
        await page.getByRole('button', { name: 'Làm mới ngay', exact: true }).click();
        const applied = new URL((await refreshResponse).url());
        assert.equal(applied.searchParams.get('from'), previous);
        assert.equal(applied.searchParams.get('to'), previous);
        await page.waitForFunction(() => document.querySelector('[data-report-refresh-status]').textContent.includes('Đã cập nhật'));
        assert.equal(await page.locator('.overview-metrics dd').nth(1).textContent(), '0');
        console.log('PASS: changing the range refreshes only the chosen business dates.');
        await page.locator('#report-from').fill(previous);
        await page.locator('#report-to').fill(from);
        await page.getByRole('button', { name: 'Xem thống kê', exact: true }).click();
        await page.getByRole('heading', { name: 'Doanh thu theo ngày', exact: true }).waitFor();
        assert.equal(await page.locator('.revenue-column').count(), 2);
        await page.locator('[data-report-panel] details').evaluate(e => { e.open = true; });
        assert.equal((await page.locator('tfoot td').textContent()).replace(/[^0-9]/g, ''), '75000');
        console.log('PASS: the selected multi-day range shows daily columns and an exact table total.');
        const beforeFailure = await page.locator('[data-report-updated]').textContent();
        await page.route('**/Reports/Today/Snapshot*', route => route.abort());
        await page.getByRole('button', { name: 'Làm mới ngay', exact: true }).click();
        await page.locator('[data-report-refresh-status]').filter({ hasText: 'Không thể cập nhật' }).waitFor();
        assert.equal(await page.locator('.overview-metrics dd').nth(1).textContent(), '3');
        assert.equal(await page.locator('[data-report-updated]').textContent(), beforeFailure);
        await page.unroute('**/Reports/Today/Snapshot*');
        await page.getByRole('button', { name: 'Làm mới ngay', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('[data-report-refresh-status]').textContent.includes('Đã cập nhật'));
        console.log('PASS: network failure preserves last totals/time, reports failure, and recovers.');
        assert.ok(await page.evaluate(() => document.querySelector('.daily-overview').getBoundingClientRect().left >= document.querySelector('.admin-sidebar').getBoundingClientRect().right), 'Desktop report must not be hidden behind the sidebar.');
        await page.screenshot({ path: '.local/report-live-desktop.png' });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: '.local/report-live-mobile.png', fullPage: true });
        assert.equal(errors.length, 0, 'Unexpected browser JavaScript errors: ' + errors.join('; '));
        console.log('PASS: desktop/mobile rendered without browser script errors.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
