const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55440';
 const bundle=JSON.parse(fs.readFileSync('catalogs/ファインズ.在庫表.20260921.catalog.json','utf8'));
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1500,height:1100}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const all=await get('/api/search?text='+encodeURIComponent('ファインズ'));
  assert.equal(all.rows.length,2000);assert.equal(all.capped,true);
  const collected=[];
  for(const country of new Set(bundle.Rows.map(r=>r.Country))){
   const part=await get('/api/search?'+new URLSearchParams({text:'ファインズ',country,available:'true'}));
   assert.equal(part.capped,false);collected.push(...part.rows);
  }
  const bySku=new Map(collected.map(r=>[r.sku,r]));
  assert.equal(bySku.size,2090);
  for(const r of bundle.Rows){
   const s=bySku.get(r.SupplierProductCode);assert(s);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice']])assert.equal(s[b],r[a]);
   assert.equal(s.quantity,r.Inventory.Quantity);assert.equal(s.inventory,r.Inventory.Status);
   const raw=JSON.parse(r.RawCellsJson);
   assert.equal(s.referenceRetailPrice,Number(raw['参考上代']));
   if(raw['在庫ｹｰｽ']==='100C以上') assert.equal(s.quantity,null);
   else assert.equal(s.quantity,Number(raw['在庫ｹｰｽ'])*Number(raw['入数'])+Number(raw['在庫本']));
  }
  assert.equal((await get('/api/search?text='+encodeURIComponent('ファインズ')+'&available=true')).rows.length,2000);
  await page.goto(base);
  await page.waitForFunction(()=>document.querySelector('#total').textContent.includes('登録商品'));
  await page.waitForFunction(()=>!document.querySelector('#search').inert && document.querySelector('#busy').hidden);
  await page.locator('#text').fill('ファインズ');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent.includes('先頭2,000件'));
  await page.screenshot({path:'artifacts/fines-search.png',fullPage:true});
  await page.locator('#text').fill('BWCMC23');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes(bundle.SourceFileName));
  await page.locator('summary').click();
  const detail=await page.locator('details').textContent();
  for(const text of ['納入価格','在庫ｹｰｽ','21280','税']) assert(detail.includes(text));
  await page.screenshot({path:'artifacts/fines-evidence.png',fullPage:true});
  assert.deepEqual(errors,[]);
  fs.writeFileSync('artifacts/fines-ui-verification.json',JSON.stringify({base,products:2090,available:2090,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('2090 products, prices, inventory, source evidence and search UI verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
