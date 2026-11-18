"""Local original renderer. Never reconstructs a worksheet from extracted values."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import zipfile
import xml.etree.ElementTree as ET

import pypdfium2 as pdfium
from PIL import ImageChops

def linux_backend_config():
    """Explicit deployment opt-in; no executable discovery or host configuration changes."""
    if not sys.platform.startswith('linux') or os.environ.get('LIPS_ORIGINAL_XLSX_BACKEND')!='libreoffice':
        raise ValueError('この環境ではExcel原典描画が未設定です。原典ダウンロードで確認してください。PDF原典画像は表示できます。')
    paths=[]
    for key in ('LIPS_ORIGINAL_SOFFICE','LIPS_ORIGINAL_UNO_PYTHON'):
        value=os.environ.get(key,'')
        path=Path(value)
        if not value or not path.is_absolute() or not path.is_file() or not os.access(path,os.X_OK):
            raise ValueError('専用LibreOffice描画環境の実行ファイル設定が不正です。管理担当へ確認してください。')
        paths.append(str(path))
    return paths

def workbook_info(source):
    with zipfile.ZipFile(source) as z:
        names=z.namelist()
        if len(names)>2000 or sum(i.file_size for i in z.infolist())>100_000_000:
            raise ValueError('Excelの展開サイズが上限を超えています。')
        blocked=('externalLinks/','connections.xml','queryTables/','vbaProject','activeX/','embeddings/','macrosheets/')
        if any(any(b.lower() in n.lower() for b in blocked) for n in names):
            raise ValueError('外部接続・マクロ・埋込オブジェクトを含むExcelは隔離表示対象外です。原典ダウンロードで確認してください。')
        def xml(name):
            b=z.read(name)
            if b'<!DOCTYPE' in b or b'<!ENTITY' in b: raise ValueError('XML宣言が表示対象外です。')
            return ET.fromstring(b)
        for name in names:
            if name.endswith('.rels'):
                for rel in xml(name):
                    if rel.get('TargetMode')=='External' and not rel.get('Type','').endswith('/hyperlink'):
                        raise ValueError('外部ファイル参照を含むExcelは隔離表示対象外です。')
        ns={'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
        rels={e.get('Id'):e.get('Target') for e in xml('xl/_rels/workbook.xml.rels') if e.get('TargetMode')!='External'}
        sheets=[]
        for i,s in enumerate(xml('xl/workbook.xml').findall('s:sheets/s:sheet',ns),1):
            target=rels.get(s.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id'),'')
            name=target.lstrip('/') if target.startswith('/') else 'xl/'+target
            if '..' in name or name not in names: raise ValueError('シート参照が不正です。')
            sheet=xml(name)
            # Disallow data-fetching formulas even when calculation is manual.
            for f in sheet.findall('.//s:f',ns):
                if any(v in (f.text or '').upper() for v in ('WEBSERVICE(', 'RTD(', 'DDE(', 'HYPERLINK(', 'IMAGE(')):
                    raise ValueError('外部参照関数を含むExcelは隔離表示対象外です。')
            source_rows=sheet.findall('s:sheetData/s:row',ns)
            rows=[int(r.get('r','0')) for r in source_rows]
            populated=[int(r.get('r','0')) for r in source_rows if r.get('hidden')!='1' and any(
                c.find('s:f',ns) is not None or (c.findtext('s:v',default='',namespaces=ns).strip()) or
                any((t.text or '').strip() for t in c.findall('.//s:t',ns))
                for c in r.findall('s:c',ns))]
            sheets.append({'index':i,'name':s.get('name'),'maxRow':max(rows,default=1),'firstContentRow':min(populated,default=None),'contentRows':populated,'hidden':s.get('state','visible')!='visible'})
        return {'kind':'xlsx','sheets':sheets,'notice':'Excel自身による原典の印刷レンダリングです。選択行の前後3行を表示し、文字を覆わない外枠で対象行を示します。原本は変更しません。'}

def render_pdf(source,output,page_number,extract_lines=True):
    with pdfium.PdfDocument(source) as doc:
        if not 1<=page_number<=len(doc): raise ValueError('ページ番号が範囲外です。')
        page=doc[page_number-1]
        w,h=page.get_size()
        scale=min(2.0,2400/max(w,h))
        bitmap=page.render(scale=scale)
        image=bitmap.to_pil().convert('RGB');image.save(output)
        rows=[]
        if extract_lines:
            text=page.get_textpage()
            for i in range(min(text.count_chars(),50000)):
                char=text.get_text_range(i,1)
                if not char.strip():continue
                left,bottom,right,top=text.get_charbox(i)
                if right<=left or top<=bottom:continue
                def device(x,y):
                    dx,dy=ctypes.c_int(),ctypes.c_int()
                    pdfium.raw.FPDF_PageToDevice(page,0,0,image.width,image.height,0,x,y,ctypes.byref(dx),ctypes.byref(dy))
                    return dx.value,dy.value
                coords=[device(x,y) for x,y in ((left,bottom),(left,top),(right,bottom),(right,top))]
                x0,y0=min(x for x,y in coords),min(y for x,y in coords)
                x1,y1=max(x for x,y in coords),max(y for x,y in coords)
                near=next((r for r in rows if min(r['bottom'],y1)-max(r['y'],y0) >= .5*min(r['bottom']-r['y'],y1-y0)),None)
                if near is None:rows.append({'x':x0,'y':y0,'right':x1,'bottom':y1,'text':char})
                else:
                    near['x']=min(near['x'],x0);near['y']=min(near['y'],y0);near['right']=max(near['right'],x1);near['bottom']=max(near['bottom'],y1);near['text']+=char
            rows.sort(key=lambda r:(r['y'],r['x']))
        return {'width':image.width,'height':image.height,'pages':len(doc),'page':page_number,'lines':rows,'renderer':'PDFium / original PDF'}

def excel_raster_factor(width_points,diff,crop):
    """Bound the clipped bitmap before allocation (at most 32 million pixels)."""
    import math
    if not math.isfinite(width_points) or width_points<=0 or diff[2]<=diff[0] or diff[3]<=diff[1]:
        raise ValueError('原典の描画位置を確認できませんでした。')
    factor=max(1.0,2.0*width_points/(diff[2]-diff[0]),32.0/(diff[3]-diff[1]))
    pixel_width=math.ceil((crop[2]-crop[0])*factor)+2
    pixel_height=math.ceil((crop[3]-crop[1])*factor)+2
    if pixel_width>16000 or pixel_height>8000 or pixel_width*pixel_height>32_000_000:
        raise ValueError('原典の横幅が高精細表示の上限を超えています。原典ダウンロードで確認してください。')
    return factor

def run(source,out,mode,sheet,row,page):
    ext=source.suffix.lower()
    if ext not in ('.xlsx','.pdf'):raise ValueError('原典画像は.xlsxと.pdfに対応しています。他形式は原典ダウンロードで確認してください。')
    before=hashlib.sha256(source.read_bytes()).hexdigest()
    if ext=='.xlsx':
        linux_config=None
        if os.name!='nt':
            linux_config=linux_backend_config()
            from libreoffice_backend import reject_encrypted_or_invalid_container
            reject_encrypted_or_invalid_container(source)
        info=workbook_info(source)
        if linux_config:
            info['notice']='LibreOfficeが原典XLSXを印刷して画像化します。選択行の前後3行を表示し対象行を強調します。数式・保護・非対応レイアウトは保留し、原本を変更しません。'
        if mode=='info':return info
        selected=next((s for s in info['sheets'] if s['index']==sheet),None)
        if not selected or selected['hidden'] or not 1<=row<=selected['maxRow']:raise ValueError('シートまたは行番号が表示範囲外です。')
        if row not in selected['contentRows']:
            first=selected['firstContentRow']
            raise ValueError(f'{row}行目には表示する文字・数値がありません。内容は{first}行目からあります。行番号を変更してください。' if first else 'このシートには表示する文字・数値がありません。別のシートを選択してください。')
        if linux_config:
            from libreoffice_backend import render as render_linux
            result=render_linux(SimpleNamespace(source=source,out=out,sheet=sheet,row=row,
                soffice=linux_config[0],uno_python=linux_config[1],timeout=60))
            if before!=hashlib.sha256(source.read_bytes()).hexdigest():raise ValueError('原典ハッシュが変化したため表示を停止しました。')
            return result
        script=Path(__file__).with_name('excel-render.ps1')
        try:
            p=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-File',str(script),'-InputFile',str(source),'-OutputDirectory',str(out),'-Sheet',str(sheet),'-Row',str(row)],capture_output=True,timeout=60)
            if p.returncode:raise ValueError('Excel原典の描画に失敗しました。非表示行・保護・結合範囲等を原典で確認してください。')
        except subprocess.TimeoutExpired:
            # The child records only the PID of its newly created private Excel instance.
            pidfile=out/'excel.pid'
            if pidfile.exists():
                pid=int(pidfile.read_text().strip())
                subprocess.run(['powershell.exe','-NoProfile','-Command',f'$p=Get-Process -Id {pid} -ErrorAction SilentlyContinue; if($p.ProcessName -eq "EXCEL"){{$p.Kill()}}'],capture_output=True,timeout=10)
            raise ValueError('Excel描画が時間切れです。自動再試行はしません。')
        result=render_pdf(out/'base.pdf',out/'base.png',1,False)
        if result['pages']!=1:raise ValueError('Excelの対象範囲が複数ページに分かれました。原典を確認してください。')
        render_pdf(out/'highlight.pdf',out/'highlight.png',1,False)
        from PIL import Image
        base_image=Image.open(out/'base.png').convert('RGB')
        marked_image=Image.open(out/'highlight.png').convert('RGB')
        diff=ImageChops.difference(base_image,marked_image).getbbox()
        if not diff:raise ValueError('選択行の強調位置を確認できませんでした。')
        content=ImageChops.difference(marked_image,Image.new('RGB',marked_image.size,'white')).getbbox()
        crop=(max(0,content[0]-20),max(0,content[1]-20),min(marked_image.width,content[2]+20),min(marked_image.height,content[3]+20))
        # The small marker PDF locates the row only. Render the untouched original
        # PDF again at native worksheet point density; never enlarge the old raster.
        metadata=json.loads((out/'excel.json').read_text(encoding='utf-8-sig'))
        width_points=metadata['rangeWidthPoints']
        factor=excel_raster_factor(width_points,diff,crop)
        with pdfium.PdfDocument(out/'base.pdf') as doc:
            pdf_page=doc[0];pw,ph=pdf_page.get_size()
            low_scale=min(2.0,2400/max(pw,ph))
            pdf_crop=(crop[0]/low_scale,ph-crop[3]/low_scale,pw-crop[2]/low_scale,crop[1]/low_scale)
            bitmap=pdf_page.render(scale=low_scale*factor,crop=pdf_crop)
            original=bitmap.to_pil().convert('RGB')
            original.save(out/'base.png')
        # Outline outside the located row, with no fill and no change to its pixels.
        from PIL import ImageDraw
        target=[round((diff[i]-crop[i%2])*factor) for i in range(4)]
        outline=[max(0,target[0]-3),max(0,target[1]-3),min(original.width-1,target[2]+3),min(original.height-1,target[3]+3)]
        marked=original.copy();ImageDraw.Draw(marked).rectangle(outline,outline='red',width=2)
        marked.save(out/'highlight.png')
        actual_diff=ImageChops.difference(original,marked).getbbox()
        diff=actual_diff
        result.update(width=original.width,height=original.height,crop=list(crop),rasterScale=low_scale*factor,
                      targetBox=target,highlightStyle='outside-outline',nativePixelsPerPoint=2.0)
        result.update(metadata)
        result.update(kind='xlsx',highlightBox=list(diff),notice=info['notice'])
    else:
        with pdfium.PdfDocument(source) as doc:
            if len(doc)>1000:raise ValueError('PDFページ数が上限を超えています。')
            if mode=='info':return {'kind':'pdf','pages':len(doc),'notice':'PDF原典のページをそのまま画像化します。テキスト位置候補を選択するか、画像上をドラッグして対象行を指定してください。'}
        result=render_pdf(source,out/'base.png',page)
        result.update(kind='pdf',notice='PDF原典の画像です。強調枠は原典画像と別に表示します。文字のないスキャンPDFも手動で範囲指定できます。')
    if before!=hashlib.sha256(source.read_bytes()).hexdigest():raise ValueError('原典ハッシュが変化したため表示を停止しました。')
    result['sourceHash']=before
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('out',type=Path);p.add_argument('--mode',choices=['info','render'],default='render');p.add_argument('--sheet',type=int,default=1);p.add_argument('--row',type=int,default=1);p.add_argument('--page',type=int,default=1)
    a=p.parse_args();a.out.mkdir(parents=True,exist_ok=True)
    try:
        data=run(a.source.resolve(),a.out.resolve(),a.mode,a.sheet,a.row,a.page)
        (a.out/(a.mode+'.json')).write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    except Exception as e:
        message=str(e) if isinstance(e,ValueError) else '原典の解析または描画に失敗しました。原典をダウンロードして確認してください。'
        (a.out/'error.json').write_text(json.dumps({'error':message},ensure_ascii=False),encoding='utf-8')
        raise SystemExit(1)
