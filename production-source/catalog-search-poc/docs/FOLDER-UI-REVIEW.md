# 親フォルダー取込・UI整理の本人確認

確認PC: THINKPAD-P14S、EdgeまたはChrome。
URL: http://127.0.0.1:55995/workspace.html
隔離DB: lips_ui_20261011、127.0.0.1:55993。旧55994環境は維持。

1. 「資料を取り込む」の「取引先別の親フォルダーを選択」から、LIPS/artifacts/folder-ui-20261011/本人確認フォルダーを選ぶ。
2. LUC/在庫、Wine to Style/カタログ、unknownを含む8ファイルが表示される。
3. 日付を変更せず「一括取込を開始」。期待結果は成功3（検証済みで取込済み）、保留3、未対応1、失敗1。
4. 「結果を保存」でJSONを保存。LUCの成功2件は同一のsourceDocumentId。Wine to Styleは別の原本ID。
5. 「取引先を指定して標準CSVを1ファイル取り込む」を開くと、任意の仕入先コード・名称を指定する既存機能を使える。

すべて合成データ。実原本の投入・本番操作はしていない。CSVは標準テンプレート専用で、仕入先独自CSVの自動変換は行わない。PDF・旧Excelは未対応。未知/複数候補の取引先、日付/形式不備は保留。保存結果不明ではキュー停止を保持。

## 実装・検証証拠

元版9628d6c6f45566994628c0c5960c748df6bfa294をgit archiveで書き出した独立Git。ローカルcloneはGit shのNtCreateDirectoryObjectアクセス拒否で失敗したため、元ソースを変更せずsnapshot化した。元履歴の子commitではない。差分はsnapshot baseline f1eeb3cとの比較で確認する。
Web build: warning/error 0。ブラウザー/API31項目合格。5取引先の深い階層を含むFolderImportTests合格。証拠はLIPS/artifacts/folder-ui-20261011/results/。
依存復元/テストで詳細なし終了が発生。復元はdisable-parallel、テストは-m:1と診断ログ付きで成功。最初の終了原因は未確定。

本番基点照合、実gateway/OneDrive実資料の受入、本番配備は未実施。新規改良禁止の撤廃は本番操作の許可ではない。
