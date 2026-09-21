"""Map validated local PDF extraction to the existing catalog import contract."""
from __future__ import annotations
import argparse
from collections import Counter
from datetime import date
import hashlib
import json
from pathlib import Path
import re

try:
    from .pdf_import import atomic_write, json_bytes
except ImportError:
    from pdf_import import atomic_write, json_bytes


def bilingual(text: str) -> tuple[str, str]:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    japanese, english = [], []
    for line in lines:
        (japanese if re.search(r"[\u3040-\u30ff\u3400-\u9fff]", line) else english).append(line)
    return "".join(japanese), " ".join(english)


def wine_experience_rows(records: list[dict]) -> list[dict]:
    output = []
    page_rows = Counter()
    for raw in records:
        page = raw["PDFページ"]
        page_rows[page] += 1
        producer_ja, producer_en = bilingual(raw["生産者"])
        name_ja, name_en = bilingual(raw["ワイン名"])
        stock = raw["在庫(本)"]
        if stock in ("〇", "○"):
            quantity, status = None, 1
        elif stock == "完売":
            quantity, status = 0, 3
        elif stock.isdigit():
            quantity = int(stock)
            status = 1 if quantity > 0 else 3
        else:
            quantity, status = None, 0
        barcode = raw["Barcode"].strip()
        note = raw["確認事項"]
        if stock in ("〇", "○"):
            note = " / ".join(filter(None, [note, "在庫〇・○は200本以上（数量未確定）"]))
        row = dict(
            SupplierProductCode=raw["WEコード"], ProducerNameJa=producer_ja,
            ProducerNameEn=producer_en, ProductNameJa=name_ja or name_en, ProductNameEn=name_en,
            Vintage=int(raw["VIN"]) if raw["VIN"].isdigit() else None, VintageRaw=raw["VIN"],
            VolumeMl=raw["容量(ml)"], CaseSize=raw["入数"], ReferenceRetailPrice=raw["小売価格"],
            PriceRaw=f'{raw["小売価格"]:,}', TaxIncluded=False,
            Inventory=dict(Quantity=quantity, Status=status, RawValue=stock),
            ProductType=raw["種別"].replace("\n", ""), Country=raw["原産国"],
            Region=raw["産地"].replace("\n", " / "), JanCodes=[barcode] if barcode.isdigit() and barcode != "0" else [],
            Grapes="", AlcoholPercent=float(raw["Alc.%"].rstrip("%")) if raw["Alc.%"] else None,
            AppellationJa="", AppellationEn="", OrganicCategory="", OrganicCertification=raw["有機認証"],
            PriceNote=note, Closure="", LegacySource="", SourceRow=page_rows[page], SourceSheet=f"PDF p.{page}",
            RawCellsJson=json.dumps(raw, ensure_ascii=False),
            Description=" ".join(filter(None, ["ワインエクスペリエンス", raw["備考"], raw["有機認証"], note])),
            OriginalSupplierProductCode=raw["WEコード"])
        output.append(row)
    return output


def export_bundle(metadata_path: Path, observed_at: str, destination: Path) -> dict:
    date.fromisoformat(observed_at)
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    if metadata["parser"] not in ("arcan", "wine-experience", "finesse", "royal", "toyotsu", "daiei", "diony", "diony-news"):
        raise ValueError("No product mapper is registered for this parser")
    root = metadata_path.parent
    for name, digest in metadata["artifacts"].items():
        if hashlib.sha256((root / name).read_bytes()).hexdigest() != digest:
            raise ValueError(f"Extraction artifact changed: {name}. Re-extract first.")
    product_file = next(name for name in metadata["artifacts"] if name.endswith(".products.json"))
    pdf_file = next(name for name in metadata["artifacts"] if name.endswith(".pdf"))
    records = json.loads((root / product_file).read_text(encoding="utf-8"))
    rows = wine_experience_rows(records) if metadata["parser"] == "wine-experience" else records
    if metadata["parser"] == "royal":
        month = re.search(r"\.(20\d{2})(\d{2})\.pdf$", metadata["source_filename"])
        if month:
            period = f"{month[1]}年{int(month[2])}月以降の価格表（資料ファイル名より）"
            for row in rows:
                row["PriceNote"] = period + "。" + row["PriceNote"]
                raw = json.loads(row["RawCellsJson"])
                raw["価格適用時期"] = period
                row["RawCellsJson"] = json.dumps(raw, ensure_ascii=False)
    if len(rows) != metadata["product_count"] or len({r["SupplierProductCode"] for r in rows}) != len(rows):
        raise ValueError("Product coverage mismatch")
    if metadata["parser"] in ("arcan", "toyotsu", "daiei", "diony", "diony-news"):
        source_month = json.loads(rows[0]["RawCellsJson"])["資料年月"]
        if observed_at != source_month + "-01":
            raise ValueError("Use the source month start for a month-only document date")
    code, name = {"arcan": ("ARCAN", "アルカン"),"diony": ("DIONY", "ディオニー"), "diony-news": ("DIONY", "ディオニー"), "daiei": ("DAIEI", "大榮産業"), "wine-experience": ("WINE_EXPERIENCE", "ワインエクスペリエンス"), "finesse": ("FINESSE", "フィネス"), "royal": ("ROYAL_OF_JAPAN", "ローヤルオブジャパン"), "toyotsu": ("TOYOTSU", "豊通食料")}[metadata["parser"]]
    bundle = dict(SchemaVersion=1, SourceFileName=metadata["source_filename"], SourceSha256=metadata["sha256"],
                  ObservedAt=observed_at, SupplierCode=code, SupplierName=name,
                  ParserVersion=f'{metadata["parser"]}Pdf/{metadata["version"]}+Catalog/1', Rows=rows)
    atomic_write(destination, json_bytes(bundle))
    return dict(bundle=str(destination), source=str(root / pdf_file), products=len(rows))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("metadata", type=Path)
    parser.add_argument("--observed-at", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(export_bundle(args.metadata, args.observed_at, args.output), ensure_ascii=False))
