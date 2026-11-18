"""Local dispatch contract tests; does not execute native LibreOffice/Excel."""
import hashlib
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import render
import libreoffice_backend
from test_libreoffice_backend import fixture

class DispatchTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.source=self.root/'source.xlsx';fixture(self.source)
        self.out=self.root/'out';self.out.mkdir()
        self.exe=self.root/'dedicated-executable';self.exe.write_text('synthetic')
        self.env={'LIPS_ORIGINAL_XLSX_BACKEND':'libreoffice','LIPS_ORIGINAL_SOFFICE':str(self.exe),
                  'LIPS_ORIGINAL_UNO_PYTHON':str(self.exe)}
    def tearDown(self):self.temp.cleanup()
    def linux(self):
        return patch.object(render,'os',SimpleNamespace(name='posix',environ=self.env,access=lambda *_:True,X_OK=os.X_OK))
    def test_info_preserves_schema_without_starting_backend(self):
        with self.linux(),patch.object(render.sys,'platform','linux'),patch.object(libreoffice_backend,'render') as native:
            result=render.run(self.source,self.out,'info',1,6,1)
        self.assertEqual(result['kind'],'xlsx');self.assertEqual(result['sheets'][0]['name'],'Native Layout')
        self.assertIn('LibreOffice',result['notice']);native.assert_not_called()
    def test_render_passes_native_result_and_bounded_private_configuration(self):
        proof={'kind':'xlsx','width':100,'height':90,'pages':1,'page':1,'lines':[],
               'sheet':'Native Layout','row':6,'firstRow':3,'lastRow':9,'highlightBox':[1,2,3,4],
               'sourceHash':hashlib.sha256(self.source.read_bytes()).hexdigest(),'notice':'native'}
        with self.linux(),patch.object(render.sys,'platform','linux'),patch.object(libreoffice_backend,'render',return_value=proof) as native:
            result=render.run(self.source,self.out,'render',1,6,1)
        self.assertEqual(result,proof)
        args=native.call_args.args[0];self.assertEqual((args.sheet,args.row,args.timeout),(1,6,60))
        self.assertEqual(args.source,self.source);self.assertEqual(args.soffice,str(self.exe))
    def test_not_enabled_is_explicit_error(self):
        self.env.clear()
        with self.linux(),patch.object(render.sys,'platform','linux'),self.assertRaisesRegex(ValueError,'未設定'):
            render.run(self.source,self.out,'render',1,6,1)
    def test_bad_executable_config_rejected(self):
        self.env['LIPS_ORIGINAL_SOFFICE']='relative'
        with self.linux(),patch.object(render.sys,'platform','linux'),self.assertRaisesRegex(ValueError,'設定が不正'):
            render.run(self.source,self.out,'render',1,6,1)
    def test_blank_row_rejected_before_native(self):
        with self.linux(),patch.object(render.sys,'platform','linux'),patch.object(libreoffice_backend,'render') as native:
            with self.assertRaisesRegex(ValueError,'文字・数値'):render.run(self.source,self.out,'render',1,1,1)
            native.assert_not_called()
    def test_backend_error_preserved_without_fallback(self):
        with self.linux(),patch.object(render.sys,'platform','linux'),patch.object(libreoffice_backend,'render',side_effect=ValueError('数式は保留')) as native:
            with self.assertRaisesRegex(ValueError,'数式は保留'):render.run(self.source,self.out,'render',1,6,1)
            self.assertEqual(native.call_count,1)
    def test_source_mutation_stops(self):
        def changed(_):self.source.write_bytes(b'changed');return {}
        with self.linux(),patch.object(render.sys,'platform','linux'),patch.object(libreoffice_backend,'render',side_effect=changed):
            with self.assertRaisesRegex(ValueError,'ハッシュ'):render.run(self.source,self.out,'render',1,6,1)
    def test_windows_info_does_not_require_linux_settings(self):
        with patch.object(render,'os',SimpleNamespace(name='nt')),patch.object(libreoffice_backend,'render') as native:
            result=render.run(self.source,self.out,'info',1,6,1)
        self.assertIn('Excel自身',result['notice']);native.assert_not_called()
    def test_pdf_path_never_reads_linux_configuration(self):
        source=self.root/'original.pdf';source.write_bytes(b'synthetic')
        class Doc:
            def __enter__(self):return self
            def __exit__(self,*_):pass
            def __len__(self):return 2
        with (patch.object(render,'linux_backend_config',side_effect=AssertionError('not applicable')),
            patch.object(render.pdfium,'PdfDocument',return_value=Doc())):
            result=render.run(source,self.out,'info',1,6,1)
        self.assertEqual(result['kind'],'pdf');self.assertEqual(result['pages'],2)

if __name__=='__main__':unittest.main()
