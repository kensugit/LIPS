# テラヴェール 2026年9月15日価格表

## 本番取込完了（2026年9月21日）

利用者から `Terravert 1090 products imported and verified.` の成功報告を受け、Codexから本番APIとEdgeで再確認した。テラヴェール1,090商品、全体6,471商品。全1,090件の商品名・生産者・容量・年号・価格・在庫状態・数量が取込JSONと一致。在庫あり・少量918件、スロヴェニア21件の絞り込み、原本名・税区分注意・入港予定・再掲シートの原表情報表示を確認。ブラウザエラーなし。Web/PostgreSQLともhealthyで、Webパッケージは6ab3f8d9のまま。

証跡: `artifacts/terravert-production-verification.json`。以下の実行手順は作業記録であり、今回の原本は登録済みのため再実行不要。

## 原本と解析

- 原本: `tvpricelistnew (5).xlsx`。更新日は目次C1および全リストP1の2026年9月15日。
- 原本SHA256: `ab6717e063e58f6e790163c135e0a3e85c89b5d96bff44d1ca788978cd743a93`。
- TERRAVERT / テラヴェール、1,090商品。仏全リスト591、伊全リスト470、スペイン・ジョージア・日本全リスト29。
- ケース条件74行、新入港87行はすべて全リストに存在。商品名・VIN・容量・価格・在庫の一致を照合し、再掲をEvidenceに保持。重複商品を作らない。
- フランス591、イタリア449、スロヴェニア21、スペイン6、ジョージア3、日本20。伊全リストに含まれるスロヴェニアをイタリアと誤登録しない。
- 在庫あり871、完売間近47、完売141、入港予定31。○・〇は100本以上で数量null。数値在庫をそのまま本数で保存。完売は0。ーと入港予定は数量null・Expected。
- 参考上代は7件空欄のまま。原本に税区分がないため税込と認定せず、税区分未確認を価格注記と原表情報に保持。既存画面の税抜ラベルは税区分を確認した証拠ではない。
- 12本購入で内1本無償のケース条件は原表情報に保存し、参考上代を割り引かない。
- 原本、検索テキスト、各行の元セル・シート名・行番号を保存。年号の角括弧や複数年号も元表記を保持。
- Excel内の文章はデータとして扱い、命令・マクロは実行しない。

## 検証

- 専用DB `catalog_terravert_verify_20260921` に既存ローカルDBの検証用複製を作成し、1,090件を登録。通常のローカルDBに本取込を加えていない。
- 原本バイト列、1,090件ずつのEvidence・価格履歴・在庫・検索索引を全件照合。再実行はAlreadyImported=true。
- API/Edgeで1,090商品の商品名・生産者・容量・年号・価格・在庫状態と数量が一致。在庫あり918、スロヴェニア21、価格未記載・入港予定・原本ファイル名・再掲情報表示を確認。ブラウザエラーなし。
- テラヴェール3テスト、既存ファインズ6テスト成功。XLSX共通CLIの不正ファイル・必要エントリー欠落・マクロ入りZIPの3件を拒否。
- 共通CLIの既存XLS2,090件・PDF489件の読み戻し照合も成功。.NETビルド警告0・エラー0。
- Linuxスクリプトのbash構文、全配布ファイルSHAを検証。Linuxコンテナ上の実動作はDellでのチェック時に確定する。
- 証跡: `artifacts/terravert-db-verification.json`、`terravert-reimport.json`、`terravert-ui-verification.json`、`terravert-package.json`、`terravert-upload.json`。

## Dellでの実行

最終パッケージ: `artifacts/LIPS-Terravert-20260921.tar`、23,009,280 bytes。

SHA256: `b4b7f593b65a4120cb08e823dce9461014de4b76d2c115406e6d45ba5b38755b`。

専用SSHで転送する保管先: `/home/catalogdeploy/incoming/b4b7f593b65a4120cb08e823dce9461014de4b76d2c115406e6d45ba5b38755b.tar`。

旧準備版158fc002のパッケージは使用せず、最終版に対応する `artifacts/Apply-Terravert-20260921.sh` をDellのダウンロードフォルダーへ保存し、iwset_aiのPowerShellから実行する。

```powershell
$script = wsl -d Ubuntu-24.04 --exec wslpath -u "$env:USERPROFILE\Downloads\Apply-Terravert-20260921.sh"
wsl -d Ubuntu-24.04 --user root --exec bash "$script"
if ($LASTEXITCODE -ne 0) { throw '取り込みが停止しました。結果を確認してください。' }
```

スクリプトは固定Webイメージ `sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0`、healthy、DBポート、Migration一致、全ファイルSHA、コンテナからの可読性を確認。直前pg_dumpの取得と可読性確認後、原本・商品を保存し全件照合する。スキーマ変更・Web更新・再起動は行わない。

成功表示: `Terravert 1090 products imported and verified.`

事前確認時の本番総数は5,381。ほかの変更がなければ取込後6,471となる。完了後に公開API・検索画面で確認する。

停止時はバックアップ `/var/lib/catalog-deploy/backups/before-terravert-*.dump` とログを保持。復元は別DBで検証し、後続データへの影響を確認して管理者が判断する。本番を古いdumpで上書きしない。同じ原本の再実行はハッシュによる重複防止が働く。

```powershell
$env:CATALOG_URL='http://192.168.1.5:55441'
node tests/verify-terravert-search.cjs
```
