const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const http=require('node:http');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root=path.resolve(__dirname,'..');
const web=process.env.CATALOG_WEB_ROOT||path.join(root,'artifacts/search-csv-refactor/source/catalog-search-poc/src/CatalogSearch.Web/wwwroot');
const csv=require(path.join(web,'catalog-csv.js'));
const rows=Array.from({length:75},(_,i)=>({supplierName:i%2?'取引先B':'取引先A',sku:'SKU'+i,productNameJa:i===0?'ワイン,"特別"\n新着':'商品'+i,productNameEn:'Wine',producerNameJa:'生産者',country:'フランス',region:'ブルゴーニュ',productType:'赤',vintageRaw:'NV',volumeMl:750,referenceRetailPrice:i?3000:null,taxIncluded:false,inventory:0,quantity:i?0:null,sourceSheet:'PDF p.1',sourceRow:i+1,sourceDocumentId:'source-'+i}));
// Parse actual CSV, including embedded newlines and escaped quotes.
function parse(text){let out=[],row=[],value='',quoted=false;for(let i=0;i<text.length;i++){const c=text[i];if(c==='"'){if(quoted&&text[i+1]==='"'){value+='"';i++}else quoted=!quoted}else if(!quoted&&(c===','||c==='\r')){row.push(value);value='';if(c==='\r'){assert.equal(text[++i],'\n');out.push(row);row=[]}}else value+=c}return out}
(async()=>{
 const encoded=Buffer.from(csv.serialize(rows));assert.equal(encoded.subarray(0,3).toString('hex'),'efbbbf');
 const table=parse(encoded.toString('utf8').slice(1));assert.equal(table.length,76);assert(table.every(r=>r.length===19));assert.equal(table[1][2],rows[0].productNameJa);assert.equal(table[1][11],'');assert.equal(table[1][14],'');assert.equal(table[2][14],'0');
 for(const text of ['=1+1','+cmd','-cmd','@SUM(A1)',' \t=1','\nformula'])assert.equal(parse(csv.serialize([{...rows[0],productNameJa:text}]).slice(1))[1][2],"'"+text);
 const server=http.createServer((req,res)=>{const file=path.join(web,req.url==='/'?'index.html':req.url.slice(1));if(!file.startsWith(web+path.sep)){res.writeHead(404).end();return}try{res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(file))}catch{res.writeHead(404).end()}});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1500,height:1000}});let mode='normal';const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/api/options',route=>route.fulfill({json:{total:75,countries:[],regions:[],types:[],volumes:[],vintages:[]}}));
  await page.route('**/api/search?*',async route=>{if(mode==='failure')return route.fulfill({status:503,json:{error:'試験エラー'}});await new Promise(r=>setTimeout(r,80));await route.fulfill({json:{rows:mode==='empty'?[]:rows,capped:mode==='capped',conditions:[]}})});
  await page.goto('http://127.0.0.1:'+server.address().port);const download=page.locator('#download-csv');await download.waitFor();await page.waitForFunction(()=>!document.getElementById('download-csv').disabled);
  assert.equal(await page.locator('#rows tr').count(),50);assert.equal(await page.locator('#rows td').first().textContent(),'取引先A');
  await page.locator('[data-sort=supplier]').click();await page.locator('#next').click();assert.equal(await page.locator('#rows tr').count(),25);
  const event=page.waitForEvent('download');await download.click();const file=await event;await file.saveAs(path.join(root,'artifacts/search-csv/download.csv'));const actual=parse(fs.readFileSync(await file.path(),'utf8').slice(1));assert.equal(actual.length,76);assert.equal(actual[1][0],'取引先A');assert.equal(actual[75][0],'取引先B');
  await page.screenshot({path:path.join(root,'artifacts/search-csv/search.png'),fullPage:true});
  const previousStatus=await page.locator('#csv-status').textContent();
  await page.locator('#priceMin').fill('9000');await page.locator('#priceMax').fill('1000');
  await page.locator('button[type=submit]').click();
  assert((await page.locator('#error').textContent()).includes('下限は上限以下'));
  assert.equal(await download.isDisabled(),false);assert.equal(await page.locator('#csv-status').textContent(),previousStatus);
  assert.equal(await page.locator('#rows tr').count(),25);
  await page.locator('#priceMin').fill('');await page.locator('#priceMax').fill('');
  await page.evaluate(()=>{window.originalCreateObjectURL=URL.createObjectURL;URL.createObjectURL=()=>{throw new Error('fixture failure')}});
  await download.click();assert((await page.locator('#error').textContent()).includes('CSVのダウンロードに失敗'));
  assert.equal(await page.locator('a[download]').count(),0);assert.equal(await download.isDisabled(),false);
  await page.evaluate(()=>{URL.createObjectURL=window.originalCreateObjectURL;delete window.originalCreateObjectURL});
  const retry=page.waitForEvent('download');await download.click();assert.equal(fs.readFileSync(await (await retry).path(),'utf8').slice(1).split('\r\n').length>75,true);
  for(const current of ['empty','failure','capped']){mode=current;await page.locator('button[type=submit]').click();await page.waitForFunction(()=>document.getElementById('busy').hidden);if(current==='capped'){assert((await page.locator('#csv-status').textContent()).includes('先頭2,000件'));assert.equal(await download.isDisabled(),false)}else assert.equal(await download.isDisabled(),true)}
  assert.deepEqual(errors,[]);console.log('PASS: UTF-8 BOM, 19 columns, CSV round-trip, null/zero, formula escaping, supplier display/sort, 75-row download from page 2, invalid-price state, download failure/retry, empty/error disabled, capped notice, browser errors.');
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve))}
})().catch(error=>{console.error(error);process.exitCode=1});
