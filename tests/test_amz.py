import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import json
import openpyxl
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from amz_import import extract

SOURCE = Path(__file__).resolve().parents[1] / 'catalogs/AMZ.StockList20260925.xlsx'

class AmzTests(unittest.TestCase):
    def test_all_rows_against_source(self):
        b = extract(SOURCE)
        w = openpyxl.load_workbook(SOURCE, data_only=True)
        self.assertEqual(b['ObservedAt'], '2026-09-25')
        self.assertEqual(len(b['Rows']), 206)
        for row in b['Rows']:
            c = [v.value for v in w['在庫表 (JAN)'][row['SourceRow']]]
            self.assertEqual(row['SupplierProductCode'], c[2])
            self.assertEqual(row['ReferenceRetailPrice'], c[20])
            self.assertEqual(json.loads(row['RawCellsJson'])[f"T{row['SourceRow']}"], c[19])
            self.assertEqual(row['Inventory']['RawValue'], c[21])
            self.assertIsNone(row['Inventory']['Quantity'])
            self.assertEqual(row['VolumeMl'], c[8])
            self.assertEqual(row['JanCodes'], list(dict.fromkeys(str(v) for v in c[17:19] if v)))
        self.assertEqual(sum(r['Inventory']['Status']==2 for r in b['Rows']),17)

    def rejected_change(self, changes):
        w = openpyxl.load_workbook(SOURCE)
        for sheet,cell,value in changes: w[sheet][cell] = value
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/SOURCE.name;w.save(p)
            with self.assertRaises(ValueError): extract(p)

    def test_sheet_disagreement(self):
        self.rejected_change([('在庫表','U4',1)])

    def test_changed_price_header(self):
        self.rejected_change([(s,'U2','仕入価格') for s in ('在庫表','在庫表 (JAN)')])

    def test_unknown_stock(self):
        self.rejected_change([(s,'V4','要確認') for s in ('在庫表','在庫表 (JAN)')])

    def test_duplicate_sku(self):
        self.rejected_change([(s,'C5','00B00112') for s in ('在庫表','在庫表 (JAN)')])

if __name__=='__main__': unittest.main()
