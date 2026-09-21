const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base = process.env.CATALOG_SEARCH_URL || 'http://127.0.0.1:55442';
const output = path.resolve(__dirname, '../artifacts/natural-search');
const checks = [];
const check = (name, predicate) => { assert(predicate, name); checks.push(name); };
const sku = rows => rows.map(r => r.sku).sort();
(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  try {
    const page = await browser.newPage({ viewport: { width: 1480, height: 1050 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const query = async params => {
      const res = await page.request.get(base + '/api/search?' + new URLSearchParams(params));
      assert.equal(res.status(), 200);
      return res.json();
    };
    const requested = '容量1500mlの３万円以下のシャンパーニュ';
    const result = await query({ text: requested });
    check('requested phrase finds real products', result.rows.length > 0);
    check('all results satisfy capacity, tax, price, region and sparkling type', result.rows.every(r => r.volumeMl === 1500 && r.taxIncluded === false && r.referenceRetailPrice != null && r.referenceRetailPrice <= 30000 && ['シャンパーニュ', 'シャンパーニュ地方', 'Champagne'].includes(r.region) && (r.productType.includes('泡') || r.productType === 'スパークリング')));
    check('inclusive price boundary retains 30000 yen', result.rows.some(r => r.referenceRetailPrice === 30000));
    const manual = await query({ volumeMl: 1500, priceMax: 30000 });
    const expected = manual.rows.filter(r => ['シャンパーニュ', 'シャンパーニュ地方', 'Champagne'].includes(r.region) && (r.productType.includes('泡') || r.productType === 'スパークリング'));
    assert.deepEqual(sku(result.rows), sku(expected)); checks.push('same rows as explicit numeric filters and independent region/type check');
    for (const text of ['1.5Lの30,000円以下のシャンパン', '１５００ｍｌの３万円以下のＣｈａｍｐａｇｎｅを探してください', '150cl 3万円以下 シャンパーニュ']) {
      assert.deepEqual(sku((await query({ text })).rows), sku(result.rows)); checks.push('equivalent phrase: ' + text);
    }
    const stock = await query({ text: '在庫ありの' + requested });
    assert.deepEqual(sku(stock.rows), sku(result.rows.filter(r => [1, 2].includes(r.inventory)))); checks.push('availability excludes unknown and out of stock');
    const narrow = await query({ text: requested, priceMax: 25000 });
    assert.deepEqual(sku(narrow.rows), sku(result.rows.filter(r => r.referenceRetailPrice <= 25000))); checks.push('manual price intersects natural price');
    for (const params of [{ text: requested, volumeMl: 750 }, { text: requested, priceMin: 40000 }, { text: '税込3万円以下のシャンパーニュ' }, { text: 'シャンパーニュ以外' }]) {
      const res = await page.request.get(base + '/api/search?' + new URLSearchParams(params));
      assert.equal(res.status(), 400); assert((await res.json()).error); checks.push('invalid/conflicting condition rejected: ' + JSON.stringify(params));
    }
    const keyword = await query({ text: result.rows[0].sku });
    check('existing SKU search works', keyword.rows.some(r => r.sku === result.rows[0].sku));
    const burgundy = await query({ text: 'ブルゴーニュ', type: '白', priceMax: 15000 });
    check('manual Burgundy white query has products and is not capped', burgundy.rows.length > 0 && !burgundy.capped);
    for (const text of ['ブルゴーニュの白で15000円以内', 'ブルゴーニュの白ワインで15000円以内', '15000円以内のブルゴーニュの白']) {
      const parsed = await query({ text });
      assert.deepEqual(sku(parsed.rows), sku(burgundy.rows));
      check('short white phrase matches manual filters: ' + text, parsed.conditions.includes('タイプ 白') && parsed.conditions.includes('キーワード ブルゴーニュ'));
    }
    const italian = await query({ country: 'イタリア', type: '赤', priceMin: 2500, priceMax: 4000 });
    check('manual Italian red price range has products and is not capped', italian.rows.length > 0 && !italian.capped);
    for (const text of ['イタリアの赤ワインで2500円から4000円', 'イタリアの赤で２５００円から４０００円まで', 'イタリアの赤で2500〜4000円']) {
      const parsed = await query({ text });
      assert.deepEqual(sku(parsed.rows), sku(italian.rows));
      check('price range matches manual filters: ' + text, parsed.conditions.includes('国 イタリア') && parsed.conditions.includes('タイプ 赤') && parsed.conditions.includes('税抜希望小売 2,500円以上') && parsed.conditions.includes('税抜希望小売 4,000円以下'));
    }
    check('both inclusive price boundaries are retained', italian.rows.some(r => r.referenceRetailPrice === 2500) && italian.rows.some(r => r.referenceRetailPrice === 4000));
    await page.goto(base);
    await page.waitForFunction(() => document.querySelector('#count').textContent.includes('件'));
    await page.locator('#text').fill('イタリアの赤ワインで2500円から4000円');
    await Promise.all([page.waitForResponse(r => r.url().includes('/api/search?') && r.status() === 200), page.locator('button[type=submit]').click()]);
    await page.waitForFunction(() => document.querySelector('#interpreted').textContent.includes('国 イタリア'));
    check('browser displays Italian red price range', (await page.locator('#count').innerText()).startsWith(italian.rows.length.toLocaleString()) && (await page.locator('#rows tr').count()) > 0);
    await page.screenshot({ path: path.join(output, 'italian-range.png'), fullPage: true });
    await page.locator('#text').fill('ブルゴーニュの白で15000円以内');
    await Promise.all([page.waitForResponse(r => r.url().includes('/api/search?') && r.status() === 200), page.locator('button[type=submit]').click()]);
    await page.waitForFunction(() => document.querySelector('#interpreted').textContent.includes('タイプ 白'));
    check('browser interprets reported phrase and displays matching rows', (await page.locator('#count').innerText()).startsWith(burgundy.rows.length.toLocaleString()) && (await page.locator('#rows tr').count()) > 0);
    await page.screenshot({ path: path.join(output, 'burgundy-white.png'), fullPage: true });
    await page.locator('#text').fill(requested);
    await Promise.all([page.waitForResponse(r => r.url().includes('/api/search?') && r.status() === 200), page.locator('button[type=submit]').click()]);
    await page.waitForFunction(n => document.querySelectorAll('#rows tr').length === n, result.rows.length);
    check('browser shows interpreted conditions', (await page.locator('#interpreted').innerText()).includes('容量 1500ml') && (await page.locator('#interpreted').innerText()).includes('30,000円以下'));
    await page.screenshot({ path: path.join(output, 'search.png'), fullPage: true });
    await page.locator('#volumeMl').selectOption('750');
    await page.locator('button[type=submit]').click();
    await page.locator('#error').waitFor({ state: 'visible' });
    check('browser displays capacity conflict', (await page.locator('#error').innerText()).includes('容量'));
    check('error clears interpreted conditions', (await page.locator('#interpreted').innerText()) === '');
    await page.locator('#reset').click();
    await page.waitForFunction(() => document.querySelector('#count').textContent.includes('件') && document.querySelector('#text').value === '');
    check('clear resets natural and detailed filters', (await page.locator('#volumeMl').inputValue()) === '');
    check('no browser JavaScript errors', errors.length === 0);
    fs.writeFileSync(path.join(output, 'verification.json'), JSON.stringify({ base, count: result.rows.length, burgundyWhiteCount: burgundy.rows.length, italianRedCount: italian.rows.length, checks, conditions: result.conditions }, null, 2));
    console.log(JSON.stringify({ passed: checks.length, count: result.rows.length, base }));
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
