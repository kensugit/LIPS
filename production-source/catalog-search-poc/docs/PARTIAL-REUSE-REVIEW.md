# 部分保存後の回答を次回の原典へ再利用する

確認PC: THINKPAD-P14S、Web http://127.0.0.1:56015/workspace.html 。専用合成DB lips_partial_lavinee_20261012（55993）。本番・実資料DBは変更していない。

## 確認する画面

- 回答元: http://127.0.0.1:56015/intake-review.html?id=f34fb5c5-772c-4245-a4ed-665b89f5235b
- 次回資料: http://127.0.0.1:56015/intake-review.html?id=8a345d0a-9c3c-42ac-ba98-ade612b01587
- 再利用設定: http://127.0.0.1:56015/intake-rules.html

回答元の原典画像・保存回答6件を確認し、再利用設定画面で容量L、税別納価PerBottle、地域AL/BGの4設定を確認する。これらは検証で登録済みなので再登録する必要はない。

次回資料では、同じ2商品について新しい原典の容量500/1500mL・在庫6/4・税別納価1535/1836が使われた。再利用するのは意味だけで、前回の容量750mL・在庫9/10・価格1235/1236を転記していない。納価はWholesaleのEvidenceとして保持し、希望小売価格はnull。

## 実動作の証拠

Edge/API35項目合格。部分保存でCompletedになった元レビューから4groupを画面で公開し、各operationをGET照合。親フォルダーから翌日資料を選択すると再利用条件が適用され、Human回答を捏造せず取込完了。

未知地域ZZはHeld、価格列の見出し変更はUnsupported（受付レビューはHeld）。両資料は業務SourceDocumentを作成せず既存商品数/値を変更しない。原本hash不変。

別担当によるDB/HTTP読取10項目合格。新原典と旧原典のEvidence・価格/在庫履歴を各1件/商品で保持し、商品IDは同一。新原典のMappingEvidenceには再利用ルールID/版/回答根拠がある。SearchTextと原本DB/HTTP hashも照合。

証拠: artifacts/folder-ui-20261011/partial-reuse-e2e-56015 のproof.json、db-proof.json、verify_db.py、PNG。既存書込試験は反復せず、再確認はGET・原典表示で行う。

## 残項目

実LaVineeの未確定地域70件・納価単位の本人判断、AOC限定同一性と他帳票への実接続、Linux HTTP統合、実資料全値受入は未完。本合成結果をそのまま実資料受入や全取引先対応と扱わない。
