"""Read Southern Cross XLS originals without evaluating macros or business defaults.

This is a source inspection adapter, not a database importer. Requires xlrd.
"""
import argparse
import hashlib
import json
from pathlib import Path


def extract(path):
    import xlrd

    content = Path(path).read_bytes()
    workbook = xlrd.open_workbook(file_contents=content, formatting_info=True)
    if workbook.sheet_names() != ["NZ"]:
        raise ValueError("Unexpected worksheet layout")
    sheet = workbook.sheet_by_name("NZ")
    expected = ["", "アイテム名", "種別", "上代価格（税別）", "JAN", "コメント"]
    if sheet.ncols != 6 or sheet.row_values(3) != expected:
        raise ValueError("Unexpected headers")
    products = []
    producer = ""
    consumed = set()
    for index in range(5, sheet.nrows):
        values = sheet.row_values(index)
        if index in consumed:
            continue
        if values[0] and not any(values[1:]):
            producer = values[0]
        if not values[1]:
            if any(values[2:]):
                raise ValueError(f"Unclassified source row {index + 1}")
            continue
        if not values[2] or values[3] == "" or index + 1 >= sheet.nrows:
            raise ValueError(f"Incomplete product row {index + 1}")
        japanese = sheet.row_values(index + 1)
        if not japanese[1] or japanese[0] or japanese[3] != "" or japanese[4] != "":
            raise ValueError(f"Unexpected paired row {index + 2}")
        products.append({
            "sheet": sheet.name,
            "rows": [index + 1, index + 2],
            "producerRaw": producer,
            "cells": {f"{chr(65 + col)}{row + 1}": sheet.cell_value(row, col)
                      for row in (index, index + 1) for col in range(6)},
            "formats": {f"{chr(65 + col)}{row + 1}": sheet.cell_xf_index(row, col)
                        for row in (index, index + 1) for col in range(6)},
        })
        consumed.add(index + 1)
    if not products:
        raise ValueError("No products")
    return {"schema": "southern-cross-source-inspection/v1",
            "sourceFile": Path(path).name,
            "sha256": hashlib.sha256(content).hexdigest().upper(),
            "size": len(content), "titleRaw": sheet.cell_value(1, 0),
            "products": products,
            "businessMapping": "Source inspection only; business mapping is not applied here"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = extract(args.source)
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"products": len(result["products"]), "sha256": result["sha256"]}))
