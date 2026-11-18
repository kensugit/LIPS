# 共通のLIPS本番展開環境

## 状態

### 最新の本番基準（2026年9月29日 MV/NV統合）

MVをNVへ統合済み。現行package `e1d7fe8949aed807660b84fc73b55405acabb77072458e96c97eb7d3b23df80c`、source `f1732a9e8760d6122cc8180be484fc5007bb787b`。クリーンなソースは `artifacts/mv-nv-20260929/source`。本番NV441件＝従来NV438件＋MV3件をID集合で照合し、条件候補・選択リスト・CSVのNV統一と原表記保持を確認。Web/DB healthy、7732件、schema/DBイメージ不変。詳細は `docs/MV-NV-20260929.md`。以下の基準は過去履歴。

### 最新の本番基準（2026年9月29日）

ヴィンテージ正規化、取扱先自由文検索、検索をまたいで選択を保持・追加できる確認リスト付きCSVを適用済み。現行packageは `9d8aae82659e4a85fd8fb528c44ee18ed18a5652cd277b94faa181d0a96534ed`、sourceは `17bff0dc39ed1ca72e52419fd9b49bea6604a046`。クリーンな本番ソースは `artifacts/vintage-normalization-20260929/source`。ファインズ特価表示を保持。Web/DB healthy、schema/DBイメージ不変、7732件。本番API・選択リスト・選択CSV確認済み。詳細・証跡は `docs/SELECTED-CSV-20260929.md`。以降の最新基準表記は当時の履歴。次の候補は最新inspectとこの本番ソースを照合して作成する。

### 最新の本番基準（2026年9月25日）

ファインズ特価331商品の登録・在庫更新と特価仕入価格の独立表示を反映済み。現行packageは `ea1736f9d5f9976e2d9d6fd57df477513b426f891f9f9fba3cf1fbdb0d4d656f`、sourceは `f226f0a98d74253af4100e35d0f4d43ae898cb9f`。対応するクリーンなソースは `artifacts/fines-special-20260924/source`。次の候補はこの版と最新inspectを照合して作成し、旧CSV版から特価表示を消さないこと。

Web/DB healthy、schema/DBイメージ不変、本番総数7,732件。331件すべての参考上代・特価・在庫・原表Evidence、検索画面、詳細、CSVの小数付き価格を本番で検証済み。証跡: `artifacts/fines-special-20260924/live-applied.json`、`production-api-ui-proof.json`。データ更新前バックアップ `/var/lib/catalog-deploy/backups/before-fines-special-20260924-20260925-170130-847195.dump`、Web展開時バックアップ `/var/lib/catalog-deploy/backups/20260925T080145-847896.dump`。詳細は `docs/FINES-SPECIAL-20260924.md`。

以下の9月22日の「最新」記述は当時の履歴。ファイル転送は9月25日の共通AGENTS指示に従い相互TLS一括転送を使用し、旧SSH uploadへ自動で切り替えない。

2026-09-22、Dellでの初回設定と専用SSH復旧が完了。新しいinspect/upload/prepare/deploy/status経路を実機で使用し、CSV機能を本番展開済みです。Linuxイメージ作成・read-onlyプレビュー・登録・バックアップ・Web切替・公開画面の受入まで成功しました。

接続許可は店舗LAN `192.168.1.110` とVPN `10.10.10.1`。Dellの設定出力とSSH接続成功を確認済み。設置前のTCP22222タイムアウトは解消しました。SSHホスト鍵は従来の固定値と一致しています。

最新の適用SHAは `61dc274aa2c50fe98413cca1cfb844614887885af22ba615f92800c48b1207cd`、ソースは `8f86d62e394dea395ecdd127ea8fa1611b7a989d`。ソースのクリーンなGitスナップショットは `artifacts/search-csv-refactor/source`。2026-09-22の検索状態・CSV処理リファクタを含みます。次のタスクはこの本番ソースを起点にし、旧版からの作り直しで変更を消さないでください。最新状態は必ずinspectで再取得します。

最新の証跡は `artifacts/search-csv-refactor/production-proof.json`、`live-before.json`、`live-after.json`、`deploy.log`、`acceptance/live-proof.json`。バックアップは `/var/lib/catalog-deploy/backups/20260922T055828-944191.dump`。Web/DB healthy、総数7,526件、CSV1,959件一致、検索45項目成功。DB/schema不変。

初回CSV展開の履歴: パッケージ `e66ac013b69027fe72dcfb76b48ede99eee25889a88e65d052332becfc3113db`、ソース `4852860da1544166b41b46dfee198fc3cc1ae492`。証跡は `artifacts/search-csv-production/`、バックアップは `/var/lib/catalog-deploy/backups/20260922T045122-920831.dump`。DBイメージ・スキーマ不変、DBコンテナは再起動なし。

## 初回設定

`artifacts/LIPS-Deployment-Environment-20260922.zip` をDellへ転送・展開し、Ubuntu-24.04の所有者として管理者PowerShellから実行します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Enable-LipsDeployment.ps1
```

この設定は既存の専用SSH・鍵を使い、接続元を上記2アドレスに限定します。Linux側の設定は `/var/lib/catalog-deploy/codex-setup-日時`、Windows側の既存許可アドレスは `C:\ProgramData\LIPS-Codex\setup-日時` に控えを保存します。一般SSH、一般シェル、パスワードログイン、iWSET JEAは追加しません。

管理者が設置する固定ビルダーに限り、プレビュー検証に成功したWebパッケージをroot保護領域へ登録する権限を付けます。任意アップロードを直接approveするコマンドは公開しません。初回設定自体はWebを切り替えません。

## 各タスクの操作

2026-10-17追記：受信キットを案内する前に[共通検査](../tools/release-preflight/README.md)のwrapper/manifest/hash検査とPS5.1初期化経路検査を実施する。生成時はerrorを返す新規Startテンプレートを使用し、全制御ハッシュを再固定する。既存商品更新は同一原本の重複スキップとは別に、索引と対象外商品の保持を隔離DBで確認する。通信結果不明時は診断を1回で採取し、取込後はreviewの完了・versionを待ってからAPI照合する。[今回の比較調査](ORVEAUX-DEPLOYMENT-RETROSPECTIVE-20261017.md)を参照。

1. そのタスクでユーザーの展開指示を確認。秘密情報を含まない `client.py inspect` のJSONを保存。
2. 現行本番のソースに今回の変更を統合し、他の未適用変更を混入させず、テスト・ブラウザ確認。独立したクリーンなGitスナップショットを作成。
3. 同じソースを `dotnet publish ... -c Release -r linux-x64 --self-contained false -p:UseAppHost=false` でpublish。
4. `build-payload.py --source <Gitルート> --publish <publishフォルダー> --state <inspectのJSON> --output <payload.tar>`。Migration指紋が違えば停止。
5. 承認済み相互TLS一括転送で固定ZIP/proofを送信し、受信サイズ/SHA・cleanup・Linux配置完了を確認する。配置したpayload SHAを使い `client.py prepare --sha256 <payload SHA>`。旧SSH uploadへ切り替えない。送信ACKだけではLinux配置完了と扱わない。
6. prepareが返した **packageSha256**（payload SHAとは別）を使い `client.py deploy --sha256 <packageSha256>`。
7. `client.py inspect` と `status`、公開HTTP、検索結果・件数・CSVを確認して完了報告。異常時は更新を止め、既存の `rollback` と復旧確認を使用。

クライアントは `tools/remote-deploy/client.py`、同梱Pythonは `C:/Users/kensu/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`。

## 固定ビルダーの処理

- rootがuploadをコピー・SHA検証。相対パス、通常ファイル、サイズ、重複、全ファイルハッシュ、必須Webファイルを検査。
- 本番パッケージ、Web/DBイメージ、schema指紋、healthyを一致確認。現行イメージにpublishだけを重ね、CLI/runtime/entrypointを維持。
- アップロードされたシェル、Dockerfile、テストスクリプトは実行しない。Dockerfile・実行コマンドはroot設置済みビルダーが生成。
- schema-checkと別ポート55443のread-only/監視OFFプレビューで件数・選択肢・検索応答を確認。
- 既存compose・DBイメージ・schemaを維持した正規package.tarを作成し、root保護のapprovedへ登録。Webはまだ切り替えない。
- 準備とdeployは既存のdeployment.lockで排他。別タスクが先に反映した候補のdeployは停止。
- deployは既存catalogctlの関数を再利用し、schema-check・pg_dump・Web更新・失敗時の旧Web復旧を実施。DB Migrationはしない。

## 検証済み範囲と復旧

ローカルで許可コマンド・固定sudo引数、アップロードのハッシュと重複、パストラバーサル・リンク・FIFO・余分なファイル・CSV候補の検証に成功。Bash/PowerShell構文検査済み。Dockerビルド・登録・展開・バックアップはDell実機で成功。異常時の本番ロールバックと同時競合の故障注入試験は行っていません。

環境だけを元に戻す場合はDell管理者が初回設定の控えからdispatch/sudoers（既存ならprepare/deployヘルパーも）とFirewall許可アドレスを復元します。アプリのロールバックとは別操作です。専用SSH鍵を失効する場合は従来のCODEX-DEPLOY.mdに従います。
