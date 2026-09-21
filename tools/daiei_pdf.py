"""Daiei July 2026 catalogs: preserve SKU rows and their colored bilingual headings."""
import io
import json
import re
from collections import Counter

import pdfplumber

JP = re.compile(r'[\u3040-\u30ff\u3400-\u9fff]')
PRICE = re.compile(r'(?P<volume>\d+(?:\.\d+)?)(?P<unit>ml|L)(?:\([^)]*\))?/(?P<case>\d+)(?P<pack>（[^）]*）)?\s+(?P<sku>[A-Z0-9][A-Z0-9.\-]*)\s+(?P<price>¥[\d,]+|OPEN)\s*$')
NOTE = '2026年7月更新。日付未記載のため基準日は月初。在庫記載なし。掲載価格の用途・税区分は資料に明記がなく要確認。'
COLORS = {(1., 0., 0.): '赤', (0., 0., .501961): '白', (0., .2, 0.): 'スパークリング',
          (.501961, 0., .501961): '', (.6, .2, 0.): 'ビール', (1., 0., 1.): 'ロゼ',
          (.780392, .372549, .035294): 'オレンジ'}


def cell(line, lo, hi):
    from pdfplumber.utils import extract_text
    return extract_text([c for c in line['chars'] if lo <= c['x0'] < hi], x_tolerance=2).strip()


def header_cells(line):
    words = pdfplumber.utils.extract_words(line['chars'])
    groups = [[]]
    last = None
    for word in words:
        if last is not None and word['x0'] - last > 15:
            groups.append([])
        groups[-1].append(word['text'])
        last = word['x1']
    if len(groups) not in (2, 3):
        raise ValueError(f'Ambiguous producer heading: {line["text"]}')
    return [' '.join(g) for g in groups]


def daiei(data):
    rows, warnings = [], [NOTE]
    name_ja = name_en = producer_ja = producer_en = region = kind = ''
    country = ''
    block = []
    pending_header = False
    pending_english = False
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        first = pdf.pages[0].extract_text()
        if not re.search(r'2026\s+(FRANCE|ITALY|GERMANY|AUSTRIA|SPAIN|OTHERS|GERMAN BEER)', first) or '2026.7更新' not in first:
            raise ValueError('Unsupported Daiei document identity/date')
        category = re.search(r'2026\s+(.+?)\s+2026.7更新', first)[1]
        country = {'FRANCE': 'フランス', 'ITALY': 'イタリア', 'GERMANY': 'ドイツ', 'AUSTRIA': 'オーストリア', 'GERMAN BEER': 'ドイツ'}.get(category, '')
        for number, page in enumerate(pdf.pages, 1):
            count = 0
            text = page.extract_text()
            expected = len(re.findall(r'(?:¥[\d,]+|OPEN)\s*$', text, re.M))
            for line in page.extract_text_lines():
                t = line['text']
                if line['top'] < 20 and '2026' in t:
                    continue
                col = tuple(line['chars'][0]['non_stroking_color'])
                # Full-width white-on-black producer band. Type bands occupy only the first column.
                if col == (1., 1., 1.) and line['x0'] > 40 and cell(line, 190, 385) and not re.match(r'(?:SPARKLING|WHITE|RED|ROSE|ROSÉ|NON-ALCOHOLIC)', t):
                    headings = header_cells(line)
                    producer_en = headings[1]
                    producer_ja = ''
                    region = headings[0]
                    pending_header = True
                    block = [t]
                    name_ja = name_en = kind = ''
                    if category == 'OTHERS':
                        country = ('オーストラリア' if 'Australia' in t else 'ドイツ' if 'Glühwein' in t else 'ポルトガル' if 'Portugal' in t else 'フランス' if any(x in t for x in ('France', 'Cognac', 'Armagnac', 'Calvados', 'Liqueur')) else '')
                    elif 'SPAIN' in category:
                        if 'Spain' in t: country = 'スペイン'
                        elif 'Portugal' in t: country = 'ポルトガル'
                        elif any(x in t for x in ('Japan', 'Hokkaido', 'Shimane')): country = '日本'
                    continue
                if pending_header:
                    headings = header_cells(line)
                    producer_ja = headings[1]
                    region = headings[0]
                    pending_header = False
                if line['x0'] < 40 and col in COLORS:
                    title = pdfplumber.utils.extract_text([c for c in line['chars'] if tuple(c['non_stroking_color']) == col], x_tolerance=2).strip()
                    if JP.search(title):
                        if pending_english:
                            name_ja = title
                            pending_english = False
                        else:
                            name_ja, name_en, block = title, '', []
                    else:
                        name_en, name_ja, block = title, '', []
                        pending_english = True
                    kind = COLORS[col]
                    if 'グラス' in title: kind = 'グラス'
                block.append(t)
                m = PRICE.search(t)
                if not m:
                    if re.search(r'(?:¥[\d,]+|OPEN)\s*$', t):
                        raise ValueError(f'Unparsed price row p.{number}: {t}')
                    continue
                if not name_ja or not producer_ja:
                    raise ValueError(f'Missing title/producer p.{number}: {t}, {name_ja}, {producer_ja}')
                count += 1
                # Read the vintage/container column by position, never confuse the JAN with vintage.
                prefix = t[:m.start()].strip()
                vintage = prefix.split()[-1] if prefix else ''
                vintage = vintage if re.fullmatch(r'(?:19|20)\d{2}|NV|\d+ANS', vintage) else ''
                jan = re.search(r'JAN:\s*(\d{8,14})(?:\s|$)', prefix)
                price = None if m['price'] == 'OPEN' else int(m['price'][1:].replace(',', ''))
                raw = {'資料年月': '2026-07', 'PDFページ': number, '原表行': t, '商品名': name_ja,
                       '商品名欧文': name_en, '生産者': producer_ja, '生産者欧文': producer_en,
                       '商品コード': m['sku'], '掲載価格': m['price'], '掲載価格用途': '未記載・要確認',
                       '税区分': '未記載・要確認', '在庫': '未記載', '商品説明原文': '\n'.join(block),
                       '原本ページ本文': text, '確認事項': NOTE}
                grapes = next((s.split('ブドウ品種', 1)[1].split('Bio認証等')[0].strip() for s in block if 'ブドウ品種' in s), '')
                rows.append(dict(SupplierProductCode=m['sku'], OriginalSupplierProductCode=m['sku'],
                    ProductNameJa=name_ja, ProductNameEn=name_en, ProducerNameJa=producer_ja, ProducerNameEn=producer_en,
                    Country=country, Region=region, ProductType=kind, Vintage=int(vintage) if vintage.isdigit() and len(vintage)==4 else None,
                    VintageRaw=vintage, VolumeMl=int(float(m['volume'])*(1000 if m['unit']=='L' else 1)), CaseSize=int(m['case']),
                    ReferenceRetailPrice=price, PriceRaw=m['price'], TaxIncluded=False,
                    Inventory=dict(Quantity=None, Status=0, RawValue='記載なし'), JanCodes=[jan[1]] if jan else [],
                    Grapes=grapes, AlcoholPercent=None, AppellationJa='', AppellationEn='', OrganicCategory='',
                    OrganicCertification='', PriceNote=NOTE, Closure='', LegacySource='', SourceRow=count, SourceSheet=f'PDF p.{number}',
                    RawCellsJson=json.dumps(raw, ensure_ascii=False), Description='大榮産業 大栄産業 ' + NOTE + ' ' + '\n'.join(block)))
            if count != expected:
                raise ValueError(f'Page {number} price row coverage: {count} != {expected}')
    counts = Counter(r['SupplierProductCode'] for r in rows)
    if not rows or any(n>1 for n in counts.values()):
        raise ValueError(f'Duplicate SKUs: {[k for k,v in counts.items() if v>1]}')
    return rows, warnings
