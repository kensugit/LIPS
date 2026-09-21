# CodexからのLIPS本番配布

## 接続範囲と権限

専用SSH鍵を持つ開発PCからVPN経由で `192.168.1.5:22222` に接続する。Windows側の受信許可は確認済みの開発PC VPNアドレス `10.10.10.1` のみ。WSL localhost転送で専用sshdの `127.0.0.1:22223` に接続する。WSLは既存のログオン不要タスクで常駐する。iWSETのJEA/WinRM、一般SSH、LIPS公開HTTPには配布用コマンドを追加しない。

許可コマンドは `status`、`upload <sha256>`、`deploy <sha256>`、`rollback`。シェル、任意コマンド、SFTP、ポート転送、rootログイン、パスワード認証は許可しない。秘密鍵は開発PCの `.ssh/lips_codex_ed25519` にのみ保存し、Git・配布ZIPには含めない。公開鍵を失効する場合は専用サービスを停止するか `/etc/lips-codex/operator.pub` を管理者が更新する。

## 初回セットアップ

1. 配布ZIPのハッシュを確認しDellで展開する。Ubuntu側で `sudo bash setup-ubuntu.sh --apply` を実行する。既存の初期化済みcatalogctlが必要。openssh-serverがない場合はUbuntuの設定済みAPTから導入する。その際、サービスの自動開始を抑止し、新規導入された一般SSHの22番待受は無効にする。元から存在するSSHサービスは変更しない。専用設定が既にある場合は上書きしない。
2. 表示された専用SSHホスト公開鍵・SHA256指紋をCodexへ返す。秘密鍵やDBパスワードではない。公開鍵を別経路で確認した後、開発PCの `.ssh/lips_known_hosts` に `[192.168.1.5]:22222` と対応づけて保存する。StrictHostKeyCheckingは無効化しない。
3. Dellの管理者PowerShellで `Install-WindowsForward.ps1 -Apply`。`22222 -> Windows localhost:22223` の専用転送と開発PC1台の受信規則を追加する。IP Helperが無効の場合は停止する。WSLの変動する内部IPには依存しない。
4. 開発PCからstatusと禁止コマンド拒否を確認。NAS経由でTCP22222が遮断される場合、既存規則を読み、開発PC→Dellの該当ポートだけを別途許可する。経路確認前に接続完了としない。

## 配布操作

```powershell
python tools/remote-deploy/client.py status
python tools/remote-deploy/client.py upload --package package.tar
```

パッケージは既存 `catalog-search-poc/deployment/build-release.sh` の形式（images.tar / compose.yaml / release.env）。このLIPSリポジトリにはカタログ本体のソースはまだ含まれないため、本体の正しいコミット・検証結果と組み合わせる。PDF配布ZIP・WindowsゲートウェイZIPをこの経路で実行することはできない。

**アップロードは適用承認ではない。** 既存catalogctlのroot保護された承認済みパッケージだけをdeploy可能にする。管理者が内容・コミット・検証結果・SHAを確認し、DellのUbuntuで次を実行する。

```bash
sudo /usr/local/sbin/catalogctl approve <確認済みSHA256>
```

その後Codex側から、指定した版の本番反映指示に従って実行する。

```powershell
python tools/remote-deploy/client.py deploy --sha256 <承認済みSHA256>
python tools/remote-deploy/client.py status
```

既存コントローラーが排他、原本ハッシュ、Migration指紋、DBイメージ不変、DB接続・スキーマ確認、pg_dump、Web更新・ヘルス、失敗時の旧Web復旧を担当する。DBスキーマ変更・DBイメージ変更・初期化はこの経路で行わない。バックアップ復元も自動実行しない。正常終了後も公開URLで件数・検索を確認する。

直前Web版への復旧は、管理者のroot保護記録 `previous` を使い、同じschemaチェック・バックアップ・ヘルス確認を経由する。

```powershell
python tools/remote-deploy/client.py rollback
```

## 撤去

Dell管理者PowerShell: `Remove-NetFirewallRule -Name LIPS-Codex-SSH` と `netsh interface portproxy delete v4tov4 listenaddress=192.168.1.5 listenport=22222`。
Ubuntu: `sudo systemctl disable --now lips-codex-sshd`。アプリ、DB、LANゲートウェイには変更しない。専用sudoers・鍵等の削除は稼働確認後に管理者が行い、既存catalogdeployアカウントや既存SSH設定は削除しない。

## 検証状況

### 実機受入（2026-09-21）

- Dell上で専用SSHとWindows転送を設定後、CodexからVPN経由のTCP22222到達と公開鍵認証に成功。
- 利用者提示のSSHホスト公開鍵を別経路で照合。指紋 `SHA256:jrbXoPCDMCb28Hvv4GzvHwiphCmAVTUDAg3RmS8cKDI` を開発PCの専用known_hostsへ固定。
- `status` 成功。現在パッケージ `cce3ae0d23ea70372325db65002b95809fcb14fa4de034e0f9281140b5f256c4`、Web/PostgreSQLともhealthy。
- 10KBの識別用tarを転送し、ハッシュ一致・同一ファイル再送時の `alreadyPresent=true` を確認。識別用ファイルのSHAは `1c74dd49f5c7ecd8a5766784d2d3ebfcd00d73b61a0d9f8f09c5c8bab5129c1e`。これは製品パッケージではなく、承認・適用しない。未承認incoming領域に保管。
- 任意コマンド `id` はexit 2、未承認tarのdeployはexit 1で拒否。Web更新・DB変更には進まず、現行パッケージ不変を再確認。
- 公開API `http://192.168.1.5:55441/api/options` はtotal=1893。
- 詳細証跡は開発PCの `artifacts/codex-deploy-acceptance-20260921.json`（Git対象外）。

専用接続・転送・承認制御の実機受入は完了。新しい製品版の実デプロイとロールバック実行は未実施で、次回の承認済みリリース時に確認する。Windows LANゲートウェイの更新・DB Migrationはこの経路の対象外。

開発側でコマンド注入拒否、アップロードの完全一致・重複・サイズ上限・破損時の一時ファイル除去・既存不一致ファイル保持・固定sudo引数を試験。実機のSSH構文・systemd・VPN到達・ホスト鍵照合・本番statusはセットアップ後に確認する。セットアップは本番アプリを更新しない。

参考: [OpenSSH sshd_config](https://man.openbsd.org/sshd_config)、[Microsoft WSL networking](https://learn.microsoft.com/en-us/windows/wsl/networking)。
