# LIPS展開前・受信結果の検査

対象別の候補・受信キットを固定した後、Dellへ案内する前に使用する。ローカル検査とGET待機だけを行い、受信開始、Firewall変更、送信、本番適用は行わない。共通予約は `artifacts/release-coordination` を使う。

## 配布前

```powershell
python tools/release-preflight/inspect_kit.py --wrapper <受信PS1> --kit <受信キットディレクトリ> --sources <固定ZIPとproofのディレクトリ>
powershell.exe -NoProfile -File tools/release-preflight/Test-ReceiverLifecycle.ps1 -KitDirectory <受信キットディレクトリ>
```

1つ目は、配布する自己完結PS1の埋込ファイル・サイズ・SHA、展開後ファイル、制御ハッシュ、Activation内の起動ファイルSHA、送受信manifest、実ZIP/proofを照合する。配布ファイルを作り直した後に実行する。
2つ目はPowerShell 5.1構文とエンジン読込経路を検査する。Activationによる事前 `Initialize-CandidateEngine`、`Add-Type`、受信定義のdot-source、事前再開検査を拒否する。エンジンは本受信の前提検査で一度だけ読み込む。静的検査であり、Dellの実ACL・証明書・Firewall検査の代用ではない。

現行R4形式が対象。新しい制御形式はこの検査を明示的に拡張・テストする。受信キットに含まれる任意コードを検査目的で実行しない。

配布は版名を付けたZIPとし、展開先を新しいGUIDディレクトリにする。内包PS1の固定SHAを確認後、`powershell.exe -NoProfile -File` で起動する。初回受信・確認済み位置からの再開は別の固定キットとし、再開では受信済みZIP・部分位置・前回cleanup・現在ルール/待受・ACLを照合する。既存ファイルの検査を省くために保存先やガード値を変更しない。

## エラーをその場で表示するテンプレート

`templates/Start-LipsR4.ps1` は検証済みR4起動手順と同じ処理に、最終JSONの `error` を追加したもの。次の新規キット生成時に採用し、control-hashes、ActivationのStartハッシュ、自己完結PS1の埋込ハッシュをすべて再生成する。既に配布・受信待機中の固定キットは変更しない。証明書・接続元・ポート・Allow/Block・再送条件は変更しない。

## 中断時の1回の診断

Dellで次を実行すると、失敗理由、完了ファイル/`.upload`のサイズ・SHA、cleanup記録が1つのJSONにまとまる。結果とmanifestは今回の固定キットの実パスを使用する。

```powershell
powershell.exe -NoProfile -File .\Read-TransferOutcome.ps1 -ResultPath <今回のlips-r4-result.json> -ManifestPath <今回のcandidate-manifest.json>
```

このツールは読取専用。cleanup記録は履歴であり、現在のポート/ルール不在までは証明しないと出力する。Linux配置も別確認のまま残す。診断結果を見てから固定位置再開を準備し、本人のREADY後に送信する。自動再送・再適用はしない。

## 取込完了後にAPIを検証

UIの処理開始直後には旧商品値が返ることがある。開始前のreview versionと原本hashを控え、GETだけで処理完了を確認してから商品検証する。

```powershell
python tools/release-preflight/wait_intake.py --base http://192.168.1.5:55441 --id <review UUID> --hash <原本SHA256> --after-version <開始前version> --output <新規結果JSON>
```

Running等は待機、Unknown/同一性変更/タイムアウトは停止する。新しい版のPartial/Held/Failedもその状態を保存し、全件成功と扱わない。HTTPエラー時にも取込POSTを再実行しない。バージョンと原本ハッシュの一致は、別操作の結果を誤って完了としないために必要。

## 商品更新を伴う候補の受入

いろはわいんのような同一原本重複スキップだけでは、既存商品を更新する経路は検証できない。隔離DBで既存原本・既存商品を準備し、更新前後のID、今回確定した値、対象外商品、履歴、検索語を比較する。取引先名・SKU・生産者・葡萄・説明・JAN検索も含め、再実行で新しい価格/在庫/Evidenceを増やさないことを確認する。完了後の本番検証でも取引先名による検索を行う。

## 検証

```powershell
python -B -m unittest discover -s tests -p test_release_preflight.py -v
python -B -m unittest discover -s tests -p test_deploy_prepare.py -v
```

商品側の索引保持・復元回帰は、現行本番ソース3cf2a1dのOrveauxExcelPriorityTestsに含まれる。今回の共通ツール追加は本番Webの変更を必要としない。
