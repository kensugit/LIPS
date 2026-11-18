const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
// Requires the isolated SupplierNaturalSearchTests database, not production.
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const base=process.env.CATALOG_URL||'http://127.0.0.1:55470';
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async p=>{const r=await page.request.get(base+p);assert.equal(r.status(),200);return r.json()};
  const all=await get('/api/search');
  const name=all.rows.find(r=>r.supplierName.endsWith('取扱社')).supplierName;
  const phrase='ピエールの白で'+name+'取り扱い';
  const expected=all.rows.filter(r=>r.supplierName===name&&r.referenceRetailPrice<=4000);
  assert.equal(expected.length,1);
  const result=await get('/api/search?'+new URLSearchParams({text:phrase,vintage:'2021',priceMax:'4000'}));
  assert.deepEqual(result.rows.map(r=>r.sku),expected.map(r=>r.sku));
  assert(result.conditions.includes('取扱先 '+name));
  await page.goto(base);
  await page.waitForFunction(()=>document.querySelector('#busy').hidden&&document.querySelector('#count').textContent.includes('件'));
  await page.locator('#text').fill(phrase);await page.selectOption('#vintage','2021');await page.locator('#priceMax').fill('4000');
  await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  assert((await page.locator('#interpreted').innerText()).includes('取扱先 '+name));
  assert((await page.locator('#rows').innerText()).includes(expected[0].sku));
  await page.locator('.product-select').check();await page.locator('#download-csv').click();
  const event=page.waitForEvent('download');await page.locator('#download-selected-csv').click();
  const download=await event;const csv=fs.readFileSync(await download.path(),'utf8');
  assert(csv.includes(expected[0].sku));assert(!csv.includes('別社'));assert(csv.includes('"2021"'));
  assert.deepEqual(errors,[]);
  await page.screenshot({path:'artifacts/vintage-normalization-20260929/supplier-preview.png',fullPage:true});
  console.log('Supplier phrase API, conditions, UI and filtered CSV verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
