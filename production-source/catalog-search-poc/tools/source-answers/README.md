# 受領済みラヴィネ回答の保存

`import_answers.py` は受領済みの容量 L と地域 10 対応だけを既存の回答 API に保存します。取込再開、再利用ルール公開、商品保存は実行しません。既定は preview、保存には `--save` が必要です。利用先は承認された隔離 loopback Web に限定してください。

必須引数:

- `--answers`: 受領 JSON のローカルパス
- `--pending`: `pending-source-rows-local.json` のパス
- `--base-url`: 隔離 Web の `http://127.0.0.1:PORT`
- `--review-id`: 事前作成した原典レビュー UUID
- `--expected-answer-sha`: 独立して確認した受領 JSON の SHA256
- `--expected-source-sha`: 独立して確認したラヴィネ原典の SHA256
- `--expected-columns-fingerprint`: 検証済みの厳密 11 列 fingerprint
- `--volume-binding` / `--region-binding`: 下記の固定 binding

入力 JSON から期待値を自動生成して指定してはいけません。レビュー版・hash は GET で取得し、原典 GET のバイト列 SHA も照合します。JSON の schema・2 項目の ID/binding/版 1/回答文/履歴空を厳密照合します。既存回答が同じなら省略し、異なる既存回答は停止します。

固定値（受領確認済み）:

```
answer SHA256: 595df47e6a9f9ae84ba7db7203e894c131b438d654010b38f457ea28a33c3ac3
source SHA256: 3df70f0f9dd75275ac4cf6adad509485a091eaff87a880806d1eb9d0b76b5f03
volume binding: 2f51f54e2e9e798bb0624a5f9cc253661f5a037f4c8e6bf7283cbc1a6a2ab866
region binding: 597a0d41f23d74aa5028ae2e6ca6ae11924be3d1543092a33da36d873c23bc97
```

未知の地域略号（選択肢空）と正整数 mL に換算できない容量は回答対象から除外し、理由別件数を出します。原略号の置換や容量の補正はしません。その他の scope/choices 不一致は停止します。全質問に回答できなければサーバーの Held を維持します。

POST の後は必ず GET で版・原本 hash・今回の Human/mapping 履歴・回答群を照合します。POST 応答不明でも再送せず GET だけ実施し、照合できない場合は終了コード 2 で停止します。版競合も自動上書きしません。結果が不明のまま `--save` を再実行せず、履歴と現状態を確認してください。

出力は JSON の状態・SHA・件数だけです。原本パス、原典本文、回答 JSON 全文、HTTP エラー本文は出しません。preview の payload は本文の代わりに SHA と対象件数で表示します。

合成 HTTP 回帰:

```
python -m unittest -q test_import_answers.py
```

実資料/本番 DB を使わず、preview・保存・再実行省略・binding/hash 改竄・別 scope/略号・Unknown・既存回答競合・409・保存前後の通信切断を検証します。CLI の実回答保存は別途承認済みの親担当が実施します。
追加のJ/納価単位質問はこの受領JSONに回答がないため除外件数へ記録します。容量Lを価格の本単位へ流用しません。
