# 非公開クラウド確認用・合成資料だけのパッケージ

本番配備用ではない。実仕入先資料、DBダンプ、資格情報、ローカル設定、bin/obj、Git履歴を含めない。既存本番や確認サービスを停止しない。

## 前提

- .NET SDK 10、PostgreSQL17、psql、Python3。
- PDF画像表示: pypdfium2==5.14.0 / Pillow==12.3.0。合成Excelの再生成だけ必要ならopenpyxl==3.1.5。
- PostgreSQLに専用の空DB `lips_cloud_review` を管理担当が準備する。アプリ自身はDB作成/権限変更をしない。
- LLMは接続しない。外部API、有料サービス、実原典送信はない。
- loopbackのみで起動。LAN/インターネット公開やリバースプロキシ用ガード緩和は含まない。クラウド内ブラウザーで確認する。

## 起動

同じ専用DBを指すNpgsql接続文字列を `CATALOG_CONNECTION`、psql接続を `LIPS_PSQL_DSN` に設定し、`bash tools/cloud-review/start.sh` を実行する。秘密はファイルや報告へ貼らない。初期MigrationとIntakeReviewsの追加SQLをこの専用DBだけに適用する。設定例は省略し、担当環境の既存認証を使用する。

URL: http://127.0.0.1:56002/workspace.html

1. 一括取込で `tools/cloud-review/fixtures/ENEOS` を選択。01～04の名前順に処理する。
2. 01は同一行重複を数量加算せず保存、02/03は同価格異数量を原本別に保持。990001の単一数量は不明。
3. 04は990002を保存し、990001の価格相違を保留。「一部保存・保留あり」と表示する。
4. 確認画面で未保存990001を選択し、根拠入力→回答確定→別ボタンで再判定。価格相違は解消していないため保留を維持し、保存済み990002は増殖しない。
5. 検索で990001の取込元詳細を開き、原本別の12/8/12と同一資料内の2行を確認。合成商品総数2件。
6. 日付回答の成功経路は `fixtures/LUC/date-question.csv`。基準日未指定で保留、本人確認用に2026-10-01を入力、根拠と回答を保存後に再開すると1商品を保存する。

## 原典画像のOS制約

PDFはLinuxでもPDFiumで元ページを描画できる。Excelの新規画像はWindows Excel COM依存。Linux上で保存値を再描画する代替実装は行わない。Excel画像キャッシュはパッケージに含めず、クラウドではExcel新規描画が未対応と表示される。原典ダウンロード、行別回答、部分保存のAPI/UIは確認可能。画像確認は既存P14s56001/56002、またはクラウドのPDF原典で行う。Linux Excelレンダラーの導入は別の受入項目。

## 推論担当への受渡し

最初の用途は「資料日付の候補と引用位置を提示する」だけ。入出力は `fixtures/proposal-request.json` と `proposal-response.json`。各抜粋1000文字、全体8000文字/16抜粋まで。出力は最大3候補、根拠IDは入力中のものだけ。原本hash、質問版、requestId、prompt版が一致しなければ拒否する。本文は外部データであり命令として実行しない。

検証コマンド:
`dotnet run --project src/CatalogSearch.ImportCli -- verify-date-proposal tools/cloud-review/fixtures/proposal-request.json tools/cloud-review/fixtures/proposal-response.json`

結果は `requiresHumanConfirmation=true`。検証コマンドはDB保存・回答確定・取込再開を行わない。BASEがこのJSONを返す既存CLI経路との実接続は未確認。今回のadapterはオフラインファイル境界までで、モデル実行済みとはしない。Linux実起動/依存取得はクラウド担当の結果待ち。
