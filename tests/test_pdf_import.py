import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, MagicMock

from tools.pdf_import import import_pdf, wine_experience


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "input.pdf"
        self.source.write_bytes(b"%PDF-1.7\nfixture")
        self.out = self.root / "out"

    @patch("tools.pdf_import.extract_pages", return_value=["page one", "page two"])
    def test_round_trip_idempotency_and_repair(self, extract):
        first = import_pdf(self.source, self.out)
        self.assertEqual(2, first["pages"])
        self.assertEqual(self.source.read_bytes(), (self.out / "input.pdf").read_bytes())
        self.assertIn("\npage two", (self.out / "input.txt").read_text())
        self.assertEqual("unchanged", import_pdf(self.source, self.out)["result"])
        self.assertEqual(1, extract.call_count)
        (self.out / "input.txt").write_text("broken")
        self.assertEqual("imported", import_pdf(self.source, self.out)["result"])
        self.assertEqual(2, extract.call_count)
        (self.out / "input.pages.json").unlink()
        self.assertEqual("imported", import_pdf(self.source, self.out)["result"])

    @patch("tools.pdf_import.extract_pages", return_value=["page"])
    def test_conflicting_original_is_not_overwritten(self, extract):
        import_pdf(self.source, self.out)
        original = (self.out / "input.pdf").read_bytes()
        self.source.write_bytes(b"%PDF-1.7\ndifferent")
        with self.assertRaisesRegex(ValueError, "Different PDF"):
            import_pdf(self.source, self.out)
        self.assertEqual(original, (self.out / "input.pdf").read_bytes())

    @patch("tools.pdf_import.extract_pages", return_value=["text", ""])
    def test_empty_page_requires_review_and_publishes_nothing(self, extract):
        with self.assertRaisesRegex(ValueError, "OCR/review"):
            import_pdf(self.source, self.out)
        self.assertFalse(self.out.exists())

    def test_unsafe_output_name(self):
        with self.assertRaises(ValueError):
            import_pdf(self.source, self.out, "../escape")

    def test_invalid_pdf(self):
        self.source.write_text("instructions are not a PDF")
        with self.assertRaisesRegex(ValueError, "signature"):
            import_pdf(self.source, self.out)

    @patch("pdfplumber.open")
    def test_product_codes_stock_and_capacity_conflict(self, open_pdf):
        page = MagicMock()
        page.extract_text.return_value = "THE WINE EXPERIENCE WEMA0004 WECA0608M"
        page.extract_tables.return_value = [[
            ["WEMA0004", "フランス", "産地", "生産者", "ワイン", "NV", "750", "6", "赤", "12%", "", "1,000", "1,100", "〇", "", ""],
            ["WECA0608M", "フランス", "産地", "生産者", "Wine(1500ML)", "NV", "750", "1", "泡", "12%", "", "1,000", "1,100", "完売", "", ""]]]
        open_pdf.return_value.__enter__.return_value.pages = [page]
        rows, warnings = wine_experience(b"fixture")
        self.assertEqual(2, len(rows))
        self.assertEqual("", rows[0]["在庫数量"])
        self.assertEqual("200本以上", rows[0]["在庫状態"])
        self.assertEqual(0, rows[1]["在庫数量"])
        self.assertEqual(750, rows[1]["容量(ml)"])
        self.assertEqual(1, len(warnings))
        page.extract_text.return_value += " WEZZ9999B"
        with self.assertRaisesRegex(ValueError, "coverage mismatch"):
            wine_experience(b"fixture")


if __name__ == "__main__":
    unittest.main()
