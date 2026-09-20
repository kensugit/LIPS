const {spawn} = require('node:child_process');
const path = require('node:path');
const assert = require('node:assert/strict');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const dir = path.join(root, 'artifacts/lan-package/gateway');
const base = 'http://127.0.0.1:55573';
const child = spawn(path.join(dir,'LipsLanGateway.exe'),[],{cwd:dir,env:{...process.env,PublicOrigin:base},stdio:'ignore',windowsHide:true});
(async()=>{
  let browser;
  try {
    for(let i=0;i<40;i++) {
      try {if((await fetch(base+'/api/options')).ok)break;} catch{}
      await new Promise(r=>setTimeout(r,250));
    }
    const upstream=await (await fetch('http://127.0.0.1:55440/api/options')).json();
    const proxied=await (await fetch(base+'/api/options')).json();
    assert.deepEqual(proxied,upstream);
    browser=await chromium.launch({channel:'msedge',headless:true});
    const page=await browser.newPage();
    const errors=[]; page.on('pageerror',e=>errors.push(e.message));
    const failures=[]; page.on('response',r=>{if(r.status()>=400)failures.push(r.url()+':'+r.status())});
    await page.goto(base,{waitUntil:'networkidle'});
    assert.ok((await page.locator('body').innerText()).includes('商品'));
    await page.goto(base+'/workspace.html',{waitUntil:'networkidle'});
    assert.ok((await page.locator('body').innerText()).includes('取込'));
    assert.deepEqual(errors,[]); assert.deepEqual(failures,[]);
    console.log(JSON.stringify({gatewayBrowser:'passed',products:proxied.total,pages:2,jsErrors:0,httpErrors:0}));
  } finally {if(browser)await browser.close();child.kill();}
})().catch(e=>{console.error(e);process.exitCode=1;});
