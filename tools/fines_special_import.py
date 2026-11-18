"""Fines special purchase prices: approved extra-list precedence, retail preserved."""
import argparse
from copy import deepcopy
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import openpyxl

VERSION = 'fines-special-1'
MAIN_HEADERS = ['商品コード','商品名','生産者','ヴィンテージ','通常納品価格','特価','割引率','参考上代','在庫数9/24','希望売価\n(最下限)']
EXTRA_HEADERS = ['商品コード','商品名','ヴィンテージ','色','容量','入り数','本数','納品価格','特価価格','参考上代']

def price(value):
    if isinstance(value, bool) or not isinstance(value, (int,float)):
        raise ValueError('Missing or nonnumeric purchase price')
    d=Decimal(str(value))
    if not d.is_finite() or d<=0: raise ValueError('Invalid purchase price')
    # Remove only sub-1e-8 numeric representation noise. Never round a half-yen.
    clean=d.quantize(Decimal('0.00000001'))
    return int(clean) if clean==clean.to_integral() else float(clean)

def read_rows(path, extra=False):
    book=openpyxl.load_workbook(path,data_only=True)
    sheet=book['リスト' if extra else '全リスト']
    headers=EXTRA_HEADERS if extra else MAIN_HEADERS
    if [sheet.cell(2,j).value for j in range(2,12)]!=headers: raise ValueError('Header changed')
    result=[]
    for i in range(3 if extra else 4,sheet.max_row+1):
        cells=[sheet.cell(i,j).value for j in range(2,12)]
        raw=dict(zip(headers,cells))
        sku=cells[0]
        if not isinstance(sku,str) or not sku.strip(): raise ValueError('Missing SKU')
        amount=price(raw['特価価格' if extra else '特価'])
        quantity=raw['本数' if extra else '在庫数9/24']
        if isinstance(quantity,bool) or not isinstance(quantity,int) or quantity<0: raise ValueError('Invalid stock')
        result.append(dict(sku=sku,price=amount,quantity=quantity,raw=raw,sheet=sheet.title,row=i,
            vintage=raw['ヴィンテージ']))
    book.close()
    if len({r['sku'] for r in result})!=len(result): raise ValueError('Duplicate SKU within sheet')
    return result

def generate(main, extra, base, output):
    base_data=json.loads(base.read_text('utf-8'))
    known={r['SupplierProductCode']:r for r in base_data['Rows']}
    main_rows,extra_rows=read_rows(main),read_rows(extra,True)
    overrides={r['sku'] for r in extra_rows}
    replaced=[r for r in main_rows if r['sku'] in overrides]
    output.mkdir(parents=True,exist_ok=True)
    summary=dict(mainRows=len(main_rows),extraRows=len(extra_rows),overridden=[r['sku'] for r in replaced],
        products=len(main_rows)+len(extra_rows)-len(replaced),excludedSheets=['オープンブル'])
    for label,path,records in [('main',main,[r for r in main_rows if r['sku'] not in overrides]),('extra',extra,extra_rows)]:
        rows=[]
        for rec in records:
            row=deepcopy(known[rec['sku']])
            if str(rec['vintage']) != row['VintageRaw']: raise ValueError('Vintage mismatch: '+rec['sku'])
            original=deepcopy(rec['raw'])
            original['補完元原本']=base_data['SourceFileName']
            original['補完元SHA256']=base_data['SourceSha256']
            original['補完元行']=row['SourceRow']
            original['参考上代の扱い']='登録済み参考上代を維持'
            original['SpecialPurchasePrice']={'Version':1,'Amount':rec['price'],'Currency':'JPY','TaxIncluded':False,
                'ObservedAt':'2026-09-24','SupplierCode':'FINES'}
            other=next((r for r in replaced if r['sku']==rec['sku']),None)
            if other: original['不採用の全リスト行']={'行':other['row'],'理由':'ユーザー指定で追加特価を優先','原表':other['raw']}
            row['RawCellsJson']=json.dumps(original,ensure_ascii=False)
            row['Inventory']={'Quantity':rec['quantity'],'Status':1 if rec['quantity']>0 else 3,'RawValue':str(rec['quantity'])+'本'}
            row['SourceSheet'],row['SourceRow']=rec['sheet'],rec['row']
            row['PriceNote']='参考上代は既存値を維持。特価仕入価格は別項目。9/24特価資料の本数で通常在庫を更新。'
            row['Description']=row.get('Description','')+' ファインズ 特価'
            rows.append(row)
        bundle=dict(SchemaVersion=1,SourceFileName=path.name,SourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
            ObservedAt='2026-09-24',SupplierCode='FINES',SupplierName='ファインズ',ParserVersion=VERSION,Rows=rows,PreserveExistingRetail=True)
        (output/(label+'.catalog.json')).write_text(json.dumps(bundle,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (output/'source-audit.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    return summary

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('main','extra','base','output'):p.add_argument('--'+name,type=Path,required=True)
    a=p.parse_args();print(json.dumps(generate(a.main,a.extra,a.base,a.output),ensure_ascii=False))
