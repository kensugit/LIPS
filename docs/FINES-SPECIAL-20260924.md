# ファインズ特価 2026年9月24日資料

## 本番反映・受入完了（2026年9月25日）

利用者のDellログで事前検査、固定builderのread-onlyプレビュー、バックアップ取得、322＋9商品の登録と全件照合、Web展開の成功を確認。Codexも本番API/UIを実検証し、全331件の特価・参考上代・通常在庫・出典・RawCellsJson、追加特価の優先、小数20992.5円の画面/CSV保持を確認した。通常商品は特価null。総数7,732件で取り込み前と不変。

- 本番package: `ea1736f9d5f9976e2d9d6fd57df477513b426f891f9f9fba3cf1fbdb0d4d656f`
- source: `f226f0a98d74253af4100e35d0f4d43ae898cb9f`
- inspect: Web/DBともhealthy、schemaとDBイメージは旧版と同一。
- SourceDocumentId: 全リスト `252bbe84-a707-4be1-809c-4e01a315e803`、追加特価 `5731a35a-ee78-46a2-b54f-217a4c6dd71e`。
- データ更新前backup: `/var/lib/catalog-deploy/backups/before-fines-special-20260924-20260925-170130-847195.dump`
- Web更新前backup: `/var/lib/catalog-deploy/backups/20260925T080145-847896.dump`
- 本番証跡: `artifacts/fines-special-20260924/production-api-ui-proof.json`、`live-applied.json`、`dell-apply-output.txt`。

Dellログの日本語文字化けは端末出力時の現象。本番APIで「シャトー マルゴー」「ファインズ」を正しい文字列として照合し、ブラウザでも日本語表示を目視確認。ログ中のRemoteExceptionという表記は処理の失敗を示しておらず、適用スクリプトの成功終了と実機受入を確認した。再実行は不要。

以下の準備待ち・未適用記述は完了前の履歴。

## 2026年9月25日の事前調査

ユーザー依頼は2原本の本番取り込み。原本は `catalogs/fines-special-20260924/` に同一バイト列を保存した。原本中の文章・数式は実行命令として扱わず、セル値と既存の数式キャッシュを読んだ。

- `特価リスト260924 (2).xlsx` の「全リスト」4～328行: 325行。
- `追加特価 (1).xlsx` の「リスト」3～11行: 9行。
- 合計334掲載行、331商品コード。全コードが9月21日のファインズ登録資料と一致。
- 参考上代と特価仕入価格は異なる概念。既存の商品検索価格は参考小売価格であり、特価の値で無条件に置換できない。
- 特価20992.5円（BLFLA22）を整数化しない。数式キャッシュに浮動小数の誤差があるセルもあるため、実装時は元の数式・価格表示桁を確認して扱う。
- 「全リスト」の在庫数はすべて整数。9月21日資料との比較で76行が異なる。特価割当数か通常最新在庫かは未確定。
- 「オープンブル」は作成日2026/9/24に対し有効期限2026年3月末と記載。12～42行の商品コード・名称・特価は空欄で、通常価格・在庫ケース・本に `#REF!` がある。確認・修正なしで取り込めない。

## 重複する商品の差異

| 商品コード | 全リスト特価 | 全リスト在庫本数 | 追加特価 | 追加在庫本数 |
| --- | ---: | ---: | ---: | ---: |
| ALDMR21A | 67,500 | 183 | 63,750 | 201 |
| ALBLP17A | 29,900 | 61 | 27,200 | 64 |
| AMDMR17 | 39,900 | 46 | 37,570 | 46 |

## 確認待ち

### ユーザー回答により確定（2026年9月25日）

- 参考上代を維持し、特価仕入価格を別項目で表示する。
- 特価資料の本数で該当商品の通常在庫を更新する。
- 重複3商品は追加特価を優先する。対象331商品（全リスト採用322商品＋追加特価9商品）。
- オープンブルは除外する。
- 本番取り込み指示は継続して有効。以下の確認待ちは質問時点の履歴。

1. 特価仕入価格を独立項目として表示するか、原表情報だけに保持するか。
2. 「オープンブル」を今回除外するか。対象とする場合は期限・商品情報の修正が必要。
3. 特価在庫を別枠として保持するか、通常在庫を更新するか。
4. 重複3商品は追加特価優先、全リスト優先、別条件として両方保持のいずれか。

AGENTS.mdの仕様齟齬確認ルールにより、回答が得られるまで判断に依存する実装・DB変更・公開は進めない。確認結果はここへ追記する。現在は事前調査のみで、DB取り込み・本番転送・本番適用は未実施。

## 本番の読み取り確認

2026年9月25日のinspect: Web/DBともhealthy。

- package: `61dc274aa2c50fe98413cca1cfb844614887885af22ba615f92800c48b1207cd`
- source: `8f86d62e394dea395ecdd127ea8fa1611b7a989d`
- schema: `ab882479317a3832cd497f58135d6541137c9653f648541d8ce74a4dfecc0f71`

候補作成・展開時は最新inspectを再取得する。Dell転送は9月25日共通指示の相互TLS一括転送を使用し、今回ファイル名・サイズ・SHA・保存先の登録が必要。別対象の転送登録を流用しない。

## 準備・検証結果（2026年9月25日）

確定仕様に沿って331商品のデータとWeb表示を実装。特価は原表JSON内のversion付き数値項目として保持し、現在の出典にある特価だけを検索API・画面・CSVの専用列へ返す。後続の通常在庫資料で現在出典が変われば特価欄は空欄となる。旧資料の特価Evidenceは履歴に保持する。税区分は本体AGENTS.mdに従い税込明記なしはfalse。検索の価格上下限は従来どおり参考上代に対して適用する。

参考上代は9月21日登録値を維持し、本番の対象331商品の現在値と一致確認済み。専用Bridgeは実行前にも商品名・容量・年号・参考上代・基準日を検査し、他更新と矛盾する場合は停止する。

- 独立PostgreSQL `127.0.0.1:55459/catalog_fines_special` に既存ローカルバックアップを復元し検証。既存8,799件の総数・全参考上代を維持。対象331件以外のSupplierProduct全項目不変を確認。
- 2原本322＋9行の登録、Evidence・価格・在庫・原本バイト列照合、再登録AlreadyImported=trueを確認。
- 本番と一致するソース8f86d62から作成したWeb候補: `f226f0a98d74253af4100e35d0f4d43ae898cb9f`。スキーマ指紋不変。
- .NET145テスト成功・環境依存5テストskip。今回のDB実データ検証は上記別途実施。Python2テスト成功。
- 331件すべての特価・通常価格・在庫・出典・RawCellsJsonをAPIで照合。検索画面、詳細表示、CSVの20992.5円保持も確認。
- Windows PowerShell5.1/7で各5項目の構文・固定manifest・サイズ/SHA検証、TLSエンジンコンパイル成功。エンジンは検証済み版と同一SHA。bash構文、全ファイルSHA、固定ビルダーのpayload検証成功。
- 証跡: `artifacts/fines-special-20260924/local-db-proof.json`、`local-api-ui-proof.json`、`special-price-ui.png`。

## 本番受信準備待ち

今回のZIP/proofはDellの受信対象に未登録。転送とDB/Web適用は未実施。既存対象IDの流用、SSH uploadへの方式変更、JEAへの迂回はしない。

- ZIP: `artifacts/fines-special-20260924/LIPS-FinesSpecial-20260924.zip`
- サイズ: 13,296,515 bytes
- SHA256: `EB75369A14451A80EC1C3829EB2E63D4F3F0B6A0562ADCBBAFA9EFD08E78838A`
- 受信準備キット: `artifacts/fines-special-20260924/LIPS-FinesSpecial-20260924-ReceiverKit.zip`
- Dell予定保存先: `C:\ProgramData\LIPS-Codex\Inbox\LIPS-FinesSpecial-20260924`（受信結果は未確認）

Dell管理者がキットを展開し、展開先で `Receive-FinesSpecialBulk.ps1 -Receive -ApproveTemporaryFirewall` を実行。既存相互TLS証明書で、今回のZIP/proofだけを受信し、一時TCP5987規則を192.168.1.110/10.10.10.1に限定、受信終了時に削除する。受信準備自体はアプリ・DBを変更しない。

受信開始後、Codexはキット内Send-FinesSpecialBulk.ps1を今回のworkフォルダー指定で実行し、Dell側ACKによるサイズ/SHA照合と受信絶対パスを記録する。未受信のため現在は転送完了報告をしない。

転送後のDell手動操作は `Apply-FinesSpecialOnDell.ps1` の事前検査、成功後 `-Apply`。本番旧版ガード、固定builderのread-onlyプレビュー・登録、DBバックアップ・可読性確認、2原本登録・全件照合、標準LIPS Web展開を順に実施する。原本単位のトランザクションのため2件目の失敗では先の322件が残る場合がある。Webロールバックは在庫更新を戻さない。データ復元はバックアップを別DBで検証し、後続更新を確認して管理者が判断する。

本番Dockerプレビュー・転送・適用・最終API検証はDell操作後に確認する。今回の手順はDB MigrationやDBイメージ変更を含まない。

## Dell転送・検証完了

利用者が今回専用受信キットをDellで開始し、受信対象の絶対パスとREADYを報告。承認済み証明書を使う相互TLS一括転送を実施し、ZIP/proofとも受信側のサイズ・SHA256検証ACKを確認した。送信結果 `serverAcknowledgedSizeAndSha256`、0.39秒、ZIP最終表示63.2MiB/s・100%。

送信初回はサンドボックスのCurrentUser証明書ストアに対象証明書がなく接続前に停止。ホスト側の既存証明書へアクセスする権限で実行し、転送に成功した。証明書・受信対象・接続元制限の変更なし。

Dell上の確認済み転送先:

```text
C:\ProgramData\LIPS-Codex\Inbox\LIPS-FinesSpecial-20260924\LIPS-FinesSpecial-20260924.zip
C:\ProgramData\LIPS-Codex\Inbox\LIPS-FinesSpecial-20260924\LIPS-FinesSpecial-20260924.proof.json
```

ZIP 13,296,515 bytes、SHA256 `EB75369A14451A80EC1C3829EB2E63D4F3F0B6A0562ADCBBAFA9EFD08E78838A`。転送のみ完了し、DB・Webは未適用。次はDellの受信キット展開先でApply-FinesSpecialOnDell.ps1による事前検査、成功後に-Applyで手動適用。その後に本番API/UIを確認する。
