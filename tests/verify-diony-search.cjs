const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55440';
 const bundle=JSON.parse(fs.readFileSync('catalogs/カタログ.ディオニー.2026voi9.catalog.json','utf8'));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1400,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const result=await get('/api/search?text=Diony');
  assert.equal(result.capped,false);
  const skus=new Set(bundle.Rows.map(r=>r.SupplierProductCode));
  const actual=result.rows.filter(r=>skus.has(r.sku));assert.equal(actual.length,516);
  const bySku=new Map(actual.map(r=>[r.sku,r]));
  for(const row of bundle.Rows){
   const r=bySku.get(row.SupplierProductCode);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice'],['ProductType','productType']])assert.equal(r[b],row[a],row.SupplierProductCode+' '+a);
   assert.equal(r.inventory,row.Inventory.Status);assert.equal(r.quantity,row.Inventory.Quantity);
  }
  const available=await get('/api/search?text=Diony&available=true');
  assert.equal(available.rows.filter(r=>skus.has(r.sku)).length,83);
  await page.goto(base);
  await page.waitForFunction(()=>document.querySelector('#busy').hidden && !document.querySelector('#search').inert);
  await page.locator('#text').fill('42835');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes('カタログ.ディオニー.2026voi9.pdf'));
  await page.locator('summary').click();
  assert((await page.locator('details').textContent()).includes('1500'));
  await page.screenshot({path:'artifacts/diony-source-ui.png',fullPage:true});
  assert.deepEqual(errors,[]);
  fs.writeFileSync(process.env.CATALOG_URL?'artifacts/diony-production-verification.json':'artifacts/diony-ui-verification.json',JSON.stringify({base,products:516,available:83,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('516 Diony products and source UI verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
