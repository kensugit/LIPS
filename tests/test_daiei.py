import hashlib
import json
from pathlib import Path
import re
import unittest
from collections import Counter

from pypdf import PdfReader
from tools.daiei_pdf import daiei


class DaieiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.files = sorted(Path('catalogs').glob('大榮*.catalog.json'))
        if len(cls.files) != 7:
            raise unittest.SkipTest('Seven original Daiei catalogs required')
        cls.bundles = [json.loads(p.read_text(encoding='utf8')) for p in cls.files]
        cls.rows = [r for b in cls.bundles for r in b['Rows']]

    def test_all_source_price_lines_independently(self):
        for path,bundle in zip(self.files,self.bundles):
            pdf = path.with_name(path.name.replace('.catalog.json','.pdf'))
            self.assertEqual(hashlib.sha256(pdf.read_bytes()).hexdigest(), bundle['SourceSha256'])
            pages = [p.extract_text(extraction_mode='layout') for p in PdfReader(pdf).pages]
            source = []
            for page_no,text in enumerate(pages,1):
                for line in text.splitlines():
                    if not re.search(r'(?:¥[\d,]+|OPEN)\s*$',line): continue
                    tokens=line.split(); sku,price=tokens[-2:]
                    cap=re.search(r'(\d+(?:\.\d+)?)(ml|L)(?:\([^)]*\))?/(\d+)',line)
                    self.assertIsNotNone(cap,line)
                    source.append((sku,None if price=='OPEN' else int(price[1:].replace(',','')),int(float(cap[1])*(1000 if cap[2]=='L' else 1)),int(cap[3]),page_no))
            actual=[(r['SupplierProductCode'],r['ReferenceRetailPrice'],r['VolumeMl'],r['CaseSize'],int(r['SourceSheet'].split('.')[-1])) for r in bundle['Rows']]
            self.assertEqual(source,actual)
            normalized=re.sub(r'\s+','','\n'.join(pages))
            for row in bundle['Rows']:
                for field in ['ProductNameJa','ProductNameEn','ProducerNameJa','ProducerNameEn']:
                    self.assertIn(re.sub(r'\s+','',row[field]), normalized, (row['SupplierProductCode'],field))

    def test_identity_and_unknown_fields(self):
        self.assertEqual(len(self.rows),758)
        self.assertEqual(len({r['SupplierProductCode'] for r in self.rows}),758)
        self.assertTrue(all(b['SupplierCode']=='DAIEI' and b['ObservedAt']=='2026-07-01' for b in self.bundles))
        self.assertTrue(all(r['Inventory']['Status']==0 and r['Inventory']['Quantity'] is None for r in self.rows))
        self.assertTrue(all('税区分' in r['PriceNote'] for r in self.rows))
        self.assertEqual(sum(r['Country']=='日本' for r in self.rows),4)

    def test_cross_page_and_unusual_formats(self):
        rows={r['SupplierProductCode']:r for r in self.rows}
        self.assertEqual(rows['PG1170-06A']['ProductNameJa'],'クローン ヴィンテージ ポート')
        self.assertEqual(rows['BIT15L']['VolumeMl'],15000)
        self.assertIsNone(rows['CH4196-15QB']['ReferenceRetailPrice'])
        self.assertEqual(rows['BX7300-004B']['VintageRaw'],'7ANS')
        self.assertEqual(rows['BITG403']['ProductType'],'グラス')
        self.assertEqual(rows['ER9100-01AQ']['ProducerNameJa'],'カンティーナ ディ サンタ クローチェ生産者協同組合')

    def test_wrong_source_rejected(self):
        from reportlab.pdfgen.canvas import Canvas
        import io
        buffer=io.BytesIO();c=Canvas(buffer);c.drawString(20,750,'Unrelated source');c.save()
        with self.assertRaises(ValueError): daiei(buffer.getvalue())


if __name__=='__main__': unittest.main()
