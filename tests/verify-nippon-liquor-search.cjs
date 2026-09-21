const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55562';
 const bundle=JSON.parse(fs.readFileSync('catalogs/nippon-liquor/shipping.catalog.json','utf8'));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const query='/api/search?text='+encodeURIComponent('日本リカー');
  const result=await get(query);assert.equal(result.rows.length,891);assert.equal(result.capped,false);
  const bySku=new Map(result.rows.map(r=>[r.sku,r]));assert.equal(bySku.size,891);
  for(const row of bundle.Rows){
   const r=bySku.get(row.SupplierProductCode);assert(r);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice']])assert.equal(r[b],row[a]);
   assert.equal(r.inventory,row.Inventory.Status);assert.equal(r.quantity,row.Inventory.Quantity);
  }
  assert.equal((await get(query+'&available=true')).rows.length,891);

  await page.goto(base);await page.waitForFunction(()=>document.querySelector('#count').textContent.includes('件') && document.querySelector('#busy').hidden && !document.querySelector('#search').inert);
  await page.locator('#text').fill('日本リカー');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='891件');
  await page.screenshot({path:'artifacts/nippon-liquor-search.png',fullPage:true});
  await page.locator('#text').fill('日本リカー 3524RR051310');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes(bundle.SourceFileName));
  await page.locator('summary').click();
  for(const s of ['税別希望小売価格','納価（円）','25200','関連原表'])assert((await page.locator('details').textContent()).includes(s));
  await page.screenshot({path:'artifacts/nippon-liquor-evidence.png',fullPage:true});
  assert.deepEqual(errors,[]);
  fs.writeFileSync('artifacts/nippon-liquor-'+(base.includes('192.168.')?'production':'ui')+'-verification.json',JSON.stringify({base,products:891,available:891,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('891 products, prices, inventory, source evidence and search UI verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
