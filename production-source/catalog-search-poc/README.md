# 酒類カタログ横断検索 PoC

仕入先カタログ・価格表・在庫表と、酒道楽の商品マスターを横断検索するための独立PoCです。

## PoCで証明すること

1. Excel/CSVを原本のまま登録し、Parser Firstで抽出する。
2. `Product`（実世界の商品）と`SupplierProduct`（仕入先SKU）を分離する。
3. 酒道楽の商品は`InternalProductRecord`として参照専用同期し、原レコードを保持する。
4. SupplierProduct/酒道楽商品をCanonical Productへ任意にLinkする。
5. 数値・区分はSQL、説明や表記揺れは全文・Vectorで検索する。
6. 検索結果から原本セルまたは酒道楽レコードへ根拠を辿れる。

## 技術方針

- .NET 10 / ASP.NET Core
- EF Core 10 + PostgreSQL 17 + pgvector
- Razor PagesまたはMVCのサーバー描画（PoCのUIを最短で作る）
- AIは抽象化し、初期はOpenAI、将来はLocal/Hybridへ差し替え可能
- iWSET・酒道楽とはDBを共有しない。酒道楽はread-only adapter経由で同期する

## 最初の縦切り

`CSV/Excel 1ファイル + 酒道楽商品スナップショット → 抽出 → 商品同定 → 検索 → Evidence表示`

Wine to Styleの`在庫リスト 2026.09.01.xlsx`を最初の実例として解析・実装済みです。詳細は`docs/WINE-TO-STYLE-ANALYSIS.md`を参照してください。

Codexで作業を再開する場合は、最初に`CODEX-HANDOFF.md`を読ませてください。確定事項、検証値、現在地、次の実装順、次マイルストーンをまとめています。

## 起動準備

```bash
docker compose up -d
dotnet restore
dotnet build
dotnet test
dotnet run --project src/CatalogSearch.ImportCli -- "/path/to/在庫リスト　2026.09.01.xlsx"
dotnet run --project src/CatalogSearch.Web
```

環境変数は`.env.example`を参照してください。実際の資格情報はリポジトリへ保存しません。

## 実装開始前に確認するもの

`docs/OPEN-QUESTIONS.md`のP0項目と、代表サンプルファイルを確認します。回答待ちでも、fixtureを使ってデータモデル・検索パイプラインの実装は開始できます。

## PostgreSQL永続化マイルストーン（2026-09-17）

実装・実DB検証済み。起動コマンド・再実行手順・対象外項目は [検証結果](docs/PERSISTENCE-VERIFICATION.md) を参照。`--persist`は明示的な`--observed-at`と`CATALOG_CONNECTION`が必要。DB変更は`migrate`コマンドで明示適用する。

## ブラウザで確認する

検索画面と取込元表示を追加済み。起動・終了方法は [動作確認手順](docs/LOCAL-DEMO-GUIDE.md) を参照してください。
