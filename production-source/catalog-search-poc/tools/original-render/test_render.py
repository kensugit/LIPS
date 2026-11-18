"""Regression checks using disposable native-layout fixtures; no catalog writes."""
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
import zipfile

from PIL import Image, ImageChops
import pypdfium2 as pdfium
from render import run, workbook_info

FIXTURES=Path(os.environ['ORIGINAL_RENDER_FIXTURES'])

class OriginalRenderTests(unittest.TestCase):
    def test_initial_row_uses_visible_content_and_rejects_blank(self):
        source=FIXTURES/'原典画像確認.xlsx'
        sheet=workbook_info(source)['sheets'][0]
        self.assertEqual(92,sheet['firstContentRow'])
        self.assertNotIn(1,sheet['contentRows'])
        self.assertIn(95,sheet['contentRows'])
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError,'1行目には表示する文字'):
                run(source,Path(temp),'render',1,1,1)

    def test_native_excel_layout_and_highlight(self):
        source=FIXTURES/'原典画像確認.xlsx';before=source.read_bytes()
        with tempfile.TemporaryDirectory() as temp:
            out=Path(temp);r=run(source,out,'render',1,95,1)
            self.assertEqual('Microsoft Excel 16.0',r['renderer'])
            self.assertEqual(95,r['row']);self.assertEqual(92,r['firstRow'])
            a=Image.open(out/'base.png');b=Image.open(out/'highlight.png')
            self.assertEqual(tuple(r['highlightBox']),ImageChops.difference(a,b).getbbox())
            self.assertGreater(a.width,900)
            # Preserve header color, formatting and native drawing objects, not text-only recreation.
            self.assertGreater(len(a.getcolors(1_000_000)),100)
            self.assertEqual(before,source.read_bytes())
            self.assertEqual(hashlib.sha256(before).hexdigest(),r['sourceHash'])

    def test_pdf_pixels_are_original_renderer_output(self):
        source=FIXTURES/'原典ページ確認.pdf';before=source.read_bytes()
        with tempfile.TemporaryDirectory() as temp:
            out=Path(temp);r=run(source,out,'render',1,1,1)
            with pdfium.PdfDocument(source) as d:
                p=d[0];expected=p.render(scale=min(2.0,2400/max(p.get_size()))).to_pil().convert('RGB')
                self.assertIsNone(ImageChops.difference(expected,Image.open(out/'base.png')).getbbox())
            row=next(x for x in r['lines'] if 'NATIVE-95' in x['text'])
            self.assertTrue(280<row['y']<320)
            self.assertGreater(row['right']-row['x'],900)
            self.assertEqual(before,source.read_bytes())

    def test_scanned_pdf_is_rendered_without_invented_rows(self):
        with tempfile.TemporaryDirectory() as temp:
            r=run(FIXTURES/'原典スキャン確認.pdf',Path(temp),'render',1,1,1)
            self.assertEqual([],r['lines']);self.assertGreater(r['width'],100)

    def test_rotated_page_coordinates_are_on_original_image(self):
        with tempfile.TemporaryDirectory() as temp:
            r=run(FIXTURES/'原典複数ページ確認.pdf',Path(temp),'render',1,1,2)
            self.assertGreater(r['width'],r['height'])
            for row in r['lines']:
                self.assertTrue(0<=row['x']<row['right']<=r['width'])
                self.assertTrue(0<=row['y']<row['bottom']<=r['height'])

    def test_invalid_page_and_row_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            for source,sheet,row,page in [('原典ページ確認.pdf',1,1,99),('原典画像確認.xlsx',1,101,1),('原典画像確認.xlsx',2,95,1)]:
                with self.assertRaises(ValueError):run(FIXTURES/source,Path(temp),'render',sheet,row,page)

    def test_active_content_is_rejected_before_excel_starts(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'active.xlsx'
            with zipfile.ZipFile(FIXTURES/'原典画像確認.xlsx') as original,zipfile.ZipFile(source,'w') as copy:
                for e in original.infolist():copy.writestr(e,original.read(e.filename))
                copy.writestr('xl/connections.xml','<connections/>')
            with self.assertRaisesRegex(ValueError,'外部接続'):workbook_info(source)

    def test_external_drawing_is_rejected_before_excel_starts(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'linked.xlsx'
            with zipfile.ZipFile(FIXTURES/'原典画像確認.xlsx') as original,zipfile.ZipFile(source,'w') as copy:
                for e in original.infolist():copy.writestr(e,original.read(e.filename))
                copy.writestr('xl/drawings/_rels/external.xml.rels','<Relationships><Relationship TargetMode="External" Type="image" Target="https://invalid.example/image.png"/></Relationships>')
            with self.assertRaisesRegex(ValueError,'外部ファイル参照'):workbook_info(source)

    def test_unsupported_format_does_not_substitute_value_rendering(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'source.csv';source.write_text('one,two')
            with self.assertRaisesRegex(ValueError,'原典ダウンロード'):run(source,Path(temp),'render',1,1,1)

if __name__=='__main__':unittest.main()
