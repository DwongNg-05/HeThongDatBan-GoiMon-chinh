// Run by --no-show with NOSHOW_BROWSER_TEST=1; requires Playwright and Chrome.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    const base = process.env.NOSHOW_TEST_URL;
    await page.goto(base + 'Account/Login');
    await page.locator('[name=Identifier]').fill('no_show_test');
    await page.locator('[name=Password]').fill(process.env.NOSHOW_TEST_PASSWORD);
    await Promise.all([page.waitForURL(url => !url.pathname.includes('/Account/Login')),page.locator('button[type=submit]').click()]);
    await page.goto(base + 'ReservationManagement');
    const map = await context.newPage(); await map.goto(base + 'TableMap');
    await page.waitForFunction(() => document.querySelector('[data-no-show-items]')?.textContent.includes('Không có'));
    let navigations = 0;
    page.on('framenavigated',frame=>{if(frame===page.mainFrame())navigations++;});
    map.on('framenavigated',frame=>{if(frame===map.mainFrame())navigations++;});
    const warning = page.locator('[data-no-show-items] .alert').filter({hasText:'N00001'});
    await warning.waitFor({timeout:45000});
    await map.locator('[data-table-code="N00001"] [data-no-show-badge]').waitFor({timeout:15000});
    assert.equal(navigations,0); console.log('PASS: 15-minute warning appears on list and table map without reload');
    const rejected = await page.request.post(base+'NoShow/Mark',{form:{id:'1'}});
    assert.equal(rejected.status(),400); console.log('PASS: CSRF required');
    page.once('dialog',dialog=>dialog.accept());
    await warning.getByRole('button',{name:'Đánh dấu khách không tới'}).click();
    await warning.waitFor({state:'detached',timeout:10000});
    await map.waitForFunction(()=>document.querySelector('[data-table-code="N00001"]')?.dataset.status==='Available');
    await map.locator('[data-table-code="N00001"] [data-no-show-badge]').waitFor({state:'detached',timeout:15000});
    await page.locator('[data-no-show-history]').filter({hasText:'3 lần'}).waitFor();
    await page.locator('#no-show-phone').fill('0084 (912)-345-678');
    await page.locator('[data-phone-history-form] button').click();
    await page.locator('[data-no-show-history]').filter({hasText:'3 lần'}).waitFor();
    assert.equal(navigations,0); console.log('PASS: no-show releases table and updates both screens/history without reload');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
