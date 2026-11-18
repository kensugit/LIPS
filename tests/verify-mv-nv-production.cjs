const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require('C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const base='http://192.168.1.5:55441',out='artifacts/mv-nv-20260929/';
 const before=JSON.parse(fs.readFileSync(out+'baseline-search.json','utf8').replace(/^\uFEFF/,''));
 const get=async(path,p)=>{const r=await page.request.get(base+path+'?'+new URLSearchParams(p));assert.equal(r.status(),200);return r.json()};
 const options=await get('/api/options',{vintageNotation:'canonical'});assert.equal(options.total,7732);assert(options.vintages.includes('NV'));assert(!options.vintages.includes('MV'));
 const expected=[...new Set([...before.mvIds,...before.nvIds])].sort();
 for(const vintage of ['NV','MV','N.V.','M.V.']){const data=await get('/api/search',{vintage});assert(!data.capped);assert.deepEqual(data.rows.map(r=>r.supplierProductId).sort(),expected);assert(data.rows.every(r=>r.vintageLabel==='NV'))}
 const all=await get('/api/search',{vintage:'NV'}),mv=all.rows.find(r=>before.mvIds.includes(r.supplierProductId));assert.equal(mv.vintageRaw,'MV');
 await page.goto(base);await page.waitForFunction(()=>document.querySelectorAll('#rows tr').length>0&&!document.querySelector('#search').inert);
 assert.equal(await page.locator('#vintage option[value=MV]').count(),0);
 await page.selectOption('#vintage','NV');await page.locator('#text').fill(mv.sku);const response=page.waitForResponse(r=>r.url().includes('/api/search?'));await page.locator('button[type=submit]').click();await response;await page.waitForFunction(()=>document.querySelector('#busy').hidden&&!document.querySelector('#search').inert);
 const row=page.locator('#rows tr').filter({hasText:'SKU '+mv.sku}).filter({hasText:mv.supplierName}).first();await row.locator('.product-select').check();await page.locator('#download-csv').click();assert((await page.locator('#selected-rows').innerText()).includes('NV'));
 const event=page.waitForEvent('download');await page.locator('#download-selected-csv').click();const file=await event;const csv=fs.readFileSync(await file.path(),'utf8');assert(csv.includes('"NV"'));assert(!csv.includes('"MV"'));assert(csv.includes(mv.sku));
 await page.screenshot({path:out+'production-mv-nv.png',fullPage:true});assert.deepEqual(errors,[]);
 const proof={total:options.total,nvCount:expected.length,previousMvCount:before.mvCount,previousNvCount:before.nvCount,exactUnion:true,mvNvEquivalent:true,mvOptionRemoved:true,rawMvPreserved:true,selectedMvCsvIsNv:true,errors};fs.writeFileSync(out+'production-proof.json',JSON.stringify(proof,null,2));console.log(JSON.stringify(proof));
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
