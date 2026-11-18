// Isolated real Edge DOM test. No server, source files, or database requests.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const script=fs.readFileSync(path.join(__dirname,'../src/CatalogSearch.Web/wwwroot/folder-import.js'),'utf8');
const checks=[];
function check(name,ok){assert(ok,name);checks.push(name);}
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  for(const mode of ['receipt','http500','network','invalid','ordinary','manual']){
   const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.setContent('<input id="folder-files" type="file" multiple><button id="folder-start">開始</button><button id="folder-stop">停止</button><button id="folder-download">結果</button><p id="folder-summary"></p><table><tbody id="folder-rows"></tbody></table>');
   await page.evaluate(mode=>{
    window.calls=0;window.mode=mode;
    window.fetch=async(url,options)=>{
     window.calls++;const filePath=options.body.get('relativePath');
     if(mode==='network')throw Error('synthetic network interruption');
     if(mode==='http500')return {ok:false,status:500};
     if(mode==='invalid')return {ok:true,json:async()=>({path:'wrong',status:'Success'})};
     if(mode==='manual')await new Promise(resolve=>window.release=resolve);
     return {ok:true,json:async()=>({path:filePath,status:mode==='manual'?'Success':'Held',message:'合成確認',stopQueue:mode==='receipt'})};
    };
    const files=[1,2].map(n=>{const f=new File(['synthetic'],n+'.csv');Object.defineProperty(f,'webkitRelativePath',{value:'LUC/'+n+'.csv'});return f;});
    Object.defineProperty(document.getElementById('folder-files'),'files',{configurable:true,value:files});
   },mode);
   await page.addScriptTag({content:script});
   await page.locator('#folder-files').dispatchEvent('change');await page.locator('#folder-start').click();
   if(mode==='manual'){
    await page.waitForFunction(()=>typeof window.release==='function');
    await page.locator('#folder-stop').click();await page.evaluate(()=>window.release());
   }
   await page.waitForFunction(()=>!document.getElementById('folder-download').disabled);
   const uncertain=!['ordinary','manual'].includes(mode);
   check(mode+' request count',await page.evaluate(()=>window.calls)===(mode==='ordinary'?2:1));
   if(uncertain){
    check(mode+' start locked',await page.locator('#folder-start').isDisabled());
    check(mode+' picker locked',await page.locator('#folder-files').isDisabled());
    check(mode+' reconciliation notice',(await page.locator('#folder-summary').innerText()).includes('保存結果を照合'));
    await page.evaluate(()=>{document.getElementById('folder-start').disabled=false;document.getElementById('folder-start').dispatchEvent(new Event('click'));document.getElementById('folder-files').disabled=false;document.getElementById('folder-files').dispatchEvent(new Event('change'));});
    check(mode+' handlers cannot bypass lock',await page.evaluate(()=>window.calls)===1);
    check(mode+' pending retained',await page.locator('#folder-rows tr').count()===2 && (await page.locator('#folder-rows').innerText()).includes('未処理'));
   }else{
    check(mode+' selection remains available',await page.locator('#folder-files').isEnabled());
    if(mode==='manual')check('manual stop can resume pending',await page.locator('#folder-start').isEnabled());
   }
   check(mode+' no JS errors',errors.length===0);await page.close();
  }
 }finally{await browser.close();}
 console.log(JSON.stringify({passed:checks.length,checks,serverRequests:0,databaseWrites:0},null,2));
})().catch(e=>{console.error(e);process.exitCode=1;});
