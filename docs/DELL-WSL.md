# Dell WSL本番環境

## 最新受入状態

2026-09-20: 利用者の実機結果によりPDF3社832件の登録・全件照合、本番総数1,893件を確認済み。LANゲートウェイ `http://192.168.1.5:55441` とWSL起動タスクはRunning、VPN接続およびWindows再起動後のログオン前アクセスを利用者が確認済み。倉庫LANは後日確認。以下の9月17日の構成・初期接続不可記録は履歴として保持する。現行の常駐・LAN構成は [LAN-ACCESS.md](LAN-ACCESS.md)、Codex用の追加管理経路は [CODEX-DEPLOY.md](CODEX-DEPLOY.md) を参照。

2026-09-20確認。構築タスク「本番環境docker構築」の2026-09-17実行記録に基づく構成。現在の実機状態は未確認。

- ホスト: Dell3420 / Windows 11、Windowsユーザー `DELL3420\iwset_ai`
- WSL2: `Ubuntu-24.04`、Ubuntuユーザー `iwset_ai`
- Docker: `catalog-web-1`、`catalog-postgres-1`
- DB: PostgreSQL 17、`catalog_search`、ユーザー `catalog`
- 永続Volume: `catalog-postgres-data`
- Web: Dell自身の `http://127.0.0.1:55440`。DB公開ポートはloopbackの55439。
- 既存アプリソース: Ubuntuの `~/lips/repo/catalog-search-poc`
- 既存配置管理: `/usr/local/sbin/catalogctl`、`/opt/catalog/releases`、`/var/lib/catalog-deploy/current`
- バックアップ: `/var/lib/catalog-deploy/backups`、root所有・0600。
- 起動: Windowsタスク `LIPS-Start-WSL`。`iwset_ai` ログオン時にWSLを起動する構成。無人起動の保証ではない。
- 9月17日時点: Wine to Style 1,061件登録とバックアップ可読性確認の記録あり。その後の変更は現物で確認する。

## 展開前の現在状態確認

Dellの `iwset_ai` のPowerShellで実行する。アプリ更新・DB書込み・再起動は行わない（WSLが停止している場合、wslコマンドで起動する）。

```powershell
wsl --list --verbose
wsl -d Ubuntu-24.04 --exec docker ps
wsl -d Ubuntu-24.04 --exec curl --fail --max-time 10 http://127.0.0.1:55440/api/options
wsl -d Ubuntu-24.04 --exec bash -lc 'cd ~/lips/repo/catalog-search-poc && git rev-parse HEAD && git status --short'
```

必要に応じUbuntu内で `sudo /usr/local/sbin/catalogctl status` を実行する。秘密情報を含む環境変数・設定ファイル全体は出力しない。

## 今回のPDF反映範囲

対象はワインエクスペリエンス220行、フィネス489行、ローヤルオブジャパン123行、計832行。原本PDFと `.catalog.json` を対にして扱う。原本・価格情報はGitHubに含めていない。

本リポジトリはPDF取り込み補助ツールのみ。上記既存アプリのソースと同じリポジトリ構造ではないため、既存 `~/lips/repo` にこのリポジトリを上書きしない。稼働ランタイムとの互換性、Migration完全一致、原本ハッシュを確認し、最新DBバックアップ取得後に既存取込処理で追加する。取込後は各社全行のEvidence・価格在庫履歴・検索を検証する。

本番DBをローカル開発用dumpで置換しない。`catalogctl initialize` の再実行やVolume削除を更新手順に使わない。異常時は追加取込を止め、既存バックアップを別DBで検証し、後続更新を含む復旧方針を判断する。

## 2026-09-20の接続確認

この開発PCから既知のDellアドレス `192.168.1.5` の22/5986/55440はすべてタイムアウト。SSH設定・秘密鍵の配備は確認できていない。タイムアウトはアプリ停止の証明ではない。LIPSはloopback公開のため、LANから55440へ到達しないこと自体は想定内。

本番への転送・取込・アプリ更新は未実施。利用可能な管理接続経路、またはDell上での状態確認結果が必要。接続のためにFirewallや認証設定を変更してはいない。

## 利用者からの実機確認結果（2026-09-20）

両コンテナhealthy、検索API total=1061、アプリソース17465b2c19adb4809c9a6802d9bc00de1fd39e32、作業ツリー差分なし。バックアップ `/var/lib/catalog-deploy/backups/before-pdf-20260920-151817.dump` は1.2M、root所有0600、pg_restoreによる一覧読取成功。開発側の取り込みランタイムと本番版のMigration/DbContextソースに差分なし。

`tools/Import-Dell-Pdfs.sh` を原本・取込JSON・接続CLIと合わせて配布する。既定 `--check` は原本ハッシュとDBスキーマ、healthy、既知の本番イメージID・DB公開ポートを検証し書込みなし。`--apply` で最新バックアップを取得・検証後、3社の登録と全件照合を行う。途中停止時は完了済みの仕入先は残る。再実行時は同一原本ハッシュによる重複防止が働く。Webの更新・再起動、Migrationの適用は行わない。Linuxコンテナでの動作確認はDell上の `--check` を通すまで未完了。
