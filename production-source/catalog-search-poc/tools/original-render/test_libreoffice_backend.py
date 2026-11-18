"""Portable synthetic fixture and opt-in native LibreOffice acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from libreoffice_backend import reject_formulas,render

def fixture(path,formula=False,hidden=False):
    from openpyxl import Workbook
    from openpyxl.styles import PatternFill,Font,Border,Side
    w=Workbook();s=w.active;s.title='Native Layout'
    s.merge_cells('A2:E2');s['A2']='SYNTHETIC ORIGINAL - NO REAL PRODUCTS'
    for r in range(3,10):
        for c in range(1,6):
            cell=s.cell(r,c,f'R{r}-C{c}');cell.font=Font(name='DejaVu Sans',size=12)
            cell.border=Border(bottom=Side(style='thin',color='225566'))
        s.row_dimensions[r].height=24
    s['B6']='NATIVE-ROW-6';s['C6']=0.75;s['D6']=1234.5;s['D6'].number_format='#,##0.00'
    s['E6']='原典確認';s['A3'].fill=PatternFill('solid',fgColor='66CC99')
    for c in 'ABCDE':s.column_dimensions[c].width=23
    s.row_dimensions[6].hidden=hidden
    if formula:s['C6']='=1+2'
    other=w.create_sheet('Not selected');other['A1']='MUST NOT APPEAR ON PRINT'
    w.save(path)

class GuardTests(unittest.TestCase):
    def test_formula_rejected_not_replaced_with_values(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'formula.xlsx';fixture(p,formula=True)
            before=p.read_bytes()
            with self.assertRaisesRegex(ValueError,'数式'):reject_formulas(p)
            self.assertEqual(before,p.read_bytes())
    def test_literal_fixture_is_allowed(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'literal.xlsx';fixture(p);reject_formulas(p)

def accept(directory,soffice,uno_python):
    import pypdfium2 as pdfium
    from PIL import Image,ImageChops
    directory.mkdir(parents=True,exist_ok=True)
    source=directory/'synthetic-native.xlsx';fixture(source);before=hashlib.sha256(source.read_bytes()).hexdigest()
    result=render(SimpleNamespace(source=source.resolve(),out=(directory/'output').resolve(),soffice=soffice,
        uno_python=uno_python,sheet=1,row=6,timeout=75))
    with pdfium.PdfDocument(directory/'output/base.pdf') as pdf:
        assert len(pdf)==1
        text=pdf[0].get_textpage().get_text_range()
        assert 'NATIVE-ROW-6' in text and 'MUST NOT APPEAR' not in text
    a=Image.open(directory/'output/base.png').convert('RGB');b=Image.open(directory/'output/highlight.png').convert('RGB')
    assert list(ImageChops.difference(a,b).getbbox())==result['highlightBox']
    assert a.size==b.size and len(a.getcolors(2_000_000))>20
    assert hashlib.sha256(source.read_bytes()).hexdigest()==before==result['sourceHash']
    assert result['row']==6 and result['firstRow']==3 and result['lastRow']==9
    proof={'status':'Passed','sourceSha256':before,'renderer':result['renderer'],'row':6,
        'singleSelectedPage':True,'originalTextPresent':True,'highlightDifferenceMatches':True,
        'originalHashUnchanged':True,'visualReviewRequired':True,'productionApplied':False}
    (directory/'proof.json').write_text(json.dumps(proof,indent=2),encoding='utf-8')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--accept',type=Path);p.add_argument('--soffice');p.add_argument('--uno-python');a=p.parse_args()
    if a.accept:
        if not a.soffice or not a.uno_python:p.error('Native acceptance needs --soffice and --uno-python')
        accept(a.accept,a.soffice,a.uno_python)
    else:unittest.main(argv=['test_libreoffice_backend'])
