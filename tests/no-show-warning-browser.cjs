const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async () => {
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
  const context=await browser.newContext(), page=await context.newPage(), base=process.env.NOSHOW_TEST_URL;
  await page.goto(base+'Account/Login');
  await page.locator('[name=Identifier]').fill('no_show_test');
  await page.locator('[name=Password]').fill(process.env.NOSHOW_TEST_PASSWORD);
  await Promise.all([page.waitForURL(u=>!u.pathname.includes('/Account/Login')),page.locator('button[type=submit]').click()]);
  for(const path of ['Reservations/Create','PublicReservations/Create','TableReservations/Create']) {
   await page.goto(base+path);
   await page.locator('[name=Phone]').fill('+84 912 345 678');
   await page.locator('[data-warning-panel]').waitFor({state:'visible'});
   assert.match(await page.locator('[data-warning-text]').textContent(),/3 lần/);
   await page.locator('[name=NoShowAcknowledged]').check();
   await page.locator('[name=Phone]').fill('0900000000');
   await page.locator('[data-warning-panel]').waitFor({state:'hidden'});
   assert.equal(await page.locator('[name=NoShowAcknowledged]').isChecked(),false);
   await page.locator('[name=Phone]').fill('0084-912-345-678');
   await page.locator('[data-warning-panel]').waitFor({state:'visible'});
   assert.match(await page.locator('[data-warning-text]').textContent(),/3 lần/);
   console.log('PASS: live warning, phone equivalence, phone change resets acknowledgement: '+path);
  }
  const csrf=await page.locator('[name=__RequestVerificationToken]').first().inputValue();
  assert.equal((await page.request.post(base+'NoShowWarning/Check',{form:{phone:'0912345678'}})).status(),400);
  const fields={TableId:'1',ReservationDate:'2030-01-02',StartTime:'12:00',CustomerName:'Browser test',Phone:'0912345678',InitialStatus:'Pending',__RequestVerificationToken:csrf};
  const response=await page.request.post(base+'TableReservations/Create',{form:fields});
  const html=await response.text();
  assert.match(html,/data-warning-panel/);
  // Server-side submission rechecks history even without the live lookup/JavaScript.
  await page.setContent(html);
  assert.equal(await page.locator('[data-warning-panel]').isVisible(),true);
  assert.match(await page.locator('[data-warning-text]').textContent(),/3 lần/);
  const oldReceipt=await page.locator('[name=NoShowWarningToken]').inputValue();
  const marked=await page.request.post(base+'NoShow/Mark',{form:{id:process.env.HOLD_TEST_ID,__RequestVerificationToken:csrf}});
  assert.equal(marked.status(),200);
  const stale=await page.request.post(base+'TableReservations/Create',{form:{...fields,NoShowAcknowledged:'true',NoShowWarningToken:oldReceipt},maxRedirects:0});
  assert.equal(stale.status(),200);
  await page.setContent(await stale.text());
  assert.match(await page.locator('[data-warning-text]').textContent(),/4 lần/);
  assert.equal(await page.locator('[name=NoShowAcknowledged]').isChecked(),false);
  const receipt=await page.locator('[name=NoShowWarningToken]').inputValue();
  const success=await page.request.post(base+'TableReservations/Create',{form:{...fields,NoShowAcknowledged:'true',NoShowWarningToken:receipt},maxRedirects:0});
  assert.equal(success.status(),302);
  console.log('PASS: server submission shows warning without lookup; confirmed warning permits booking; CSRF enforced');
  console.log('PASS: history grows from 3 to 4 during creation; stale receipt rejected and updated warning requires reconfirmation');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
