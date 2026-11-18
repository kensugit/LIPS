// Read-only acceptance against the local catalog API and real browser download.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base=process.env.CATALOG_BASE_URL||'http://127.0.0.1:55440';
const csv=require('../tools/catalog-csv.js');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1500,height:1000}});
  const response=await page.request.get(base+'/api/search?text=ブルゴーニュ');assert.equal(response.status(),200);const result=await response.json();assert(result.rows.length>0);assert(result.rows.every(r=>typeof r.supplierName==='string'&&r.supplierName.length>0));
  await page.goto(base);await page.locator('#busy').waitFor({state:'hidden'});await page.locator('#text').fill('ブルゴーニュ');await page.locator('button[type=submit]').click();await page.locator('#busy').waitFor({state:'hidden'});
  assert.equal(await page.locator('#rows td').first().textContent(),result.rows[0].supplierName);
  const pending=page.waitForEvent('download');await page.locator('#download-csv').click();const download=await pending;const bytes=fs.readFileSync(await download.path());assert.equal(bytes.toString('utf8'),csv.serialize(result.rows));
  const out=process.env.CATALOG_PROOF_DIR||path.resolve(__dirname,'../artifacts/search-csv');fs.mkdirSync(out,{recursive:true});await download.saveAs(path.join(out,'live-download.csv'));await page.screenshot({path:path.join(out,'live-search.png')});
  const proof={passed:true,verifiedUrl:base,rows:result.rows.length,suppliers:[...new Set(result.rows.map(r=>r.supplierName))],csvBytes:bytes.length};fs.writeFileSync(path.join(out,'live-proof.json'),JSON.stringify(proof,null,2));console.log(JSON.stringify(proof));
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
