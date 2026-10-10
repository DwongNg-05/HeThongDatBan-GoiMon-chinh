// Isolated SQL + real Chrome. The fixture's actual +30 minute deadline is ~75 seconds away.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    const base = process.env.NOSHOW_TEST_URL;
    const id = process.env.HOLD_TEST_ID;
    await page.goto(base+'Account/Login');
    await page.locator('[name=Identifier]').fill('no_show_test');
    await page.locator('[name=Password]').fill(process.env.NOSHOW_TEST_PASSWORD);
    await Promise.all([page.waitForURL(url=>!url.pathname.includes('/Account/Login')),page.locator('button[type=submit]').click()]);
    await page.goto(base+'ReservationManagement');
    const map = await context.newPage(); await map.goto(base);
    const snapshot = await context.newPage(); await snapshot.goto(base+'TableMap');
    const item = `[data-reservation-id="${id}"]`;
    await page.locator('[data-no-show-items] '+item).waitFor();
    const state = (await (await page.request.get(base+'NoShow/Alerts')).json()).find(x=>x.id==id);
    assert.equal(state.canExtend,true);
    const tableCode=state.tableCode;
    let navigations=0;
    for(const tab of [page,map,snapshot]) tab.on('framenavigated',f=>{if(f===tab.mainFrame())navigations++;});
    const token=await page.locator('#no-show-alerts [name=__RequestVerificationToken]').first().inputValue();
    assert.equal((await page.request.post(base+'NoShow/Extend',{form:{id}})).status(),400);
    page.once('dialog',d=>d.accept());
    await page.locator(item).getByRole('button',{name:'Gia hạn 15 phút',exact:true}).click();
    for(const tab of [page,map,snapshot]) {
      await tab.locator('[data-extended-holds] '+item).waitFor({timeout:15000});
      await tab.waitForFunction(id=>!document.querySelector(`[data-no-show-items] [data-reservation-id="${id}"]`),id);
      assert.match(await tab.locator(item).textContent(),/Đang gia hạn/);
      assert.ok((await tab.locator(item).textContent()).includes(state.extensionDeadline));
      assert.equal(await tab.locator(item).getByRole('button',{name:'Đã gia hạn 1/1 lần',exact:true}).isDisabled(),true);
    }
    for(const tab of [map,snapshot]) {
      await tab.locator(`[data-table-code="${tableCode}"] [data-hold-badge]`).waitFor();
      assert.equal(await tab.locator(`[data-table-code="${tableCode}"]`).getAttribute('data-status'),'Reserved');
      assert.equal(await tab.locator(`[data-table-code="${tableCode}"] [data-no-show-badge]`).count(),0);
      assert.equal(await tab.locator(`[data-table-code="${tableCode}"] [data-hold-badge]`).evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);
    }
    const form={id,__RequestVerificationToken:token};
    assert.equal((await page.request.post(base+'NoShow/Extend',{form})).status(),409);
    assert.equal((await page.request.post(base+'NoShow/Mark',{form})).status(),409);
    console.log('PASS: all three pages show the fixed deadline, remove warning, keep table reserved; repeat and stale no-show rejected');
    await page.locator('[data-no-show-items] '+item).waitFor({timeout:95000});
    for(const tab of [page,map,snapshot]) {
      await tab.locator('[data-no-show-items] '+item).waitFor({timeout:15000});
      assert.match(await tab.locator(item).textContent(),/Đã hết hạn gia hạn/);
      assert.equal(await tab.locator(item).getByRole('button',{name:'Đã gia hạn 1/1 lần',exact:true}).isDisabled(),true);
    }
    console.log('PASS: real server time reaches deadline; warning returns on all screens without reload');
    page.once('dialog',d=>d.accept());
    await page.locator(item).getByRole('button',{name:'Đánh dấu khách không tới'}).click();
    await page.waitForFunction(code=>document.querySelector('[data-no-show-history]')?.textContent.includes(code),state.code);
    for(const tab of [map,snapshot]) {
      await tab.waitForFunction(code=>document.querySelector(`[data-table-code="${code}"]`)?.dataset.status==='Available',tableCode);
      await tab.waitForFunction(code=>!document.querySelector(`[data-table-code="${code}"] [data-no-show-badge]`),tableCode);
    }
    assert.equal((await page.request.post(base+'NoShow/Mark',{form})).status(),409);
    const history=await (await page.request.get(base+'NoShow/History?id='+id)).json();
    assert.equal(history.filter(h=>h.code===state.code).length,1);
    assert.equal(navigations,0);
    console.log('PASS: expired hold can be marked no-show once, table released and one history row; zero page reloads');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
