# 選択商品の確認・CSV出力（2026-09-29）

## 本番適用完了（2026-09-29 11:15 JST）

Dell管理者による受信開始後、相互TLS一括転送を2.87秒で完了し、サーバーのサイズ・SHA256一致ACKを取得。標準prepareの読み取り専用プレビュー7732件を通過後、package `9d8aae82659e4a85fd8fb528c44ee18ed18a5652cd277b94faa181d0a96534ed`（source `17bff0dc39ed1ca72e52419fd9b49bea6604a046`）を適用した。

バックアップ: `/var/lib/catalog-deploy/backups/20260929T021521-2618144.dump`。Web/DB healthy、DBイメージ・schema不変、DB再起動なし。本番7732件。実画面で取扱先検索6件、2021/[2021]同一検索、NV統一438件、検索をまたぐ2商品の選択保持、確認リストで1件解除、残る1件だけのCSVを確認。ブラウザエラー0。

証跡: `artifacts/vintage-normalization-20260929/prepare.log`、`deploy.log`、`live-after.json`、`status-after.json`、`production-acceptance.json`、`production-selection.png`。本番検証スクリプト: `tests/verify-selection-production-20260929.cjs`。以下の「未転送・本番未適用」は受信開始前の履歴。

## 確定仕様

利用者は検索結果から商品を選択し、CSVボタンで選択中リストを確認して選択解除できる。リスト内のCSVボタンで残った選択商品のみ出力する。

再検索時の扱いを確認し、利用者から「保持して追加してください」と回答を得た。再検索・条件クリア・ページ移動・ソート・0件・検索失敗でも選択を保持し、別検索から追加できる。ページを再読み込み/離脱した場合の永続保存は今回含まない。

- 選択は初期0件。0件ではCSV不可。CSV確認ボタン自体は即ダウンロードせず、モーダルを開く。
- 安定したSupplierProductIdで管理。同一SKUでも別取引先は別商品。再取得した選択済みIDは最新取得値へ更新し、二重に追加しない。
- 確認リストでの解除は検索結果のチェックにも反映。全件解除で空状態を表示。
- 選択中の全商品を現在のソート順でCSV化。従来のBOM・エスケープ・数式対策・小数価格を保持。
- CSV失敗時はリスト内にエラーを表示し、選択を保持する。
- ヴィンテージ統一・取扱先自由文検索も今回版に含む。DB/schema更新なし。
- `/api/options`の既定応答は互換性を維持。検索画面は `vintageNotation=canonical` を指定して統一した年号/NVを取得する。本番prepareの既存の選択肢・件数比較ガードは変更しない。

## 検証

181 .NETテスト成功・環境依存7skip。専用PostgreSQLで年号・取扱先の38テスト成功（skipなし）。Web build警告0・エラー0。Edgeで再検索・ページ移動・ソート・同一SKU別取引先・原本更新後再取得・0件・検索失敗・解除・CSV実ファイル・小数・ダウンロード失敗・スマートフォン幅を確認。年号・取扱先検索の実API/画面テストも選択CSVの導線に更新して成功。

テスト：`tests/verify-selected-csv.cjs`、`tests/verify-vintage-normalization.cjs`、`tests/verify-supplier-natural-search.cjs`。証跡と画面：`artifacts/vintage-normalization-20260929/selection-proof.json`、`selected-review.png`、`selected-mobile.png`。

## 本番展開の状態

利用者の「作業が終わったら本番へ展開してください」に基づく展開指示あり。再確認は不要。

本番inspectで基準source `f226f0a98d74253af4100e35d0f4d43ae898cb9f`、package `ea1736f9d5f9976e2d9d6fd57df477513b426f891f9f9fba3cf1fbdb0d4d656f`、Web/DB healthyを再確認。候補source `17bff0dc39ed1ca72e52419fd9b49bea6604a046`。

- Web payload SHA256: `7e9d0bd02f92ed690a68a679c025254dacc5c18f9fe109eb10c702d1c1925fe9`
- ZIP: `artifacts/vintage-normalization-20260929/release/LIPS-SearchSelection-20260929.zip`、7,355,282 bytes
- ZIP SHA256: `292CA1AE0D1DC5E4353D696D131229FE896D9E3BD90C48FAC931CCD3E4FDE43E`
- 受信キット: `artifacts/vintage-normalization-20260929/LIPS-SearchSelection-20260929-Receiver.zip`

**未転送・本番未適用。** TCP5987は待受なし。今回対象の管理者受信開始が必要。9月25日の共通指示により相互TLS一括転送を使用し、未登録/停止中を理由にSSH uploadやiWSET JEAへ迂回しない。

Dellで受信キットを展開し、管理者PowerShellで実行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Receive-SearchSelectionBulk.ps1 -Receive -ApproveTemporaryFirewall
```

送信元制限付きの一時TCP5987受信規則を作成し終了時に削除。既存証明書を使用。今回のZIP/proofだけを受信する。READY後、Codex側は受信キット内 `Send-SearchSelectionBulk.ps1 -SourceDirectory <release絶対パス>` をWindows PowerShellで実行する。受信後サイズ/SHAのACKを保存。受信処理はZIPを検証してペイロードを `/home/catalogdeploy/incoming/<payloadSHA>.tar` に配置し再検証する。ここではアプリ切替をしない。

その後に最新inspect再照合、専用 `client.py prepare --sha256 <payloadSHA>`、返されたpackageSha256への `client.py deploy`、公開API/画面/CSV/DB件数・healthyの確認を行う。期待版不一致ではガード値だけ変更しない。問題時は既存rollbackと稼働版の確認を用いる。

予定受信先は `C:\ProgramData\LIPS-Codex\Inbox\LIPS-SearchSelection-20260929`。実受信の証跡が得られるまでは転送済みと扱わない。
