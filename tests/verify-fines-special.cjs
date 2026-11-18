const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const base=process.env.CATALOG_URL||'http://127.0.0.1:55460';
 const expected=['main','extra'].flatMap(n=>JSON.parse(fs.readFileSync(`catalogs/fines-special-20260924/${n}.catalog.json`,'utf8')).Rows);
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1800,height:1100}});const errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const get=async path=>{const r=await page.request.get(base+path);assert.equal(r.status(),200);return r.json()};
  const found=new Map();
  for(const country of new Set(expected.map(r=>r.Country))){
   const data=await get('/api/search?'+new URLSearchParams({text:'ファインズ',country}));
   assert.equal(data.capped,false);for(const row of data.rows)found.set(row.sku,row);
  }
  assert.equal([...found.values()].filter(r=>r.specialPurchasePrice!=null).length,331);
  for(const r of expected){
   const s=found.get(r.SupplierProductCode);assert(s,r.SupplierProductCode);
   const raw=JSON.parse(r.RawCellsJson);
   assert.equal(s.referenceRetailPrice,r.ReferenceRetailPrice);
   assert.equal(s.specialPurchasePrice,raw.SpecialPurchasePrice.Amount);
   assert.equal(s.quantity,r.Inventory.Quantity);
   assert.equal(s.sourceSheet,r.SourceSheet);assert.equal(s.sourceRow,r.SourceRow);
   const e=await get(`/api/evidence/${s.sourceDocumentId}/${encodeURIComponent(s.sku)}`);
   assert.deepEqual(JSON.parse(e.rawCellsJson),raw);
  }
  assert.equal(found.get('ALDMR21A').specialPurchasePrice,63750);
  assert.equal(found.get('ALDMR21A').quantity,201);
  assert.equal(found.get('BLFLA22').specialPurchasePrice,20992.5);
  const regular=[...found.values()].find(r=>r.specialPurchasePrice==null);assert(regular);
  await page.goto(base);await page.waitForFunction(()=>!document.querySelector('#search').inert&&document.querySelector('#busy').hidden);
  await page.locator('#text').fill('BLFLA22');await page.locator('button[type=submit]').click();
  await page.waitForFunction(()=>document.querySelector('#rows tr')?.dataset.sku==='BLFLA22');
  assert((await page.locator('#rows').textContent()).includes('20,992.5円'));
  assert((await page.locator('thead').textContent()).includes('特価仕入価格'));
  await page.screenshot({path:'artifacts/fines-special-20260924/special-price-ui.png',fullPage:true});
  const downloadPromise=page.waitForEvent('download');await page.locator('#download-csv').click();
  const download=await downloadPromise;const path=await download.path();const csv=fs.readFileSync(path,'utf8');
  assert(csv.includes('特価仕入価格（税抜）'));assert(csv.includes('"20992.5"'));
  await page.locator('#rows button').click();await page.locator('dialog[open]').waitFor();
  assert((await page.locator('#detail-body').textContent()).includes('20,992.5円'));
  assert.deepEqual(errors,[]);
  fs.writeFileSync('artifacts/fines-special-20260924/local-api-ui-proof.json',JSON.stringify({base,products:331,retailPreserved:true,stockMatches:true,allEvidenceMatches:true,extraPrecedence:true,csvDecimalPreserved:true,ordinarySpecialPrice:null,errors},null,2));
  console.log('331 special prices, retail, stock, evidence, precedence, UI and CSV verified');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
