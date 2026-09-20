from copy import deepcopy
import unittest
from tools.catalog_export import bilingual, wine_experience_rows


class CatalogExportTests(unittest.TestCase):
    def fixture(self):
        return {"WEコード": "WECA0608M", "原産国": "フランス", "産地": "シャンパーニュ", "生産者": "Champagne Castelnau\nシャンパーニュ・カステルノー", "ワイン名": "Hors Categorie(1500ML)\nオー・カテゴリー(1500ML)", "VIN": "NV", "容量(ml)": 750, "入数": 6, "種別": "泡\nロゼ", "Alc.%": "12.5%", "Barcode": "---", "小売価格": 70000, "税込小売": 77000, "在庫(本)": "〇", "備考": "再入荷", "有機認証": "", "PDFページ": 1, "確認事項": "商品名の容量と容量欄が不一致"}

    def test_bilingual_wrapping(self):
        self.assertEqual(("長い日本語商品名の続き", "Long English name"), bilingual("Long English\nname\n長い日本語商品名\nの続き"))

    def test_original_meaning_and_metadata(self):
        row = wine_experience_rows([self.fixture()])[0]
        self.assertEqual("オー・カテゴリー(1500ML)", row["ProductNameJa"])
        self.assertEqual(750, row["VolumeMl"])
        self.assertEqual(6, row["CaseSize"])
        self.assertEqual(12.5, row["AlcoholPercent"])
        self.assertEqual("泡ロゼ", row["ProductType"])
        self.assertEqual(70000, row["ReferenceRetailPrice"])
        self.assertFalse(row["TaxIncluded"])
        self.assertEqual([], row["JanCodes"])
        self.assertIsNone(row["Vintage"])
        self.assertEqual("NV", row["VintageRaw"])
        self.assertIsNone(row["Inventory"]["Quantity"])
        self.assertIn("200本以上", row["PriceNote"])
        self.assertIn("不一致", row["PriceNote"])
        self.assertEqual("〇", row["Inventory"]["RawValue"])
        self.assertIn("税込小売", row["RawCellsJson"])

    def test_stock_states_and_page_row_identity(self):
        records = []
        for i, stock in enumerate(["○", "完売", "7", "", "0"]):
            record = deepcopy(self.fixture())
            record["WEコード"] = f"WECA{i:04}B"
            record["在庫(本)"] = stock
            record["PDFページ"] = 1 if i < 2 else 2
            records.append(record)
        rows = wine_experience_rows(records)
        self.assertEqual([None, 0, 7, None, 0], [x["Inventory"]["Quantity"] for x in rows])
        self.assertEqual([1, 3, 1, 0, 3], [x["Inventory"]["Status"] for x in rows])
        self.assertEqual([1, 2, 1, 2, 3], [x["SourceRow"] for x in rows])
        self.assertEqual("PDF p.2", rows[-1]["SourceSheet"])


if __name__ == "__main__":
    unittest.main()
