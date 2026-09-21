"""Layout-specific PDF adapters emitting the existing catalog row contract."""
import hashlib
import io
import json
import re
import unicodedata
from collections import Counter
import pdfplumber


def normalize(value):
    return unicodedata.normalize("NFKC", value).strip()


def identity(prefix, values):
    # These PDFs have no supplier SKU. Do not mistake a printed row number for one.
    key = "\x1f".join(normalize(str(value)) for value in values)
    return prefix + "-" + hashlib.sha256(key.encode("utf-8")).hexdigest()[:24].upper()


def row_contract(sku, name, producer, country, region, color, vintage, volume, case, price,
                 stock, page, row, raw, *, name_en="", producer_en="", description="", notes="", jan=None, grapes="", closure="", appellation=""):
    status = {"〇": 1, "○": 1, "△": 2, "×": 3}.get(stock, 0)
    return dict(SupplierProductCode=sku, ProducerNameJa=producer, ProducerNameEn=producer_en,
                ProductNameJa=name, ProductNameEn=name_en, Vintage=int(vintage) if vintage.isdigit() else None,
                VintageRaw=vintage, VolumeMl=volume, CaseSize=case, ReferenceRetailPrice=price,
                PriceRaw=str(raw.get("小売価格", raw.get("参考価格（税別）", ""))), TaxIncluded=False,
                Inventory=dict(Quantity=None, Status=status, RawValue=stock), ProductType=color, Country=country,
                Region=region, JanCodes=jan or [], Grapes=grapes, AlcoholPercent=None, AppellationJa=appellation,
                AppellationEn="", OrganicCategory="", OrganicCertification="", PriceNote=notes, Closure=closure,
                LegacySource="", SourceRow=row, SourceSheet=f"PDF p.{page}", RawCellsJson=json.dumps(raw, ensure_ascii=False),
                Description=description, OriginalSupplierProductCode="")


def unique(rows):
    counts = Counter(row["SupplierProductCode"] for row in rows)
    if not rows or any(n > 1 for n in counts.values()):
        raise ValueError(f"Empty extraction or ambiguous product identity: {[k for k,v in counts.items() if v>1]}")


def royal(data):
    columns = ["No", "国", "生産地", "色", "テーマ", "商品名", "年代", "画像", "メーカー名", "葡萄品種", "コメント", "甘辛", "格付", "容量", "入数", "栓", "参考価格（税別）", "卸価格・混載3cs（税別）", "Bar-Code"]
    rows, numbers = [], []
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        if "ローヤルオブジャパン" not in pdf.pages[0].extract_text():
            raise ValueError("Not a Royal of Japan PDF")
        date_match = re.search(r"(20\d{2})/(\d{1,2})/(\d{1,2})", pdf.pages[0].extract_text())
        if not date_match: raise ValueError("Royal document date missing")
        source_date = f"{date_match[1]}-{int(date_match[2]):02}-{int(date_match[3]):02}"
        for page_no, page in enumerate(pdf.pages, 1):
            for table in page.extract_tables():
                for cells in table:
                    if not (cells[0] or "").isdigit():
                        continue
                    if len(cells) != 19 or any(cell is None for cell in cells):
                        raise ValueError("Royal table shape changed")
                    raw = dict(zip(columns, cells))
                    raw["PDFページ"] = page_no
                    raw["資料表記日"] = source_date
                    number = int(cells[0]); numbers.append(number)
                    flat = lambda key: normalize(raw[key].replace("\n", ""))
                    annotations = re.findall(r"【[^】]*】", flat("商品名"))
                    name = re.sub(r"【[^】]*】", "", flat("商品名")).strip()
                    vol = re.fullmatch(r"(\d+)ml", flat("容量"), re.I)
                    case = re.fullmatch(r"(\d+)本", flat("入数"))
                    retail = re.fullmatch(r"[¥￥]([\d,]+)", flat("参考価格（税別）"))
                    if not (name and vol and case and retail):
                        raise ValueError(f"Royal required field invalid, row {number}")
                    producer, vintage = flat("メーカー名"), flat("年代")
                    color, theme = flat("色"), flat("テーマ")
                    if any(word in name + theme for word in ["シャンパーニュ", "クレマン", "スパークリング", "スプマンテ", "プロセッコ", "カヴァ", "瓶内二次発酵", "微発泡"]):
                        color = "泡ロゼ" if color == "ロゼ" else "泡"
                    notes = f"資料日{source_date}。在庫欄なし（在庫不明）。卸価格は原表参照（混載3cs条件・税別）"
                    if annotations: notes += " / " + " ".join(annotations)
                    sku = identity("ROYAL", [producer, name, vintage, vol[1], flat("色")])
                    row = row_contract(sku, name, producer, flat("国"), flat("生産地"), color, vintage,
                        int(vol[1]), int(case[1]), int(retail[1].replace(",", "")), "", page_no, number, raw,
                        description="ローヤルオブジャパン Royal of Japan " + " ".join([flat("コメント"), theme, *annotations]),
                        notes=notes, jan=[flat("Bar-Code")] if flat("Bar-Code").isdigit() else [], grapes=flat("葡萄品種"),
                        closure=flat("栓"), appellation=flat("格付"))
                    annotation = " ".join(annotations)
                    if "欠品" in annotation or "入荷予定" in annotation:
                        row["Inventory"] = dict(Quantity=None, Status=3 if "欠品" in annotation else 4, RawValue=annotation)
                        row["PriceNote"] = row["PriceNote"].replace("在庫欄なし（在庫不明）", "在庫欄なし。商品注記の欠品・入荷予定を保持（実入荷未確認）")
                    rows.append(row)
    if numbers != list(range(1, len(numbers) + 1)):
        raise ValueError("Royal row sequence incomplete")
    unique(rows)
    return rows, ["在庫欄なし。欠品・入荷予定の明記のみ状態へ反映し、それ以外は不明", "SKU記載なし。生産者・商品名・年号・容量・色から内部識別子を生成"]


def lines(words):
    groups = []
    for word in sorted(words, key=lambda w: (w["top"], w["x0"])):
        if not groups or abs(groups[-1][0]["top"] - word["top"]) > 2:
            groups.append([])
        groups[-1].append(word)
    return [sorted(group, key=lambda w: w["x0"]) for group in groups]


def finesse(data):
    rows, warnings = [], []
    producer_ja = producer_en = region = subregion = ""
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        if "フィネス" not in pdf.pages[0].extract_text():
            raise ValueError("Not a Finesse PDF")
        month_match = re.search(r"(20\d{2})\.(\d{2})", pdf.pages[0].extract_text())
        if not month_match: raise ValueError("Finesse document month missing")
        source_month = f"{month_match[1]}-{month_match[2]}"
        for page_no, page in enumerate(pdf.pages, 1):
            # Product pages carry this exact column header. Intro/index pages do not.
            text = page.extract_text() or ""
            if "(円/税抜)" not in text or "在庫" not in text:
                continue
            words = page.extract_words(extra_attrs=["fontname", "size"])
            if not 590 < page.width < 600:
                raise ValueError("Finesse page geometry changed")
            anchors = [w for w in words if 307 < w["x0"] < 327 and 8.5 < w["size"] < 9.5 and w["text"] in ("〇", "○", "△", "×")]
            left = lines([w for w in words if w["x0"] < 300 and w["top"] < 810])
            events = [(group[0]["top"], 0, group) for group in left]
            events += [(w["top"] + 2, 1, w) for w in anchors]
            product = None
            page_count = 0
            products = []
            for y, kind, value in sorted(events, key=lambda event: (event[0], event[1])):
                if kind == 0:
                    group = value
                    content = " ".join(w["text"] for w in group)
                    if content.startswith("□"):
                        break  # Producer/region narrative follows the final product table.
                    bold = [w for w in group if "Bold" in w["fontname"] and w["size"] > 9.5]
                    if bold:
                        title = " ".join(w["text"] for w in bold)
                        if "地方" in title:
                            subregion = title.strip("＜＞").replace("地方", "")
                        elif re.search(r"[\u3040-\u30ff]", title):
                            producer_ja = title; subregion = ""; product = None
                        else:
                            producer_en = title; product = None
                        continue
                    if any(w["size"] >= 11.5 for w in group):
                        region = content.replace("地方", "")
                        continue
                    if group[0]["x0"] > 40 or content.startswith(("■", "※", "□")) or y < 40:
                        continue
                    if 7.0 < group[0]["size"] < 8.5 and re.match(r"[\u3040-\u30ff]", content):
                        product = dict(ja=content, en="", y=y, count=0)
                        products.append(product)
                    elif product and re.match(r"[A-ZÀ-Ž]", content):
                        product["en"] += (" " if product["en"] else "") + content
                    continue
                anchor = value
                if not product or not product["en"] or not producer_en:
                    raise ValueError(f"Finesse unassigned stock row on page {page_no} at {y}")
                baseline = anchor["top"]
                same = [w for w in words if abs(w["top"] - baseline) < 2]
                def column(lo, hi): return " ".join(w["text"] for w in sorted(same, key=lambda w:w["x0"]) if lo <= w["x0"] < hi)
                color = column(331, 354); vintage = column(354, 385); volume = column(385, 417)
                case = column(417, 445); price = column(445, 494); note = column(494, 580)
                for label, number in [("volume", volume), ("case", case)]:
                    if number and not number.isdigit(): raise ValueError(f"Finesse invalid {label} p{page_no}: {number}")
                if price and price != "オープン" and not re.fullmatch(r"\d{1,3}(?:,\d{3})*", price):
                    raise ValueError(f"Finesse invalid price p{page_no}: {price}")
                page_count += 1; product["count"] += 1
                effective_region = subregion or region
                raw = {"生産者": producer_ja, "Producer": producer_en, "商品名": product["ja"], "Product": product["en"],
                       "在庫": anchor["text"], "色": color, "年号": vintage, "容量(ml)": volume, "入数": case,
                       "小売価格": price, "備考": note, "PDFページ": page_no, "掲載地域": effective_region,
                       "PDF行位置": round(baseline, 2)}
                raw["資料年月"] = source_month + "（日の記載なし。DB基準日は月初）"
                if "特別ロット" in region:
                    raw["取引条件"] = "\n".join(line for line in text.splitlines() if line.startswith("※") or "ただし" in line)
                # Keep narrative and dosage as evidence, but do not infer missing fields from prose.
                raw["商品説明"] = page.crop((30, product["y"] - 1, 570, baseline + 12)).extract_text() or ""
                notes = []
                if not volume: notes.append("容量記載なし（検索用0は未設定を表す）")
                if not case: notes.append("入数記載なし")
                if not price: notes.append("価格記載なし")
                if price == "オープン": notes.append("オープン価格")
                if anchor["text"] == "△": notes.append("在庫△は12本以下（正確な本数は不明）")
                if note and not note.isdigit(): notes.append(note)
                if raw.get("取引条件"): notes.append(raw["取引条件"].replace("\n", " / "))
                normalized_color = normalize(color)
                if any(x in product["en"] for x in ["CHAMPAGNE", "CRÉMANT"]):
                    normalized_color = "泡ロゼ" if normalized_color == "ロゼ" else "泡"
                sku = identity("FINESSE", [producer_en, product["en"], normalize(color), vintage, volume, "特別ロット" if "特別ロット" in region else "通常"])
                rows.append(row_contract(sku, product["ja"], producer_ja, "アメリカ" if "アメリカ" in region else "フランス",
                    effective_region, normalized_color, vintage, int(volume) if volume else 0, int(case) if case else 0,
                    int(price.replace(",", "")) if price and price != "オープン" else None, anchor["text"], page_no, page_count, raw,
                    name_en=product["en"], producer_en=producer_en, notes=" / ".join(notes),
                    description="フィネス Finesse " + raw["商品説明"], jan=[note] if re.fullmatch(r"\d{13}", note) else []))
            if page_count != len(anchors) or any(p["count"] == 0 for p in products):
                raise ValueError(f"Finesse incomplete product coverage on page {page_no}: {[p for p in products if not p['count']]}")
    unique(rows)
    missing = sum(row["VolumeMl"] == 0 for row in rows)
    warnings += [f"容量記載なし{missing}件。検索用VolumeMl=0、原表は空欄を保持", "SKU記載なし。生産者・商品名・色・年号・容量・取引区分から内部識別子を生成"]
    return rows, warnings


def toyotsu(data):
    """Read all 13 source columns; collapse only identical repeated supplier SKUs."""
    columns = ['商品コード', '国', '地域', '生産者', '商品名', '生産年', '色', '容量', '入数', '小売価格', '在庫', 'AOPなど', '入荷予定']
    records, locations = {}, {}
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        cover = pdf.pages[0].extract_text() or ''
        month = re.search(r'豊通食料株式会社\s*ワイン在庫リスト\s*(20\d{2})年(\d{1,2})月', cover)
        if not month:
            raise ValueError('Toyotsu source title/month missing')
        source_month = f'{month[1]}-{int(month[2]):02}'
        for page_no, page in enumerate(pdf.pages[1:], 2):
            text = page.extract_text() or ''
            compact = re.sub(r'\s+', '', text)
            if '価格：税抜価格' not in compact or '12本未満…△' not in compact or '60本未満…◯' not in compact or '60本以上…◎' not in compact:
                raise ValueError(f'Toyotsu price/stock legend changed on page {page_no}')
            expected = re.findall(r'(?m)^\s*([A-Z][A-Z0-9]{5})', text)
            actual = []
            for table in page.extract_tables():
                if table[0] != columns:
                    raise ValueError(f'Toyotsu table header changed on page {page_no}')
                for source_row, cells in enumerate(table[1:], 1):
                    if not any(c and c.strip() for c in cells):
                        continue
                    if len(cells) != 13 or any(c is None for c in cells) or not re.fullmatch(r'[A-Z][A-Z0-9]{5}', cells[0]):
                        raise ValueError(f'Toyotsu invalid table row on page {page_no}: {source_row}')
                    sku = cells[0]
                    actual.append(sku)
                    raw = dict(zip(columns, cells))
                    loc = {'PDFページ': page_no, '表内行': source_row}
                    if sku in records:
                        if records[sku] != raw:
                            raise ValueError(f'Toyotsu conflicting repeated SKU: {sku}')
                        locations[sku].append(loc)
                    else:
                        records[sku], locations[sku] = raw, [loc]
            if not expected or Counter(actual) != Counter(expected):
                raise ValueError(f'Toyotsu product coverage mismatch on page {page_no}')
    rows = []
    for sku, raw in records.items():
        flat = lambda key: normalize(raw[key].replace('\n', ' '))
        if not all(flat(k) for k in ['国', '地域', '生産者', '商品名', '色']):
            raise ValueError(f'Toyotsu required field missing: {sku}')
        if not re.fullmatch(r'NV|(?:19|20)\d{2}', flat('生産年')):
            raise ValueError(f'Toyotsu invalid vintage: {sku}')
        if not all(re.fullmatch(r'[1-9]\d*', flat(k)) for k in ['容量', '入数']):
            raise ValueError(f'Toyotsu invalid volume/case: {sku}')
        price_raw, stock = flat('小売価格'), flat('在庫')
        if price_raw.lower() not in ('', 'open') and not re.fullmatch(r'\d{1,3}(?:,\d{3})*|\d+', price_raw):
            raise ValueError(f'Toyotsu invalid price: {sku}')
        if stock not in ('◎', '◯', '△', '×'):
            raise ValueError(f'Toyotsu unknown stock: {sku}')
        price = int(price_raw.replace(',', '')) if price_raw.lower() not in ('', 'open') else None
        note = {'◎': '在庫60本以上（数量未確定）', '◯': '在庫60本未満（数量未確定）', '△': '在庫12本未満（数量未確定）', '×': '在庫×（欠品）'}[stock]
        note += f'。資料は{source_month}の月次リスト（日付記載なし、基準日は月初として登録）'
        if not price_raw: note += '。小売価格記載なし'
        elif price_raw.lower() == 'open': note += '。オープン価格'
        if flat('入荷予定'): note += '。入荷予定欄：' + flat('入荷予定')
        loc = locations[sku][0]
        raw.update({'資料年月': source_month, '掲載箇所': locations[sku]})
        row = row_contract(sku, flat('商品名'), flat('生産者'), flat('国'), flat('地域'), flat('色'), flat('生産年'), int(flat('容量')), int(flat('入数')), price, stock, loc['PDFページ'], loc['表内行'], raw, description='豊通食料 ' + note, notes=note, appellation=flat('AOPなど'))
        row['Inventory'] = dict(Quantity=None, Status={'◎': 1, '◯': 1, '△': 2, '×': 3}[stock], RawValue=stock)
        row['OriginalSupplierProductCode'] = sku
        rows.append(row)
    unique(rows)
    duplicates = sum(len(v) - 1 for v in locations.values())
    return rows, [f'同一内容の再掲載{duplicates}行を商品コードで統合。全掲載ページを原表データに保持。', '月のみ記載のため基準日は月初。記号在庫の本数は未設定。']
