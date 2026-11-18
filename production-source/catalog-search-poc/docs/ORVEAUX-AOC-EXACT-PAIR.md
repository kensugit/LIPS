# オルヴォー限定ペアの回答照合

2026-10-12、受領済み回答を特定原典ペアに限定して照合する副作用のない Application policy を追加した。

## 対象と照合

- Excel 原典 SHA-256: `eae86237e63cfef92a22aef292b714831341d9a1525a00657d8126c2f4706947`。シート `新着 `（末尾空白あり）の 66 行と、関連価格の 65 行。
- PDF 原典 SHA-256: `4461f609796057ec6f03388e573e6446fe6618d1937f81558f99dc9bc9ff6828`。15 頁の対象名・2024 年・750 mL・60 本の組合せ。座標は創作せず、頁と当該組合せで限定する。
- 受領回答 export SHA-256: `595df47e6a9f9ae84ba7db7203e894c131b438d654010b38f457ea28a33c3ac3`。既存項目 binding と版 1 を厳密照合する。既存確認表や binding は変更していない。

公開 `OrveauxAocPairPolicy.Resolve` は両原典と回答 export の bytes を必須とし、固定 SHA を自ら計算して照合する。呼出側が提示する候補の取引先・位置・原文・VT・容量・在庫・価格も確認する。候補抽出は呼出側の責務であり、この policy 自体が Excel/PDF を解析するものではない。

過去の「AOC だけの違い」という説明は不十分だった。Excel の `Olric` と PDF の `Orlic`、日本語の `セスラ` と `セスラス` の差もある。両原文を保持し、受領済みの同一ペアに対する回答を失効させない。一般的な AOC 削除・名前正規化へ拡張しない。別の 2023 年・70 本の行は対象外。

価格は通常納価 1,980、参考上代 3,300、明記税込 3,630、条件付き 1,880 を別 Evidence として保持する。条件付き価格も既定の税区分規則と税別列を根拠に税抜とするが、12 本価格の単位・詳細条件は未確定であり、通常納価へ採用しない。

## 検証と未接続範囲

対象試験は 18 件合格（合成・否定 17 件、提供済み原典と回答 export の実 bytes による公開入口の正例 1 件）。実ファイル 3 件の前後 SHA は不変。取引先・hash・位置・VT・容量・在庫・原文・価格混同・回答 binding/版/意味の変更は保留になることを確認した。実ファイル試験は `LIPS_AOC_EXCEL`、`LIPS_AOC_PDF`、`LIPS_AOC_ANSWERS` による opt-in であり、通常試験に実原典依存を持ち込まない。

当初の結果は `ResolvedEvidenceOnly`、`BusinessImportAllowed=false`。この結果を商品登録・実取込の完了とは扱わない。本番操作は行っていない。

## 2026-10-13 内部 ID による固定ペア対応付け

本人から「内部IDでこの1組だけ対応付ける」と明示回答を受領した。`OrveauxExactPairStore` と forward-only SQL `20261013010000_ExactSourcePairLinks.sql` を追加した。両原典と回答 export の bytes を既存 policy で確認後、`ExactSourcePairLinks` に固定 pair key・予約内部 ID (`ReservedProductId`)・版 1・各原典位置と原文・回答 binding を保存する。`ExactSourcePairOperations` は操作 ID と receipt を保存する。対応付けは immutable であり、変更・自動統合・他年への適用を行わない。

同じペアへの並行保存・別操作からの保存でも予約内部 ID は一つ。同じ操作 ID の再送は同じ receipt を返し、異なる payload は拒否する。応答不明時は `GetOperation` と `Get` で保存結果を照合し、未確認のまま自動再送しない。

専用隔離 DB `lips_aoc_identity_20261013` で統合試験 1 件合格。SQL 反復適用、並行保存、再取得で両原典位置が同じ ID に結び付くこと、重複防止、異 payload/別 VT/誤版の拒否、原本前後 SHA 不変を確認した。

保存済み予約内部 ID は `6f84c524-e4c5-44b4-b77c-f98535ba09fa`、版 1。非秘密の確認記録は artifact `aoc-identity-store-proof.json`。内部 ID 方針は本人承認済みであり、同じ判断を再質問しない。

この ID は予約済み対応付けであり、既存 `Products` や `SupplierProducts` の商品登録ではない。canonical 名や仕入先 SKU を捏造しないため、商品・SourceDocument・価格・在庫・検索テーブルは作成も変更もしていない。専用 DB に業務テーブルが存在しないことも検証した。検索・取込・UI 接続は未完。価格単位未確定の保留と、原典全体を取込済みにしない境界は維持する。

## 続行：種別付き内部 ID の限定取込

上記は ledger 単独の検証記録。続く実装では新たな隔離 DB `lips_aoc_import_20261013` に限定して `OrveauxExactPairImportStore` の受入を検証した。`CodeKind=InternalIdentity`、技術キー `internal:<予約ID>`、原典 SKU は null。既存商品の種別既定は `SupplierSku` を維持する additive SQL `20261013020000_ExactPairCatalogImport.sql` を用いる。`Products` は作らず、`SupplierProduct.ProductId` は null、表示名は両原文の併記である。

当該 DB の内部 ID は `b2ec3bac-b41d-470a-b7f6-494f83b81cd6`、取引先商品 ID は `6d91c31b-ced8-4cd1-a13d-51dddae95130`。別の ledger 単独試験 DB の予約 ID とは別環境であり、同一 DB 内の重複発行はない。原典 2 件を各々 SourceDocument/SourceEvidence として保存し、共通商品への根拠を保持。在庫は原典別に 60、商品現在値も 60 で加算しない。4価格の単位は Unknown のまま Evidence に保持し、現在価格・価格履歴の採用額は null。

Excel G1 の 2026-09-30 を明示的な基準日として使用し、PDF 自体の日付と偽らない。各 ExtractionJob は Partial/1 行。通常 store に同原典を渡しても全原典取込済みへ読み替えず、専用照合が必要として保留する。

初回並行受入の追加合計 1、2原典再取得、別 VT 拒否、原本 SHA 不変、現在価格未採用、既存 SupplierSku 既定、全原典取込済み防止を隔離統合試験で確認。再 POST は今回追加 0・既取込を返し、operation GET は初回 receipt を変更せず保持する。証拠は `aoc-fixed-pair-import-proof.json`（初回）と `aoc-fixed-pair-replay-proof.json`（再送表示修正後）。本番未適用。HTTP/UI の確認は別工程として記録する。

## 親フォルダーの受付履歴からの接続

`GET /api/import/orveaux-pair/review-candidates` は固定原典 hash を専用検索し、最新 100 件の履歴制限に依存せず安全な Held/Ready レビューを返す。同じ hash の別 path が複数あれば自動選択せず、`excelReviews` / `pdfReviews` と `requiresReviewSelection` で選択を要求する。

`POST /api/import/orveaux-pair/reviews/preview` と `/reviews/commit` は受領回答 JSON 一件とレビュー参照（ID/版/hash）を受け取る。原典は再アップロードさせず保存済み Content の SHA を確認し、固定 bytes に対してだけ既知の抽出候補を構成する。汎用 parser の追加ではない。commit 時はレビュー行を transaction 内でロックし、版/hash/状態を再照合する。Unknown、Running、StopQueue は拒否。応答不明は既存 operation GET で照合する。

対象試験 6 件合格。親フォルダー受付→通常判定で Excel は Held、PDF は Unsupported の結果を持つ Held レビューとなり、どちらも原本内容を保持すること、そこから限定ペアの保存を呼び出せることを確認した。限定ペア保存後もレビューは Held・元版のまま残し、全原典完了と偽装しない。UI/HTTP の実確認は別証拠に記録する。
