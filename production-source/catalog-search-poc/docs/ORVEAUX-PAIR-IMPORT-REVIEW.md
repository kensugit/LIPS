# オルヴォー固定ペアの隔離取込確認

2026-10-13。本人の「内部IDでこの1組だけ対応付ける」回答に基づく限定接続。

P14s `http://127.0.0.1:56017/`、専用DB `lips_aoc_import_20261013`、PostgreSQL 55993。旧WebとDBは維持。本番操作なし。

## 確認手順

商品検索画面で `Cesseras` を検索する。対象の2024年・750mLが1商品だけ表示され、2原典の名称が併記される。仕入先SKUは「記載なし（内部IDで管理）」となる。在庫は60本であり、2原典の合計120本にはならない。単位未確定の価格は採用していない。原典の価格成分はEvidenceへ別々に保持する。

## APIと証拠

`OrveauxPair:Enabled` と既存 `IntakeReview:Enabled` の両方が有効な隔離Webだけで限定APIを開く。

- POST `/api/import/orveaux-pair/preview`：multipartの `excel`・`pdf`・`answers` と `candidate` JSONを固定SHA・位置・回答版で照合。DB登録なし。
- POST `/api/import/orveaux-pair/commit`：同じ入力と新しい `operationId` で明示登録。
- GET `/api/import/orveaux-pair/operations/{id}`：応答不明時の結果照合。自動再送しない。
- GET `/api/import/orveaux-pair`：固定ペアの保存結果。

原典ファイル全体の登録完了を意味しない。ExtractionJobはPartialであり、汎用取込の全件重複判定を拒否する。別資料・別年への自動拡張はしない。正規Productは作らず、型を明示した内部技術キーと原典SKU欠損を区別する。

専用DB統合試験では初回/並行/重複/operation照合/別VT拒否・2原典Evidence・原本SHA不変を確認。実HTTP10項目ではpreviewの無書込、他VT拒否、Origin/action guard、重複追加0、operation/pair GET、検索のコード種別、件数不変、原本SHA不変を確認。buildは警告0・エラー0。証拠はartifact `aoc-fixed-pair-import-proof.json`、`aoc-fixed-pair-replay-proof.json`、`aoc-http-proof.json`。

このDBの内部IDは隔離環境独自であり、先行ledger専用DBのIDと混同しない。取込済み商品は1件。一般のORVEAUX帳票parser、親フォルダーの保留からこの固定ペアを選ぶ画面導線、回答の受領JSONからのUI接続は未完。APIや検索の成功を目標全体の完了とはしない。
