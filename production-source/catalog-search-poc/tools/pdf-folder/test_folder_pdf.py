import unittest
from types import SimpleNamespace
from unittest.mock import patch
import parse_folder_pdf as adapter


class FolderPdfTests(unittest.TestCase):
    def text_reader(self, texts):
        return SimpleNamespace(is_encrypted=False, pages=[SimpleNamespace(extract_text=lambda t=t:t) for t in texts])

    def test_jan_optional_ean8_and_continuation_pages_preserve_columns(self):
        header='商品名称１ 在庫数 小売価格 JANコード 国名\n'
        text=['46803202 原典商品10年 750 12 145 7,200 0 19 5601519335000 Portugal',
              '71419000 原典1850Y 750 12 3 530,000 1850 20 Portugal',
              '77814014 原典ビール 330 24 3672 490 0 5.1 56088168 Portugal']
        with patch('pypdf.PdfReader',return_value=self.text_reader([header+text[0],text[1],text[2]])):
            rows,_=adapter.kinoshita(b'fixture')
        self.assertEqual([145,3,3672],[r['Inventory']['Quantity'] for r in rows])
        self.assertEqual([7200,530000,490],[r['ReferenceRetailPrice'] for r in rows])
        self.assertEqual([None,1850,None],[r['Vintage'] for r in rows])
        self.assertEqual([],rows[1]['JanCodes']);self.assertEqual(['56088168'],rows[2]['JanCodes'])
        self.assertEqual('PDF p.2',rows[1]['SourceSheet'])

    def test_changed_numeric_row_is_not_silently_dropped(self):
        with patch('pypdf.PdfReader',return_value=self.text_reader(['商品名称 在庫数 小売価格 JAN 国名\n46803202 unknown layout'])):
            with self.assertRaises(ValueError):adapter.kinoshita(b'fixture')

    def test_blank_vintage_does_not_consume_alcohol_or_jan(self):
        text='商品名称 在庫数 小売価格 JAN 国名\n46817000 原典商品5年 500 6 34 3,000 19 5601519734100 Portugal'
        with patch('pypdf.PdfReader',return_value=self.text_reader([text])):
            rows,_=adapter.kinoshita(b'fixture')
        self.assertEqual('',rows[0]['VintageRaw']);self.assertEqual(19,rows[0]['AlcoholPercent'])
        self.assertEqual(['5601519734100'],rows[0]['JanCodes'])

    def test_duplicate_supplier_code_requires_review(self):
        text='商品名称 在庫数 小売価格 JAN 国名\n'+'46803202 商品 750 12 145 7,200 0 19 5601519335000 Portugal\n'*2
        with patch('pypdf.PdfReader',return_value=self.text_reader([text])):
            with self.assertRaises(ValueError):adapter.kinoshita(b'fixture')

    def test_invalid_and_unknown_documents_return_actions_without_source_text(self):
        result=adapter.parse(b'not pdf','KINOSHITA')
        self.assertEqual('InvalidPdf',result['code']);self.assertTrue(result['remedy'])
        result=adapter.parse(b'%PDF-unknown','UNKNOWN_SUPPLIER')
        self.assertEqual('UnsupportedPdfLayout',result['code']);self.assertTrue(result['remedy'])

    def test_source_content_never_escapes_extraction_error(self):
        with patch.object(adapter,'kinoshita',side_effect=ValueError('SOURCE-CONTENT-DO-NOT-LOG')):
            result=adapter.parse(b'%PDF-fixture','KINOSHITA')
        self.assertNotIn('SOURCE-CONTENT',str(result));self.assertFalse(result['ok'])


if __name__=='__main__':unittest.main()
