"""Arcan regular retail catalog. Alcohol only; original rows remain evidence."""
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


def arcan_price(data):
    reader = PdfReader(io.BytesIO(data))
    texts = [p.extract_text() or "" for p in reader.pages]
    footer = texts[-1]
    month = re.search(r"(20\d{2})\.(\d{2})", footer)
    if "株式会社アルカン" not in footer or not month or "標準希望" not in "".join(texts):
        raise ValueError("Arcan source title/month missing")
    source_month = f"{month[1]}-{month[2]}"
    rows, warnings, candidates = [], [], []
    country = ""
    non_alcohol = False
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        for page_no, page in enumerate(pdf.pages, 1):
            text = texts[page_no - 1]
            if "NON ALCOHOLIC BEVERAGE" in text:
                non_alcohol = True
            if non_alcohol or "品番" not in text:
                continue
            if not 594 < page.width < 596 or "小売価格(本体）" not in re.sub(r"\s+", "", text):
                raise ValueError(f"Page {page_no}: header/geometry changed")
            words = page.extract_words(extra_attrs=["size"])
            anchors = [w for w in words if w["x0"] < 60 and re.fullmatch(r"\d{8,9}", w["text"])]
            expected = re.findall(r"(?m)^\s*(\d{8,9})\s", reader.pages[page_no-1].extract_text(extraction_mode="layout"))
            if [a["text"] for a in anchors] != expected:
                raise ValueError(f"Page {page_no}: independent code coverage mismatch")
            # Left grid segments delimit rows even where the name and price grids are split.
            ys = sorted({round(e["top"], 2) for e in page.horizontal_edges if e["x0"] < 20 and e["x1"] > 50})
            headers = [w for w in words if w["text"] == "品番" and w["x0"] < 60]
            layout_rows = [line for line in reader.pages[page_no-1].extract_text(extraction_mode="layout").splitlines() if re.match(r"^\s*\d{8,9}\s",line)]
            for index, a in enumerate(anchors, 1):
                h = max((w for w in headers if w["top"] < a["top"]), key=lambda w:w["top"])
                heading_words = [w for w in words if h["top"]-35 < w["top"] < h["top"]-10]
                en = " ".join(w["text"] for w in heading_words if w["size"] > 10 and w["x0"] < 400)
                ja = " ".join(w["text"] for w in heading_words if w["size"] < 10 and w["x0"] < 400)
                region = " ".join(w["text"] for w in heading_words if w["size"] > 10 and w["x0"] >= 400)
                prior_titles = [w for w in words if w["x0"] < 300 and w["top"] < h["top"] and w["size"] > 10]
                country_text = " ".join(w["text"] for w in prior_titles)
                for token, value in [("フランス","フランス"),("イタリア","イタリア"),("スペイン","スペイン"),("イギリス","イギリス"),("オーストリア","オーストリア"),("ハンガリー","ハンガリー"),("アメリカ","アメリカ"),("チリ","チリ")]:
                    if token in country_text:
                        country = value
                lo = max(y for y in ys if y < a["top"])
                hi = min(y for y in ys if y > a["bottom"])
                rw = [w for w in words if lo <= (w["top"]+w["bottom"])/2 < hi]
                column_headers = {w["text"]:w for w in words if abs(w["top"]-h["top"])<2 and w["text"] in ("年号","タイプ","容量","入数")}
                if len(column_headers)!=4:
                    raise ValueError(f"Page {page_no}: header columns changed")
                centers = [(column_headers[k]["x0"]+column_headers[k]["x1"])/2 for k in ("年号","タイプ","容量","入数")]
                boundaries = [130, centers[0]-13, *[(x+y)/2 for x,y in zip(centers, centers[1:])], centers[-1]+12, 576]
                def col(left,right):
                    selected = [w for w in rw if left <= (w["x0"]+w["x1"])/2 < right]
                    groups=[]
                    for w in sorted(selected,key=lambda w:(round(w["top"]),w["x0"])):
                        if not groups or abs(groups[-1][0]["top"]-w["top"])>2:
                            groups.append([])
                        groups[-1].append(w)
                    return "\n".join(" ".join(w["text"] for w in sorted(g,key=lambda w:w["x0"])) for g in groups)
                name = col(boundaries[0],boundaries[1])
                vintage, kind, volume, case, price_note = [col(boundaries[i],boundaries[i+1]) for i in range(1,6)]
                jan = col(60,130).strip()
                names=name.splitlines()
                name_ja=names[0] if names else ""
                name_en=" ".join(names[1:])
                v=re.fullmatch(r"(\d+)ml",volume)
                price_lines=price_note.splitlines()
                numeric=[s for s in price_lines if re.fullmatch(r"\d+(?:,\d{3})*",s)]
                if not (name_ja and name_en and en and ja and country and v and case.isdigit() and len(numeric)<=1):
                    raise ValueError(f"Page {page_no}: invalid table row {index}")
                if kind not in ("赤","白","白甘","ロゼ","泡","ロゼ泡","蒸留酒","シードル"):
                    raise ValueError(f"Page {page_no}: unknown type row {index}: {kind}")
                independent = re.search(r"(\d+)ml\s+(\d+)(?:\s+(.*))?\s*$", layout_rows[index-1])
                if not independent or (int(independent[1]),int(independent[2]),independent[3].strip() if independent[3] and re.fullmatch(r"[0-9][0-9,]*",independent[3].strip()) else "") != (int(v[1]),int(case),numeric[0] if numeric else ""):
                    raise ValueError(f"Page {page_no}: independent volume/case/price coverage mismatch row {index}")
                annotations=[s for s in price_lines if s not in numeric]
                if any(not ("入荷予定" in s or re.fullmatch(r"[-‐−―ー₋–]",s)) for s in annotations):
                    raise ValueError(f"Page {page_no}: unrecognized price/availability row {index}: {annotations}")
                if jan and jan not in ("-","－") and not jan.isdigit():
                    raise ValueError(f"Page {page_no}: invalid JAN row {index}")
                note="標準希望小売価格（本体・税別）。在庫数量未記載。"
                if not numeric:note+="価格未記載。"
                if vintage not in ("NV",) and not re.fullmatch(r"(?:19|20)\d{2}",vintage):
                    note+="年号は原表表記を保持（補完なし）。"
                availability=" / ".join(s for s in annotations if "入荷予定" in s)
                if jan.isdigit() and len(jan)!=13:note+="JAN欄の桁数が13桁でないため原表のみ保持（補正なし）。"
                if availability:note+="入荷予定："+availability+"（実入荷未確認）。"
                raw={"商品コード":a["text"],"JAN":jan,"商品名":name_ja,"Product":name_en,"生産者":ja,"Producer":en,
                     "年号":vintage,"タイプ":kind,"容量":volume,"入数":case,"小売価格":numeric[0] if numeric else "",
                     "価格・注記原文":price_note,"資料年月":source_month,"資料日":None,"税区分":"税別（本体価格）",
                     "国":country,"地域":region,"PDFページ":page_no,"PDF行位置":[lo,hi],"掲載箇所":[{"page":page_no,"row":index,"top":lo,"bottom":hi}],"確認事項":note}
                row=row_contract(a["text"],name_ja,ja,country,region,kind,vintage,int(v[1]),int(case),
                    int(numeric[0].replace(",","")) if numeric else None,"",page_no,index,raw,
                    name_en=name_en,producer_en=en,notes=note,jan=[jan] if jan.isdigit() and len(jan)==13 else [],description="アルカン "+name_ja+" "+name_en)
                # Short/unknown year notation is never promoted to an actual year.
                row["Vintage"]=int(vintage) if re.fullmatch(r"(?:19|20)\d{2}",vintage) else None
                row["OriginalSupplierProductCode"]=a["text"]
                if availability:row["Inventory"]={"Quantity":None,"Status":4,"RawValue":availability}
                candidates.append(row)
    # Exact repeat listings are one item with all source positions retained.
    seen={}
    for row in candidates:
        raw=json.loads(row["RawCellsJson"])
        key=tuple(str(row[k]) for k in ("SupplierProductCode","ProductNameJa","ProductNameEn","VintageRaw","VolumeMl","CaseSize","ReferenceRetailPrice","JanCodes","ProducerNameEn","Country","Inventory","PriceRaw"))
        if key in seen:
            original=seen[key]; evidence=json.loads(original["RawCellsJson"])
            evidence["掲載箇所"]+=raw["掲載箇所"]
            original["RawCellsJson"]=json.dumps(evidence,ensure_ascii=False)
        else:
            seen[key]=row;rows.append(row)
    counts=Counter(r["SupplierProductCode"] for r in rows)
    for row in rows:
        if counts[row["SupplierProductCode"]]>1:
            code=row["SupplierProductCode"]
            row["SupplierProductCode"]=identity("ARCAN",[code,row["ProductNameEn"],row["VintageRaw"],row["VolumeMl"],*row["JanCodes"]])
            note="同じ品番が別商品にも記載。原表保持・別商品として登録。発注前に品番確認。"
            row["PriceNote"]+=note
            raw=json.loads(row["RawCellsJson"]);raw["確認事項"]+=note
            row["RawCellsJson"]=json.dumps(raw,ensure_ascii=False)
    unique(rows)
    warnings=["酒類のみ対象。ジュース・ソーダ・ワイングッズは対象外。",f"酒類{len(candidates)}掲載行、同一内容の再掲載{len(candidates)-len(rows)}行を掲載箇所保持で統合。", "品番重複の別商品は内部識別子と原品番を分離。資料は月のみで日付未記載。"]
    return rows,warnings
