const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55561';
 const bundle=JSON.parse(fs.readFileSync('catalogs/テラヴェール.価格表.20260915.catalog.json','utf8'));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const query='/api/search?text='+encodeURIComponent('テラヴェール');
  const result=await get(query);assert.equal(result.rows.length,1090);assert.equal(result.capped,false);
  const bySku=new Map(result.rows.map(r=>[r.sku,r]));assert.equal(bySku.size,1090);
  for(const row of bundle.Rows){
   const r=bySku.get(row.SupplierProductCode);assert(r);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice']])assert.equal(r[b],row[a]);
   assert.equal(r.inventory,row.Inventory.Status);assert.equal(r.quantity,row.Inventory.Quantity);
  }
  assert.equal((await get(query+'&available=true')).rows.length,918);
  assert.equal((await get(query+'&country='+encodeURIComponent('スロヴェニア'))).rows.length,21);
  await page.goto(base);await page.waitForFunction(()=>document.querySelector('#count').textContent.includes('件') && document.querySelector('#busy').hidden && !document.querySelector('#search').inert);
  await page.locator('#text').fill('テラヴェール');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1,090件');
  await page.screenshot({path:'artifacts/terravert-search.png',fullPage:true});
  await page.locator('#text').fill('テラヴェール AL372');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes(bundle.SourceFileName));
  await page.locator('summary').click();
  for(const s of ['税区分要確認','入港予定','再掲シート'])assert((await page.locator('details').textContent()).includes(s));
  await page.screenshot({path:'artifacts/terravert-evidence.png',fullPage:true});
  assert.deepEqual(errors,[]);
  fs.writeFileSync('artifacts/terravert-'+(base.includes('192.168.')?'production':'ui')+'-verification.json',JSON.stringify({base,products:1090,available:918,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('1090 products, prices, inventory, source evidence and search UI verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
