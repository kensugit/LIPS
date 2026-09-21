# 日本リカー 2026年9月9日在庫表

## 状態

2026年9月21日、本番取り込み完了。利用者から `Nippon Liquor 891 products imported and verified.` の成功報告を受け、Codexから本番APIとEdgeの検索画面で再確認した。

日本リカー891商品のコード・名称・生産者・容量・年号・税別希望小売価格・在庫状態・数量が取込JSONと全件一致。在庫あり検索891件、原本ファイル名・納価・関連原表の表示、ブラウザエラー0件を確認。本番全体は7,526商品。Web/PostgreSQLはともにhealthy、Webパッケージは6ab3f8d9のまま。証跡は `artifacts/nippon-liquor-production-verification.json`。

以下は実施済みの作業記録。今回の原本は登録済みのため再実行不要。専用SSHはstatus/upload/Webリリースのdeploy/rollbackのみで、データ取込スクリプトはDell側で実行した。

## データの扱い

- 4原本をバイト列・SHA256・ファイル名付きで保持。観測日は原表の2026-09-09。
- NL希望小売価格891行、NL出荷価格891行、ボルドー148行、ルイ・ジャド294行。商品コードで照合した独立商品は891件。同じ商品を別仕入先に分割しない。
- 取込順はボルドー、ルイ・ジャド、希望小売価格、出荷価格。同日の現在値は共通NL表を採用。両NL表は納価列以外の全セル一致を確認。
- 税別希望小売価格を検索用価格に登録。納価は原表のキャッシュ値を別項目として保持し、掛率から再計算しない。原本は書き換えない。
- 共通NL表の〇は300本以上、ブランド別表の◎は50ケース以上、○・◯は20～50ケース。記号から正確な本数を推定しない。NL表の239件は数量null、652件は記載本数。891件すべて在庫あり。
- ブランド別表の価格・容量・入数・JAN・数値在庫は共通表と照合。マールの年号0と共通表の「-」を原文として保持。別名・所有形態・有機表示・備考・関連原表も保持。
- ルイ・ジャド表のレゾナンスをフランスに誤分類せず、共通NL表の国・地方を出典付きで使用。短いJAN/UPCを勝手に補完しない。
- 原本内の文章はデータとして扱い、指示やマクロとして実行しない。

## 検証

- `tests/test_nippon_liquor.py`：在庫凡例、空欄と0、価格区分、商品重複、関連表の不一致拒否。テラヴェール・共通変換と合計9テスト成功。
- 共通CatalogPdfBridgeを警告0・エラー0でビルド。DBスキーマ変更なし。
- `catalog_nippon_liquor_final_20260921` はローカル既存DBを複製した独立検証DB。4原本の計2,224件のEvidence・価格履歴・在庫を照合、検索索引891件、同一原本再実行は4件ともAlreadyImported=true。
- APIとEdgeで全891商品のコード・名称・生産者・容量・年号・価格・在庫を照合。納価25200と希望小売42000の区別、原表表示を確認。ブラウザエラーなし。
- `tests/verify-nippon-liquor-search.cjs` は本番適用後もCATALOG_URLを指定して利用できる。
- 証跡：`artifacts/nippon-liquor-*-verify.json`、`*-reimport.json`、`nippon-liquor-ui-verification.json`、`nippon-liquor-package.json`、`nippon-liquor-upload.json`。

## 本番適用

最終パッケージ：`artifacts/LIPS-NipponLiquor-20260921.tar`（28,026,880 bytes）。

SHA256：`b76e0ce32229af56c9ee9b5e507918d361dd32f26763edf01a807ee6cf9541a7`。

専用SSH転送先：`/home/catalogdeploy/incoming/b76e0ce32229af56c9ee9b5e507918d361dd32f26763edf01a807ee6cf9541a7.tar`。

準備版c0b1e46c・030a3b51は使用しない。次の最終版スクリプトは上記SHAだけを受け付ける。

`artifacts/Apply-NipponLiquor-20260921.sh` をDellの利用者のDownloadsへ保存し、その利用者のPowerShellで実行：

```powershell
$script = wsl -d Ubuntu-24.04 --exec wslpath -u "$env:USERPROFILE\Downloads\Apply-NipponLiquor-20260921.sh"
wsl -d Ubuntu-24.04 --user root --exec bash "$script"
if ($LASTEXITCODE -ne 0) { throw '取り込みが停止しました。結果を確認してください。' }
```

固定Webイメージe16969fe、コンテナhealthy、DBポート、全ファイルSHA、Migration一致、コンテナからのファイル可読性を確認。pg_dumpとpg_restore -lによるバックアップ検証後に4原本を保存し、全件を読み戻す。Web更新・再起動・Migrationは行わない。

成功表示：`Nippon Liquor 891 products imported and verified.`

停止時はログと`/var/lib/catalog-deploy/backups/before-nippon-liquor-*.dump`を保持。原本単位でトランザクションがあり再実行可能。復元は別DBで検証して後続データへの影響を確認し、本番を古いdumpで安易に上書きしない。

```powershell
$env:CATALOG_URL='http://192.168.1.5:55441'
node tests/verify-nippon-liquor-search.cjs
```

配布DLLは本番適用実績のあるテラヴェール配布物と同じランタイムを使用。最終確認DBは`catalog_nippon_liquor_release_20260921`。
