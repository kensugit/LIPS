"""Read-only candidate verification on Dell. No imported data or DB mutations."""
import json
import os
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import urlopen

def get(port, path, params=None):
    if port == 55443:
        port = int(os.environ.get('CATALOG_VERIFY_CANDIDATE_PORT', '55443'))
    url = f'http://127.0.0.1:{port}{path}'
    if params:
        url += '?' + urlencode(params)
    with urlopen(url, timeout=30) as response:
        return json.load(response)

def sparkling(row):
    return row['region'] in ['シャンパーニュ', 'シャンパーニュ地方', 'Champagne'] and ('泡' in row['productType'] or row['productType'] == 'スパークリング')

def keys(rows):
    return sorted((r['sourceDocumentId'], r['sku']) for r in rows)

before = get(55440, '/api/options')
after = get(55443, '/api/options')
assert before == after, 'Catalog options/count changed'
manual = get(55440, '/api/search', {'volumeMl': 1500, 'priceMax': 30000})
assert not manual['capped'], 'Expected result was capped'
expected = [r for r in manual['rows'] if sparkling(r)]
assert expected, 'No source-matched products; cannot accept empty-result smoke test'
query = '容量1500mlの３万円以下のシャンパーニュ'
result = get(55443, '/api/search', {'text': query})
assert keys(result['rows']) == keys(expected), 'Candidate differs from existing numeric search'
assert result['conditions'], 'Missing interpreted conditions'
assert all(r['volumeMl'] == 1500 and r['referenceRetailPrice'] is not None and r['referenceRetailPrice'] <= 30000 and not r['taxIncluded'] and sparkling(r) for r in result['rows'])
alias = get(55443, '/api/search', {'text': '1.5Lの30,000円以下のシャンパン'})
assert keys(alias['rows']) == keys(expected)
stock = get(55443, '/api/search', {'text': '在庫ありの' + query})
assert keys(stock['rows']) == keys([r for r in expected if r['inventory'] in [1, 2]])
white_manual = get(55440, '/api/search', {'text': 'ブルゴーニュ', 'type': '白', 'priceMax': 15000})
assert white_manual['rows'] and not white_manual['capped']
for phrase in ['ブルゴーニュの白で15000円以内', 'ブルゴーニュの白ワインで15000円以内', '15000円以内のブルゴーニュの白']:
    white = get(55443, '/api/search', {'text': phrase})
    assert keys(white['rows']) == keys(white_manual['rows']), 'Burgundy white phrase differs from manual filters'
    assert 'タイプ 白' in white['conditions']
italian_manual = get(55440, '/api/search', {'country': 'イタリア', 'type': '赤', 'priceMin': 2500, 'priceMax': 4000})
assert italian_manual['rows'] and not italian_manual['capped']
for phrase in ['イタリアの赤ワインで2500円から4000円', 'イタリアの赤で2500〜4000円']:
    italian = get(55443, '/api/search', {'text': phrase})
    assert keys(italian['rows']) == keys(italian_manual['rows']), 'Italian red range differs from manual filters'
    assert all(r['country'] == 'イタリア' and r['productType'] == '赤' and not r['taxIncluded'] and 2500 <= r['referenceRetailPrice'] <= 4000 for r in italian['rows'])
try:
    get(55443, '/api/search', {'text': query, 'volumeMl': 750})
    raise AssertionError('Conflicting volume was accepted')
except HTTPError as error:
    assert error.code == 400
case_file = Path(__file__).with_name('natural-search-cases.json')
if not case_file.exists():
    case_file = Path(__file__).parent.parent / 'tests/natural-search-cases.json'
pattern_checks = []
for case in json.loads(case_file.read_text(encoding='utf-8')):
    reference = get(55440, '/api/search', case['manual'])
    assert not reference['capped'], case['name']
    def accepts(row):
        bubbles = '泡' in row['productType'] or row['productType'] == 'スパークリング'
        return (not case.get('champagne') or sparkling(row)) and (not case.get('sparkling') or bubbles) and (not case.get('color') or case['color'] in row['productType']) and ('below' not in case or row['referenceRetailPrice'] < case['below']) and ('above' not in case or row['referenceRetailPrice'] > case['above'])
    wanted = [row for row in reference['rows'] if accepts(row)]
    assert wanted, 'Empty reference: ' + case['name']
    if 'below' in case or 'above' in case:
        assert len(wanted) < len(reference['rows']), 'Price boundary not exercised: ' + case['name']
    for phrase in case['phrases']:
        actual = get(55443, '/api/search', {'text': phrase})
        assert keys(actual['rows']) == keys(wanted), phrase
        pattern_checks.append({'phrase': phrase, 'count': len(wanted)})
print(json.dumps({'verified': True, 'catalogTotal': before['total'], 'matches': len(expected), 'availableMatches': len(stock['rows']), 'burgundyWhiteMatches': len(white_manual['rows']), 'italianRedMatches': len(italian_manual['rows']), 'conditions': result['conditions'], 'patternChecks': pattern_checks}, ensure_ascii=False, indent=2))
