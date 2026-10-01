// Real Chromium + real application/database. No mocked page or synthetic render timings.
const { chromium, request } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { performance } = require('node:perf_hooks');
const baseURL = process.env.RM_PERF_URL || 'http://127.0.0.1:5217';
const expected = Number(process.env.RM_PERF_TABLE_COUNT || 60);
const output = process.env.RM_PERF_REPORT || path.resolve('.local/table-map-performance.json');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const report = { measuredAtUtc: new Date().toISOString(), tableCount: expected, environment: 'local Chromium headless; loopback HTTP; local SQL Server', acceptanceStandardConfirmed: false, readyDefinition: 'all tiles laid out, sync/details handlers attached, two paint opportunities', scenarios: {}, polls: [], clickMs: [], errors: [] };
let browser, authentication;
async function login() {
    authentication = await request.newContext({ baseURL });
    const loginPage = await authentication.get('/Account/Login');
    const token = (await loginPage.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    if (!token) throw new Error('Login form token missing');
    const result = await authentication.post('/Account/Login', { maxRedirects: 0, form: { Identifier: 'waiter', Password: process.env.RM_DEMO_PASSWORD, __RequestVerificationToken: token } });
    if (result.status() !== 302) throw new Error('Performance fixture login failed');
    if (result.headers().location?.includes('DoiMatKhau')) throw new Error('Fixture requires password change before benchmarking');
    return authentication.storageState();
}
async function measure(page) {
    await page.goto(baseURL + '/TableMap', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForFunction(count => { const map = document.querySelector('.snapshot-map'); return Number(map?.dataset.tableCount) === count && !!map.dataset.readyMs; }, expected, { timeout: 30000 });
    return page.locator('.snapshot-map').evaluate(element => Number(element.dataset.readyMs));
}
function observe(page) {
    page.on('pageerror', error => report.errors.push(error.message));
    page.on('requestfinished', async req => {
        if (!req.url().includes('/api/table-map/changes')) return;
        try {
            const timing = req.timing();
            const response = await req.response();
            if (response?.status() === 200) report.polls.push({ milliseconds: Math.round(timing.responseEnd), bytes: (await response.body()).length });
        } catch {
            // Navigation can discard an already finished polling response before CDP reads its body.
            // It is not a slow/failing application response and is excluded from polling samples.
        }
    });
}
async function writeStatus(status) {
    const edit = await authentication.get('/Tables/Edit/1');
    const token = (await edit.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    if (!token) throw new Error('Edit form token missing');
    const response = await authentication.post('/Tables/Edit', { maxRedirects: 0, form: { Id: '1', Code: 'A01', AreaId: '1', MinCapacity: '1', MaxCapacity: '4', TableType: 'Standard', Status: status, __RequestVerificationToken: token } });
    if (response.status() !== 302) throw new Error('Fixture status update failed');
}
async function tenColdLoads(storageState, slow) {
    const times = [];
    for (let index = 0; index < 10; index++) {
        const context = await browser.newContext({ storageState, viewport: { width: 1366, height: 768 } });
        const page = await context.newPage(); observe(page);
        if (slow) {
            const cdp = await context.newCDPSession(page);
            await cdp.send('Network.enable');
            await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 100, downloadThroughput: 4 * 1024 * 1024 / 8, uploadThroughput: 1024 * 1024 / 8 });
        }
        times.push(await measure(page));
        await context.close();
    }
    return times;
}
(async () => {
    try {
        const storageState = await login();
        browser = await chromium.launch({ headless: true, executablePath: process.env.RM_PERF_BROWSER || 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
        report.browserVersion = browser.version();
        report.scenarios.cold = await tenColdLoads(storageState, false);
        fs.writeFileSync(output, JSON.stringify(report, null, 2));
        console.log(`${expected} tables: cold loads measured`);
        const warmContext = await browser.newContext({ storageState, viewport: { width: 1366, height: 768 } });
        const page = await warmContext.newPage(); observe(page);
        await measure(page);
        report.scenarios.warm = [];
        for (let index = 0; index < 10; index++) report.scenarios.warm.push(await measure(page));
        fs.writeFileSync(output, JSON.stringify(report, null, 2));
        if (expected === 60) {
            report.slowNetwork = { latencyMs: 100, downloadMbps: 4, uploadMbps: 1, simulated: true };
            report.scenarios.slowCold = await tenColdLoads(storageState, true);
            fs.writeFileSync(output, JSON.stringify(report, null, 2));
            console.log('60 tables: slow-network loads measured');
            const clients = await Promise.all(Array.from({ length: 6 }, async () => {
                const context = await browser.newContext({ storageState });
                const client = await context.newPage(); observe(client);
                await measure(client); return { context, page: client };
            }));
            const writes = (async () => { for (let index = 0; index < 12; index++) { await writeStatus(index % 2 ? 'Available' : 'Cleaning'); await sleep(500); } })();
            report.scenarios.concurrent = [];
            for (let index = 0; index < 10; index++) report.scenarios.concurrent.push(...await Promise.all(clients.map(client => measure(client.page))));
            await writes;
            await Promise.all(clients.map(client => client.context.close()));
            // Keep an open detail panel during status synchronization and validate remote refresh.
            await measure(page);
            await page.locator('[data-open-details="A01"]').click();
            await page.locator('[data-details-content]').waitFor({ state: 'visible' });
            const changedAt = performance.now();
            await writeStatus('Cleaning');
            await page.waitForFunction(() => document.querySelector('[data-details-status]').textContent === 'Đang dọn');
            report.remoteDetailsMs = Math.round(performance.now() - changedAt);
            await page.locator('[data-details-close]').click();
            await page.evaluate(() => { window.perfLongTasks = []; new PerformanceObserver(list => window.perfLongTasks.push(...list.getEntries().map(entry => entry.duration))).observe({ type: 'longtask', buffered: false }); });
            const duration = Number(process.env.RM_PERF_SOAK_SECONDS || 120);
            report.soakSeconds = duration;
            const started = performance.now();
            while (performance.now() - started < duration * 1000) {
                const before = performance.now();
                await page.locator('[data-open-details="A03"]').click();
                await page.locator('[data-details-content]').waitFor({ state: 'visible' });
                report.clickMs.push(Math.round(performance.now() - before));
                if (!(await page.locator('[data-details-subtotal]').innerText()).includes('₫')) throw new Error('Serving fixture subtotal missing');
                await page.locator('[data-details-close]').click();
                await writeStatus(report.clickMs.length % 2 ? 'Available' : 'Cleaning');
                await sleep(5000);
            }
            report.longTasksMs = await page.evaluate(() => window.perfLongTasks);
            await page.locator('[data-open-details="A03"]').click();
            await page.locator('[data-details-content]').waitFor({ state: 'visible' });
            await page.screenshot({ path: path.join(path.dirname(output), 'table-details-60.png') });
        }
        await warmContext.close();
        report.pass = Object.values(report.scenarios).every(samples => samples.length >= 10 && samples.every(ms => ms < 2000)) && report.errors.length === 0;
        report.maxReadyMs = Math.max(...Object.values(report.scenarios).flat());
        console.log(`${expected} tables: max readiness ${report.maxReadyMs} ms; ${report.pass ? 'PASS' : 'FAIL'}`);
        if (!report.pass) process.exitCode = 1;
    } catch (error) {
        report.pass = false; report.errors.push(error.message); console.error(error.message); process.exitCode = 1;
    } finally {
        if (browser) await browser.close();
        if (authentication) await authentication.dispose();
        fs.mkdirSync(path.dirname(output), { recursive: true });
        fs.writeFileSync(output, JSON.stringify(report, null, 2));
    }
})();
