const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base=process.env.FOLDER_TEST_URL||'http://127.0.0.1:55994';
if (!/^http:\/\/127\.0\.0\.1:55994$/.test(base)) throw new Error('Dedicated local test server only');
const output=path.resolve(process.env.FOLDER_TEST_OUTPUT || path.join(__dirname,'../../../results'));fs.mkdirSync(output,{recursive:true});
const root=fs.mkdtempSync(path.join(output,'folder-fixtures-'));
const csv='SKU,ProductNameJa,Vintage,VolumeMl,Price,Tax,Inventory\nBROWSER-FOLDER,試験用,NV,750,3300,,\n';
function file(name,body){const p=path.join(root,name);fs.mkdirSync(path.dirname(p),{recursive:true});fs.writeFileSync(p,body);}
file('LUC/01-2026-10-11.csv',csv);file('LUC/02-2026-10-11.csv',csv);
file('LUC/03-2026-10-11.pdf','pdf');file('LUC/04.csv',csv);
file('unknown/05-2026-10-11.csv',csv);file('LUC/06-2026-10-11.csv','bad');file('LUC/07-2026-10-11.csv','');
const checks=[];function check(name,condition){assert(condition,name);checks.push(name);}
async function post(relativePath,body=csv,headers={}){
 const f=new FormData();f.append('file',new Blob([body]),'upload.csv');f.append('relativePath',relativePath);
 return fetch(base+'/api/import/folder',{method:'POST',body:f,headers:{Origin:base,'X-Catalog-Action':'local-demo',...headers}});
}
(async()=>{
 check('cross origin rejected',(await post('LUC/2026-10-11.csv',csv,{Origin:'http://evil.invalid'})).status===403);
 check('missing action rejected',(await post('LUC/2026-10-11.csv',csv,{'X-Catalog-Action':''})).status===403);
 check('path traversal rejected',(await (await post('../LUC/2026-10-11.csv')).json()).status==='Failed');
 const multi=new FormData();multi.append('file',new Blob([csv]),'1.csv');multi.append('file',new Blob([csv]),'2.csv');multi.append('relativePath','LUC/2026-10-11.csv');
 check('multiple files rejected before saving',(await fetch(base+'/api/import/folder',{method:'POST',headers:{Origin:base,'X-Catalog-Action':'local-demo'},body:multi})).status===400);
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1500,height:1000}}), errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(base+'/workspace.html');await page.locator('#busy').waitFor({state:'hidden'});
  await page.locator('#folder-files').setInputFiles(root);
  check('all folder files shown',await page.locator('#folder-rows tr').count()===7);
  let calls=0;page.on('request',r=>{if(r.url().endsWith('/api/import/folder'))calls++;});
  await page.locator('#folder-start').click();await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  const statuses=await page.locator('#folder-rows tr td:nth-child(4)').allTextContents();
  check('success and duplicate shown',statuses.filter(x=>x==='成功').length===2);
  check('held shown',statuses.filter(x=>x==='保留').length===3);
  check('unsupported shown',statuses.includes('未対応'));check('failed shown',statuses.includes('失敗'));
  check('empty file not sent',calls===6);
  check('duplicate explains no new registration',(await page.locator('#folder-rows').innerText()).includes('重複登録しませんでした'));
  const dl=page.waitForEvent('download');await page.locator('#folder-download').click();const download=await dl;await download.saveAs(path.join(output,'browser-receipts.json'));
  const report=JSON.parse(fs.readFileSync(path.join(output,'browser-receipts.json')));check('receipt downloadable',report.length===7&&report.filter(x=>x.sourceDocumentId).length===2);
  check('same content same source',report.filter(x=>x.sourceDocumentId)[0].sourceDocumentId===report.filter(x=>x.sourceDocumentId)[1].sourceDocumentId);
  await page.screenshot({path:path.join(output,'folder-results.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});check('mobile picker visible',await page.locator('#folder-files').isVisible());
  // Explicit restart of the selection is safe: persisted content returns the same receipt.
  await page.locator('#folder-files').setInputFiles(root);await page.locator('#folder-start').click();await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  check('reselection does not add duplicates',(await page.locator('#folder-rows').innerText()).includes('取込済み'));
  // HTTP200 can still report an unknown database commit result.
  let heldCalls=0;
  await page.route('**/api/import/folder',route=>{
    heldCalls++;
    const relativePath=route.request().postData().match(/name="relativePath"\r?\n\r?\n([^\r\n]+)/)[1];
    return route.fulfill({json:{path:relativePath,status:'Held',message:'保存結果不明の試験',stopQueue:true}});
  });
  await page.locator('#folder-files').setInputFiles(root);await page.locator('#folder-start').click();
  await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  check('HTTP200 uncertain save stops queue',heldCalls===1);
  check('uncertain file held and pending retained',(await page.locator('#folder-rows tr td:nth-child(4)').allTextContents()).filter(x=>x==='未処理').length===5);
  await page.waitForTimeout(300);check('uncertain save never automatically retried',heldCalls===1);
  const uncertainDl=page.waitForEvent('download');await page.locator('#folder-download').click();
  const uncertainFile=await uncertainDl;const uncertainReport=JSON.parse(fs.readFileSync(await uncertainFile.path(),'utf8'));
  check('uncertainty stop flag retained in receipt',uncertainReport[0].stopQueue===true);
  check('uncertain save locks restart and folder selection',await page.locator('#folder-start').isDisabled() && await page.locator('#folder-files').isDisabled());
  await page.unroute('**/api/import/folder');
  // New isolated scenario after the synthetic unknown result was reconciled.
  await page.reload();await page.locator('#busy').waitFor({state:'hidden'});
  let normalHeldCalls=0;
  await page.route('**/api/import/folder',route=>{
    normalHeldCalls++;
    const relativePath=route.request().postData().match(/name="relativePath"\r?\n\r?\n([^\r\n]+)/)[1];
    return route.fulfill({json:{path:relativePath,status:'Held',message:'入力不備の試験',stopQueue:false}});
  });
  await page.locator('#folder-files').setInputFiles(root);await page.locator('#folder-start').click();
  await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  check('ordinary input holds continue remaining files',normalHeldCalls===6);
  await page.unroute('**/api/import/folder');
  // Transport interruption stops the queue and leaves the result uncertain, without replay.
  await page.route('**/api/import/folder',route=>route.abort());await page.locator('#folder-files').setInputFiles(root);
  const before=calls;await page.locator('#folder-start').click();await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  check('network interruption holds result',(await page.locator('#folder-rows').innerText()).includes('通信が中断し結果が不明'));
  check('network interruption stops queue',calls===before+1);check('pending files retained',(await page.locator('#folder-rows').innerText()).includes('未処理'));
  await page.unroute('**/api/import/folder');
  await page.reload();await page.locator('#busy').waitFor({state:'hidden'});
  let release;let arrived;const seen=new Promise(r=>arrived=r);const gate=new Promise(r=>release=r);
  await page.route('**/api/import/folder',async route=>{arrived();await gate;await route.continue();});
  await page.locator('#folder-files').setInputFiles(root);await page.locator('#folder-start').click();await seen;
  await page.locator('#folder-stop').click();release();await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
  check('stop waits for current receipt',(await page.locator('#folder-rows tr td:nth-child(4)').first().textContent())==='成功');
  check('stop leaves unprocessed files',(await page.locator('#folder-rows').innerText()).includes('未処理'));
  check('no javascript errors',errors.length===0);
 }finally{await browser.close();}
 fs.writeFileSync(path.join(output,'browser-proof.json'),JSON.stringify({base,checks,passed:checks.length},null,2));
 console.log(JSON.stringify({passed:checks.length,checks}));
})().catch(e=>{console.error(e);process.exitCode=1;});
