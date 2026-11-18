"""AMZ September 2026 layout adapter for the existing CatalogPdfBridge contract."""
from pathlib import Path
import argparse
import hashlib
import json
import re
from datetime import date
import openpyxl

HEADERS = ['行No.', '備考', '商品', 'ビン\nテージ', '商品名', '生産者名', '商品名(カナ)',
           '生産者名(カナ)', '内容量', '色', '地区コード', '地区コード', '地区名', '生産者コード',
           '原産国', '生産地方', '地区名', 'JAN1', 'JAN2', '卸価格', '参考上代価格']

def text(v):
    return '' if v is None else str(v).strip()

def extract(path):
    path = Path(path)
    raw = path.read_bytes()
    w = openpyxl.load_workbook(path, data_only=False, keep_links=False)
    if w.sheetnames != ['在庫表', '在庫表 (JAN)']:
        raise ValueError('Unexpected sheet layout')
    if any(c.data_type == 'f' for s in w for row in s for c in row):
        raise ValueError('Formula requires separate cached-value validation')
    sheets = []
    observed = None
    for s in w:
        match = re.fullmatch(r'株式会社\s+エイ・エム・ズィー\s+在庫リスト\((\d{4})/(\d{1,2})/(\d{1,2})付\)', text(s.cell(1,1).value))
        if not match: raise ValueError('Unknown supplier/date')
        d = date(*map(int, match.groups()))
        if observed is not None and observed != d: raise ValueError('Date mismatch')
        observed = d
        header = [c.value for c in s[2]]
        if header != HEADERS + [f'{d.month}/{d.day}\n在庫']: raise ValueError('Header changed')
        rows = {}
        for n, values in enumerate(s.values, 1):
            if n <= 2: continue
            c = list(values)
            if c[2] is None:
                if any(c[i] not in (None, '', '-') for i in range(1,22)):
                    raise ValueError(f'Unrecognized non-product row {n}')
                continue
            sku = text(c[2])
            if not re.fullmatch('[A-Z0-9]+', sku) or sku in rows: raise ValueError('Invalid/duplicate SKU')
            rows[sku] = (n,c)
        sheets.append(rows)
    if not sheets[0] or sheets[0] != sheets[1]: raise ValueError('AMZ sheets disagree')
    result = []
    for sku, (n,c) in sheets[1].items():
        stock = text(c[21])
        if not re.fullmatch(r'\d+[↑↓]',stock): raise ValueError('Unknown stock mark')
        if type(c[8]) is not int or c[8] <= 0: raise ValueError('Invalid volume')
        price = c[20]
        if text(price).upper() in ('', '-', 'OPEN', 'オープン'): price = None
        elif type(price) not in (int,float) or price < 0: raise ValueError('Invalid retail price')
        jan = list(dict.fromkeys(code for v in c[17:19] for code in re.findall(r'(?<!\d)\d{8,14}(?!\d)',text(v))))
        fallback = lambda value, other: text(other) if text(value) in ('','-') else text(value)
        vintage = text(c[3])
        evidence = {openpyxl.utils.get_column_letter(i+1)+str(n):v for i,v in enumerate(c)}
        evidence.update(原本ファイル='StockList'+observed.strftime('%Y%m%d')+'.xlsx',
                        価格区分='検索価格は参考上代（税抜）。卸価格はT列に保持。',
                        照合済シート=['在庫表','在庫表 (JAN)'], 在庫表記='↑=在庫あり、↓=少量。正確な本数は不明。')
        result.append(dict(SupplierProductCode=sku, ProducerNameJa=fallback(c[7],c[5]),
            ProducerNameEn=text(c[5]), ProductNameJa=fallback(c[6],c[4]), ProductNameEn=text(c[4]),
            Vintage=int(vintage) if re.fullmatch('[12][0-9]{3}',vintage) else None, VintageRaw=vintage,
            VolumeMl=c[8], CaseSize=0, ReferenceRetailPrice=price, PriceRaw=text(c[20]), TaxIncluded=False,
            Inventory=dict(Quantity=None,Status=1 if stock.endswith('↑') else 2,RawValue=stock),
            ProductType=text(c[9]), Country=text(c[14]), Region=text(c[15]), JanCodes=jan,
            Grapes='',AlcoholPercent=None,AppellationJa=text(c[16]),AppellationEn='',OrganicCategory='',
            OrganicCertification='',PriceNote=text(c[1]),Closure='',LegacySource='',SourceRow=n,
            SourceSheet='在庫表 (JAN)',RawCellsJson=json.dumps(evidence,ensure_ascii=False)))
    return dict(SchemaVersion=1,SourceFileName='StockList'+observed.strftime('%Y%m%d')+'.xlsx',
                SourceSha256=hashlib.sha256(raw).hexdigest(),ObservedAt=observed.isoformat(),
                SupplierCode='AMZ',SupplierName='AMZ',ParserVersion='AMZ/202609-layout-v1',Rows=result)

if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('output',type=Path)
    args=p.parse_args();bundle=extract(args.source)
    args.output.write_text(json.dumps(bundle,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(products=len(bundle['Rows']),sourceSha256=bundle['SourceSha256'])))
