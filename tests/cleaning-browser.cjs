const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const {execFileSync}=require('node:child_process');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const base=process.env.CLEANING_TEST_URL;
  const pages=[];
  for(const path of ['', '', 'TableMap']){
   const context=await browser.newContext(), page=await context.newPage();
   if(pages.length===1) await context.route('**/api/table-status/stream',route=>route.abort());
   await page.goto(base+'Account/Login');
   await page.locator('#Identifier').fill('demo-manager');
   await page.locator('#Password').fill(process.env.RM_DEMO_PASSWORD);
   await Promise.all([page.waitForURL(u=>!u.pathname.includes('/Account/Login')),page.locator('form').filter({has:page.locator('#Identifier')}).locator('button[type=submit]').click()]);
   await page.goto(base+path); pages.push(page);
   await page.locator('[data-table-code=CLEAN1] [data-complete-cleaning]').waitFor({state:'visible'});
   await page.locator('[data-table-code=CLEAN1] [data-cleaning-warning]').waitFor({state:'visible',timeout:5000});
   for(const code of ['FREE1','RES1','SERV1','RACE1']) assert.equal(await page.locator(`[data-table-code=${code}] [data-cleaning-warning]`).isVisible(),false);
   for(const code of ['FREE1','RES1','SERV1']) assert.equal(await page.locator(`[data-table-code=${code}] [data-complete-cleaning]`).isVisible(),false);
  }
  const page=pages[0],token=await page.locator('[data-cleaning-config] [name=__RequestVerificationToken]').inputValue();
  const post=code=>page.request.post(base+`api/table-cleaning/${code}/complete`,{form:{__RequestVerificationToken:token}});
  assert.equal((await page.request.post(base+'api/table-cleaning/CLEAN1/complete')).status(),400);
  for(const code of ['FREE1','RES1','SERV1','BUSY1','MISSING']) assert.equal((await post(code)).status(),409);
  const start=Date.now();
  await page.locator('[data-table-code=CLEAN1] [data-complete-cleaning]').click();
  for(const tab of pages){
   await tab.waitForFunction(()=>document.querySelector('[data-table-code=CLEAN1]').dataset.status==='Available',null,{timeout:5000});
   assert.equal(await tab.locator('[data-table-code=CLEAN1] [data-complete-cleaning]').isVisible(),false);
   await tab.locator('[data-table-code=CLEAN1] [data-cleaning-warning]').waitFor({state:'hidden',timeout:5000});
  }
  assert.ok(Date.now()-start<5000);
  console.log(`PASS: three independent browser sessions, both maps updated in ${Date.now()-start} ms without reload`);
  assert.equal((await post('CLEAN1')).status(),409);
  const race=await Promise.all([post('RACE1'),post('RACE1')]);
  assert.deepEqual(race.map(r=>r.status()).sort(),[200,409]);
  // Re-enter Cleaning while the maps stay open: the button must appear again.
  await page.request.post(base+'api/table-status/CLEAN1',{data:{status:'Cleaning'}});
  for(const tab of pages) await tab.locator('[data-table-code=CLEAN1] [data-complete-cleaning]').waitFor({state:'visible',timeout:5000});
  for(const tab of pages) assert.equal(await tab.locator('[data-table-code=CLEAN1] [data-cleaning-warning]').isVisible(),false);
  const start2=Date.now(); await pages[2].locator('[data-table-code=CLEAN1] [data-complete-cleaning]').click();
  for(const tab of pages) await tab.waitForFunction(()=>document.querySelector('[data-table-code=CLEAN1]').dataset.status==='Available',null,{timeout:5000});
  assert.ok(Date.now()-start2<5000);
  console.log('PASS: snapshot-map button, wrong status, active session, CSRF, duplicate and concurrent requests');
  // Prepare a near-boundary fixture, then keep all three pages open through the deadline.
  assert.match(process.env.CLEANING_TEST_DB,/^RestaurantCleaningTest_[a-f0-9]{32}$/);
  execFileSync('sqlcmd',['-S','(localdb)\\MSSQLLocalDB','-d',process.env.CLEANING_TEST_DB,'-E','-b','-I','-Q',"UPDATE dbo.DiningTables SET StatusChangedAt=DATEADD(second,-590,SYSUTCDATETIME()) WHERE Code='BUSY1';"]);
  await Promise.all(pages.map(tab=>tab.reload()));
  assert.equal((await (await page.request.get(base+'api/table-cleaning/warnings')).json()).find(x=>x.code==='BUSY1').overdue,false);
  // Browser clock deliberately differs from the server by one day.
  await pages[1].clock.setFixedTime(new Date(Date.now()+86400000));
  for(const tab of pages) await tab.locator('[data-table-code=BUSY1] [data-cleaning-warning]').waitFor({state:'visible',timeout:15000});
  assert.equal((await post('BUSY1')).status(),409);
  console.log('PASS: warning crosses threshold without reload on both maps, despite client clock skew; active-session completion stays blocked');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
