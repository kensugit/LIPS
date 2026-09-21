const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const cases = require('./natural-search-cases.json');
const base = process.env.CATALOG_SEARCH_URL || 'http://127.0.0.1:55446';
const output = path.resolve(__dirname, '../artifacts/natural-search-patterns');
const identity = rows => rows.map(r => r.sourceDocumentId + '/' + r.sku).sort();
const bubbles = row => row.productType.includes('泡') || row.productType === 'スパークリング';
(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  const checks = [];
  try {
    const page = await browser.newPage({ viewport: { width: 1480, height: 1050 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const query = async params => {
      const response = await page.request.get(base + '/api/search?' + new URLSearchParams(params));
      assert.equal(response.status(), 200, JSON.stringify(params));
      return response.json();
    };
    await page.goto(base);
    await page.waitForFunction(() => document.querySelector('#count').textContent.includes('件'));
    for (const c of cases) {
      const reference = await query(c.manual);
      assert.equal(reference.capped, false, c.name);
      const expected = reference.rows.filter(r =>
        (!c.champagne || (['シャンパーニュ', 'シャンパーニュ地方', 'Champagne'].includes(r.region) && bubbles(r))) &&
        (!c.sparkling || bubbles(r)) && (!c.color || r.productType.includes(c.color)) &&
        (c.below === undefined || r.referenceRetailPrice < c.below) && (c.above === undefined || r.referenceRetailPrice > c.above));
      assert(expected.length > 0, 'Nonempty source-backed expectation: ' + c.name);
      if (c.below !== undefined || c.above !== undefined) assert(expected.length < reference.rows.length, 'Boundary exclusion must be exercised: ' + c.name);
      for (const text of c.phrases) {
        const actual = await query({ text });
        assert.deepEqual(identity(actual.rows), identity(expected), text);
        assert(actual.conditions.length > 0);
        checks.push({ text, count: actual.rows.length, conditions: actual.conditions });
      }
      await page.locator('#text').fill(c.phrases[0]);
      await Promise.all([page.waitForResponse(r => r.url().includes('/api/search?') && r.status() === 200), page.locator('button[type=submit]').click()]);
      await page.waitForFunction(n => document.querySelector('#count').textContent.startsWith(n.toLocaleString()), expected.length);
      assert((await page.locator('#rows tr').count()) > 0);
      checks.push({ ui: c.name, count: expected.length });
    }
    for (const text of ['イタリアの赤で4000円から2500円','3000円以上3000円未満','税込3000円以下の赤','イタリアまたはフランスの赤','イタリアの赤と白で4000円以下']) {
      const response = await page.request.get(base + '/api/search?' + new URLSearchParams({ text }));
      assert.equal(response.status(), 400, text);
      checks.push({ rejected: text });
    }
    assert.deepEqual(errors, []);
    await page.screenshot({ path: path.join(output, 'pattern-search.png'), fullPage: true });
    fs.writeFileSync(path.join(output, 'verification.json'), JSON.stringify({ base, checks }, null, 2));
    console.log(JSON.stringify({ passed: checks.length, base }));
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
