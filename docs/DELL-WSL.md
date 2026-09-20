# Dell WSL本番環境

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
