# 自由文検索のDell展開準備（2026-09-21）

## 本番展開完了（2026-09-21）

以下は準備時点の履歴です。管理者が正確なパッケージをapprove後、ユーザーの指示に従い専用SSHで展開を実施し、正常終了を確認しました。

- 適用SHA: `33459fd747b89872af1622fc1ba8f61de3fad9947e7dc1c6f13e34b65f9ee30c`
- Webイメージ: `sha256:cbfa10b30124e407baf7c58bfdfe1b3d780b8e1ab81752c0a458e99066c66b95`
- schema-check: ready=true、pending=0、unknown=0。
- 適用前バックアップ: `/var/lib/catalog-deploy/backups/20260921T084824-514296.dump`。
- 展開後status: 新パッケージに一致、Web/PostgreSQLともhealthy。DBイメージ・DBコンテナの再起動なし。
- 本番公開URL `http://192.168.1.5:55441` でAPI/Edge19項目成功。要求例12件、税抜30000円の境界、容量1500ml、産地/発泡、在庫条件、手動条件との積集合、矛盾時400、従来SKU検索、条件表示・クリアを確認。
- 証跡: `artifacts/natural-search-release/production-deployment.json`、`production-verification.json`、`production-search.png`。
- DB migration・原本更新なし。旧版向けprepare.shの再実行は不要です。

利用者から動作確認後の本番展開指示あり。現行Web/DB healthy、現行パッケージ `cce3ae0d23ea70372325db65002b95809fcb14fa4de034e0f9281140b5f256c4` を専用SSHのstatusで再確認済み。本番変更はまだありません。

## 検証済み配布候補

- 基準: `17465b2c19adb4809c9a6802d9bc00de1fd39e32` のカタログソース。今回の自由文検索6ファイルだけを追加/変更。他の仕入先対応や価格注記の未コミット変更は含めません。
- 分離したソーススナップショット: `ddd821aeab2f2501681204b1101e06438d894cbc`。ローカルの `artifacts/natural-search-release/source` にGit記録を保持、ZIP内のsource.tar.gzでも確認可能。
- 自動51件成功・専用DB/実原本依存5件skip。Linux向けframework-dependent publish成功。その配布用バイナリをWindows .NETで起動し、実ローカルDB/API/Edge19項目成功。要求例12件、在庫あり10件。
- DBモデル/Migration/原本変更なし。Linuxコンテナ実行はDellでの準備処理に含めており、まだ未検証。
- ZIP: `artifacts/LIPS-natural-search-Dell.zip`（7,698,143 bytes）。SHA256: `d15395d0c6801ea0d7dff845392e9e399c30cb2620a17e2b528492f6c2b5ca67`。

## 現在のブロッカー

開発PCにDockerもWSLディストリビューションもありません。Dell専用SSHはstatus/upload/deploy/rollbackのみで、ビルド・管理者approveを実行できません。認証・権限制限を拡張せず、Dell管理者が次の準備と登録を実行する必要があります。会話上の展開承認の再取得ではなく、サーバー側の実行権限が必要です。

## Dellでの操作

ZIPをDellにダウンロードし、`LIPS-natural-search-Dell` フォルダーを展開します。そのフォルダーでUbuntu-24.04から `sudo bash prepare.sh` を実行します。

準備スクリプトは各ファイルのSHA、現行パッケージ/Web/DBイメージ、healthy、スキーマ指紋を検証します。既存の固定Webイメージに新Webのみを重ね、CLI・runtime・entrypointは維持します。別ポート55443・フォルダー監視OFF・DB接続read-onlyで候補を起動して、現行検索結果との一致、在庫、容量/価格、条件エラーを検証します。プレビューは終了時に削除します。本番Webは切り替えません。

成功時に既存のcompose.yaml・DBイメージ・スキーマ指紋をそのまま使った正規形式のpackage.tarを作成し、incomingへ配置します。verification.jsonと出力SHAを確認し、表示された `sudo /usr/local/sbin/catalogctl approve <SHA>` をDell管理者が実行します。root保護の承認済み領域への登録を省略しません。

そのSHAをCodexへ返せば、既に受けた展開指示に従い `tools/remote-deploy/client.py deploy --sha256 <SHA>` で展開できます。既存コントローラーがschema-check・バックアップ・Web更新・失敗時の旧Web復旧を実施します。最後に本番公開URLで件数、自由文検索、手動検索を再確認します。

スキーマ不一致や現行パッケージ変更なら準備は停止します。DB migration/restoreや既存パッケージの上書きで回避しないでください。

## スキーマ指紋修正（v2）

初版ZIPはWindows CRLFの作業ファイルから指紋を計算していたため、Dellでスキーマ指紋不一致により停止。Web切替・DB変更前の停止です。v2は基準コミットのGit blob（LF）から算出し、7ファイルすべてがCRLF除去後の作業ファイルと一致することを確認しました。PythonとGNU sha256sumのLinux相当のテキスト形式で計算を照合。期待値は `ab882479317a3832cd497f58135d6541137c9653f648541d8ce74a4dfecc0f71`。本番の照合は維持し、不一致時には両方の値を表示します。

修正版: `artifacts/LIPS-natural-search-Dell-v2.zip`、SHA256 `1a45ddec1e50d5b75824a0b9caeee7ac0b977f644a8b6b727349af1070d24b68`。初版ZIPの代わりに使用してください。アプリバイナリは変更していません。
