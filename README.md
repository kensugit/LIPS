# LIPS PDFリスト取り込み

## ソース管理と配置

GitHub: https://github.com/kensugit/LIPS 。本番はDellのWSL2 / Ubuntu-24.04上のDocker構成であることを構築記録から確認しました。現在状態と配置前チェックは [Dell WSL本番環境](docs/DELL-WSL.md) を参照してください。

取り込みコード・テスト・手順書をGitで管理します。原本と抽出データの `catalogs/`、DBバックアップ・検証結果の `artifacts/` はローカルに保持し、Gitの対象から除外します。原本を使うテストは、上記3社の資料をこのREADMEの名前で `catalogs/` に配置してから実行してください。

このリポジトリ単体に商品検索Webアプリ・PostgreSQLサーバーは含まれません。`CatalogPdfBridge` のビルドには互換性のあるカタログ本体のランタイムが必要です。`Start-CatalogSearch.ps1` は検証済みのこのPC用起動手順で、本番配置スクリプトではありません。リモートリポジトリと本番ホスト・既存DBは未設定です。本番への適用には配置先の特定、既存DBのバックアップ、互換性確認、3社の原本・取込JSONの転送、取込後の全件照合が必要です。既存本番DBをこのPCのDBダンプで上書きしないでください。

`tools/pdf_import.py` は各仕入先で共通のPDF取り込みCLIです。原本、ページ別テキスト、SHA-256、ページ数を保存します。PDFの文中にある指示は実行しません。

## 実行

Python環境に `requirements.txt` の依存関係を用意して実行します。

```powershell
python tools/pdf_import.py '仕入先のリスト.pdf' --name '仕入先.リスト.202609'
python tools/pdf_import.py '【在庫表】ワインエクスペリエンス_260831.pdf' --name 'ワインエクスペリエンス.在庫表.20260901' --parser wine-experience
python -m unittest discover -s tests -v
```

この端末のCodex同梱Pythonは `C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe` です。PowerShellでは `& '実行ファイルのパス' -X utf8 tools/pdf_import.py ...` と指定できます。

## 出力と再取り込み

既定の出力先は `catalogs/`。`--output` で変更できます。

- `.pdf`: 入力と同一の原本。
- `.txt`: ページ番号付き検索用テキスト。
- `.pages.json`: ページ別テキスト。
- `.meta.json`: 原本ハッシュ、抽出バージョン、解析方法、各出力のハッシュ、ページ数、警告。最後に保存される完了記録。
- `.csv` / `.products.json`: 仕入先専用解析を選択した場合の商品レコード。

同じ保存名・原本ハッシュ・解析バージョンで出力も正常なら `unchanged` で終了します。欠損・破損した派生ファイルは再生成します。同名で異なる原本があれば停止し、原本を上書きしません。更新版は別の保存名を指定してください。各ファイルは一時ファイルから置換するため、中断後も再実行できます。同一保存名への並行実行は避けてください。

文字が取得できないページがあれば取り込みを停止し、OCRまたは目視確認が必要なページを表示します。自動OCRは実装していません。文字取得の成功だけで表の意味が正しく解析できたと判断しません。

## 仕入先別の範囲

| 仕入先 | ページ数 | 今回の取り込み |
| --- | ---: | --- |
| ワインエクスペリエンス | 11 | 原本・テキスト・220商品のCSV/JSON |
| フィネス | 39 | 原本・ページ別テキスト・489件のCSV/JSON・DB登録 |
| ローヤルオブジャパン | 11 | 原本・ページ別テキスト・123件のCSV/JSON・DB登録 |

原本・テキスト処理とDB接続CLIは3社共通です。仕入先別の表の解析は `supplier_pdfs.py` と `pdf_import.py`、DB形式への変換は `catalog_export.py` で行います。追加の仕入先も `PARSERS` に解析関数を登録し、共通の保存・検証処理を利用できます。

## ワインエクスペリエンスの扱い

PDFのファイル名は260831ですが、表紙の日付は2026年9月1日です。在庫・価格はその掲載時点の値です。全商品・全容量を保持し、ページ別に本文の商品コードと抽出行を照合します。

- 生産者・ワイン名は原表の欧文／和文をセル内改行で保持します。
- 原表の16列を維持し、PDFページ、確認事項、在庫数量、在庫状態を追加します。
- `〇` と `○` は表紙の凡例に従って「200本以上」。正確な数量には変換しません。
- `完売` は在庫数量0。数値在庫は記載本数を保存します。
- 価格は「小売価格」と「税込小売」を別々に保持し、仕入価格には転用しません。
- 不明な在庫表記は「不明」として警告し、欠損を0に置き換えません。
- CSVはExcel向けUTF-8 BOMです。Barcodeを厳密な文字列として扱う場合はExcelのテキスト/CSV取り込みで列型を文字列にしてください。
- `WECA0608M` は商品名に1500ML、容量欄に750とあるため、750を保持して確認事項を付けています。

商品コードの重複、表の列構成変更、価格・容量の不正値、本文と商品行の件数不一致があれば、公開前に停止します。仕入先のレイアウト変更時は解析処理の更新と原本照合が必要です。

## 商品情報DBへの登録と検索

2026年9月20日、独立したローカルPostgreSQL `catalog_search_poc` にワインエクスペリエンス220商品を登録済みです。在庫あり185件（うち数量未確定49件）、完売35件。既存Wine to Style 1,061件とLUC 2,902件も保持し、登録直後の総数は4,183件です。

検索画面: http://127.0.0.1:55440

「商品名・生産者・産地」に **ワインエクスペリエンス** を入力して検索すると全220件を表示します。「在庫あり・少量のみ」で185件。商品名・生産者・SKUでも検索できます。取込元ボタンからPDFファイル名、基準日、ページ、原表の各項目を確認できます。このURLはこのPC専用です。

停止後は `tools/Start-CatalogSearch.ps1` で既存DBと検索画面を起動できます。元のカタログ開発フォルダーと利用者用PostgreSQLを利用します。共有中のサービスを停止・入れ替えず、起動済みなら再利用します。この起動方法では自動フォルダー監視はOFFです。

### 共通のDB接続方法

`catalog_export.py` が仕入先別の抽出結果を既存 `SupplierInventoryRow` 形式へ変換し、`CatalogPdfBridge` が既存 `PostgresInventoryStore` を呼び出します。接続CLIは仕入先共通です。DB登録は `--persist` を付けた場合だけ行います。

```powershell
python tools/catalog_export.py 'catalogs/ワインエクスペリエンス.在庫表.20260901.meta.json' --observed-at 2026-09-01 --output 'catalogs/ワインエクスペリエンス.在庫表.20260901.catalog.json'
dotnet build tools/CatalogPdfBridge -p:CatalogRuntime='C:/Users/kensu/OpenAI/Codex/iWSET/iWSET/商品情報DB/product-catalog-poc-20260917/catalog-search-poc/src/CatalogSearch.Web/bin/Debug/net10.0'

# まずPDFハッシュ・商品行を検証（DB書き込みなし）
dotnet tools/CatalogPdfBridge/bin/Debug/net10.0/CatalogPdfBridge.dll 'catalogs/ワインエクスペリエンス.在庫表.20260901.catalog.json' 'catalogs/ワインエクスペリエンス.在庫表.20260901.pdf'

# ローカル利用者DBへ保存
$env:CATALOG_CONNECTION='Host=127.0.0.1;Port=55439;Database=catalog_search_poc;Username=catalog'
dotnet tools/CatalogPdfBridge/bin/Debug/net10.0/CatalogPdfBridge.dll 'catalogs/ワインエクスペリエンス.在庫表.20260901.catalog.json' 'catalogs/ワインエクスペリエンス.在庫表.20260901.pdf' --persist

# 全商品・原本・価格在庫履歴・Evidence・索引を読み戻して照合
dotnet tools/CatalogPdfBridge/bin/Debug/net10.0/CatalogPdfBridge.dll 'catalogs/ワインエクスペリエンス.在庫表.20260901.catalog.json' 'catalogs/ワインエクスペリエンス.在庫表.20260901.pdf' --verify
```

保存する原本は変換CSVではなくPDFそのものです。SHA-256で再登録を判定し、同じPDFは `AlreadyImported=true` になります。既存保存処理のトランザクション・仕入先別の同期制御・履歴・新旧日付判定を再利用します。入数、度数、税込小売、備考、有機認証、容量不一致はEvidenceに保持します。税抜小売を検索用の参考小売価格として登録し、自動の商品統合は行いません。

本体アプリのソース変更、DBスキーマ変更、本番デプロイは行っていません。接続CLIはローカルの `catalog_` データベースに限定し、適用済みMigrationが既存ランタイムと一致しない場合は停止します。ランタイム更新後は接続CLIを再ビルドして検証してください。

### 検証記録

- Pythonテスト9件成功。
- .NET接続CLIビルド: 警告0、エラー0。
- 利用者DBの複製に対する登録・再登録成功（商品増加なし）。
- 利用者DBの220商品・220件ずつのEvidence／価格履歴／在庫履歴／検索索引と原本バイト列を照合。
- ブラウザ/API検証16項目成功。全220商品の価格・数量・容量・年号・出典ページ、複合絞り込み、ページ送り、SKU検索、容量警告、原表表示を確認。
- 証跡: `artifacts/wine-experience-import.json`、`artifacts/wine-experience-db-verification.json`、`artifacts/wine-experience-ui-verification.json`。
- 登録前バックアップ: `artifacts/catalog-before-wine-experience.dump`。復元する場合は後続の他仕入先登録を消さないよう、別DBに復元して確認してください。

ブラウザ検証の再実行は `node tests/verify-catalog-search.cjs`。PlaywrightとEdgeを使用し、起動した検証ブラウザは終了します。検索サービスは利用者が検索できるよう起動状態を維持します。

## フィネス・ローヤルのDB登録（2026年9月20日）

2社612件を追加し、利用者用DBの登録総数は4,795件になりました。ワインエクスペリエンス220件、Wine to Style1,061件、LUC2,902件も保持しています。

| 仕入先 | 登録件数 | 掲載時点の在庫状態 | 今回の登録分に限定する検索語 |
| --- | ---: | --- | --- |
| フィネス | 489 | 在庫あり270、少量95、欠品124 | `フィネス FINESSE-` |
| ローヤルオブジャパン | 123 | 不明111、入荷予定11、欠品1 | `ローヤルオブジャパン ROYAL-` |

「フィネス」だけでも検索できますが、既存の他社商品にある「フィネス」という説明も検索対象です。登録分だけを見る場合は表の検索語を使ってください。商品名、生産者、年号、価格、容量による検索もできます。

### フィネスの解釈

- 商品表のあるPDF15～39ページを処理し、年号・容量・通常／特別ロットの別に489行を保持。前半の表紙、案内、生産者紹介、索引は商品登録せず、原本・テキストを保存。
- 資料は2026年9月版。日の記載がないためDB基準日は月初の2026年9月1日とし、その扱いを各行の原表情報に明記。
- `△` は12本以下。正確な数量は不明なのでnull。`〇`・`×`も原表の記号を保存。
- 欠品124行では容量・年号・価格・入数が空欄。750mlや0円を補完せず、価格null・年号空欄、容量と入数は既存DBの未設定値0で登録。画面では容量を「規格を確認」、価格を「金額記載なし／要確認」と表示。
- オープン価格8行は金額null。特別ロットの納品条件と価格帯は原表情報・注意欄で保持。
- 同じ商品の複数年号と特別ロットは別の掲載行として扱い、自動統合しない。

### ローヤルの解釈

- 表記日2026年9月14日、原ファイル名では2026年10月以降のリスト。DB基準日は資料の表記日、検索結果の注意欄に価格適用月を別途明示。
- 参考価格（税別）を検索用小売価格として登録。卸価格（混載3cs・税別）は別の原表項目に保持し、小売価格と混同しない。
- 在庫欄はない。商品注記に欠品・入荷予定が明記されたものだけ状態へ反映。予定日が過ぎていても入荷済みとは推測しない。
- 330mlのシードル、500mlの商品も含め全123行を保持。半角カナは検索用に正規化し、原表記はEvidenceに保持。

2社とも仕入先SKUの記載がないため、`FINESSE-` / `ROYAL-` で始まる内部識別子を生成しています。これは仕入先の正式な発注コードではありません。既存画面の「仕入先SKU」「原本の商品コード」には内部識別子が表示されるため、発注には原本の商品名・年号・容量を参照してください。ローヤルのNoは掲載順であり、識別子として転用していません。

### 再実行

```powershell
python tools/pdf_import.py 'catalogs/フィネス.カタログ.202609.pdf' --parser finesse
python tools/catalog_export.py 'catalogs/フィネス.カタログ.202609.meta.json' --observed-at 2026-09-01 --output 'catalogs/フィネス.カタログ.202609.catalog.json'
python tools/pdf_import.py 'catalogs/ローヤルオブジャパン.ワインリスト.202610.pdf' --parser royal
python tools/catalog_export.py 'catalogs/ローヤルオブジャパン.ワインリスト.202610.meta.json' --observed-at 2026-09-14 --output 'catalogs/ローヤルオブジャパン.ワインリスト.202610.catalog.json'
```

DB登録・全件照合は上記 `CatalogPdfBridge` のJSON/PDFパスを各社のファイルに変更して実行します。未記載容量・入数0を許容するため、今回更新した接続CLIをビルドして使ってください。本体アプリ・DBスキーマは変更していません。

Pythonテスト14件、ブラウザ/API19項目成功。フィネスの数値がある全365行は本文から別の方法で取り出した価格・年号・容量・入数・在庫記号と照合しました。両社とも複製DBで再登録して重複が増えないことを確認し、利用者DBで612件すべての商品・Evidence・価格在庫履歴・索引・原本PDFを照合しました。

検証スクリプト: `tests/test_supplier_pdfs.py`、`tests/verify-other-pdf-search.cjs`。記録: `artifacts/other-pdf-ui-verification.json` と各社の `*-import.json` / `*-verification.json`。登録前バックアップは `artifacts/catalog-before-finesse-royal.dump`。
