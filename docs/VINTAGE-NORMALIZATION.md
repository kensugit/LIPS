# ヴィンテージ表記の統一（2026-09-29）

## 仕様

利用者の指示：年号の括弧有無を同一検索条件にし、仕入先ごとに異なるノンヴィンテージ表記をLIPS内で統一する。

- `2021`、`[2021]`、`(2021)` は選択肢・一覧・並べ替え・CSVで `2021` に統一する。全角数字・全角括弧にも対応。
- `NV`、`N.V`、`N.V.` は `NV` に統一する。大文字小文字、全角、前後空白にも対応。
- APIの旧表記での条件指定も、同じグループの商品を取得する。
- MV、複数年、熟成年数、空欄、ハイフン、0、3桁年号をNVや4桁年号に推定変換しない。
- 元の `VintageRaw`、原本・Evidenceは保持。APIに表示専用 `VintageLabel` を追加し、詳細画面には「年号の原表記」を表示する。
- 表記ゆれの元値をSQL条件に適用してから検索件数を制限する。他の価格・在庫等の条件は維持。
- DB更新・Migrationなし。

## 作業元と状態

本番inspectで `ea1736f9d5f9976e2d9d6fd57df477513b426f891f9f9fba3cf1fbdb0d4d656f` / source `f226f0a98d74253af4100e35d0f4d43ae898cb9f` を確認した。ファインズ特価・CSV等を保持するため、この本番ソースから独立候補 `artifacts/vintage-normalization-20260929/source` を作成した。

開発元 `product-catalog-poc-20260917/catalog-search-poc` にも今回の差分だけ反映済み。他タスクの未コミット変更は保持。

本番未反映。今回の変更に対する展開指示後、最新inspectとの再照合と共通展開手順が必要。以前のWeb更新への承認を今回版の承認として扱わない。

## 検証

- 候補全体の.NETテスト166件成功、環境依存6件skip。
- 独立PostgreSQLでヴィンテージ22テスト成功（結合テストを含みskipなし）。旧表記検索・件数制限・価格条件との併用・MVとの区別・原表記保持を確認。
- Web build警告0・エラー0。
- Edgeで選択肢の一意化、括弧違い3商品の検索、NV3表記の検索、一覧・並べ替え・CSV、詳細の原表記を確認。
- テストコード：候補の `VintageNotationTests.cs` と `tests/verify-vintage-normalization.cjs`。後者は専用fixture DB向け。本番DBへfixtureを投入しない。
- 画面・検証結果：`artifacts/vintage-normalization-20260929/preview.png`、`ui-proof.json`。
- 配布対象差分：`tools/patches/vintage-normalization.patch`（上記本番source基準）。
