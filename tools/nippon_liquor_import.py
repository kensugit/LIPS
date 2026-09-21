"""Read four Nippon Liquor workbooks as data, preserving originals and source evidence."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import openpyxl

NL = [None, 'NL商品コード', 'JANコード', 'ブランド', '国', '地方', '商品名', '容量ml', 'VT', '色', 'CS\n入数', '希望小売\n価格(円)', '出荷可能\n本数', '有機\n表示', '備考']
HEADERS = {
    'retail': NL,
    'shipping': NL[:12] + ['納価（円）'] + NL[12:],
    'bordeaux': ['No.', '日本リカー\n商品コード', 'JANコード', '生産地', '商品名', '色', '年', '容量', '入数', '小売価格\n（税別）', '在庫本数'],
    'jadot': ['No.', '日本リカー\n商品コード', 'JANコード', '生産者', '所有形態', '商品名', '色', '年', '容量', '入数', '小売価格\n（税別）', '在庫本数'],
}
LEGEND = {'retail': '〇は300本以上。正確な本数は未確定。', 'shipping': '〇は300本以上。正確な本数は未確定。',
          'bordeaux': '◎は50ケース以上、○・◯は20～50ケース、20ケース未満は実数（本）。',
          'jadot': '◎は50ケース以上、○・◯は20～50ケース、20ケース未満は実数（本）。'}
ORDER = ['bordeaux', 'jadot', 'retail', 'shipping']

def text(v):
    return '' if v is None else str(v)

def integer(v):
    if isinstance(v, bool) or not re.fullmatch(r'\d+(?:\.0+)?', text(v)):
        raise ValueError(f'Invalid nonnegative integer: {v!r}')
    return int(float(v))

def inventory(v, kind):
    symbols = ('〇',) if kind in ('retail', 'shipping') else ('○', '◯', '◎')
    if v in symbols: return dict(Quantity=None, Status=1, RawValue=text(v))
    if v in (None, ''): return dict(Quantity=None, Status=0, RawValue=text(v))
    qty = integer(v)
    return dict(Quantity=qty, Status=1 if qty else 3, RawValue=text(v))

def read_source(path, kind):
    book = openpyxl.load_workbook(path, data_only=True, keep_links=False)
    try:
        if len(book.worksheets) != 1: raise ValueError('Unexpected sheets')
        sheet = book.active
        header_row = 4 if kind in ('retail', 'shipping') else 7 if kind == 'bordeaux' else 9
        if [c.value for c in sheet[header_row]] != HEADERS[kind]: raise ValueError(f'Unexpected header: {path}')
        prefix = ' '.join(text(v) for row in sheet.iter_rows(max_row=header_row-1, values_only=True) for v in row)
        match = re.search(r'(20\d{2})年\s*(\d{1,2})月\s*(\d{1,2})', prefix)
        if not match: raise ValueError('Missing source date')
        observed = f'{match[1]}-{int(match[2]):02}-{int(match[3]):02}'
        if kind in ('retail', 'shipping') and ('価格は全て税別' not in prefix or '300本以上' not in prefix):
            raise ValueError('Price/stock legend changed')
        if kind in ('bordeaux', 'jadot') and ('50ケース以上' not in prefix or '20ケース未満' not in prefix):
            raise ValueError('Stock legend changed')
        rows = {}
        for i, cells in enumerate(sheet.values, 1):
            if i <= header_row or not any(v is not None for v in cells): continue
            if not isinstance(cells[0], (int, float)):
                if any(v is not None for v in cells[1:]): raise ValueError(f'Unrecognized row: {i}')
                continue  # section titles and footer notes
            integer(cells[0])
            code = cells[1]
            if not re.fullmatch('[A-Z0-9]+', text(code)) or code in rows: raise ValueError('Invalid/duplicate SKU')
            rows[code] = dict(row=i, cells=list(cells), sheet=sheet.title)
        if not rows: raise ValueError('No product rows')
        return dict(path=Path(path), date=observed, rows=rows, sha=hashlib.sha256(Path(path).read_bytes()).hexdigest())
    finally:
        book.close()

def raw_record(source, kind, item):
    return dict(file=source['path'].name, sha256=source['sha'], sheet=item['sheet'], row=item['row'],
                cells=dict(zip([h or 'No.' for h in HEADERS[kind]], item['cells'], strict=True)), legend=LEGEND[kind])

def extract(paths):
    sources = {k: read_source(paths[k], k) for k in ORDER}
    if len({s['date'] for s in sources.values()}) != 1: raise ValueError('Source dates differ')
    master = sources['retail']['rows']
    shipping = sources['shipping']['rows']
    if master.keys() != shipping.keys(): raise ValueError('NL SKU sets differ')
    for code, item in master.items():
        sc = shipping[code]['cells']
        if item['cells'] != sc[:12] + sc[13:]: raise ValueError(f'NL source disagreement: {code}')
        integer(sc[12])  # preserve the cached supplied shipping price; never compute from a rate
    for kind in ('bordeaux', 'jadot'):
        for code, item in sources[kind]['rows'].items():
            if code not in master: raise ValueError('Supplement SKU missing from NL')
            c = item['cells']; m = master[code]['cells']
            if (text(c[2]), c[-4], integer(c[-3]), c[-2]) != (text(m[2]), m[7], integer(m[10]), m[11]):
                raise ValueError(f'Supplement identity/price differs: {code}')
            if c[-5] != m[8] and not (c[-5] == 0 and m[8] == '-'):
                raise ValueError(f'Supplement vintage differs: {code}')
            if isinstance(c[-1], (int, float)) and isinstance(m[12], (int, float)) and c[-1] != m[12]:
                raise ValueError(f'Supplement numeric stock differs: {code}')
    bundles = {}
    for kind in ORDER:
        source = sources[kind]; rows = []
        for code, item in source['rows'].items():
            c = item['cells']; m = master[code]['cells']
            nl = kind in ('retail', 'shipping')
            vintage = m[8] if nl else c[-5]
            volume = m[7] if nl else c[-4]
            if not re.fullmatch(r'\d+ML', text(volume)): raise ValueError('Invalid volume')
            record = raw_record(source, kind, item)
            raw = dict(record['cells'])
            raw.update(原本ファイル=record['file'], 原本SHA256=record['sha256'], シート=record['sheet'], 行=record['row'], 在庫凡例=record['legend'])
            raw['価格区分'] = '税別希望小売価格。納価は別項目に保存し、希望小売価格へ転用しない。'
            raw['現在値の採用方針'] = '同日資料はNL共通在庫表を優先。ブランド別資料は原表・履歴として保存。'
            raw['関連原表'] = json.dumps([raw_record(sources[k], k, sources[k]['rows'][code]) for k in ORDER if k != kind and code in sources[k]['rows']], ensure_ascii=False)
            if not nl: raw['国・地方・ブランドの補完元'] = json.dumps(raw_record(sources['retail'], 'retail', master[code]), ensure_ascii=False)
            name = m[6] if nl else c[-7]
            producer = c[3] if kind == 'jadot' else m[3]
            aliases = ' '.join(text(sources[k]['rows'][code]['cells'][-7]) for k in ('bordeaux', 'jadot') if code in sources[k]['rows'])
            rows.append(dict(SupplierProductCode=code, OriginalSupplierProductCode=code,
                ProducerNameJa=text(producer), ProducerNameEn='', ProductNameJa=text(name), ProductNameEn='',
                Vintage=int(vintage) if re.fullmatch(r'(19|20)\d{2}', text(vintage)) else None, VintageRaw=text(vintage),
                VolumeMl=int(volume[:-2]), CaseSize=integer(m[10] if nl else c[-3]),
                ReferenceRetailPrice=integer(m[11] if nl else c[-2]), PriceRaw=text(m[11] if nl else c[-2]), TaxIncluded=False,
                Inventory=inventory(m[12] if nl else c[-1], kind), ProductType=text(m[9] if nl else c[-6]),
                Country=text(m[4]), Region=text(c[3] if kind == 'bordeaux' else m[5]),
                JanCodes=[text(c[2])] if c[2] is not None else [], Grapes='', AlcoholPercent=None,
                AppellationJa='', AppellationEn='', OrganicCategory='有機農産物加工酒類表示あり' if m[13] == '●' else '',
                OrganicCertification='', PriceNote='税別希望小売価格。'+LEGEND[kind], Closure='', LegacySource='',
                SourceRow=item['row'], SourceSheet=item['sheet'], RawCellsJson=json.dumps(raw, ensure_ascii=False),
                Description='日本リカー '+text(m[14])+' '+aliases))
        bundles[kind] = dict(SchemaVersion=1, SourceFileName=source['path'].name, SourceSha256=source['sha'],
            ObservedAt=source['date'], SupplierCode='NIPPON_LIQUOR', SupplierName='日本リカー', ParserVersion='nippon-liquor-xlsx-1', Rows=rows)
    return bundles

def main():
    p = argparse.ArgumentParser(description=__doc__)
    for k in ORDER: p.add_argument('--'+k, type=Path, required=True)
    p.add_argument('--output', type=Path, default=Path('catalogs/nippon-liquor'))
    args = p.parse_args(); paths = {k: getattr(args, k) for k in ORDER}; bundles = extract(paths)
    args.output.mkdir(parents=True, exist_ok=True)
    for kind, bundle in bundles.items():
        dest = args.output / paths[kind].name
        data = paths[kind].read_bytes()
        if dest.exists() and dest.read_bytes() != data: raise ValueError('Existing original differs; use a new output folder')
        target = args.output / (kind+'.catalog.json')
        if target.exists() and json.loads(target.read_text('utf-8'))['SourceSha256'] != bundle['SourceSha256']:
            raise ValueError('Existing bundle differs; use a new output folder')
        dest.write_bytes(data)
        target.write_text(json.dumps(bundle, ensure_ascii=False, indent=2)+'\n', 'utf-8')
        (args.output / (kind+'.txt')).write_text('\n'.join(f"--- {r['SourceSheet']} row {r['SourceRow']} ---\n{r['RawCellsJson']}" for r in bundle['Rows']), 'utf-8')
    summary = dict(products=len(bundles['shipping']['Rows']), sourceRows={k: len(v['Rows']) for k,v in bundles.items()},
        hashes={k:v['SourceSha256'] for k,v in bundles.items()}, observedAt=bundles['shipping']['ObservedAt'],
        inventory=dict(Counter(r['Inventory']['Status'] for r in bundles['shipping']['Rows'])))
    (args.output/'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), 'utf-8')
    print(json.dumps(summary, ensure_ascii=False))

if __name__ == '__main__': main()
