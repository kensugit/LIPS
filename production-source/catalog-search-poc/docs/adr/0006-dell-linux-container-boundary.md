# ADR 0006: Dell dedicated Linux VM and constrained container deployment
Status: Proposed for Dell application, implementation prepared 2026-09-17.

既存Dell Windows/iWSET/JEAを維持し、Hyper-V上の専用Ubuntu 24.04 LTS amd64 VMへDocker Engineを置く。OS適合は既存Windows 11 Pro記録、現在JEA状態読取の成功まで確認。RAM/仮想化/空き容量/Hyper-V状態は新しい実機readinessが必要。VMの作成・ホスト再起動は別の明示的承認後。既存NICの外部スイッチ化はスクリプトでは行わない。

Linux host networkをWebだけに使い既存127.0.0.1/Host/Origin制限を保持。DBは専用volumeと127.0.0.1:55439。未認証PoCをLANへ公開せず、SSH転送経由で127.0.0.1:55440へアクセスする。Docker socket/TCPを公開しない。iWSET DBやサービス・既存JEA許可範囲は変更しない。

環境構築は既定planのみ。Linux初期DB構築はDell側VM管理者が実行。CodexからはSHA256承認済みパッケージによるWeb-only更新のみ。schema-checkが未適用/未知migration/接続失敗を検出した場合拒否。DB image変更も拒否。各パッケージの利用者承認とVM管理者によるroot保護領域への登録が必要で、環境構築承認を個別リリース承認に流用しない。

DB更新を伴うリリースはCodex自動デプロイ対象外。バックアップ・VM管理者の手動移行・事後確認を必要とする。rollbackはDB無変更の直前Web imageのみ。source原本はイメージへ含めず、実データ移送は別工程。
