# 原典画像表示の所要時間（2026-10-12）

THINKPAD-P14S / 隔離Web56001 / 合成Excelで測定。本番・商品データは変更していない。

## 変更

- Excelの印刷設定をPrintCommunication=falseでまとめ、trueで確定する。元の書式・原本は変更しない。
- 子COM参照を終了前に解放。Quit後に残る専用Excelの終了待ちを3秒から0.5秒へ短縮。起動前に存在しなかったPIDかつ開始時刻が一致する専用プロセスだけを処理する。
- 待機中の画面に経過秒数を表示し、成功・失敗時にタイマーを止める。自動再試行は追加しない。
- 工程別時間を派生キャッシュのtimings.jsonへ保存する。文書内容は記録しない。

## 実測と限界

| 測定 | 結果 |
|---|---|
| 変更前、95行の新規描画 | 21.97秒 |
| 印刷設定集約後、95行の新規描画 | 10.62秒 |
| 終了待ち短縮後、95行の新規描画 | 17.51秒 |
| Webから97行を初めて表示 | 44.91秒 |
| 同じ97行を再読込、3回 | 0.304 / 0.362 / 0.246秒 |

PC負荷の観測値はCPU100%。Excelの起動・読込・PDF出力すべてにばらつきがある。連続試験にはOSキャッシュ・負荷差があるため、変更だけで初回時間が何%短縮されたとは評価しない。終了工程は3.57秒から1.62～1.75秒だったが、初回の遅さは未解消。別範囲の初回描画にはExcel起動が必要。次の検討は負荷が低い時の再計測と、原典忠実性・私有Excel終了保証を保つ描画単位の改善。既存キャッシュと原本ハッシュ検証は維持する。

変更前後のbase.png/highlight.png（1023×437）はそれぞれ全ピクセル一致。専用Excel終了を確認。Edgeで経過秒表示、表示完了後のタイマー停止、原典画像decode、再表示3回、ブラウザーエラーなしを確認。

証拠はLIPS/artifacts/folder-ui-20261011配下のrender-performance-before.json、render-performance-after.json、render-performance-final.json、render-ui-performance.json。

## 確認

THINKPAD-P14Sで次を開く。合成画像の確認だけで、日付回答・取込は不要。

http://127.0.0.1:56001/intake-review.html?id=f2036333-4d2c-4815-b226-346dd13545d4&view=original&sheet=1&row=95

未表示の行を指定して表示すると待機中に秒数が進む。同じ範囲を再表示すると保存済み画像を使う。原典の文字・罫線と強調範囲を確認する。

印刷設定集約の仕様: https://learn.microsoft.com/en-us/office/vba/api/excel.application.printcommunication
