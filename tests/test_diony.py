import hashlib
import json
from pathlib import Path
import re
import unittest
from collections import Counter

ROOT=Path(__file__).resolve().parents[1]/'catalogs'


class DionySourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.main=json.loads((ROOT/'カタログ.ディオニー.2026voi9.products.json').read_text(encoding='utf-8'))
        cls.news=json.loads((ROOT/'カタログ.ディオニー.ナチュラルワイン.202609.products.json').read_text(encoding='utf-8'))
        cls.by_sku={r['SupplierProductCode']:r for r in cls.main}

    def test_complete_sources_and_artifact_integrity(self):
        self.assertEqual(len(self.main),516)
        self.assertEqual(len(self.by_sku),516)
        self.assertEqual(len(self.news),13)
        for p in ROOT.glob('*ディオニー*.meta.json'):
            m=json.loads(p.read_text(encoding='utf-8'))
            for name,digest in m['artifacts'].items():
                self.assertEqual(hashlib.sha256((ROOT/name).read_bytes()).hexdigest(),digest)

    def test_all_prices_inventory_and_skus_match_cards(self):
        for r in self.main:
            t=json.loads(r['RawCellsJson'])['商品カード原文']
            self.assertTrue(t.startswith(r['SupplierProductCode']+' '))
            self.assertEqual(r['ReferenceRetailPrice'],int(re.search(r'参考小売/\s*([\d,]+)',t)[1].replace(',','')))
            compact=t.replace(' ','')
            if 'SOLDOUT' in compact:self.assertEqual(r['Inventory']['Status'],3)
            elif re.search(r'在庫\d+(?:本|ケース)',compact):self.assertEqual(r['Inventory']['Status'],1)
            else:self.assertEqual(r['Inventory']['Status'],0)
        self.assertEqual(Counter(r['Inventory']['Status'] for r in self.main),{0:338,1:83,3:95})

    def test_wrapped_types_and_volume(self):
        for sku in ['36901','36832','37110','36938','36936']:
            self.assertEqual(self.by_sku[sku]['ProductType'],'オレンジ')
        self.assertEqual(self.by_sku['60066']['ProductType'],'白極微泡')
        self.assertEqual(self.by_sku['60067']['ProductType'],'ロゼ微泡')
        self.assertEqual(self.by_sku['42835']['VolumeMl'],1500)
        self.assertEqual(self.by_sku['36900']['CaseSize'],12)
        self.assertEqual(self.by_sku['36117']['Inventory'],{'Quantity':None,'Status':1,'RawValue':'在庫6ケース'})

    def test_news_overlap_and_price_vintage_consistency(self):
        for r in self.news:
            main=self.by_sku[r['SupplierProductCode']]
            self.assertEqual(r['ReferenceRetailPrice'],main['ReferenceRetailPrice'])
            self.assertEqual(r['Vintage'],main['Vintage'])
            self.assertEqual(r['Inventory']['Status'],0)
            self.assertEqual(r['VolumeMl'],1500 if r['SupplierProductCode']=='42835' else 0)


if __name__=='__main__':unittest.main()
