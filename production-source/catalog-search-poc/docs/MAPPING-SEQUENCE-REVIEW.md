# 疑義の順次確認と再利用保存の競合対策

2026-10-12。対象は隔離環境。今回の本番適用依頼は受領済みだが、本番操作は未実施。

## 確認方法

P14sで http://127.0.0.1:56006/intake-review.html?id=d4c94543-75de-4e0f-990e-997f1041a4bb を開く。
質問選択、前・次・未回答への移動で、原典のシート/行と回答対象が一致することを確認する。
行を移っても入力は保持される。回答保存、次回用ルール保存、取込再開は別操作。
56006の回答は今回の試験で変更していない。

最新バックエンドを含む隔離確認用は http://127.0.0.1:56007/intake-review.html?id=d4c94543-75de-4e0f-990e-997f1041a4bb 。
DBは復元合成専用 lips_migration_restore_20261012、port55993。既存56006とは独立。
56007はビルド出力へwwwrootを配置して起動。HTTP200、Held、未回答2件を確認。

## 修正と検証

- 資料内の質問選択、前後移動、次未回答、原典画像と選択行の一致確認。
- 描画中・保存結果不明・Unknown/Runningの操作抑止、未保存入力保持。
- 再利用ルール保存は回答元レビューを同一トランザクションで行ロックし、版/hashを再検証。
- 実Excel画像GETを使用したブラウザー24項目、既存UI回帰17項目合格。保存POSTは代替応答、実回答前後一致。
- 専用復元DBで回答更新先行/ルール保存先行の実ロック待機を含む5試験合格、skip0。
- Web統合ビルド0警告0エラー。

証拠はartifact直下 mapping-sequence-proof.json、verify-mapping-sequence.cjs。
実装対象の業務回答はLUC容量単位。全取引先・全疑義種別の受入完了ではない。

## 本番までの残項目

既存許可済みDell接続経路の復旧、現行inspectとの照合、正式候補作成・対象登録・一括転送、Dell管理者のバックアップ付きDB適用、gateway/実PC受入。
Linux担当の独立合成描画は正例と5拒否試験合格との報告。LIPS backend統合ではない。
内部ソース4ファイルのLIQUOR-BASE送信は自動承認レビュー拒否後の本人回答待ち。別経路送信は行わない。

## 保存結果不明のキュー停止（追加修正）

HTTP200 stopQueue、HTTP500、通信中断、不正receiptでは、そのページの開始・フォルダー選択をロックする。未処理行を保持し、保存履歴の照合後に再読み込みする案内を表示する。通常の保留は後続処理を継続し、手動停止は再開可能。
tests/verify-folder-unknown-stop.cjs は35チェック合格（合成通信、実DB書込なし）。永続キュー復元や再読み込み後の自動照合を実装したという意味ではない。

## 2026-10-12 全行回答後の再利用（本人確定）

同じ取引先・帳票の容量単位は、対象列の全行の回答が揃い、意味が一致してから再利用登録する。APIも保存原本をhash検証し質問集合を復元して判定する。Completed後の結果から質問が消えていても条件を緩めない。部分回答、矛盾、古い回答、Unknown/Runningは拒否する。保存・再利用登録・取込再開は別操作。

IntakeRulePublicationTests 12件、IntakeRepeatReceiptTests 7件合格。build 0警告0エラー。隔離Web56008 / lips_migration_restore_20261012で実API8項目合格（部分回答拒否、operationなし、別々の保存で全回答保持、全件後登録・再取得、取込未実行、原本不変）。証拠: artifacts/folder-ui-20261011/mapping-integrated-c76769a58d/all-answer-api-resumed-proof.json。試験スクリプトのOrigin欠落と空404本文解析を訂正し、保存済み状態から再開した。

確認PCはTHINKPAD-P14S。http://127.0.0.1:56008/intake-review.html?id=0ae66446-5391-42ed-9ced-1c24ca5859be は回答・ルール保存済み、取込再開前の合成資料。本番適用は未実施。

同一Completed資料の再アップロードは取込済みreceiptを返すよう補正した。保存済み結果・履歴を変更しない（単体検証、本番未適用）。

## 392484b の実ブラウザー通し受入

56008 / 復元合成DBで29項目合格。親フォルダー選択、原典Excel行6/7表示、順次回答、保存後の再読込、全行後の明示的ルール登録、明示的取込再開、次日資料への限定再利用、新原典の価格/在庫/容量、再アップロード時の二重防止、原本SHA不変を実APIで検証した。通信interceptなし。証拠は mapping-integrated-c63c43ec8e/integrated-proof.json。LUC容量単位の受入であり、全取引先/全疑義種別の完了を意味しない。

初回の別合成資料 mapping-integrated-3ad2de79ec は既存日付10/6と重なり同日別内容ガードでHeldになった。上書きせず記録を保持し、未使用10/8・10/9の新合成資料で検証した。

Completed再アップロード単独4項目も合格。取込済みreceipt、商品総数不変、review全体(履歴/版含む)不変、原本bytes不変。証拠は mapping-integrated-b36e89d654/repeat-receipt-live-proof.json。

P14s確認: http://127.0.0.1:56008/intake-review.html?id=21c2888a-c34a-406e-aaed-436166ce5df6 で原典と保存済み回答/履歴を確認し、workspace検索 E2E-c63c43ec8e で次回資料の500ml/3000ml・3300円/9000円・在庫8/数量不明を確認する。全て合成で実在庫ではない。本番未操作。

## 同一対象の初回再利用保存の競合

別画面が同じ対象の初回設定を別RuleIdで同時保存すると複数identityができ、次回が競合保留になる欠陥を修正。取引先・帳票・fingerprint・商品/VT・種別・対象列を含むscope単位でtransaction lockし、新規identity時は失効済みを含め既存identityを照合する。既存データの自動統合/削除はしない。同IDのCASとoperation再取得は維持。

復元合成DBで8件合格skip0（4並行初回保存1勝、失効後の別ID拒否、別scope許可、既存ロック/CAS/冪等回帰）。build0警告0エラー。56009実API3項目合格（409、操作記録404、ルール全件不変）。証拠scope-rule-api-proof.json。SQL変更なし。本番未操作。

P14s http://127.0.0.1:56009/workspace.html 起動。既存56008の合成reviewを同じIDで参照可能。競合表示が出た場合は自動再送せず、画面を再読込して既存ルールと最新版を取得してから操作する。現在UIは競合後も保存状態照合のため取込再開を停止する。

## 再利用設定の取消画面

P14s http://127.0.0.1:56010/intake-rules.html 。workspaceの「次回取込の再利用設定」から開く。一覧で対象を選び、元回答/原典・変更履歴を確認し、理由と停止確認を入力して取り消す。次回から確認質問へ戻る。既存商品、原典、元回答を変更せず、取消理由を別項目で新しい版へ追記する。復活はこの画面では提供しない。

保存不明時は操作IDをブラウザーに保持し、再読込後もGET照合だけで復帰する。未確認なら再取消しない。「保存状態を再確認」で操作記録を照合する。異なる理由のreceiptは受け入れない。

Store11/11合格skip0（復元合成DB）、Web build0警告0エラー。画面11項目合格（合成応答、通信断・再読込・GET復帰）。実Edge/API/DB6項目合格（取消、reload、append-only履歴/原典Evidence保持、元回答/商品数不変、次回2質問に戻る、商品未取込）。証拠rule-management-ui-proof.json / rule-management-live-proof.json / rule-management-live.png。

実試験で取り消したのは合成c76769a58dのルールだけ。確認用の次回保留資料は http://127.0.0.1:56010/intake-review.html?id=ddb5a1f6-f583-4925-8f71-c672afba077f 。SQL変更・本番操作なし。取消時点で既に開始済みの取込を巻き戻す機能ではない。
