"""Shared local PDF catalog ingestion. Documents are data, never instructions."""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tempfile
from collections import Counter

VERSION = 7
PARSER_VERSIONS = {"diony": 11, "diony-news": 10}
PRODUCT_CODE = r"WE[A-Z]{2}\d{4}[A-Z]?"


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(data)
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def json_bytes(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def extract_pages(data: bytes) -> list[str]:
    from pypdf import PdfReader
    reader = PdfReader(io.BytesIO(data))
    if reader.is_encrypted:
        raise ValueError("Encrypted PDF is not supported. Supply an unlocked copy.")
    return [page.extract_text(extraction_mode="layout") or "" for page in reader.pages]


def wine_experience(data: bytes) -> tuple[list[dict], list[str]]:
    import pdfplumber
    columns = ["WEコード", "原産国", "産地", "生産者", "ワイン名", "VIN", "容量(ml)",
               "入数", "種別", "Alc.%", "Barcode", "小売価格", "税込小売", "在庫(本)", "備考", "有機認証"]
    rows, warnings = [], []
    with pdfplumber.open(io.BytesIO(data)) as pdf:
        if "THE WINE EXPERIENCE" not in (pdf.pages[0].extract_text() or ""):
            raise ValueError("This PDF is not a Wine Experience catalog.")
        for number, page in enumerate(pdf.pages, 1):
            expected = Counter(re.findall(r"\bWE[A-Z0-9]+\b", page.extract_text() or ""))
            actual = Counter()
            for table in page.extract_tables():
                for row in table:
                    if not row or not re.fullmatch(PRODUCT_CODE, row[0] or ""):
                        continue
                    if len(row) != len(columns) or any(cell is None for cell in row):
                        raise ValueError(f"Page {number}: unsupported table structure for {row[0]}")
                    record = dict(zip(columns, [cell.strip() for cell in row]))
                    record["PDFページ"] = number
                    record["確認事項"] = ""
                    for name in ("容量(ml)", "入数", "小売価格", "税込小売"):
                        if not re.fullmatch(r"\d+(?:,\d{3})*", record[name]):
                            raise ValueError(f"Page {number}: invalid {name}: {record[name]}")
                        record[name] = int(record[name].replace(",", ""))
                    stock = record["在庫(本)"]
                    record["在庫数量"] = int(stock) if stock.isdigit() else (0 if stock == "完売" else "")
                    record["在庫状態"] = "200本以上" if stock in ("〇", "○") else ("完売" if stock == "完売" else "在庫あり" if stock.isdigit() and int(stock) > 0 else "在庫なし" if stock == "0" else "不明")
                    if record["在庫状態"] == "不明":
                        warnings.append(f"{record['WEコード']}: unknown stock notation {stock!r}")
                    capacities = re.findall(r"(\d+)\s*ML", record["ワイン名"], re.I)
                    if capacities and any(int(cap) != record["容量(ml)"] for cap in capacities):
                        record["確認事項"] = "商品名の容量と容量欄が不一致（原表の容量欄を保持）"
                        warnings.append(f"{record['WEコード']}: {record['確認事項']}")
                    actual[record["WEコード"]] += 1
                    rows.append(record)
            if actual != expected:
                raise ValueError(f"Page {number}: table coverage mismatch: expected {expected}, got {actual}")
    if not rows:
        raise ValueError("No products extracted")
    codes = Counter(row["WEコード"] for row in rows)
    if any(count != 1 for count in codes.values()):
        raise ValueError("Duplicate product codes; review source before importing")
    return rows, warnings


try:
    from .supplier_pdfs import finesse, royal, toyotsu
except ImportError:
    from supplier_pdfs import finesse, royal, toyotsu

try:
    from .daiei_pdf import daiei
except ImportError:
    from daiei_pdf import daiei

try:
    from .diony_pdf import diony, diony_news
except ImportError:
    from diony_pdf import diony, diony_news

PARSERS = {"diony": diony, "diony-news": diony_news, "daiei": daiei, "wine-experience": wine_experience, "finesse": finesse, "royal": royal, "toyotsu": toyotsu}


def import_pdf(source: Path, output: Path, name: str | None = None, parser: str = "text") -> dict:
    source = source.resolve(strict=True)
    name = name or source.stem
    if name in ("", ".", "..") or re.search(r'[\\/:*?"<>|]', name):
        raise ValueError("Name must be a single safe filename stem")
    if source.suffix.lower() != ".pdf":
        raise ValueError("Source must be a PDF")
    data = source.read_bytes()
    if not data.startswith(b"%PDF-"):
        raise ValueError("Source does not have a PDF signature")
    version = PARSER_VERSIONS.get(parser, VERSION)
    digest = hashlib.sha256(data).hexdigest()
    output = output.resolve()
    target = output / f"{name}.pdf"
    meta_path = output / f"{name}.meta.json"
    if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() != digest:
        raise ValueError(f"Different PDF already exists: {target}. Use a new name.")
    if meta_path.exists():
        previous = json.loads(meta_path.read_text(encoding="utf-8"))
        if previous.get("sha256") == digest and previous.get("source_filename") == source.name and previous.get("parser") == parser and previous.get("version") == version:
            artifacts = previous.get("artifacts", {})
            if artifacts and all((output / file).is_file() and hashlib.sha256((output / file).read_bytes()).hexdigest() == sha for file, sha in artifacts.items()):
                return {**previous, "result": "unchanged"}
    # Parse and validate completely before publishing any output.
    texts = extract_pages(data)
    empty = [i for i, text in enumerate(texts, 1) if not text.strip()]
    if not texts or empty:
        raise ValueError(f"Text extraction incomplete; OCR/review required for pages {empty}")
    payloads = {f"{name}.pdf": data,
                f"{name}.txt": "\n\n".join(f"--- PDF page {i} ---\n{text}" for i, text in enumerate(texts, 1)).encode("utf-8"),
                f"{name}.pages.json": json_bytes([{"page": i, "text": text} for i, text in enumerate(texts, 1)])}
    warnings, count = [], None
    if parser != "text":
        records, warnings = PARSERS[parser](data)
        count = len(records)
        payloads[f"{name}.products.json"] = json_bytes(records)
        stream = io.StringIO(newline="")
        writer = csv.DictWriter(stream, fieldnames=list(records[0]))
        writer.writeheader()
        writer.writerows(records)
        payloads[f"{name}.csv"] = stream.getvalue().encode("utf-8-sig")
    metadata = {"version": version, "source_filename": source.name, "sha256": digest,
                "parser": parser, "pages": len(texts), "page_characters": [len(t) for t in texts],
                "product_count": count, "warnings": warnings,
                "artifacts": {file: hashlib.sha256(content).hexdigest() for file, content in payloads.items()}}
    for file, content in payloads.items():
        destination = output / file
        if destination.exists() and destination.read_bytes() == content:
            continue
        atomic_write(destination, content)
    # Metadata is the completion marker; interrupted writes are repaired on the next run.
    atomic_write(meta_path, json_bytes(metadata))
    return {**metadata, "result": "imported"}


def main() -> None:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("source", type=Path)
    cli.add_argument("--output", type=Path, default=Path("catalogs"))
    cli.add_argument("--name")
    cli.add_argument("--parser", choices=["text", *PARSERS], default="text")
    args = cli.parse_args()
    try:
        result = import_pdf(args.source, args.output, args.name, args.parser)
    except (ValueError, OSError) as error:
        cli.exit(1, f"Import failed: {error}\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
