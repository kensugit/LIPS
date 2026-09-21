"""Arcan Bordeaux offer table; wholesale prices never become retail prices."""
import io
import json
import re
from collections import Counter

import pdfplumber
from pypdf import PdfReader

try:
    from .supplier_pdfs import row_contract, unique, identity
except ImportError:
    from supplier_pdfs import row_contract, unique, identity


def arcan(data):
    rows, warnings, printed_numbers = [], [], []
    layouts = [p.extract_text(extraction_mode="layout") for p in PdfReader(io.BytesIO(data)).pages]
    month = re.search(r"(20\d{2})年(\d{1,2})月吉日", layouts[0])
    if not month or "アルカン" not in layouts[0] or "ボルドーワイン" not in layouts[0]:
        raise ValueError("Not a dated Arcan Bordeaux list")
    source_month = f"{month[1]}-{int(month[2]):02}"
    columns = ["商品コード", "専売印", "アイテム", "Vintage", "地区", "タイプ", "容量", "通常納品価格", "特別納価・条件"]
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        for page_no, page in enumerate(pdf.pages, 1):
            expected = []
            for line in layouts[page_no - 1].splitlines():
                m = re.match(r"^\s*(\d{1,3})\s*(\d{8})\s+.*?\s(\d{4})\s+AOC.*?\s(\d+)ml\s+(.+)$", line)
                if m:
                    printed_numbers.append(int(m[1]))
                    expected.append((m[2], m[3], int(m[4]), re.findall(r"[¥￥\\]?([\d,]+)", m[5])))
            actual = []
            for table in page.find_tables():
                extracted = table.extract()
                header = next((i for i, r in enumerate(extracted) if r[0] == "商品コード"), None)
                if header is None:
                    continue
                xs = sorted({x for cell in table.rows[header].cells if cell for x in (cell[0], cell[2])})
                if len(xs) != 10:
                    raise ValueError("Arcan header columns changed")
                for table_row, cells in zip(table.rows, extracted):
                    if not cells[0] or not re.match(r"^\d{8}(?:\n|$)", cells[0]):
                        continue
                    # Some source grid lines end early. Read each column using the
                    # header boundaries and the SKU cell's actual row height.
                    box = table_row.cells[0]
                    cells = [page.crop((xs[i], box[1], xs[i+1], box[3])).extract_text() or "" for i in range(9)]
                    if len(cells) != 9:
                        raise ValueError(f"Page {page_no}: table shape changed")
                    raw = dict(zip(columns, [c or "" for c in cells]))
                    codes = raw["商品コード"].splitlines()
                    if any(not re.fullmatch(r"\d{8}", code) for code in codes):
                        raise ValueError("Invalid Arcan code")
                    vintage = raw["Vintage"]
                    volume = re.fullmatch(r"(\d+)ml", raw["容量"])
                    if not re.fullmatch(r"\d{4}", vintage) or not volume or raw["タイプ"] not in ("赤", "白", "白甘"):
                        raise ValueError("Invalid vintage, volume or type")
                    prices = []
                    for key in ("通常納品価格", "特別納価・条件"):
                        first = raw[key].splitlines()[0] if raw[key] else ""
                        if first in ("", "なし"):
                            raw[key + "数値"] = None
                        else:
                            m = re.fullmatch(r"[¥￥\\]?(\d+(?:,\d{3})*)", first)
                            if not m:
                                raise ValueError(f"Unrecognized price: {first}")
                            prices.append(m[1])
                            raw[key + "数値"] = int(m[1].replace(",", ""))
                    actual.append((codes[0], vintage, int(volume[1]), prices))
                    names = raw["アイテム"].splitlines()
                    if len(names) != 2:
                        raise ValueError("Unexpected bilingual item cell")
                    note = "小売価格未記載。納品価格・特別納価は原表情報を参照（税区分未記載）。在庫数量未記載。"
                    if raw["特別納価・条件"]:
                        note += f"特別納価は{source_month}末まで、条件：{raw['特別納価・条件']}。"
                    sku = codes[0]
                    if len(codes) > 1:
                        sku = identity("ARCAN", [*codes, *names, vintage, volume[1]])
                        note += "原本の同一行に商品コードが複数記載。発注前に確認（内部識別子を使用）。"
                        warnings.append(f"Page {page_no}: ambiguous supplier codes {codes}")
                    raw.update({"PDFページ": page_no, "資料年月": source_month, "基準日扱い": "日付未記載のため月初", "在庫": "未記載", "税区分": "未記載", "特別納価期限": source_month + "末", "確認事項": note})
                    row = row_contract(sku, names[0], "", "フランス", raw["地区"], raw["タイプ"], vintage,
                        int(volume[1]), 0, None, "", page_no, len(actual), raw,
                        name_en=names[1], notes=note, description="アルカン ボルドー " + " ".join(codes), appellation=raw["地区"])
                    row["OriginalSupplierProductCode"] = codes[0] if len(codes) == 1 else ""
                    rows.append(row)
            if not expected or expected != actual:
                raise ValueError(f"Page {page_no}: independent row/price coverage mismatch ({len(expected)} vs {len(actual)})")
    if printed_numbers != list(range(1, len(rows) + 1)):
        raise ValueError("Printed row sequence mismatch")
    unique(rows)
    return rows, warnings
