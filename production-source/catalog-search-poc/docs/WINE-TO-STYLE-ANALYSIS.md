# Wine to Style inventory workbook analysis

Source: `在庫リスト 2026.09.01.xlsx`（原本はプロジェクトへ複製しない）

## Confirmed layout

- Sheet: `ワインリスト`
- Header: row 2
- Data: rows 3–1063
- Products: 1,061
- Identity: SKU; duplicate SKUなし
- 主要列: 日英生産者、日英商品名、VTG、容量、入数、希望小売、在庫、タイプ、国、産地、JAN、品種、アルコール度数、呼称、有機、評価

## Rules derived from actual values

| Source value | Quantity | Status |
|---|---:|---|
| `OK` | null | IN_STOCK |
| `少量` | null | LOW_STOCK |
| numeric | value | IN_STOCK（0が出現した場合のみOUT_OF_STOCK） |
| blank | null | UNKNOWN |

- 空欄は7件あり、OUT_OF_STOCKへ変換しない。
- 希望小売の`オープン`は22件。金額null＋PriceRaw=`オープン`として保持する。
- VTGは数値年のほかNV 106件、MV 3件。`Vintage`はnull、`VintageRaw`は維持する。
- JANは文字列。`0`は欠損扱い。瓶・化粧箱・木箱の複数JANは配列として抽出し、RawもEvidenceへ残す。
- `旧`列は`WIS`、`nakato`、`-`の3値で、旧管理元を示す属性候補。意味は要確認。
- 希望小売は仕入価格ではないため、`REFERENCE_RETAIL`として登録する。
- 税区分は「税込」の明記がある場合だけ`TaxIncluded=true`とし、記載がなければ税抜として`false`にする。この在庫表の`希望小売`には税込表記がないため、全件`TaxIncluded=false`とする。

## Search document fields

日英商品名、日英生産者、VTG、タイプ、国、産地、日英呼称、品種、有機区分、評価を検索文書へ入れる。SKU/JANは完全一致用のkeywordとして別に保持する。価格・在庫・容量は構造化filterとしEmbeddingへ入れない。
