# Dell LAN / VPN / 倉庫からの利用

## 適用範囲

利用者の指定によりログインなしで検索・取込・商品確認・紐付けを公開する。許可範囲は店舗 `192.168.1.0/24`、リモートVPN `10.10.10.0/24`、倉庫 `192.168.100.0/24`。URLは全拠点共通の `http://192.168.1.5:55441`。外部インターネットへのポート開放はしない。許可範囲の利用者は全員更新操作が可能で、ユーザー別監査は提供しない。匿名サンプル投入APIだけはLAN側で公開しない。

倉庫範囲は社内ネットワーク図（2026-09-20読取）とQL-1115NWB移設記録で確認した。NAS `192.168.1.8` の `inet iwset_vpn forward` は原則drop、既存の倉庫印刷許可はDell→倉庫プリンタTCP9100と応答のみ。LIPS向けTCP55441の許可が別途必要な可能性がある。現行ルール・ルートを読んでから限定的に追加し、既存印刷・VPNを維持する。

## 構成

Windowsの `LipsLanGateway` (Microsoft YARP 2.3.0 / .NET 10同梱) が55441を受け、既存WSLの `127.0.0.1:55440` へ中継。実ソケットの接続元IPで3範囲とloopbackだけを許可し、Hostは `192.168.1.5:55441` に限定する。更新は同じOriginと既存画面のX-Catalog-Actionを確認してから、localhost用のHost/Originへ変換する。X-Forwarded-*による接続元偽装は許可しない。データ本文は変更しない。

DB、既存コンテナ、iWSETの80/443ポートを変更しない。DBポート55439を公開しない。Windows Firewallには宛先192.168.1.5/TCP55441・3範囲・専用exeの規則を追加し、PublicネットワークをPrivateに変更しない。WSLのNATアドレスに依存するportproxyも追加しない。

## 常駐

既存タスクのInteractive、最大72時間、バッテリー条件を変更する。`LIPS-Start-WSL` はUbuntuを所有するWindowsアカウントのパスワード付き起動時タスクにし、`Keep-LipsWsl.ps1` でWSL保持プロセスを監視・再起動する。パスワードはDell上のGet-Credentialで入力し、Windows Task Schedulerの保存機構を使う。配布ファイルやログに保存しない。PIN・UbuntuのsudoパスワードではなくWindowsアカウントのパスワードが必要。

`LIPS-LAN-Gateway` はLOCAL SERVICEで起動。両タスクとも実行時間無制限、バッテリー起動可、失敗時再起動。AC電源時のみスリープ・休止タイムアウトを0にする。バッテリー電源のスリープと既存iWSETサービスは変更しない。アカウントパスワード変更時にはWSLタスクの保存資格情報も更新する。

## Dellでの適用

Ubuntuを開いたまま、Ubuntuを所有する `iWSET_AI` の管理者Windows PowerShellで配布ZIPを展開して実行する。

```powershell
.\Install-DellLan.ps1          # 計画・ハッシュ・前提条件のみ
.\Install-DellLan.ps1 -Apply   # 設定保存後に適用し、HTTP/DB応答を確認
```

初回専用。既存のインストールディレクトリ・タスク・ポート競合は停止する。保存先は `C:\ProgramData\LIPS-LAN`、旧タスクXMLとAC電源設定を保持する。設定適用後の失敗時は復旧スクリプトを実行する。復旧ファイルを残すので、失敗後は原因を確認してから再実行する。

手動復旧（管理者）:

```powershell
& 'C:\ProgramData\LIPS-LAN\Remove-DellLan.ps1'
```

専用ゲートウェイのタスクとFirewall規則を削除し、元のWSLタスクとAC設定を復元する。アプリとDBには変更しない。

## 実機受入

1. Dell上のインストーラーでHTTP/DB応答と両タスクRunningを確認。
2. Ubuntuを閉じて1分以上経過してもAPIが応答すること。
3. 店舗LAN、VPN、倉庫の各PCからURLを開き、商品検索と原表表示を確認。接続できなければ各経路のTCP55441とNAS規則を確認。
4. 許可範囲外の端末は拒否されること。
5. 保守時間帯のWindows再起動後、ログオン前に別PCからAPIが応答すること。既存iWSETへの影響があるため、インストーラーは自動再起動しない。
6. 更新操作は実データの意図した変更で利用者確認。未検証の本番書込みをテスト目的で行わない。

開発側検証: LAN/VPN/倉庫・Host・Originの19判定、隔離HTTPサーバーの7統合試験（2MB multipart、JSON POST、20同時リクエスト等）、既存ローカル4,795件の検索画面と取込画面のブラウザ検証成功。PowerShellは構文検証済み。Dellでのタスク登録、ログオン前起動、各拠点経路は適用後の実機受入待ち。

根拠: [YARP Direct Forwarding](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/direct-forwarding?view=aspnetcore-10.0)、[Register-ScheduledTask](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/register-scheduledtask?view=windowsserver2025-ps)、[WSL networking](https://learn.microsoft.com/en-us/windows/wsl/networking)。
