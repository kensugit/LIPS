import json,re,unittest
from collections import Counter
from pathlib import Path
import pdfplumber
from tools.supplier_pdfs import toyotsu

class ToyotsuTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.path=Path(__file__).resolve().parents[1]/'catalogs/豊通食料.ワインリスト.202609.pdf'
        cls.rows,cls.warnings=toyotsu(cls.path.read_bytes())
        cls.by_sku={r['SupplierProductCode']:r for r in cls.rows}

    def test_all_rows_independently_reconciled(self):
        seen=Counter()
        with pdfplumber.open(self.path) as pdf:
            for page_no,page in enumerate(pdf.pages[1:],2):
                for line in page.extract_text().splitlines():
                    match=re.match(r'^([A-Z][A-Z0-9]{5}).*?\s(NV|(?:19|20)\d{2})\s+(白泡|ロゼ泡|ビール|甘赤|甘白|赤|白|ロゼ|甘)\s+(\d+)\s+(\d+)\s+(?:(open|[\d,]+)\s+)?([◎◯△×])(?:\s|$)',line)
                    if not re.match(r'^[A-Z][A-Z0-9]{5}',line):continue
                    self.assertIsNotNone(match,line)
                    sku,year,color,volume,case,price,stock=match.groups()
                    row=self.by_sku[sku]
                    self.assertEqual((year,color,int(volume),int(case),None if price in (None,'open') else int(price.replace(',','')),stock),(row['VintageRaw'],row['ProductType'],row['VolumeMl'],row['CaseSize'],row['ReferenceRetailPrice'],row['Inventory']['RawValue']))
                    self.assertIn(page_no,[l['PDFページ'] for l in json.loads(row['RawCellsJson'])['掲載箇所']])
                    seen[sku]+=1
        self.assertEqual(660,sum(seen.values()))
        self.assertEqual(640,len(seen))
        self.assertEqual(Counter({1:620,2:20}),Counter(seen.values()))

    def test_missing_prices_stock_and_special_sku(self):
        self.assertEqual(640,len(self.rows))
        self.assertEqual(Counter({1:444,2:74,3:122}),Counter(r['Inventory']['Status'] for r in self.rows))
        self.assertTrue(all(r['Inventory']['Quantity'] is None and r['TaxIncluded'] is False for r in self.rows))
        self.assertEqual(26,sum(r['PriceRaw']=='open' for r in self.rows))
        self.assertEqual(101,sum(r['PriceRaw']=='' for r in self.rows))
        self.assertEqual(2600,self.by_sku['L67941']['ReferenceRetailPrice'])
        self.assertEqual(1500,self.by_sku['F03351']['VolumeMl'])
        self.assertEqual(375,self.by_sku['F06051']['VolumeMl'])
        self.assertEqual(3,self.by_sku['F02560']['Inventory']['Status'])
        self.assertIn('10月',self.by_sku['F02560']['PriceNote'])
        self.assertTrue(all(r['OriginalSupplierProductCode']==r['SupplierProductCode'] for r in self.rows))

if __name__=='__main__':unittest.main()
