"""Le Caveau 2026-09-29 source-faithful adapter for CatalogPdfBridge.

Wholesale prices remain in evidence/PriceNote; they are never reference retail.
Expected bottles remain in evidence and do not become current inventory.
"""
import argparse
from collections import Counter
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import openpyxl


SECTIONS = {
    'Bourgogne rouges': ('フランス', 'ブルゴーニュ', '赤'),
    'Bourgogne blancs': ('フランス', 'ブルゴーニュ', '白'),
    'Bordeaux rouges': ('フランス', 'ボルドー', '赤'),
    'Bordeaux blancs & Sauternes': ('フランス', 'ボルドー', '白'),
    'Rhone rouges & blancs': ('フランス', 'ローヌ', ''),
    'Jura & Alsace & Loire': ('フランス', '', ''),
    'Champagne': ('フランス', 'シャンパーニュ', '泡'),
    'California & U.S. Red': ('アメリカ', '', '赤'),
    'California & U.S. White': ('アメリカ', '', '白'),
    'Italia': ('イタリア', '', ''),
}


def extract(path):
    path = Path(path)
    w = openpyxl.load_workbook(path, data_only=False, keep_links=False)
    cached = openpyxl.load_workbook(path, data_only=True, keep_links=False)
    if w.sheetnames != ['Sheet1']:
        raise ValueError('Sheet layout changed')
    s = w.active
    if (s['A1'].value != 'Le Caveau' or s['G10'].value != '卸価格'
            or not str(s['A4'].value).startswith('価格は消費税別です。')):
        raise ValueError('Supplier/price semantics changed')
    if s['H9'].value != '=TODAY()' or cached.active['H9'].value != datetime(2026, 9, 29):
        raise ValueError('Source date changed; review cached date and filename')
    if any(c.data_type == 'f' and c.coordinate != 'H9' for row in s for c in row):
        raise ValueError('Unreviewed formula')
    rows = []
    section = None
    for n, cells in enumerate(s.values, 1):
        c = list(cells)
        if n < 9:
            continue
        if isinstance(c[0], str) and c[0].strip() in SECTIONS:
            section = c[0].strip()
            continue
        if c[1] == 'Bt' or not any(v is not None for v in c):
            continue
        if section is None or type(c[1]) is not int or c[1] <= 0:
            raise ValueError(f'Unrecognized row {n}')
        if c[0] not in ('入荷済', '10/6頃', '10/8頃'):
            raise ValueError(f'Unknown arrival {n}')
        if type(c[5]) is not int or c[5] <= 0 or type(c[6]) not in (int, float) or c[6] <= 0:
            raise ValueError(f'Invalid volume/wholesale price {n}')
        if c[2] is not None or not isinstance(c[4], str) or not c[4].strip():
            raise ValueError(f'Unexpected order/product {n}')
        vintage = str(c[3])
        if not re.fullmatch(r'[12][0-9]{3}|NV', vintage):
            raise ValueError(f'Unknown vintage {n}')
        name = c[4].strip()
        # Source has no SKU. Preserve label-condition text as part of identity.
        identity = json.dumps([name, vintage, c[5]], ensure_ascii=False)
        sku = 'LC-' + hashlib.sha256(identity.encode('utf-8')).hexdigest()[:24].upper()
        country, region, kind = SECTIONS[section]
        arrived = c[0] == '入荷済'
        evidence = {openpyxl.utils.get_column_letter(i + 1) + str(n): v for i, v in enumerate(c)}
        evidence.update(価格区分='卸価格（税別）。参考上代の記載なし。',
                        WholesalePrice=c[6], WholesaleTaxIncluded=False,
                        入荷区分=c[0], 原本本数=c[1], 原本区分=section,
                        商品識別='原本に商品コードなし。商品名・VT・容量から生成。',
                        日付='H9のTODAY式は再計算せず保存済み値2026-09-29を採用')
        note = f'卸価格（税別）: {c[6]:g}円。{c[0]}。原本本数: {c[1]}本。'
        rows.append(dict(SupplierProductCode=sku, ProducerNameJa='', ProducerNameEn='',
            ProductNameJa=name, ProductNameEn=name,
            Vintage=int(vintage) if vintage != 'NV' else None, VintageRaw=vintage,
            VolumeMl=c[5], CaseSize=0, ReferenceRetailPrice=None, PriceRaw='', TaxIncluded=False,
            Inventory=dict(Quantity=c[1] if arrived else None, Status=1 if arrived else 4,
                           RawValue=f'{c[0]} / {c[1]}本'),
            ProductType=kind, Country=country, Region=region, JanCodes=[], Grapes='',
            AlcoholPercent=None, AppellationJa='', AppellationEn='', OrganicCategory='',
            OrganicCertification='', PriceNote=note, Closure='', LegacySource='',
            SourceRow=n, SourceSheet=s.title, RawCellsJson=json.dumps(evidence, ensure_ascii=False),
            Description=f'{section}。{note} Parker欄: {c[7]}'))
    if len(rows) != 729 or len({r['SupplierProductCode'] for r in rows}) != 729:
        raise ValueError('Row count/identity mismatch')
    if Counter(r['Inventory']['Status'] for r in rows) != {1: 660, 4: 69}:
        raise ValueError('Arrival counts changed')
    return dict(SchemaVersion=1, SourceFileName=path.name,
                SourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(), ObservedAt='2026-09-29',
                SupplierCode='LE_CAVEAU', SupplierName='ルカヴォー',
                ParserVersion='LE_CAVEAU/20260929-v1', Rows=rows)


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('source', type=Path)
    p.add_argument('output', type=Path)
    args = p.parse_args()
    bundle = extract(args.source)
    args.output.write_text(json.dumps(bundle, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(products=len(bundle['Rows']), sourceSha256=bundle['SourceSha256'])))
