import os
import json
from pathlib import Path
import unittest
from collections import Counter
from unittest.mock import patch
import parse_folder_pdf as adapter


class ArcanRetailTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = os.environ.get("ARCAN_TEST_PDF")
        if not path:
            raise unittest.SkipTest("Set ARCAN_TEST_PDF to original read-only catalog")
        cls.data = Path(path).read_bytes()
        cls.result = adapter.parse(cls.data,"ARCAN")
        if not cls.result.get("ok"):
            raise AssertionError(cls.result)
        cls.rows = cls.result["rows"]
        cls.by_code = {r["OriginalSupplierProductCode"]:r for r in cls.rows}

    def test_complete_alcohol_only_with_source_locations(self):
        self.assertEqual(392,len(self.rows))
        self.assertEqual(392,len({r["SupplierProductCode"] for r in self.rows}))
        self.assertEqual(394,sum(len(json.loads(r["RawCellsJson"])["掲載箇所"]) for r in self.rows))
        self.assertEqual(set(range(4,23)),{int(r["SourceSheet"].split(".")[1]) for r in self.rows})
        self.assertEqual({"赤":176,"白":122,"泡":53,"白甘":15,"ロゼ泡":10,"蒸留酒":8,"ロゼ":7,"シードル":1},dict(Counter(r["ProductType"] for r in self.rows)))
        self.assertFalse(any(r["SupplierProductCode"].startswith(("318","416")) for r in self.rows))

    def test_conflicting_code_is_two_products_with_original_identity(self):
        rows=[r for r in self.rows if r["OriginalSupplierProductCode"]=="606020718"]
        self.assertEqual(2,len(rows))
        self.assertEqual({15000,20000},{r["ReferenceRetailPrice"] for r in rows})
        self.assertEqual({"3760347084107","3760347083650"},{r["JanCodes"][0] for r in rows})
        for r in rows:
            self.assertTrue(r["SupplierProductCode"].startswith("ARCAN-"))
            self.assertIn("同じ品番が別商品",r["PriceNote"])

    def test_unknown_stock_prices_and_years_are_not_invented(self):
        self.assertEqual({0:373,4:19},dict(Counter(r["Inventory"]["Status"] for r in self.rows)))
        self.assertTrue(all(r["Inventory"]["Quantity"] is None for r in self.rows))
        self.assertTrue(all(r["TaxIncluded"] is False for r in self.rows))
        r=self.by_code["07438700"]
        self.assertEqual("10",r["VintageRaw"]);self.assertIsNone(r["Vintage"])
        self.assertIsNone(self.by_code["07816722"]["ReferenceRetailPrice"])
        self.assertEqual([],self.by_code["08850106"]["JanCodes"])
        self.assertEqual("841063188001",json.loads(self.by_code["08850106"]["RawCellsJson"])["JAN"])
        self.assertIsNone(json.loads(r["RawCellsJson"])["資料日"])

    def test_exact_repeats_keep_all_evidence(self):
        for code in ["267007719","07758724"]:
            rows=[r for r in self.rows if r["OriginalSupplierProductCode"]==code]
            self.assertEqual(1,len(rows))
            self.assertEqual(2,len(json.loads(rows[0]["RawCellsJson"])["掲載箇所"]))

    def test_changed_independent_price_fails_closed(self):
        from pypdf._page import PageObject
        original=PageObject.extract_text
        def changed(page,*args,**kwargs):
            text=original(page,*args,**kwargs)
            if kwargs.get("extraction_mode")=="layout":
                return text.replace("15,000","15,001")
            return text
        with patch.object(PageObject,"extract_text",changed):
            result=adapter.parse(self.data,"ARCAN")
        self.assertFalse(result["ok"])
        self.assertEqual("PdfExtractionFailed",result["code"])
        self.assertNotIn("Rosé",result["message"])


if __name__=="__main__":unittest.main()
