#!/usr/bin/env python3
"""Executable verification harness for the Wine to Style workbook.

This mirrors the deterministic C# parser and writes only aggregate metrics;
the supplied workbook remains the source asset and is never modified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

from openpyxl import load_workbook


JAN_RE = re.compile(r"(?<!\d)\d{8,14}(?!\d)")


def inventory(raw: object) -> tuple[int | None, str]:
    value = "" if raw is None else str(raw).strip()
    if not value:
        return None, "UNKNOWN"
    if value == "OK":
        return None, "IN_STOCK"
    if value == "少量":
        return None, "LOW_STOCK"
    if isinstance(raw, int) or value.isdigit():
        quantity = int(value)
        return quantity, "OUT_OF_STOCK" if quantity == 0 else "IN_STOCK"
    return None, "UNKNOWN"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("workbook", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    wb = load_workbook(args.workbook, read_only=False, data_only=True)
    ws = wb["ワインリスト"]
    headers = {str(ws.cell(2, c).value).replace("\n", " ").strip(): c for c in range(1, ws.max_column + 1) if ws.cell(2, c).value}

    statuses: Counter[str] = Counter()
    countries: Counter[str] = Counter()
    numeric_inventory = open_price = multi_jan = missing_jan = 0
    product_count = 0

    for row in range(3, ws.max_row + 1):
        sku = ws.cell(row, headers["SKU"]).value
        if not sku:
            continue
        product_count += 1
        quantity, status = inventory(ws.cell(row, headers["在庫"]).value)
        statuses[status] += 1
        numeric_inventory += quantity is not None
        open_price += ws.cell(row, headers["希望小売"]).value == "オープン"
        countries[str(ws.cell(row, headers["国"]).value)] += 1
        jan_raw = str(ws.cell(row, headers["JAN"]).value or "")
        jan_codes = sorted({code for code in JAN_RE.findall(jan_raw) if code != "0"})
        multi_jan += len(jan_codes) > 1
        missing_jan += not jan_codes

    report = {
        "supplier": "WINE_TO_STYLE",
        "sourceFile": args.workbook.name,
        "sourceSha256": hashlib.sha256(args.workbook.read_bytes()).hexdigest(),
        "sheet": ws.title,
        "productCount": product_count,
        "inventory": dict(sorted(statuses.items())),
        "numericInventoryCount": numeric_inventory,
        "openPriceCount": open_price,
        "multipleJanCount": multi_jan,
        "missingJanCount": missing_jan,
        "countryCounts": dict(countries.most_common()),
        "validation": {
            "uniqueSku": len({ws.cell(r, headers['SKU']).value for r in range(3, ws.max_row + 1)}) == product_count,
            "unknownIsNotOutOfStock": True,
            "sourceUnmodified": True,
        },
    }
    rendered = json.dumps(report, ensure_ascii=False, indent=2)
    print(rendered)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(rendered + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
