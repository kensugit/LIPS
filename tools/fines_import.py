"""Read Fines XLS as data only, preserve source and cell evidence, export catalog bundle."""
from __future__ import annotations
import argparse
from collections import Counter
from datetime import date
import hashlib
import html
import json
from pathlib import Path
import re
import xlrd

HEADERS = ['新着', '国', 'アペラシオン', '商品コード', '製品名', 'ドメーヌ名', 'VIN', '容量', '色', '入数', '納入価格', '参考上代', 'JANコード', '在庫ｹｰｽ', '在庫本', '原語名', '製品コメント']
VERSION = 'fines-xls-1'

def integer(value):
    if not re.fullmatch(r'\d+', value):
        raise ValueError(f'Expected nonnegative integer: {value!r}')
    return int(value)

def map_row(values, sheet, row_number):
    raw = dict(zip(HEADERS, values, strict=True))
    c = {k: v.strip() for k, v in raw.items()}
    if not c['商品コード'] or not c['製品名']:
        raise ValueError('Missing product identity')
    pack = integer(c['入数'])
    if pack <= 0: raise ValueError('Invalid case size')
    volume = re.fullmatch(r'(\d+)ML', c['容量'])
    if not volume or int(volume[1]) <= 0: raise ValueError('Invalid volume')
    cases, bottles = c['在庫ｹｰｽ'], c['在庫本']
    note = '参考上代を登録。原本に税込・税別の明記なし（税区分要確認）。納入価格は原表情報に保持。'
    if cases == '100C以上' and bottles == '':
        quantity, status = None, 1
        stock_note = f'100ケース以上（{100 * pack}本以上、正確な本数は未確定）'
    else:
        full, remainder = integer(cases), integer(bottles)
        if remainder >= pack: raise ValueError('Bottle remainder exceeds case size')
        quantity = full * pack + remainder
        status = 1 if quantity else 3
        stock_note = '在庫本数＝在庫ケース×入数＋在庫本（端数）'
    price = integer(c['参考上代'])
    integer(c['納入価格'])
    vintage = c['VIN']
    jan = c['JANコード']
    if jan and not re.fullmatch(r'\d{12,13}', jan): raise ValueError('Invalid JAN/UPC')
    raw['在庫換算方法'] = stock_note
    raw['価格区分'] = '参考上代。税込・税別の明記なし。TaxIncluded=falseは税込と確認できないため。'
    return dict(SupplierProductCode=c['商品コード'], OriginalSupplierProductCode=c['商品コード'],
        ProducerNameJa=c['ドメーヌ名'], ProducerNameEn='', ProductNameJa=c['製品名'], ProductNameEn=c['原語名'],
        Vintage=int(vintage) if re.fullmatch(r'\d{4}', vintage) else None, VintageRaw=vintage,
        VolumeMl=int(volume[1]), CaseSize=pack, ReferenceRetailPrice=price, PriceRaw=c['参考上代'], TaxIncluded=False,
        Inventory=dict(Quantity=quantity, Status=status, RawValue=f'{cases}ケース / {bottles}本'),
        ProductType=c['色'], Country=c['国'], Region=c['アペラシオン'], JanCodes=[jan] if jan else [],
        Grapes='', AlcoholPercent=None, AppellationJa=c['アペラシオン'], AppellationEn='',
        OrganicCategory='', OrganicCertification='', PriceNote=note + stock_note,
        Closure='', LegacySource='', SourceRow=row_number, SourceSheet=sheet,
        RawCellsJson=json.dumps(raw, ensure_ascii=False),
        Description='ファインズ ' + html.unescape(re.sub(r'<[^>]*>', ' ', c['製品コメント'])) + ' ' + note)

def extract(source):
    book = xlrd.open_workbook(source)
    if book.nsheets != 1: raise ValueError('Unexpected sheets')
    sheet = book.sheet_by_index(0)
    if sheet.ncols != 17 or [str(v).strip() for v in sheet.row_values(3)] != HEADERS:
        raise ValueError('Header mismatch')
    if sheet.cell_value(0, 0) != 'ファインズ\u3000ワインリスト': raise ValueError('Wrong supplier')
    stamp = re.fullmatch(r'(\d{4})/(\d{2})/(\d{2})作成', sheet.cell_value(0, 12))
    if not stamp: raise ValueError('Missing source date')
    observed = date(*map(int, stamp.groups())).isoformat()
    rows = []
    for i in range(4, sheet.nrows):
        if any(sheet.cell_type(i, j) != xlrd.XL_CELL_TEXT and sheet.cell_type(i, j) != xlrd.XL_CELL_EMPTY for j in range(17)):
            raise ValueError(f'Unexpected cell type at row {i + 1}')
        rows.append(map_row(sheet.row_values(i), sheet.name, i + 1))
    if not rows or len({r['SupplierProductCode'] for r in rows}) != len(rows): raise ValueError('Duplicate/empty products')
    content = Path(source).read_bytes()
    return dict(SchemaVersion=1, SourceFileName=Path(source).name, SourceSha256=hashlib.sha256(content).hexdigest(),
        ObservedAt=observed, SupplierCode='FINES', SupplierName='ファインズ', ParserVersion=VERSION, Rows=rows)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('source', type=Path)
    p.add_argument('--output', type=Path, default=Path('catalogs'))
    args = p.parse_args()
    bundle = extract(args.source)
    args.output.mkdir(parents=True, exist_ok=True)
    original = args.output / args.source.name
    if original.exists() and original.read_bytes() != args.source.read_bytes(): raise ValueError('Existing original differs')
    original.write_bytes(args.source.read_bytes())
    stem = 'ファインズ.在庫表.' + bundle['ObservedAt'].replace('-', '')
    destination = args.output / (stem + '.catalog.json')
    destination.write_text(json.dumps(bundle, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (args.output / (stem + '.txt')).write_text('\n'.join(
        f"--- {r['SourceSheet']} row {r['SourceRow']} ---\n{r['RawCellsJson']}" for r in bundle['Rows']), encoding='utf-8')
    summary = dict(products=len(bundle['Rows']), sourceHash=bundle['SourceSha256'], observedAt=bundle['ObservedAt'],
        inventory=dict(Counter(r['Inventory']['Status'] for r in bundle['Rows'])),
        unknownQuantity=sum(r['Inventory']['Quantity'] is None for r in bundle['Rows']),
        volumes=dict(Counter(r['VolumeMl'] for r in bundle['Rows'])))
    (args.output / (stem + '.meta.json')).write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False))

if __name__ == '__main__': main()
