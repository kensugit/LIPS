import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch
from tools.nippon_liquor_import import extract, read_source, inventory, ORDER

ROOT = Path('catalogs/nippon-liquor')
PATTERNS = {'retail':'①*.xlsx', 'shipping':'②*.xlsx', 'bordeaux':'ボルドー*.xlsx', 'jadot':'ルイ*.xlsx'}

class NipponLiquorTests(unittest.TestCase):
    def test_inventory_legends_and_unknown(self):
        self.assertEqual({'Quantity':None, 'Status':1, 'RawValue':'〇'}, inventory('〇', 'shipping'))
        for v in ['○','◯','◎']: self.assertIsNone(inventory(v, 'jadot')['Quantity'])
        self.assertEqual(174, inventory(174, 'retail')['Quantity'])
        self.assertEqual(3, inventory(0, 'retail')['Status'])
        self.assertEqual(0, inventory(None, 'retail')['Status'])
        with self.assertRaises(ValueError): inventory('◎', 'retail')
        with self.assertRaises(ValueError): inventory('確認中', 'jadot')

    @unittest.skipUnless(ROOT.exists(), 'Source workbooks required')
    def test_all_sources_and_price_separation(self):
        paths = {k:next(ROOT.glob(PATTERNS[k])) for k in ORDER}
        bundles = extract(paths)
        self.assertEqual([148,294,891,891], [len(bundles[k]['Rows']) for k in ORDER])
        rows = {r['SupplierProductCode']:r for r in bundles['shipping']['Rows']}
        self.assertEqual(891, len(rows))
        self.assertEqual(239, sum(r['Inventory']['Quantity'] is None for r in rows.values()))
        r=rows['3524RR051310']
        self.assertEqual(42000,r['ReferenceRetailPrice'])
        self.assertFalse(r['TaxIncluded'])
        self.assertEqual(25200,json.loads(r['RawCellsJson'])['納価（円）'])
        self.assertEqual(174, rows['3521PX08220N']['Inventory']['Quantity'])
        self.assertEqual('アメリカ', rows['3623RS212200']['Country'])
        self.assertEqual('-', rows['2227LJ0100NN']['VintageRaw'])
        self.assertEqual('84692657841', rows['3623RS212200']['JanCodes'][0])
        self.assertEqual(4, sum(len(b['Rows']) > 0 for b in bundles.values()))

    @unittest.skipUnless(ROOT.exists(), 'Source workbooks required')
    def test_reconciliation_rejects_conflicts(self):
        paths = {k:next(ROOT.glob(PATTERNS[k])) for k in ORDER}
        sources={k:read_source(paths[k],k) for k in ORDER}
        for kind, field, value in [('shipping',11,1),('jadot',-2,1),('bordeaux',-1,999)]:
            bad=copy.deepcopy(sources)
            next(iter(bad[kind]['rows'].values()))['cells'][field]=value
            with patch('tools.nippon_liquor_import.read_source',side_effect=lambda p,k:bad[k]):
                with self.assertRaises(ValueError): extract(paths)

if __name__ == '__main__': unittest.main()
