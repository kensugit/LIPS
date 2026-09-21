# 豊通食料 2026年9月ワインリスト取り込み

## 照合結果

- 原本20ページ、掲載660行。冒頭の泡一覧と産地別の同一内容20行を統合し640商品。重複の掲載箇所はEvidenceにすべて保持。
- SupplierCode: TOYOTSU。商品コードは原表どおり（L67941を含む）。全容量・ビールも保持。
- 税抜小売価格。空欄101件、open26件はnull。
- 在庫あり444件、少量74件、欠品122件。記号在庫の正確な数量は未設定。
- 原本は2026年9月表記のみ。基準日2026-09-01は月初の代表値であり、実際の更新日と断定しない。入荷予定欄を保持。
- 原本SHA256: bb132e2ee2e89d271ed6954d98591d1feb53f9a30565e75e3c815ce68c1548e8

## 実施済み

ローカルDBはバックアップを取得後、640商品を登録（全体5,435件）。640件ずつのEvidence・価格履歴・在庫・検索索引と原本バイト列を照合。Pythonテスト28件成功。検索APIの640商品全項目、在庫あり絞り込み518件、検索画面・原表表示を確認。

証跡は artifacts/toyotsu-import.json、toyotsu-db-verification.json、toyotsu-ui-verification.json。登録前ローカルバックアップは artifacts/catalog-before-toyotsu.dump。

## 本番適用待ち

本番Web/PostgreSQLはhealthy。専用SSHは商品登録コマンドを許可していないため、Web deployや権限拡張で代用しない。本番DB登録は未実施。

原本・JSON・検証済み共通ランタイム・取り込みスクリプトのtarをDellの専用incomingへ転送済み。

- パッケージ: artifacts/LIPS-Toyotsu-202609.tar
- SHA256: 0121e0b46151af35d2b3484359a9d92845c74cd553d51e1317bfe11c81ae7320
- 転送先: /home/catalogdeploy/incoming/0121e0b46151af35d2b3484359a9d92845c74cd553d51e1317bfe11c81ae7320.tar
- 実行補助: artifacts/Apply-Toyotsu-202609.sh

補助スクリプトをDellのダウンロードへ保存し、iwset_aiのPowerShellから次を実行する。

```powershell
$script = wsl -d Ubuntu-24.04 --exec wslpath -u "$env:USERPROFILE\Downloads\Apply-Toyotsu-202609.sh"
wsl -d Ubuntu-24.04 --user root --exec bash "$script"
if ($LASTEXITCODE -ne 0) { throw '取込が停止しました。結果を確認してください。' }
```

パッケージSHAと全ファイルSHA、既知の本番Webイメージ、healthy、DBポート、Migration一致を検査する。検査に失敗したら適用しない。最新pg_dumpを取得しpg_restoreで一覧を検査してから登録し、640件すべてを読み戻し照合する。スキーマ変更・Web再起動はしない。同一原本の再実行は重複登録しない。

成功後、豊通食料で640件（在庫あり518件）を検索できることを確認する。他の登録がなければ本番総数は1,893→2,533件。失敗時は追加登録を止め、表示されたバックアップを保持する。復元は別DBで検証し、後続データを失わない方針を決めてから管理者が実施する。バックアップを無条件に本番へ上書きしない。

### 初回適用の権限エラーと修正

2026-09-21、利用者のDell実行で `realpath(/import/runtime/CatalogPdfBridge.dll) failed: Permission denied`。初回補助スクリプトがroot所有0700の作業ディレクトリを非rootコンテナへマウントしていたため、DLLを読み取れなかった。`--check` 内で停止しており、この実行は `--apply` に進んでいない。

修正版 `artifacts/Apply-Toyotsu-202609-v2.sh`（管理対象原稿 `tools/Apply-Dell-Toyotsu-202609.sh`）は、固定の本番イメージのUID/GIDを取得し、今回新規作成した展開先だけをその所有者に変更する。ディレクトリ0500・ファイル0400とし、同じコンテナ内でDLLとデータの読み取り確認後に事前検査へ進む。DB秘密ファイル・本番アプリ・既存展開先の権限は変更しない。パッケージの内容とSHA256は不変。bash構文検査は成功、Dellでの再実行結果は未確認。

## 本番反映完了（2026-09-21）

利用者がDellでv2スクリプトを実行し、Migration2件一致、バックアップ取得・可読性検査、640商品登録、640件ずつのEvidence/価格/在庫/検索索引および原本ハッシュ照合の成功を確認。

- SourceDocumentId: eed6dab0-4627-4fbd-a9c8-8b29dbaaafd2
- バックアップ: /var/lib/catalog-deploy/backups/before-toyotsu-20260921-151303-460759.dump
- Codexから本番検索APIを再確認: 総数2,533件、豊通640件、在庫あり・少量518件。
- 本番APIで公開される名称・生産者・年号・容量・税抜価格・在庫・国・地域・種別・出典ページ/行を640件すべて照合済み。
- 本番版の検索APIはpriceNoteを返さない。入荷予定など原表EvidenceはDellのDB全件照合で確認。Webアプリの更新は実施していない。
- 証跡: artifacts/toyotsu-production-verification.json。

上記「本番適用待ち」は完了前の履歴。今回の豊通データは本番登録済みであり、再実行は不要。
