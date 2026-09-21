import json
from pathlib import Path
import unittest
from tools.terravert_import import extract, inventory, map_row

class TerravertTests(unittest.TestCase):
    def test_stock_semantics(self):
        self.assertEqual({'Quantity':None,'Status':1,'RawValue':'〇'},inventory('〇',''))
        self.assertEqual(4,inventory('ー','9月\n入港予定')['Status'])
        self.assertIsNone(inventory('ー','')['Quantity'])
        self.assertEqual(0,inventory('ー','')['Status'])
        self.assertEqual(0,inventory('完売','')['Quantity'])
        self.assertEqual(2,inventory(9,'完売間近')['Status'])
        with self.assertRaises(ValueError): inventory('確認中','')

    def test_missing_price_and_raw_vintage(self):
        v=['69W23','スロヴェニア','Producer\n生産者','Wine\n商品','[2023]','白',1500,'','','','ー','9月\n入港予定','','','','6',4562233539239]
        r=map_row(v,'伊全リスト',27)
        self.assertIsNone(r['ReferenceRetailPrice'])
        self.assertEqual('スロヴェニア',r['Country'])
        self.assertEqual('[2023]',r['VintageRaw'])
        self.assertEqual(2023,r['Vintage'])
        self.assertEqual(1500,r['VolumeMl'])
        self.assertEqual('商品',r['ProductNameJa'])
        self.assertIn('税区分要確認',r['PriceNote'])
        self.assertEqual('',json.loads(r['RawCellsJson'])['参考上代'])

    @unittest.skipUnless(Path('catalogs/tvpricelistnew (5).xlsx').exists(),'Source required')
    def test_full_source_reconciliation(self):
        b,extra=extract(Path('catalogs/tvpricelistnew (5).xlsx'))
        self.assertEqual(1090,len(b['Rows']))
        self.assertEqual(1090,len({r['SupplierProductCode'] for r in b['Rows']}))
        self.assertEqual({'ケース条件11対1':74,'新入港＆入港予定':87},extra)
        self.assertEqual('2026-09-15',b['ObservedAt'])
        self.assertEqual(7,sum(r['ReferenceRetailPrice'] is None for r in b['Rows']))
        self.assertEqual(31,sum(r['Inventory']['Status']==4 for r in b['Rows']))
        self.assertEqual(21,sum(r['Country']=='スロヴェニア' for r in b['Rows']))
        self.assertEqual({'仏全リスト','伊全リスト','スペイン・ジョージア・日本全リスト'},{r['SourceSheet'] for r in b['Rows']})

if __name__=='__main__': unittest.main()
