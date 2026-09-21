const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55440';
 const bundle=JSON.parse(fs.readFileSync('catalogs/豊通食料.ワインリスト.202609.catalog.json','utf8'));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1500,height:1100}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const all=await get('/api/search?text='+encodeURIComponent('豊通食料'));
  assert.equal(all.rows.length,640);
  const bySku=new Map(all.rows.map(r=>[r.sku,r]));
  for(const r of bundle.Rows){
   const s=bySku.get(r.SupplierProductCode);assert(s);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice'],['PriceNote','priceNote']])assert.equal(s[b],r[a]);
   assert.equal(s.quantity,null);assert.equal(s.inventory,r.Inventory.Status);assert.equal(s.taxIncluded,false);
  }
  assert.equal((await get('/api/search?text='+encodeURIComponent('豊通食料')+'&available=true')).rows.length,518);
  await page.goto(base);
  await page.waitForFunction(()=>document.querySelector('#total').textContent.includes('登録商品'));
  await page.waitForFunction(()=>!document.querySelector('#search').inert && document.querySelector('#busy').hidden);
  await page.locator('#text').fill('豊通食料');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='640件');
  await page.screenshot({path:'artifacts/toyotsu-search.png',fullPage:true});
  await page.locator('#text').fill('F54200');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes(bundle.SourceFileName));
  await page.locator('summary').click();assert((await page.locator('details').textContent()).includes('掲載箇所'));
  await page.screenshot({path:'artifacts/toyotsu-evidence.png',fullPage:true});
  assert.deepEqual(errors,[]);
  fs.writeFileSync('artifacts/toyotsu-ui-verification.json',JSON.stringify({base,products:640,available:518,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('640 products and values, 518 available, UI count and original evidence verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
