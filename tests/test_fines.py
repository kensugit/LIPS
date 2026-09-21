import json
from pathlib import Path
import unittest
from tools.fines_import import map_row, extract

class FinesTests(unittest.TestCase):
    def fixture(self):
        return ['', 'フランス', 'シャブリ', 'CODE23', '製品', '生産者', '2023', '1500ML', '白', '6', '21280', '38000', '4573542529439', '4', '4', 'WINE', '説明<br />本文']

    def test_case_remainder_and_price_meanings(self):
        r = map_row(self.fixture(), 'ﾜｲﾝﾘｽﾄ', 6)
        self.assertEqual(28, r['Inventory']['Quantity'])
        self.assertEqual(38000, r['ReferenceRetailPrice'])
        self.assertEqual(1500, r['VolumeMl'])
        self.assertEqual('21280', json.loads(r['RawCellsJson'])['納入価格'])
        self.assertIn('税区分要確認', r['PriceNote'])
        self.assertEqual(6, r['SourceRow'])

    def test_threshold_not_exact(self):
        v = self.fixture(); v[13:15] = ['100C以上', '']
        r = map_row(v, 'sheet', 1)
        self.assertIsNone(r['Inventory']['Quantity'])
        self.assertEqual(1, r['Inventory']['Status'])
        self.assertIn('600本以上', r['PriceNote'])

    def test_upc_preserved_without_padding(self):
        v = self.fixture(); v[12] = '707319096013'
        self.assertEqual(['707319096013'], map_row(v, 'sheet', 1)['JanCodes'])

    def test_zero_and_missing_are_distinct(self):
        v = self.fixture(); v[13:15] = ['0', '0']
        self.assertEqual(3, map_row(v, 'sheet', 1)['Inventory']['Status'])
        v[14] = ''
        with self.assertRaises(ValueError): map_row(v, 'sheet', 1)

    def test_unexpected_remainder_rejected(self):
        v = self.fixture(); v[14] = '6'
        with self.assertRaises(ValueError): map_row(v, 'sheet', 1)

    @unittest.skipUnless(Path('catalogs/最新在庫情報_20260921151534.xls').exists(), 'Source required')
    def test_original_coverage(self):
        b = extract(Path('catalogs/最新在庫情報_20260921151534.xls'))
        self.assertEqual(2090, len(b['Rows']))
        self.assertEqual('2026-09-21', b['ObservedAt'])
        self.assertEqual(256, sum(r['Inventory']['Quantity'] is None for r in b['Rows']))
        self.assertEqual(list(range(5, 2095)), [r['SourceRow'] for r in b['Rows']])

if __name__ == '__main__': unittest.main()
