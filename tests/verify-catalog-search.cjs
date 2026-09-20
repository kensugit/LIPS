const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base = 'http://127.0.0.1:55440';
const root = path.resolve(__dirname, '..');
const source = JSON.parse(fs.readFileSync(path.join(root, 'catalogs/ワインエクスペリエンス.在庫表.20260901.products.json'), 'utf8'));
const tests = [], errors = [];
const check = (name, condition) => { assert(condition, name); tests.push(name); };

(async () => {
  const browser = await chromium.launch({headless: true, channel: 'msedge'});
  try {
    const page = await browser.newPage({viewport: {width: 1480, height: 1050}});
    page.on('pageerror', error => errors.push(error.message));
    const get = async url => { const response = await page.request.get(base + url); assert.equal(response.status(), 200); return response.json(); };
    const query = async filters => (await get('/api/search?' + new URLSearchParams({text: 'ワインエクスペリエンス', ...filters}))).rows;
    const rows = await query({});
    check('all 220 supplier products searchable', rows.length === 220);
    const bySku = new Map(rows.map(row => [row.sku, row]));
    for (const original of source) {
      const stored = bySku.get(original['WEコード']);
      assert(stored, original['WEコード']);
      assert.equal(stored.volumeMl, original['容量(ml)']);
      assert.equal(stored.referenceRetailPrice, original['小売価格']);
      assert.equal(stored.taxIncluded, false);
      assert.equal(stored.vintageRaw, original.VIN);
      assert.equal(stored.quantity, original['在庫数量'] === '' ? null : original['在庫数量']);
      assert.equal(stored.sourceSheet, 'PDF p.' + original['PDFページ']);
    }
    check('all 220 source capacities prices vintages quantities and PDF pages match', true);
    check('49 stock symbols retain null exact quantity', rows.filter(row => row.inventory === 1 && row.quantity === null).length === 49);
    check('35 sold out records searchable as out of stock', rows.filter(row => row.inventory === 3 && row.quantity === 0).length === 35);
    const available = await query({available: 'true'});
    check('available filter returns 185', available.length === 185 && available.every(row => row.inventory === 1));
    const filtered = await query({volumeMl: '750', priceMin: '3000', priceMax: '6000', available: 'true'});
    const expected = source.filter(row => row['容量(ml)'] === 750 && row['小売価格'] >= 3000 && row['小売価格'] <= 6000 && row['在庫(本)'] !== '完売').map(row => row['WEコード']).sort();
    assert.deepEqual(filtered.map(row => row.sku).sort(), expected);
    check('combined stock volume and price filters match source', true);
    for (const sku of ['WECA0608M', 'WEMA0004', 'WEPB0008B', 'WETH0004B']) {
      const row = bySku.get(sku);
      const result = await get(`/api/evidence/${row.sourceDocumentId}/${sku}`);
      const evidence = JSON.parse(result.extractedRowJson);
      const raw = JSON.parse(result.rawCellsJson);
      assert.equal(result.fileName, '【在庫表】ワインエクスペリエンス_260831.pdf');
      assert.equal(result.observedAt.slice(0, 10), '2026-09-01');
      assert.equal(raw['WEコード'], sku);
      assert.equal(evidence.CaseSize, raw['入数']);
      assert.equal(evidence.AlcoholPercent, Number(raw['Alc.%'].replace('%', '')));
    }
    check('PDF evidence preserves original fields across first middle and final pages', true);
    check('capacity conflict visible in query results', bySku.get('WECA0608M').priceNote.includes('不一致'));
    const options = await get('/api/options');
    check('prior 3963 products retained', options.total >= 4183);
    await page.goto(base);
    await page.locator('#busy').waitFor({state: 'hidden'});
    await page.locator('#text').fill('ワインエクスペリエンス');
    await page.locator('button[type=submit]').click();
    await page.waitForFunction(() => document.querySelector('#count').textContent === '220件');
    check('UI supplier keyword shows 220 results', await page.locator('#rows tr').count() === 50);
    await page.screenshot({path: path.join(root, 'artifacts/wine-experience-search.png'), fullPage: true});
    await page.locator('#next').click();
    check('UI pagination works', (await page.locator('#page').textContent()).includes('51–100 / 220件'));
    await page.locator('#available').check();
    await page.locator('button[type=submit]').click();
    await page.waitForFunction(() => document.querySelector('#count').textContent === '185件');
    check('UI available filter works', true);
    await page.locator('#available').uncheck();
    await page.locator('#text').fill('WECA0608M');
    await page.locator('button[type=submit]').click();
    await page.waitForFunction(() => document.querySelector('#count').textContent === '1件');
    check('UI SKU search and capacity warning work', (await page.locator('#rows').textContent()).includes('不一致'));
    await page.locator('#rows button').click();
    await page.locator('dialog[open]').waitFor();
    check('UI evidence contains PDF filename and observation date', (await page.locator('#detail-body').textContent()).includes('2026-09-01') && (await page.locator('#detail-body').textContent()).includes('ワインエクスペリエンス_260831.pdf'));
    await page.locator('summary').click();
    check('UI source raw values available', (await page.locator('details').textContent()).includes('税込小売'));
    await page.screenshot({path: path.join(root, 'artifacts/wine-experience-evidence.png'), fullPage: true});
    check('no browser JavaScript errors', errors.length === 0);
    fs.writeFileSync(path.join(root, 'artifacts/wine-experience-ui-verification.json'), JSON.stringify({passed: tests.length, tests, errors}, null, 2));
    console.log(JSON.stringify({passed: tests.length, errors}));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
