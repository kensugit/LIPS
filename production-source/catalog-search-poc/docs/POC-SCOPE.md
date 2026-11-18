# PoC scope and acceptance criteria

## In scope

- 仕入先1〜2社のCSV/Excel
- 酒道楽商品マスターのread-only同期（接続不可なら匿名fixture）
- Product / SupplierProduct / InternalProductRecord
- 価格履歴、在庫Snapshot、Evidence
- Product matching候補とReview Queue
- SQL + PostgreSQL FTS + pgvectorのHybrid search
- Product単位と酒道楽商品単位の検索表示

## Out of scope (第2段階)

- PDF/画像/複雑HTMLの本格取込
- 酒道楽への書戻し
- iWSET本番連携
- 自動発注、推奨数量
- 完全自動の同一商品統合

## Acceptance scenarios

1. `○/△/×/残3/空欄`を規則通り解釈し、空欄はUNKNOWNになる。
2. 同一商品を扱う2仕入先が1 Productに表示され、価格・在庫は別々に見える。
3. 酒道楽で別コードの2商品が原レコードを維持したまま同じProductにLinkできる。
4. 未Linkの酒道楽商品も名称のセマンティック検索に出る。
5. 「1万円以下・在庫あり」はSQL条件、「エレガントなピノ」はVector順位に使う。
6. 検索結果から原本ファイル/Sheet/Row/Cellまたは酒道楽SourceKeyへ辿れる。
7. AIをDisabledにしても決定論的fixtureの取込・検索が動く。
8. Providerを差し替えてExtraction/Search indexを再生成してもCanonical IDが変わらない。
