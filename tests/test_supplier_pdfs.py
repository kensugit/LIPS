from collections import Counter
import json
from pathlib import Path
import re
import unittest
import pdfplumber
from tools.supplier_pdfs import finesse, royal, identity

ROOT = Path(__file__).resolve().parents[1]


class SupplierPdfTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.finesse_path = ROOT / "catalogs/フィネス.カタログ.202609.pdf"
        cls.royal_path = ROOT / "catalogs/ローヤルオブジャパン.ワインリスト.202610.pdf"
        cls.finesse, _ = finesse(cls.finesse_path.read_bytes())
        cls.royal, _ = royal(cls.royal_path.read_bytes())

    def test_finesse_all_stock_rows_and_unknown_capacity(self):
        self.assertEqual(489, len(self.finesse))
        self.assertEqual({1:270, 2:95, 3:124}, Counter(r["Inventory"]["Status"] for r in self.finesse))
        self.assertEqual(124, sum(r["VolumeMl"] == 0 for r in self.finesse))
        self.assertTrue(all(r["Inventory"]["Quantity"] is None for r in self.finesse))
        self.assertTrue(all(r["ReferenceRetailPrice"] is None for r in self.finesse if not r["VolumeMl"]))

    def test_finesse_numeric_fields_independent_text_reconciliation(self):
        expected = Counter()
        with pdfplumber.open(self.finesse_path) as pdf:
            for page_no, page in enumerate(pdf.pages, 1):
                for match in re.finditer(r"([〇△])\s+(赤|白|ﾛｾﾞ)?\s*(NV|20\d{2})\s+(\d+)\s+(\d+)\s+([\d,]+|オープン)", page.extract_text() or ""):
                    expected[(f"PDF p.{page_no}", *match.groups())] += 1
        actual = Counter()
        for row in self.finesse:
            raw = json.loads(row["RawCellsJson"])
            if row["VolumeMl"]:
                actual[(row["SourceSheet"], raw["在庫"], raw["色"] or None, raw["年号"], raw["容量(ml)"], raw["入数"], raw["小売価格"])] += 1
        self.assertEqual(365, sum(expected.values()))
        self.assertEqual(expected, actual)

    def test_finesse_names_regions_and_variants(self):
        sugar = [r for r in self.finesse if "SUGR VISION" in r["ProductNameEn"]]
        self.assertEqual(1, len(sugar))
        self.assertIn("シュガー", sugar[0]["ProductNameJa"])
        self.assertNotIn("SPIRIT", sugar[0]["ProductNameEn"])
        continued = [r for r in self.finesse if r["SourceSheet"] == "PDF p.19" and r["SourceRow"] <= 3]
        self.assertTrue(all(r["Region"] == "アルザス" for r in continued))
        self.assertEqual(12, sum(r["Country"] == "アメリカ" for r in self.finesse))
        self.assertEqual(8, sum(r["PriceRaw"] == "オープン" for r in self.finesse))
        self.assertEqual(4, sum(r["ProductNameEn"] == "PICPOUL DE PINET" for r in self.finesse))
        self.assertEqual(2, sum(r["ProductNameEn"] == "PICPOUL DE PINET" and "特別ロット" in r["Region"] for r in self.finesse))
        self.assertEqual(489, len({r["SupplierProductCode"] for r in self.finesse}))

    def test_royal_all_prices_not_wholesale(self):
        self.assertEqual(123, len(self.royal))
        for row in self.royal:
            raw = json.loads(row["RawCellsJson"])
            self.assertEqual(int(raw["参考価格（税別）"].replace("¥", "").replace(",", "")), row["ReferenceRetailPrice"])
            self.assertIn("卸価格・混載3cs（税別）", raw)
            self.assertFalse(row["TaxIncluded"])
            self.assertIsNone(row["Inventory"]["Quantity"])
        self.assertEqual(580, self.royal[0]["ReferenceRetailPrice"])
        self.assertEqual(330, self.royal[0]["VolumeMl"])

    def test_royal_annotation_inventory_and_internal_identifiers(self):
        counts = Counter(r["Inventory"]["Status"] for r in self.royal)
        self.assertEqual({0:111, 3:1, 4:11}, counts)
        self.assertTrue(all(r["OriginalSupplierProductCode"] == "" for r in self.royal))
        self.assertEqual(123, len({r["SupplierProductCode"] for r in self.royal}))
        self.assertEqual(identity("X", ["Ａ", "Ｂ"]), identity("X", ["A", "B"]))


if __name__ == "__main__":
    unittest.main()
