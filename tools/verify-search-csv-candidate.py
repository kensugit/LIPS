"""Read-only Dell candidate checks; run before package approval."""
import json
import os
from urllib.parse import urlencode
from urllib.request import urlopen

baseline = os.environ.get('CATALOG_BASELINE_URL', 'http://127.0.0.1:55440')
candidate = os.environ.get('CATALOG_CANDIDATE_URL', 'http://127.0.0.1:55443')

def get(base, path, params=None):
    with urlopen(base + path + ('?' + urlencode(params) if params else ''), timeout=30) as response:
        return json.load(response)

assert get(baseline, '/api/options') == get(candidate, '/api/options'), 'Catalog totals/options differ'
checks = []
for filters in [{}, {'text': 'ブルゴーニュ', 'type': '白', 'priceMax': 15000},
                {'text': '容量1500mlの３万円以下のシャンパーニュ'},
                {'country': 'イタリア', 'type': '赤', 'priceMin': 2500, 'priceMax': 4000},
                {'available': 'true', 'volumeMl': 750, 'priceMax': 6000},
                {'text': 'NO_MATCH_CSV_ACCEPTANCE_20260922'}]:
    old, new = get(baseline, '/api/search', filters), get(candidate, '/api/search', filters)
    assert old['capped'] == new['capped']
    assert old.get('conditions') == new.get('conditions')
    def keyed(rows):
        return {(r['sourceDocumentId'], r['sku']): r for r in rows}
    expected, actual = keyed(old['rows']), keyed(new['rows'])
    assert expected.keys() == actual.keys(), filters
    for key, row in actual.items():
        assert row.get('supplierName', '').strip(), 'Missing supplier name'
        for field, value in expected[key].items():
            if field not in ('supplierName', 'priceNote'):
                assert row[field] == value, (filters, key, field)
    checks.append({'filters': filters, 'rows': len(actual), 'capped': new['capped']})
assert checks[0]['rows'] > 0
for asset, marker in [('index.html', 'download-csv'), ('catalog-csv.js', 'serialize'), ('catalog-sort.js', 'supplier')]:
    with urlopen(candidate + '/' + asset, timeout=10) as response:
        assert marker in response.read().decode('utf-8')
print(json.dumps({'passed': True, 'checks': checks, 'writesPerformed': False}, ensure_ascii=False, indent=2))
