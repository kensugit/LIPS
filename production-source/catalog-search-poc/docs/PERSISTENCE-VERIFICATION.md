# 商品情報DB PoC：PostgreSQL永続化の検証結果

2026-09-17 / branch: `feature/product-catalog-poc-20260917` / base: `3c18e0f`

## 完了した範囲

iWSET-origの専用worktreeに、提供されたcatalog-search-poc-v4を独立.NET 10ソリューションとして追加した。既存iWSETのDB・起動処理・画面・プロジェクト参照は変更していない。

SourceDocument（元Excelのバイト列・SHA-256）、ExtractionJob、Supplier、仕入先SKU、価格履歴、在庫Snapshot/Item、セルEvidence、ReviewTask、検索文書、独立DB内OperationLogsを保存する。Canonical自動Linkはしない。
Application serviceからParser・File adapter・PostgreSQL adapterへ接続。原本同一hashは取込済み扱い。SKU単位の現在値と追記履歴を分離し、古い資料の後投入で現在値を巻き戻さない。全保存をtransactionにまとめ、同時投入を仕入先lockで直列化する。

元Parserの通貨表示付きExcel価格が全件nullになる不具合を修正。価格セルの数値を取得し、表示原文も保持する。税込明記のみtrue、それ以外はfalse。脆弱性警告が出た依存パッケージと不足していたOpenAPI参照を修正した。

## 検証結果

|確認|結果|
|---|---|
|.NET 10 restore/build|成功。PoCは警告0・エラー0|
|自動テスト|17件成功、失敗0、スキップ0|
|既存iWSET Release build|成功、既存コードの警告95・エラー0|
|PostgreSQL|17.11、localhost:55439の検証専用DB|
|初回Migration適用|成功|
|Migrationとモデルの差分|なし|
|生成SQLを空DBへ適用・再適用|2回とも成功|
|実Excelの2回投入|1061 SKU、SourceDocumentは1件。再投入で履歴も増えない|
|在庫あり / 少量 / 不明|655 / 399 / 7|
|数量明示 / オープン価格|329 / 22|
|複数JAN / JAN欠損|19 / 208|
|価格履歴 / 在庫Item / Evidence / 検索文書|各1061件|
|全1061行のDB再読込|SKU、日英商品名・生産者、Vintage、容量、価格、在庫、Sheet/Rowが一致|
|指定検索条件|フランス・ブルゴーニュ・赤・IN_STOCK/LOW_STOCK・税抜10000円以下：40件、条件違反0|
|同時投入|片方のみ新規、もう片方は取込済み。同じSource ID|
|履歴・失敗時|過去資料で現在値が戻らない。意図的なDB保存失敗で全体rollback|
|原本|プロジェクトへコピーなし。検証前後SHA-256不変、DB保存バイト列も一致|

テスト証跡: `catalog-search-poc/artifacts/tests/persistence.trx`。再実行手順: `catalog-search-poc/verification/verify-persistence.ps1`。

## 今回実施していない範囲

DockerはこのPCにないため、EDB公式WindowsバイナリのPostgreSQL 17で実DB検証した。pgvector拡張の作成とEmbeddingは後続Migrationで追加する（ADR 0004）。今回の検索はSQL・FTSおよび日本語部分一致であり、セマンティック検索の完成ではない。
検索画面・認証UI・酒道楽fixture/実接続・フォルダー監視・他形式Parser・Review操作は後続作業。既存UIは変更しておらず、UIブラウザ回帰は実施していない。iWSET実DBにも接続していない。
masterへのマージ・push・正式パッケージ作成・本番環境へのデプロイは未実施。今回のユーザー指示により本番デプロイは作業対象外。

## 起動と再確認

独立したPostgreSQL DBの接続先を`CATALOG_CONNECTION`へ設定する。資格情報をコマンド引数や追跡ファイルへ保存しない。

```powershell
cd catalog-search-poc
dotnet restore
dotnet build
dotnet run --project src/CatalogSearch.ImportCli -- migrate
dotnet run --project src/CatalogSearch.ImportCli -- "<元Excel絶対パス>" --persist --observed-at 2026-09-01
dotnet run --project src/CatalogSearch.ImportCli -- "<元Excel絶対パス>" --persist --observed-at 2026-09-01
dotnet run --project src/CatalogSearch.ImportCli -- search --country フランス --region ブルゴーニュ --type 赤 --available --price-max 10000
```

検証は`CATALOG_TEST_CONNECTION`（匿名fixture用）と`CATALOG_ACCEPTANCE_CONNECTION`（実Excel専用）を別DBに設定し、`verification/verify-persistence.ps1 -Workbook "<元Excel絶対パス>"`を実行する。受入テストは専用DB内1061 SKUを前提とする。`dotnet test`だけでは接続環境がないDBテストは明示的にSkippedとなる。実Excelを別DBへ初めて投入する前にはmigrateを行う。
Migration SQLはPostgreSQL専用。iWSETのSQL Serverへ適用しない。既存Migrationは変更せず、変更は新規Migrationで前進させる。`database update 0`等のdown操作を運用手順に使わない。

Docker環境では`.env.example`を参考に`POSTGRES_PASSWORD`を設定して`docker compose up -d`を利用できる。SQL初回Migrationはvectorに依存しない。

## 終了時確認

PoC Webのhealth・architecture・OpenAPIはHTTP 200。起動したWebと検証専用PostgreSQLは停止済みで、55439/55440の待受なし。
