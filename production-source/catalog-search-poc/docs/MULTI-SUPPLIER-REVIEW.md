# 複数取引先親フォルダーの現行版確認

確認PC: THINKPAD-P14S、http://127.0.0.1:56015/workspace.html 。専用合成DB lips_partial_lavinee_20261012（55993）。

合成確認フォルダー: LIPS/artifacts/folder-ui-20261011/multi-supplier-e2e-56015/parent（fixture.json記載の絶対パスを使用）。実原本は含めない。

## 確認手順と期待結果

親フォルダーを選択し「一括取込を開始」。5ファイルの結果は成功1・保留2・未対応1・失敗1。

- LA_VINEE指定帳票: 登録済みの同社ルールを適用し2商品行を保存。
- LUC指定帳票: 容量単位2質問を保留。LA_VINEEの容量Lを流用しない。
- LUC/LA_VINEE両社名が混在する階層: 取引先不明として保留。
- PDF: この親フォルダー取込では未対応。
- 空CSV: ファイルごとに失敗を表示し送信しない。

既にこの試験は完了しているため再取込は不要。次のLUC画面から原典画像と未回答2質問を確認できる。
http://127.0.0.1:56015/intake-review.html?id=b6c64de7-5a6e-487f-ac02-f8a51ef49fd4

## 証拠

実Edge/API27項目合格。再選択では成功原本は今回追加0、同SourceDocument/同review、商品総数4と成功review履歴は不変。API Evidenceの原本hash、在庫・容量、元fixture不変も確認。2回のバッチでfolder POST8回のみ。空ファイルPOST0、回答/再利用登録POST0。LUCの実原典描画と行6の質問を確認した。

proof.json、fixture.json、first-batch.png、duplicate-batch.png、luc-original-questions.pngを上記artifactへ保存。今回のmixed試験では卸価格明細の独立DB検査は繰り返していない。先行partial-reuse-e2eのDB10項目とは分けて扱う。

## 未完事項

同一商品との受領回答があるオルヴォー1ペアの処理接続、他の実資料判断・適用は未完。P14sの操作経路と全取引先の全形式対応を混同しない。元PDFを表示できることとPDF取込対応も別である。本番操作なし。
