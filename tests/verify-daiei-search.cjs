const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55440';
 const bundles=fs.readdirSync('catalogs').filter(n=>n.startsWith('大榮')&&n.endsWith('.catalog.json')).map(n=>JSON.parse(fs.readFileSync('catalogs/'+n,'utf8')));
 const rows=bundles.flatMap(b=>b.Rows),browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const query='/api/search?text='+encodeURIComponent('大榮産業');
  const result=await get(query);assert.equal(result.rows.length,758);assert.equal(result.capped,false);
  const bySku=new Map(result.rows.map(r=>[r.sku,r]));
  for(const row of rows){
   const r=bySku.get(row.SupplierProductCode);assert(r);
   for(const [a,b] of [['ProductNameJa','productNameJa'],['ProducerNameJa','producerNameJa'],['VolumeMl','volumeMl'],['VintageRaw','vintageRaw'],['ReferenceRetailPrice','referenceRetailPrice']])assert.equal(r[b],row[a]);
   assert.equal(r.inventory,0);assert.equal(r.quantity,null);
  }
  assert.equal((await get(query+'&available=true')).rows.length,0);
  await page.goto(base);await page.waitForFunction(()=>document.querySelector('#count').textContent.includes('件') && document.querySelector('#busy').hidden && !document.querySelector('#search').inert);
  await page.locator('#text').fill('大榮産業');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='758件');
  await page.screenshot({path:'artifacts/daiei-search.png',fullPage:true});
  await page.locator('#text').fill('BIT15L');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes('▲LIST-GERMAN-BEER.pdf'));
  await page.locator('summary').click();
  for(const s of ['OPEN','税区分','在庫'])assert((await page.locator('details').textContent()).includes(s));
  assert.deepEqual(errors,[]);
  const output=process.env.CATALOG_URL?'artifacts/daiei-production-verification.json':'artifacts/daiei-ui-verification.json';
  fs.writeFileSync(output,JSON.stringify({base,products:758,allValuesMatched:true,sourceEvidenceVerified:true,errors},null,2));
  console.log('758 Daiei products, source evidence, unknown inventory and UI verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
