"""Diony 2026 Vol.9 catalog and newsletter; source text is data only."""
import io
import json
import re
from collections import Counter
import pdfplumber

NOTE = '2026 Vol.9（2026年9月版）。日未記載のため基準日は月初。参考小売の税区分は未記載・要確認。在庫無表記は不明。'


def base(sku, page, number, raw):
    return dict(SupplierProductCode=sku, OriginalSupplierProductCode=sku,
        ProductNameJa='', ProductNameEn='', ProducerNameJa='', ProducerNameEn='',
        Country='', Region='', ProductType='', Vintage=None, VintageRaw='', VolumeMl=0, CaseSize=0,
        ReferenceRetailPrice=None, PriceRaw='', TaxIncluded=False,
        Inventory=dict(Quantity=None, Status=0, RawValue='記載なし'), JanCodes=[], Grapes='',
        AlcoholPercent=None, AppellationJa='', AppellationEn='', OrganicCategory='', OrganicCertification='',
        PriceNote=NOTE, Closure='', LegacySource='', SourceRow=number, SourceSheet=f'PDF p.{page}',
        RawCellsJson=json.dumps(raw, ensure_ascii=False), Description='ディオニー Diony ' + NOTE)


def diony(data):
    rows=[]
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        if len(pdf.pages)!=60 or '2026 Vol.9' not in pdf.pages[0].extract_text() or 'ディオニー' not in pdf.pages[-1].extract_text():
            raise ValueError('Unsupported Diony catalog identity')
        for num,p in enumerate(pdf.pages[2:59],3):
            anchors=[w for w in p.extract_words() if re.fullmatch(r'\d{5}',w['text']) and any(abs(w['x0']-x)<1 for x in [35.04,215.28,395.52])]
            if len(anchors)!=p.extract_text().count('参考小売/'):
                raise ValueError(f'Product coverage mismatch p.{num}')
            for number,w in enumerate(anchors,1):
                x=27+round((w['x0']-35.04)/180.24)*180.24
                y=w['top']-2
                chars=[c for c in p.chars if x <= (c['x0']+c['x1'])/2 < x+180.24 and y <= (c['top']+c['bottom'])/2 < y+190.92]
                def cell(left,top,right,bottom):
                    cs=[c for c in chars if x+left <= (c['x0']+c['x1'])/2 < x+right and y+top <= (c['top']+c['bottom'])/2 < y+bottom]
                    return pdfplumber.utils.extract_text(cs,x_tolerance=1).strip()
                text=cell(0,0,180.24,190.92)
                vintage=cell(0,13,34,24).replace('VIN','').replace(' ','')
                title=cell(34,25,180.24,50).splitlines()
                if len(title)<2: raise ValueError(f'Missing name {w["text"]}: {title}')
                price=cell(34,60,180.24,74)
                m=re.fullmatch(r'参考小売/\s*([\d,]+)\s+JAN/\s*(\d{13}|-)?',price)
                if not m: raise ValueError(f'Price/JAN invalid {w["text"]}: {price}')
                spec=cell(34,73,180.24,87).replace('\n','').replace(' ','')
                volume=cell(50,73,76,87).replace('\n','').replace(' ','').replace('/','')
                case=re.search(r'×(\d+)(?:×(\d+))?',spec)
                if not re.fullmatch(r'\d+ml',volume) or not case: raise ValueError(f'Volume/case invalid {w["text"]}: {volume}, {spec}')
                separators=[c for c in chars if 70 <= c['top']-y <= 82 and c['x0']-x > 90]
                slash=next(c for c in separators if c['text']=='/')
                dot=next((c for c in separators if c['text']=='・' and c['x0']>slash['x1']),None)
                kind=cell(slash['x0']-x-1,70,dot['x0']-x if dot else 180.24,87).replace('\n','').replace(' ','').replace('/','')
                region=cell(34,0,180.24,13).split('酸化')[0].replace('準定番','').strip()
                raw={'資料年月':'2026-09','商品コード':w['text'],'PDFページ':num,'商品カード原文':text,'税区分':'未記載・要確認','資料版':'2026 Vol.9'}
                r=base(w['text'],num,number,raw)
                r.update(ProductNameJa=' '.join(title[1:]), ProducerNameJa=title[0], ProductNameEn=cell(34,13,180.24,25),
                    Vintage=int(vintage) if re.fullmatch(r'(19|20)\d{2}',vintage) else None, VintageRaw=vintage,
                    VolumeMl=int(volume[:-2]),CaseSize=int(case[1]) * (int(case[2]) if case[2] else 1),ReferenceRetailPrice=int(m[1].replace(',','')),PriceRaw=m[1],
                    JanCodes=[m[2]] if m[2] and m[2]!='-' else [],Region=region,ProductType=kind,
                    Country='フランス' if num<=30 else region.split('／')[0] if '／' in region else '',
                    AppellationJa=cell(34,49,180.24,61).replace('呼称/','').strip(),
                    Grapes=cell(34,111,180.24,134).replace('\n',' '),
                    OrganicCategory=cell(53,87,100,99).replace('\n',' '),
                    OrganicCertification=cell(122,87,180.24,99).replace('\n',' '))
                stock=cell(34,99,180.24,112)
                quantity=re.search(r'在庫\s*(\d+)\s*本',stock)
                if 'SOLDOUT' in stock.replace(' ',''):
                    r['Inventory']=dict(Quantity=0,Status=3,RawValue='SOLD OUT')
                elif quantity:
                    q=int(quantity[1]);r['Inventory']=dict(Quantity=q,Status=1 if q else 3,RawValue=quantity[0])
                if case[2]: r['PriceNote']+=' 入数は原表の'+case[0]+'を乗算。'
                elif_stock = re.search(r'在庫\s*\d+\s*ケース',stock)
                if elif_stock and not quantity:
                    r['Inventory']=dict(Quantity=None,Status=1,RawValue=elif_stock[0])
                    r['PriceNote']+=' ケース在庫は原表を保持し、本数へ換算しない。'
                r['Description']+=' '+text
                rows.append(r)
    if len(rows)!=516 or len({r['SupplierProductCode'] for r in rows})!=len(rows): raise ValueError('Catalog SKU count changed')
    return rows,[NOTE]


def diony_news(data):
    rows=[]
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        if len(pdf.pages)!=2 or 'Diony' not in pdf.pages[0].extract_text(): raise ValueError('Unsupported newsletter')
        for num,p in enumerate(pdf.pages,1):
            for t in p.extract_tables():
                for i,c in enumerate(t):
                    if not c or not re.fullmatch(r'\d{5}',c[0] or ''): continue
                    if len(c)!=7 or not re.fullmatch(r'¥[\d,]+',c[6]): raise ValueError('Newsletter table changed')
                    raw=dict(zip(['商品コード','商品名','VIN','呼称','種類','品種','参考小売'],c))
                    raw.update(資料年月='2026-09',PDFページ=num,原本ページ本文=p.extract_text(),税区分='未記載・要確認')
                    r=base(c[0],num,len(rows)+1,raw)
                    name=c[1].replace('\n',' ')
                    jp=re.search(r'[\u3040-\u30ff\u3400-\u9fff]',name)
                    r.update(ProductNameJa=name[jp.start():] if jp else name,ProductNameEn=name[:jp.start()].strip() if jp else name,
                        Vintage=2000+int(c[2]) if re.fullmatch(r'\d{2}',c[2]) else None,VintageRaw=c[2],
                        ReferenceRetailPrice=int(c[6][1:].replace(',','')),PriceRaw=c[6],AppellationJa=c[3].replace('\n',' '),
                        ProductType=c[4].replace('\n',''),Grapes=c[5].replace('\n',' '))
                    vol=re.search(r'(\d+)ml',name)
                    if vol:r['VolumeMl']=int(vol[1])
                    r['PriceNote']+=' 新着資料。入荷本数は現在庫とみなさない。容量未記載は未設定。'
                    r['Description']+=' '+name+' '+(t[i+1][1] if i+1<len(t) and t[i+1][0] is None else '')
                    rows.append(r)
    if len(rows)!=13 or len({r['SupplierProductCode'] for r in rows})!=13:raise ValueError('Newsletter coverage changed')
    return rows,[NOTE,'13商品は総合カタログと重複。同一仕入先SKUとして登録し、新着資料→総合カタログの順で登録する。']
