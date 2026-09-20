const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const base = 'http://127.0.0.1:55440';
const names = ['フィネス.カタログ.202609', 'ローヤルオブジャパン.ワインリスト.202610'];
const cases = names.map(name => ({name, bundle: JSON.parse(fs.readFileSync(path.join(root, 'catalogs', name + '.catalog.json'), 'utf8'))}));
const tests = [], errors = [];
function check(name, pass) {assert(pass, name); tests.push(name);}
(async()=>{
  const browser = await chromium.launch({channel: 'msedge', headless: true});
  try {
    const page = await browser.newPage({viewport: {width: 1500, height: 1100}});
    page.on('pageerror', error=>errors.push(error.message));
    async function get(url) { const res = await page.request.get(base+url); assert.equal(res.status(),200); return res.json(); }
    await page.goto(base);
    await page.waitForFunction(()=>document.querySelector('#total').textContent.includes('登録商品'));
    for(const {name,bundle} of cases) {
      const keyword = bundle.SupplierName + ' ' + (bundle.SupplierCode==='FINESSE'?'FINESSE-':'ROYAL-');
      const search = async (filter={}) => (await get('/api/search?'+new URLSearchParams({text:keyword,...filter}))).rows;
      const rows = await search();
      check(bundle.SupplierName+' all records searchable', rows.length===bundle.Rows.length);
      const bySku = new Map(rows.map(row=>[row.sku,row]));
      for(const expected of bundle.Rows) {
        const row=bySku.get(expected.SupplierProductCode);
        assert(row);
        assert.equal(row.productNameJa,expected.ProductNameJa);
        assert.equal(row.productNameEn,expected.ProductNameEn);
        assert.equal(row.producerNameJa,expected.ProducerNameJa);
        assert.equal(row.volumeMl,expected.VolumeMl);
        assert.equal(row.vintageRaw,expected.VintageRaw);
        assert.equal(row.referenceRetailPrice,expected.ReferenceRetailPrice);
        assert.equal(row.quantity,expected.Inventory.Quantity);
        assert.equal(row.inventory,expected.Inventory.Status);
        assert.equal(row.taxIncluded,false);
        assert.equal(row.priceNote,expected.PriceNote);
      }
      check(bundle.SupplierName+' all values reconciled',true);
      const available=await search({available:'true'});
      check(bundle.SupplierName+' available filter',available.length===bundle.Rows.filter(r=>[1,2].includes(r.Inventory.Status)).length);
      const filtered=await search({volumeMl:'750',priceMax:'5000'});
      assert.deepEqual(filtered.map(r=>r.sku).sort(),bundle.Rows.filter(r=>r.VolumeMl===750&&r.ReferenceRetailPrice!==null&&r.ReferenceRetailPrice<=5000).map(r=>r.SupplierProductCode).sort());
      check(bundle.SupplierName+' price and volume filters',true);
      await page.locator('#text').fill(keyword);
      await page.locator('button[type=submit]').click();
      await page.waitForFunction(n=>document.querySelector('#count').textContent===n+'件',bundle.Rows.length);
      check(bundle.SupplierName+' UI shows result count',await page.locator('#rows tr').count()===50);
      await page.screenshot({path:path.join(root,'artifacts',name+'-search.png'),fullPage:true});
      const sample = bundle.SupplierCode==='FINESSE' ? bundle.Rows.find(r=>r.VolumeMl===0) : bundle.Rows.find(r=>r.Inventory.Status===3);
      await page.locator('#text').fill(sample.SupplierProductCode);
      await page.locator('button[type=submit]').click();
      await page.waitForFunction(()=>document.querySelector('#count').textContent==='1件');
      if(bundle.SupplierCode==='FINESSE') {
        check('missing capacity is not presented as 750ml', (await page.locator('#rows').textContent()).includes('規格を確認'));
        check('missing price is not presented as zero yen',(await page.locator('#rows').textContent()).includes('金額記載なし'));
      } else {
        check('Royal future price period displayed',(await page.locator('#rows').textContent()).includes('2026年10月以降'));
      }
      await page.locator('#rows button').click();
      await page.locator('dialog[open]').waitFor();
      check(bundle.SupplierName+' PDF evidence and source date', (await page.locator('#detail-body').textContent()).includes(bundle.SourceFileName)&&(await page.locator('#detail-body').textContent()).includes(bundle.ObservedAt));
      await page.locator('summary').click();
      check(bundle.SupplierName+' original raw evidence', (await page.locator('details').textContent()).includes(bundle.SupplierCode==='FINESSE'?'容量(ml)':'卸価格'));
      await page.screenshot({path:path.join(root,'artifacts',name+'-evidence.png'),fullPage:true});
      await page.locator('#close').click();
    }
    check('existing WE still searchable',(await get('/api/search?text='+encodeURIComponent('ワインエクスペリエンス'))).rows.length===220);
    check('no JS errors',errors.length===0);
    fs.writeFileSync(path.join(root,'artifacts/other-pdf-ui-verification.json'),JSON.stringify({passed:tests.length,tests,errors},null,2));
    console.log(JSON.stringify({passed:tests.length,errors}));
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1});
