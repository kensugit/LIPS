"""Read-only PDF adapter. Source bytes and every source row remain evidence."""
import contextlib
import hashlib
import io
import json
import re
import sys
from collections import Counter
from pathlib import Path

# Private, version-pinned dependencies in the Web package. Never pip-install or
# modify the existing renderer environment at runtime. Windows tests use their
# isolated dependency directory as before.
_vendor = Path(__file__).with_name("vendor") / "linux-cp312"
_vendor_problem = False
if sys.platform.startswith("linux") and _vendor.is_dir():
    import platform
    if sys.version_info[:2] != (3, 12) or platform.machine().lower() not in ("x86_64", "amd64"):
        _vendor_problem = True
    else:
        sys.path.insert(0, str(_vendor))

PARSERS = {"KINOSHITA": "kinoshita", "TOYOTSU": "toyotsu", "FINESSE": "finesse",
           "WINE_EXPERIENCE": "wine-experience", "ROYAL_OF_JAPAN": "royal", "ARCAN": "arcan",
           "DAIEI": "daiei", "DIONY": "diony"}


def kinoshita(data):
    from pypdf import PdfReader
    from supplier_pdfs import row_contract, unique
    reader = PdfReader(io.BytesIO(data))
    if reader.is_encrypted:
        raise ValueError("encrypted")
    rows = []
    # Numeric suffix is anchored from the right: digits in the wine name cannot shift columns.
    pattern = re.compile(r"^(?P<sku>\d{8})\s*(?P<name>.+?)\s+(?P<volume>\d+)\s+(?P<case>\d+)\s+"
                         r"(?P<stock>\d+(?:CS)?)\s+(?P<price>\d[\d,]*)\s+(?:(?P<vintage>\d{1,4})\s+)?"
                         r"(?P<alcohol>\d{1,2}(?:\.\d{1,2})?)\s+(?:(?P<jan>\d{13}|\d{8})\s+)?(?P<country>[A-Za-z][A-Za-z .-]*)$")
    for page_no, page in enumerate(reader.pages, 1):
        text = page.extract_text() or ""
        compact = re.sub(r"\s+", "", text)
        if page_no == 1 and not all(h in compact for h in ("商品名称", "在庫数", "小売価格", "JAN", "国名")):
            raise ValueError(f"page {page_no}: header_changed_or_no_text")
        expected = Counter(re.findall(r"(?m)^\s*(\d{8})", text))
        actual = Counter()
        for line_no, line in enumerate(text.splitlines(), 1):
            if not re.match(r"^\s*\d{8}", line):
                continue
            m = pattern.fullmatch(line.strip())
            if not m:
                raise ValueError(f"page {page_no}, line {line_no}: row_layout_changed")
            raw = m.groupdict()
            volume, case = int(raw["volume"]), int(raw["case"])
            if volume <= 0 or case <= 0:
                raise ValueError("invalid_volume_or_case")
            vintage = raw["vintage"] or ""
            row = row_contract(raw["sku"], raw["name"], "", raw["country"], "", "", vintage,
                               volume, case, int(raw["price"].replace(",", "")), raw["stock"],
                               page_no, line_no, {**raw, "原文": line}, jan=[raw["jan"]] if raw["jan"] else [])
            row["PriceRaw"] = raw["price"]
            row["Vintage"] = int(vintage) if re.fullmatch(r"[12]\d{3}", vintage) else None
            row["AlcoholPercent"] = float(raw["alcohol"])
            stock = raw["stock"]
            row["Inventory"] = {"Quantity": int(stock) if stock.isdigit() else None,
                                "Status": 3 if stock == "0" else 1, "RawValue": stock}
            rows.append(row)
            actual[raw["sku"]] += 1
        if not expected or expected != actual:
            raise ValueError(f"page {page_no}: coverage_mismatch")
    unique(rows)
    return rows, []


def parse(data, supplier):
    if _vendor_problem:
        return {"ok": False, "code": "PdfReaderUnavailable", "message": "同梱PDF読取り環境とPythonの版・CPUが一致しません。",
                "remedy": "管理担当者がLinux x64・Python 3.12用の配布物と実行パスを確認してください。既存Python環境を直接変更しないでください。"}
    if not data.startswith(b"%PDF-"):
        return {"ok": False, "code": "InvalidPdf", "message": "PDFのファイル形式が不正です。",
                "remedy": "原本を開けるか確認し、正しいPDFを選択してください。"}
    if supplier not in PARSERS:
        return {"ok": False, "code": "UnsupportedPdfLayout", "message": "この取引先のPDF帳票は未対応です。",
                "remedy": "取引先のExcel版または標準CSVを用意するか、この原本の読取り対応を依頼してください。"}
    try:
        # Third-party libraries must never print source content into the protocol output.
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            if supplier == "KINOSHITA":
                rows, warnings = kinoshita(data)
            else:
                from pdf_import import PARSERS as existing
                rows, warnings = existing[PARSERS[supplier]](data)
                if supplier == "WINE_EXPERIENCE":
                    rows = [wine_experience_row(r) for r in rows]
            if not rows or len(rows) > 10000 or any(not r.get("SupplierProductCode") or not r.get("ProductNameJa") for r in rows):
                raise ValueError("invalid_rows")
        return {"ok": True, "version": ("FolderPdf/2/" if supplier == "ARCAN" else "FolderPdf/1/") + PARSERS[supplier], "rows": rows,
                "warnings": warnings, "hash": hashlib.sha256(data).hexdigest().upper()}
    except ImportError:
        return {"ok": False, "code": "PdfReaderUnavailable", "message": "サーバーのPDF読取り環境が不足しています。",
                "remedy": "管理担当者がPDF読取り用Pythonとrequirements.txtの依存ライブラリを設定してください。"}
    except Exception as exc:
        # Only controlled categories and numeric positions enter the UI; never echo exceptions/source text.
        reason = str(exc).lower()
        page = re.search(r"page (\d{1,4})", reason)
        line = re.search(r"(?:line|page \d{1,4}:)\s*(\d{1,4})", reason)
        position = (f"PDF {page[1]}ページ" if page else "PDF") + (f"・行{line[1]}" if line else "")
        cause = "帳票・列・商品行を安全に読み取れませんでした。"
        if "header" in reason: cause = "列見出しが対応済み帳票と一致しません。"
        elif "invalid table row" in reason or "row_layout" in reason: cause = "必須項目または列構成が対応済み帳票と一致しません。"
        elif "coverage" in reason or "sequence" in reason: cause = "原典の商品行数と読取り結果が一致しません。"
        elif "source title" in reason or "title/month" in reason: cause = "対応する表題・対象年月を検出できません。"
        elif "legend" in reason: cause = "価格・在庫記号の凡例が対応済み帳票と異なります。"
        elif "encrypted" in reason or "password" in reason: cause = "暗号化されているため読み取れません。"
        return {"ok": False, "code": "PdfExtractionFailed", "message": position + "：" + cause,
                "remedy": "表示箇所の原典と列・凡例を確認し、この帳票の読取り対応を依頼してください。暗号化PDFは解除済みコピー、画像PDFは文字を選択できるPDFまたはExcel版を用意してください。"}


def wine_experience_row(r):
    from supplier_pdfs import row_contract
    stock = str(r["在庫(本)"])
    row = row_contract(r["WEコード"], r["ワイン名"], r["生産者"], r["原産国"], r["産地"], r["種別"],
                       str(r["VIN"]), r["容量(ml)"], r["入数"], r["小売価格"], stock,
                       r["PDFページ"], 0, r, jan=[r["Barcode"]], notes=r["備考"])
    row["Inventory"] = {"Quantity": r["在庫数量"] if isinstance(r["在庫数量"], int) else None,
                        "Status": 3 if stock in ("0", "完売") else 1 if stock.isdigit() or stock in ("〇", "○") else 0,
                        "RawValue": stock}
    row["PriceRaw"] = str(r["小売価格"])
    row["AlcoholPercent"] = float(r["Alc.%"]) if re.fullmatch(r"\d+(\.\d+)?", str(r["Alc.%"])) else None
    return row


if __name__ == "__main__":
    # No input filenames or document text in diagnostics. One bounded JSON message on stdout.
    data = sys.stdin.buffer.read(20_000_001)
    result = parse(data, sys.argv[1]) if len(data) <= 20_000_000 else {"ok": False, "code": "FileTooLarge",
        "message": "PDFが20MBを超えています。", "remedy": "20MB以下の資料に分けてください。"}
    sys.stdout.buffer.write(json.dumps(result, ensure_ascii=False, allow_nan=False).encode("utf-8"))
