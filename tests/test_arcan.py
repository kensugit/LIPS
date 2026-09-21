import io
import json
from pathlib import Path
import unittest
from collections import Counter

from tools.arcan_pdf import arcan


class ArcanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = Path('catalogs/アルカン.ボルドー.202609.pdf')
        if not cls.source.exists():
            raise unittest.SkipTest('Arcan original required')
        cls.rows, cls.warnings = arcan(cls.source.read_bytes())
        cls.by_code = {r['SupplierProductCode']: r for r in cls.rows}

    def test_complete_rows_and_unknown_retail_inventory(self):
        self.assertEqual(len(self.rows), 164)
        self.assertEqual(Counter(r['SourceSheet'] for r in self.rows), {'PDF p.1':40,'PDF p.2':45,'PDF p.3':45,'PDF p.4':34})
        for row in self.rows:
            self.assertIsNone(row['ReferenceRetailPrice'])
            self.assertEqual(row['Inventory'], dict(Quantity=None, Status=0, RawValue=''))
            self.assertEqual(row['CaseSize'], 0)

    def test_wholesale_conditions_and_no_regular_price(self):
        raw = json.loads(self.by_code['07047221']['RawCellsJson'])
        self.assertEqual(raw['通常納品価格数値'], 27380)
        self.assertEqual(raw['特別納価・条件数値'], 23980)
        self.assertIn('3本単位', raw['特別納価・条件'])
        raw = json.loads(self.by_code['07090916']['RawCellsJson'])
        self.assertIsNone(raw['通常納品価格数値'])
        self.assertEqual(raw['特別納価・条件数値'], 2200)
        self.assertIn('11対1', json.loads(self.by_code['08109124']['RawCellsJson'])['特別納価・条件'])

    def test_ambiguous_sku_and_nonstandard_volume(self):
        self.assertEqual(len(self.warnings), 1)
        row = next(r for r in self.rows if r['SupplierProductCode'].startswith('ARCAN-'))
        self.assertEqual(json.loads(row['RawCellsJson'])['商品コード'], '07034419\n07034619')
        self.assertEqual(row['OriginalSupplierProductCode'], '')
        self.assertEqual(self.by_code['08150119']['VolumeMl'], 2250)
        self.assertEqual(self.by_code['07001807']['VolumeMl'], 375)
        self.assertEqual(self.by_code['07071912']['VolumeMl'], 1500)

    def test_wrong_source_rejected(self):
        from reportlab.pdfgen.canvas import Canvas
        stream = io.BytesIO()
        canvas = Canvas(stream); canvas.drawString(20, 20, 'Other supplier'); canvas.save()
        with self.assertRaises(ValueError):
            arcan(stream.getvalue())
