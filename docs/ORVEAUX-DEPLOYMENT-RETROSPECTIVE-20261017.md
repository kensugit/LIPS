# オルヴォー展開といろはわいん展開の比較・改善

2026-10-17。対象はLIPS本番への取込対応と今回の作業記録。ユーザーの業務判断待ちと実装・運用の不備を分けて記録する。改善はローカル共通ツールと手順に実装し、進行中のAI接続用受信キット、本番Web、DB、ネットワーク設定は変更していない。

## 結論

難航の主因は、既存商品更新という追加の業務経路と、更新時の検索索引保持漏れで修正版展開が増えたこと。さらにproof転送中断後の再開キットに二重初期化を入れてしまい、Dell操作を増やした。後者2点はアシスタント側の実装・検証不足だった。

| 観点 | いろはわいん最終版 | オルヴォー |
|---|---|---|
| 業務処理 | 同じ原本SHAの172商品を重複スキップ、追加0 | 同日のPDFとExcelを照合し既存160商品を更新、未確定248行を保留 |
| 本番切替 | 比較対象latestの完了記録は1回 | 初期在庫対応d5550e9f、Excel優先faebea58、索引修復757b48eeの3回 |
| 一括転送 | 最終版のACKは0.42秒 | 初期0.41秒、優先版0.43秒。索引版はZIP検証後proof0バイトで60.42秒タイムアウト、proofのみ再開は0.22秒 |
| 手戻り | 既存原本・検索・他社保持を確認 | 生産者名の相違と優先原本の判断、索引欠落、受信復旧、診断の追加取得 |

転送自体が一貫して遅かったわけではない。総作業時間には本人操作待ち等が含まれるため、これらの秒数を案件全体の所要時間と比較しない。

## 原因と確度

1. **確定：業務範囲の違い。** いろはは同一原本の重複スキップ。オルヴォーは旧PDFの生産者とExcelの生産者が異なるTinto等を含み、126/90本も競合した。本人が「定番品126本」「今回Excel優先・対応確定商品から更新」「容量未確定の新着を保留」と決めた。この確認は必要であり、推測による省略は改善に含めない。ただし候補作成前の既存原本・同日別内容・商品照合の調査を先に行えば、仕様確定前の展開を減らせる。
2. **確定：検索索引の保持漏れ。** source0f6ea528の更新処理はSearchTextを限られた項目で置換し、取引先名・過去の説明等を落とした。SKUで390件取得できる一方、取引先名検索ではオルヴォー230件となった。本番総数だけの検証では検出できない。3cf2a1dで既存索引と過去Evidenceから語を保持・復元し、取引先名390件、160商品12項目、他230商品保持を確認済み。隔離DBの回帰も追加済み。
3. **未確定：proof転送の通信中断の根本原因。** 両端の記録から、ZIP7,837,317バイトのSHAは一致し、proof `.upload` は0バイト、送信側は応答待ちタイムアウト、受信側はTransfer interrupted。一時ルール・待受の削除は成功した。同じエンジンでいろはと前2版は成功し、proof専用再開も成功。ネットワーク、コンソール選択停止、TLS内部などのどれが原因かは裏付けがなく断定しない。タイムアウト延長・再送自動化・認証緩和は行っていない。
4. **確定：再開キットr1の二重初期化。** Activationが検査用にエンジンを読み込み、本受信の前提検査が同じエンジンを再読込して拒否した。新しいPowerShellで実行しても失敗する。個別関数のガードテストは通ったが、Activationを含む経路の確認が不足した。r2で事前読込を削除し、本受信直前の1回に統一済み。
5. **確定：診断と状態の見え方。** 起動ラッパーが最終出力でerrorを省き、別ファイルの読取りを再度本人へ依頼した。送信ACK後にもcleanup/WSL配置が続くため、早いprepareは未配置で停止した。索引処理中のRunningを待たずAPI照合し、一時的な旧値に検証失敗したこともある。いずれも再実行ではなく確定した完了結果を待つべきだった。
6. **確認済みの共通不整合：schema指紋。** 汎用build-payload.pyはCRLFをLFに変換し、直下ファイルだけを集計していた。Linuxのdeployment/build-release.shは再帰列挙・元バイトのSHAを使う。これまで別のraw-schemaスクリプトで回避していた差であり、今回だけの通信障害とは別件。共通ビルダーを正規契約へ合わせた。

PS1のハッシュ不一致と、Applyなし起動によるPlan onlyも発生した。ダウンロードされた実バイトの不一致原因は断定しない。版別ZIP、新しいGUID展開先、内包PS1のハッシュ照合、実行引数の固定を継続する。

## 今回実装した改善

- `tools/release-preflight/inspect_kit.py`：配布ラッパーと展開後kit、制御ハッシュ、送受信manifest、固定ZIP/proofの一致を配布前に検証。
- `Test-ReceiverLifecycle.ps1`：PS5.1構文とActivationからのエンジン事前読込を検査。今回の二重初期化を再現したコードを拒否する。
- `templates/Start-LipsR4.ps1`：最終JSONに失敗理由を出し、error取得だけの往復を削減。新規キット生成時に採用し、全ハッシュを再固定する。
- `Read-TransferOutcome.ps1`：失敗理由・完了ファイル/部分ファイルのサイズとSHA・cleanup記録を1回で採取。現在のルール/待受とLinux配置は未検査と明示する。
- `wait_intake.py`：原本ハッシュ・review ID・開始前versionを拘束してGETで完了を待つ。Running、古い結果、Unknown、タイムアウトを成功にしない。取込POSTはしない。
- `tools/remote-deploy/build-payload.py`：Linuxと同じ元バイト・再帰列挙・順序でschema指紋を計算。実際のMigration差異は従来通り拒否する。

使用方法は[共通検査README](../tools/release-preflight/README.md)。本番展開前の検査順に組み込む。AI工程は別担当が予約を保有しており、稼働中・配布済みキットを差し替えない。

## 検証と根拠

自動テストは `tests/test_release_preflight.py` の19件と既存の `tests/test_deploy_prepare.py` の3件、計22件成功。二重初期化拒否、実いろは/r2キット整合、起動ファイルSHAの更新漏れ、Running→Partial、古いversion、Unknown、タイムアウト、原本差異、診断JSON、CRLF/再帰schema、実本番ソース指紋を確認した。結果件数とコマンドは `outputs/orveaux-202610/retrospective/verification.json` に保存。

共通build-payload.pyを実本番ソース3cf2a1dと保存済みinspectでローカル実行し、182ファイルとmanifestが適用済み索引版payloadの内容と完全一致した。検証用tarは `retrospective/local-validation-only.tar`。送信・候補登録・本番適用には使用していない。診断ツールと新テンプレートのWindows PowerShell 5.1実行はローカル検証であり、次回Dellでの実運用結果は後続担当が記録する。

主要な実績証拠：

- `artifacts/iroha-20261017/latest/deployment-proof.json` と `transfer-20261017-130516.json`
- `outputs/orveaux-202610/latest/deploy.log`、`priority/deploy.log`、`index-fix/deploy.log`
- `outputs/orveaux-202610/index-fix/transfer-*.json`、`staging-receipt.json`、`deployment-proof.json`
- `outputs/orveaux-202610/index-fix/accepted-handoff.json`（SHA69A710EC33C22F9B8BA66CA4C8D09244CD678A6F7CBDB0DDA65859FF677E558D）

サザンクロスの後続候補は、AI担当の完了引渡し後の最新本番から作る。古い総数15,389や旧packageを今後の正解として固定しない。受信/切替の共有予約と、AI有効後のWeb/llm-link同時再作成の手順を維持する。
