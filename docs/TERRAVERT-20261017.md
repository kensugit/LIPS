# テラヴェール一括取込対応（2026-10-17）

対象: 在庫リスト.テラヴェール.20261006.xlsx。SHA256 `1b687a1534982e3dbd83bfda40831968dbfd8f5ec40a13e8d2c1e9dd2605736a`、685519 bytes。原本は未変更。

## 本人の確定回答

ファイル名は20261006だが、Excelの目次C1と国別全一覧P1は2026-10-05。本人に確認し「Excel内の更新日2026/10/05」と回答を受領。元ファイル名・内部更新日を保持し、上記hashの原本に限って資料日付2026-10-05を採用する。画面がファイル名から補完した2026-10-06も、この原本では確認済み10/05へ解決する。他の日付入力は保存せず保留。別hashでは一般の日付一致ガードを維持。

## 実装

専用checkout `outputs/terravert-202610/source`、本番一致基点 `3cf2a1dcebf2e75c13615356a592a9698433dbb5`。TerravertInventoryParserと対応テストを新設し、WorkbookParserRegistryとFolderImportServiceへ局所接続。DB/Migration/Packages/PostgresInventoryStoreは未変更。

既存tools/terravert_import.pyの契約を移植。6シート構成と主一覧のヘッダー/日付を照合、国別3全一覧1064商品を登録対象にする。ケース条件73行・新入港54行は主一覧と商品名/VT/容量/価格/在庫が一致することを照合してRawCellsJsonへ再掲位置・セル値を保持する。参考上代を仕入価格へ変えず、税込明記なしはfalseと注記。在庫○/〇は本数null、予定のーは0へ変換しない。未知記号・重複SKU・再掲不一致で保存しない。

## 検証

- 原本1064件の全件を既存Python出力と商品名・造り手・容量・入数・価格・国・地域・VT原文・在庫・原典位置で照合。
- 在庫あり862、残少44、完売136、予定22。価格未記載1。再掲127位置。
- 専用試験14件成功。DB試験追加後、専用PostgreSQLを含む15件全成功。
- 最終回帰553件成功、環境依存38件skip、失敗0。専用PostgreSQL試験は別途15件全成功。
- 空の専用DB `catalog_terravert_isolated`（127.0.0.1:56149）に現行EFモデルでschema生成、1064件保存・2回目重複skip・原本バイト一致・Evidence/価格/在庫各1064・全件検索1064・在庫あり検索906を確認。これは保存契約の検証であり、本番Migration適用試験ではない。HTTP用には既存追加SQLだけを同専用DBへ適用。
- Linux publish成功。
- ローカルHTTP 56150で一括取込POSTの重複skip・資料日付10/05・1064件・hash・review完了、health/workspace/options/search/reviewsの200を確認。HTTPはDB試験で登録済みの原本を使った重複確認。ブラウザー接続がタイムアウトし、実画面操作の受入は未確認。
- 証拠: `outputs/terravert-202610/test-results`、`source-inspection.json`、`source-catalog.json`、`http-proof.json`。

## 担当間協調と残作業

本人の指示に基づきLIPS主担当01a0ac9e、ファインズ担当01a14878へ照会。主担当から基点・範囲・共通部の専用差分提出・AI→サザンクロス→ファインズの先行順を受領。Dell5987と本番切替は主担当予約下で、独自転送・切替・本人へのDell操作依頼なし。ファインズ+サザンクロス統合候補は未配備のため本番と混同しない。

テラヴェール候補の最終共有部統合は先行作業の正式引渡しと最新inspect一致を確認してから行う。AI有効後はWebとllm-linkを同時再作成するレビュー済み手順を保持。今回依頼では本番展開の明示指示はまだなく、本番転送・切替・業務データ登録は未実施。本番適用前の実画面受入も残る。

固定commitは`de7ed734dfc2ffe8385d3d9e6635fdc4fb56bfdd`、clean確認済み。`outputs/terravert-202610/integration-handoff.json`（SHA256 `06852ee62c6babfe7994f94fef0044963b1be2f196c98494ee57f07f371d6376`）に変更5ファイルのSHA・TRX・patch/bundle・原本hash・日付確定・未完了事項を保持。LIPS主担当とファインズ担当へ送信済み、送信と受領・統合完了は区別する。

2026-10-17、LIPS主担当01a0ac9eから受領回答あり。主担当自身が現物SHA・clean・差分の照合に着手し、正式AI→SC→FINES本番引渡し後のRegistry/FolderImportService統合と再検証を担当すると確認した。日付10/05、原ファイル名10/06保持、当該hash限定例外、実画面未確認を引継ぎ済み。本回答は候補の受領確認であり、現物照合完了・統合完了・本番適用承認ではない。Dell追加操作・5987受信開始は不要との回答を保持する。固定handoff JSONは変更せず、受領証跡を`outputs/terravert-202610/handoff-ack.json`に別保存。

