"""Extract Terravert XLSX as data; reconcile supplementary sheets without duplicate products."""
import argparse
from collections import Counter
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import openpyxl

HEADERS = ['商品記号','地域','造り手','ワイン','VIN','色','サイズ','参考上代','ケースで\nお得！','新入港','在庫状況',None,'メモ\n特徴・評価等','セパージュ','テクニカルデータ','入数','JAN']
SHEETS = ['目次','ケース条件11対1','新入港＆入港予定','仏全リスト','伊全リスト','スペイン・ジョージア・日本全リスト']
TAX_NOTE = '参考上代を登録。原本に税込・税別の明記なし（税区分要確認）。'

def text(v):
    return '' if v is None else str(v)

def number(v, optional=False):
    if optional and v in (None, ''): return None
    if isinstance(v, bool) or not re.fullmatch(r'\d+(?:\.0+)?', text(v)): raise ValueError(f'Invalid number: {v!r}')
    return int(float(v))

def inventory(stock, note):
    if stock in ('〇','○'): return dict(Quantity=None, Status=1, RawValue=text(stock))
    if stock == '完売': return dict(Quantity=0, Status=3, RawValue=stock)
    if stock in ('ー','-',None,''):
        return dict(Quantity=None, Status=4 if '入港予定' in text(note) else 0, RawValue=text(stock))
    qty=number(stock)
    return dict(Quantity=qty, Status=3 if qty==0 else 2 if '完売間近' in text(note) else 1, RawValue=text(stock))

def bilingual(value):
    parts=text(value).strip().split('\n',1)
    return (parts[1].strip(),parts[0].strip()) if len(parts)==2 else (parts[0],'')

def country(sheet, region):
    if sheet=='仏全リスト': return 'フランス'
    if region=='スロヴェニア': return 'スロヴェニア'
    if sheet=='伊全リスト': return 'イタリア'
    if region=='長野': return '日本'
    if region in ('イメレティ','カルトリ','カヘティ'): return 'ジョージア'
    if region in ('ナバーラ','リベイロ','シェリー','ガリシア'): return 'スペイン'
    raise ValueError(f'Unmapped country: {sheet}/{region}')

def map_row(values, sheet, row):
    raw=dict(zip([h or '在庫補足' for h in HEADERS],values,strict=True))
    raw['価格区分']=TAX_NOTE+' TaxIncluded=falseは税込と確認できないため。'
    raw['在庫凡例']='○・〇は100本以上。正確な本数は未確定。入港予定のーは在庫0ではない。'
    raw['ケース条件説明']='By the Glass表記：12本購入で内1本無償。参考上代に割引を適用しない。'
    producer_ja,producer_en=bilingual(values[2]); name_ja,name_en=bilingual(values[3])
    if not producer_ja or not name_ja: raise ValueError('Missing identity')
    volume,pack=number(values[6]),number(values[15])
    if not volume or not pack: raise ValueError('Zero volume/case size')
    vintage=text(values[4]); match=re.fullmatch(r'\[?((?:19|20)\d{2})\]?',vintage)
    jan=text(number(values[16])) if values[16] not in (None,'') else ''
    if jan and not re.fullmatch(r'\d{13}',jan): raise ValueError('Invalid JAN')
    return dict(SupplierProductCode=text(values[0]),OriginalSupplierProductCode=text(values[0]),
        ProducerNameJa=producer_ja,ProducerNameEn=producer_en,ProductNameJa=name_ja,ProductNameEn=name_en,
        Vintage=int(match[1]) if match else None,VintageRaw=vintage,VolumeMl=volume,CaseSize=pack,
        ReferenceRetailPrice=number(values[7],True),PriceRaw=text(values[7]),TaxIncluded=False,
        Inventory=inventory(values[10],values[11]),ProductType=text(values[5]),Country=country(sheet,values[1]),
        Region=text(values[1]).replace('\n',''),JanCodes=[jan] if jan else [],Grapes=text(values[13]),
        AlcoholPercent=None,AppellationJa='',AppellationEn='',OrganicCategory='',OrganicCertification='',
        PriceNote=TAX_NOTE+' '+text(values[11]),Closure='',LegacySource='',SourceRow=row,SourceSheet=sheet,
        RawCellsJson=json.dumps(raw,ensure_ascii=False),Description='テラヴェール '+text(values[12])+' '+text(values[14])+' '+TAX_NOTE)

def extract(source):
    book=openpyxl.load_workbook(source,data_only=True,keep_links=False)
    if book.sheetnames!=SHEETS: raise ValueError('Unexpected sheets')
    observed=book['目次']['C1'].value
    if not isinstance(observed,datetime): raise ValueError('Missing source date')
    rows=[]; original={}
    for sheet in list(book)[3:]:
        if sheet['P1'].value!=observed or [c.value for c in sheet[3]]!=HEADERS: raise ValueError('Date/header mismatch')
        for i,values in enumerate(sheet.values,1):
            if i<=4: continue
            values=list(values)
            if not any(v not in (None,'') for v in values): continue
            if not re.fullmatch('[A-Z0-9]+',text(values[0])) or not values[3]: raise ValueError(f'Unrecognized product row {sheet.title}:{i}')
            if values[0] in original: raise ValueError('Duplicate main SKU')
            original[values[0]]=values
            rows.append(map_row(values,sheet.title,i))
    by_code={r['SupplierProductCode']:r for r in rows}
    supplemental=Counter()
    for sheet in list(book)[1:3]:
        for i,values in enumerate(sheet.values,1):
            if not re.fullmatch('[A-Z0-9]+',text(values[0])) or not values[3]: continue
            values=list(values[:17]); code=values[0]
            if code not in original or any(values[j]!=original[code][j] for j in [3,4,6,7,10]): raise ValueError(f'Supplement mismatch {sheet.title}:{i}')
            raw=json.loads(by_code[code]['RawCellsJson'])
            raw.setdefault('再掲シート',[]).append(dict(sheet=sheet.title,row=i,cells=values))
            by_code[code]['RawCellsJson']=json.dumps(raw,ensure_ascii=False)
            supplemental[sheet.title]+=1
    content=Path(source).read_bytes()
    return dict(SchemaVersion=1,SourceFileName=Path(source).name,SourceSha256=hashlib.sha256(content).hexdigest(),
        ObservedAt=observed.date().isoformat(),SupplierCode='TERRAVERT',SupplierName='テラヴェール',ParserVersion='terravert-xlsx-1',Rows=rows),dict(supplemental)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=Path);p.add_argument('--output',type=Path,default=Path('catalogs'));args=p.parse_args()
    bundle,supplemental=extract(args.source);args.output.mkdir(parents=True,exist_ok=True)
    dest=args.output/args.source.name
    content=args.source.read_bytes()
    if dest.exists() and dest.read_bytes()!=content: raise ValueError('Existing original differs')
    dest.write_bytes(content)
    stem='テラヴェール.価格表.'+bundle['ObservedAt'].replace('-','')
    output=args.output/(stem+'.catalog.json')
    if output.exists() and json.loads(output.read_text(encoding='utf-8'))['SourceSha256']!=bundle['SourceSha256']: raise ValueError('Existing bundle source differs')
    output.write_text(json.dumps(bundle,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    summary=dict(products=len(bundle['Rows']),sourceHash=bundle['SourceSha256'],observedAt=bundle['ObservedAt'],supplemental=supplemental,
        inventory=dict(Counter(r['Inventory']['Status'] for r in bundle['Rows'])),countries=dict(Counter(r['Country'] for r in bundle['Rows'])),
        missingPrice=sum(r['ReferenceRetailPrice'] is None for r in bundle['Rows']))
    (args.output/(stem+'.meta.json')).write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    (args.output/(stem+'.txt')).write_text('\n'.join(f"--- {r['SourceSheet']} row {r['SourceRow']} ---\n{r['RawCellsJson']}" for r in bundle['Rows']),encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False))

if __name__=='__main__': main()
