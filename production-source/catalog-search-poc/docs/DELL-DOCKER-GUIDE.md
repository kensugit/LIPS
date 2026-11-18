# Dell Docker構成・構築・Codexデプロイ手順

2026-09-17。**スクリプト準備済み、Dell実機の仮想化条件とDocker実起動は未確認。導入・本番デプロイは未実施。**

## 調査結果

- 2026-09-10の実機収集記録はDELL3420 / Windows 11 Pro / build26200。OSはDocker Desktop/Hyper-Vの対応範囲。現在のBIOS仮想化、RAM、空き容量、Hyper-V、仮想スイッチは記録にないため確認が必要。
- 今回、許可済み192.168.1.110から192.168.1.5:5986へ接続し、証明書検証付きJEAでiWSET Running、commit 3c18e0fbcdc69861396921b15777b2731818db99 を確認した。既存JEAはiWSET状態/配布に限定され、ホスト全体の構成収集やDocker操作は公開していない。権限は拡張していない。
- 現開発PCはDocker Engine未導入、WSLのLinuxディストリビューションなし。Docker実起動・Linuxイメージビルド・Dell再起動後の自動復旧は未検証。構成ファイル検証やスクリプト試験と区別する。

推奨は **Dell Windows → Hyper-V専用Ubuntu 24.04 LTS amd64 VM → Docker Engine → 商品カタログ＋専用PostgreSQL**。Docker DesktopもOS要件上の候補だが、常時稼働をWindowsの利用者セッションから分離するため専用VMを採用候補とした。Docker Desktopが実機で動かないという判定ではない。

### 公式資料

- [Docker Desktop Windows要件](https://docs.docker.com/desktop/setup/install/windows-install/)
- [Hyper-Vのハードウェア要件](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/host-hardware-requirements)
- [Docker Engine Ubuntu導入](https://docs.docker.com/engine/install/ubuntu/)
- [Docker host network](https://docs.docker.com/engine/network/drivers/host/)
- [Docker socketアクセス制御](https://docs.docker.com/engine/security/protect-access/)

## 1. Dellで読み取り確認（変更なし）

`Get-DellDockerReadiness.ps1` をDellへコピーし、管理者PowerShellから実行する。

```powershell
powershell -File .\Get-DellDockerReadiness.ps1 -OutputPath .\dell-docker-readiness.json
```

CPU仮想化/SLAT、RAM、空きメモリ、Cドライブ空き、Hyper-V、既存仮想スイッチを収集する。秘密情報・パスワード・アプリ設定を収集せず、機能有効化も行わない。最終可否を決めるため、この結果を確認する。

VM用の暫定運用基準はホストRAM16GB以上、空きRAM6GB以上、C空き80GB以上、VMは4GB/2CPU/最大64GB。Docker公式の最低要件とは別に、稼働中iWSETの余裕を残すための基準。負荷や実データ量により見直す。

## 2. Dell側のVM作成（承認後、管理者）

同梱deploymentフォルダーを保持して実行。既定は計画表示のみ。

```powershell
.\New-DellCatalogVm.ps1
```

Hyper-Vが無効なら、影響と保守時間の承認後のみ以下を実行する。スクリプトは自動再起動しない。Windows再起動中は既存iWSETも停止するので、稼働時間帯には実行しない。

```powershell
.\New-DellCatalogVm.ps1 -EnableHyperV -Apply
```

手動再起動後、readinessを再実行。Ubuntu公式配布元の24.04 LTS amd64 ISOと公式SHA256を照合する。**既存の承認済み外部仮想スイッチ** とゲスト用予約IPが必要。既存NICを外部スイッチへ変換すると通信に影響し得るため、その作成は本スクリプトでは実施しない。スイッチ未整備ならネットワーク構築の別工程が必要。

```powershell
.\New-DellCatalogVm.ps1 -IsoPath C:\Install\ubuntu-24.04-server-amd64.iso `
  -IsoSha256 '<公式配布元のSHA256>' -SwitchName '<承認済み外部スイッチ名>' -Apply
```

VMは作成だけで停止状態。Hyper-Vマネージャーから起動し、Ubuntu 24.04 amd64とOpenSSH Serverをインストール。固定/予約IPを設定し、SSHは管理用の許可済み経路からだけアクセスさせる。インストール後はISOを外し、仮想ディスクからの起動を確認する。自動起動はWindows起動60秒後、終了はゲストの正常終了を設定済み。

## 3. UbuntuのDocker基盤（承認後、VM管理者）

専用の新規VMだけが対象。既存Dockerや既存catalog設定がある場合は停止し、上書きしない。再実行時も自動削除・初期化しない。

```bash
bash deployment/bootstrap-ubuntu.sh                 # 計画のみ
sudo bash deployment/bootstrap-ubuntu.sh --apply /path/operator-ed25519.pub 192.168.1.110
```

Docker公式APT署名付きリポジトリ、Docker Engine/Compose、OpenSSH、専用標準ユーザーcatalogdeployを構成する。Dockerグループやroot全権はcatalogdeployに付与しない。rootが管理する`catalogctl status`と`catalogctl deploy <SHA256>`だけをsudo許可する。Docker TCP/socketは外部公開しない。
操作者公開鍵はsource-IP制限付き。秘密鍵は操作者側に保管する。VMのSSHホスト鍵fingerprintをコンソールで確認し、Codex端末の専用known_hostsへ照合登録する。ホスト鍵検証の無効化や未確認鍵の自動承認はしない。
DBパスワードはVM内で生成してroot専用ディレクトリへ保存し、コンテナーへread-only secretとして渡す。ZIP・git・画面出力へ含めない。

## 4. Dockerリリース作成（Linuxの検証環境）

リポジトリの該当ブランチを**クリーンなGit checkout**で取得し、catalog-search-pocへ移動する。必要ツールはGit、Docker Engine/Compose、bash。構築キットZIPだけには.gitがないため、このビルドスクリプトはZIP展開だけでは実行できない。

```bash
bash deployment/build-release.sh
```

Dockerfileで.NET10のrestore/test/publish、非root WebとCLIのイメージを作成。DB込みイメージ保存、Compose設定確認を行い、`artifacts/docker-<commit>/package.tar`とSHA256を生成する。原本Excelや開発DBは同梱しない。Docker内のテストはDB環境変数がないためDBテストをskipする。実DB試験は別ゲートで必要。
画像は実際のimage IDで固定。Web-only更新では現在のPOSTGRES_IMAGEと同じimage IDを引数に指定する（そのビルド環境に保存済みのIDを使用）。DBイメージを更新する場合は自動デプロイを拒否する。

```bash
bash deployment/build-release.sh sha256:<現在のPostgreSQLイメージID>
```

Docker実起動と検証用VMへの配布試験を通過してから、その同一パッケージをDell適用候補にする。今回の構築キットを、ビルド済み・実起動検証済みの製品イメージとは扱わない。

## 5. 転送・承認・初期DB構築

Codex端末から、同梱`Deploy-CatalogFromCodex.ps1`へVMホスト・鍵ファイル・照合済みknown_hosts・パッケージ・SHAを渡す。既定はPlanのみ。次はPowerShell例であり、実値は導入時に確定する。

```powershell
$release = @{
  VmHost = '<予約済みVMホスト名またはIP>'
  IdentityFile = '<操作者秘密鍵のローカルパス>'
  KnownHostsFile = '<照合済みknown_hostsのパス>'
  PackagePath = '<package.tarのローカルパス>'
  ExpectedSha256 = '<レビューしたパッケージSHA256>'
}
.\Deploy-CatalogFromCodex.ps1 @release               # 計画
.\Deploy-CatalogFromCodex.ps1 @release -Action Upload
```

転送だけでは稼働状態を変更しない。VM管理者がソース/差分/検証結果/パッケージSHAをレビューし、**そのリリースへの利用者承認後** に次を実行する。

```bash
sudo /usr/local/sbin/catalogctl approve <SHA256>
sudo /usr/local/sbin/catalogctl initialize <SHA256>  # 初回のみ、VM管理者
```

初期DB作成とMigrationはこの手動工程だけ。既存テーブルがある場合は拒否し、再初期化しない。途中失敗時は状態を確認し、勝手にvolumeを削除しない。初期化後は空DBなので、確認画面からサンプルを投入し、その後に承認済み実在庫表を取り込む。現在開発PCの1061件を無断でDellへ移送しない。

## 6. Codexからの通常更新

個別リリースの確認・承認登録後、次を実行できる設計。

```powershell
.\Deploy-CatalogFromCodex.ps1 @release -Action Status
.\Deploy-CatalogFromCodex.ps1 @release -Action Deploy
```

自動更新はWebだけ。承認SHA一致、初期化済み、Migrationファイル指紋不変、DB image不変、未適用/未知Migrationなし、DB接続成功が必須。排他ロックを取り、pg_dumpバックアップ後に置換する。新image IDとHTTP/DB接続を確認し、失敗時は直前のWebへ戻す。旧Webも復旧しなければ停止して管理者対応を求める。操作はsyslogのcatalog-deploy、データバックアップは`/var/lib/catalog-deploy/backups`へ記録。

**DB Migration・DB image変更・適用状態不明では「Codexによる自動デプロイは行いません。Dellで手動デプロイしてください」。** 初期化コマンドを更新用に再利用しない。旧WebへのロールバックはDB無変更時だけであり、DB更新の巻戻しはバックアップと別途計画が必要。

## 7. ブラウザ確認・停止

現PoCは認証未実装のため、LANのHTTP公開は行わない。VMのloopbackをSSHで転送し、操作者端末の http://127.0.0.1:55440 で確認する。Dellでブラウザを使う場合も同様にVMへトンネルを張る。別端末を使う場合はその端末の鍵と許可元IPを別途管理する。

```powershell
ssh -N -L 127.0.0.1:55440:127.0.0.1:55440 -i '<秘密鍵>' `
  -o StrictHostKeyChecking=yes -o UserKnownHostsFile='<known_hosts>' `
  catalogdeploy@<VMホスト>
```

「取込・横断検索・商品確認」でCSV2件と匿名マスター4件を取り込み、現役5件/終売含む6件、紐付け・解除を確認する。VM再起動後も件数と履歴が残り、Dockerが自動起動することを必須受入とする。SSHトンネル終了はCtrl+C。アプリ自体はVM内で継続稼働する。
停止は管理者が該当releaseのComposeで`stop`。**`down -v`、volume削除、Docker pruneはしない。** inboxの実パスはVM側`/var/lib/catalog/inbox`（画面ではコンテナー内`/data/inbox`）。共有フォルダーや自動的な資料移送は未設定。バックアップをVM外へ保管する運用と定期バックアップは稼働開始前に決める。

## 実施済み検証 / 未実施

- .NET build：警告0・エラー0、既存自動テスト28件成功・skip0。
- 実PostgreSQLのschema-check：空DB拒否、適用済み許可、未知Migration拒否、接続不可拒否の4条件成功。
- Bash fixture：未承認/改ざん/初回自動/DB差分/DB image差分/Migrationファイル変更拒否、起動失敗復旧など10項目成功。Docker呼出はmockであり実コンテナー試験ではない。
- PowerShell/Bash構文検証、VM構築/Ubuntu構築の既定plan-onlyを確認。
- 公式チェックサム検証済みDocker Compose v5.5.1でcompose.yamlの構成検証成功。Docker Engineは導入していない。
- 未実施：Dellの新readiness、Hyper-V/Ubuntu導入、Linux Docker build/実起動、SSH/sudo実接続、DB保存のコンテナー再作成/VM再起動確認。これら完了前に稼働可能と断定しない。

本番設定・Windows機能・Firewall・JEA・iWSETサービスは変更していない。本番配布・再起動なし。検証用PostgreSQLは終了済み。
