import json
from pathlib import Path
import tempfile
import unittest
from tools.fines_special_import import generate,price

class FinesSpecialTests(unittest.TestCase):
    def test_decimal_precision_and_invalids(self):
        self.assertEqual(20992.5,price(20992.5))
        self.assertEqual(3759,price(3758.9999999999995))
        for value in (None,True,0,-1,float('nan')):
            with self.assertRaises(ValueError):price(value)

    def test_precedence_retail_preservation_and_full_source(self):
        root=Path('catalogs/fines-special-20260924')
        base=Path('catalogs/ファインズ.在庫表.20260921.catalog.json')
        known={r['SupplierProductCode']:r for r in json.loads(base.read_text('utf-8'))['Rows']}
        with tempfile.TemporaryDirectory() as d:
            summary=generate(root/'特価リスト260924 (2).xlsx',root/'追加特価 (1).xlsx',base,Path(d))
            self.assertEqual(331,summary['products'])
            rows=[r for name in ('main','extra') for r in json.loads((Path(d)/(name+'.catalog.json')).read_text('utf-8'))['Rows']]
            self.assertEqual(331,len({r['SupplierProductCode'] for r in rows}))
            for r in rows:self.assertEqual(known[r['SupplierProductCode']]['ReferenceRetailPrice'],r['ReferenceRetailPrice'])
            by={r['SupplierProductCode']:r for r in rows}
            for sku,amount,quantity in [('ALDMR21A',63750,201),('ALBLP17A',27200,64),('AMDMR17',37570,46)]:
                self.assertEqual(amount,json.loads(by[sku]['RawCellsJson'])['SpecialPurchasePrice']['Amount'])
                self.assertEqual(quantity,by[sku]['Inventory']['Quantity'])
                self.assertIn('不採用の全リスト行',json.loads(by[sku]['RawCellsJson']))
            self.assertEqual(20992.5,json.loads(by['BLFLA22']['RawCellsJson'])['SpecialPurchasePrice']['Amount'])

if __name__=='__main__':unittest.main()
