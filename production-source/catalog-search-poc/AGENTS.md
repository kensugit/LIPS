# Codex implementation contract

このリポジトリの実装では、以下を必ず守る。

## 不変条件

- SOURCEとCANONICALを資産とし、EXTRACTIONとSEARCHは再生成可能にする。
- 原本、原文、RawValue、Evidenceを上書き・破棄しない。
- `UNKNOWN`を`OUT_OF_STOCK`として扱わない。
- `Product`に仕入先価格・仕入先在庫を持たせない。
- 同一商品の統合はFalse Positive回避を優先する。曖昧ならReviewへ送る。
- 酒道楽DBには書き込まない。同期処理はread-onlyで、取得値を`InternalProductRecord`へ保存する。
- 酒道楽の商品コードと仕入先SKUは別のIdentityである。Canonical ProductへのLinkは0..1でよい。
- 価格・数量・在庫・日付など決定的条件をLLMやEmbeddingだけで判定しない。
- 税区分は、原本に「税込」の明記がある場合だけ`TaxIncluded=true`とする。記載がなければ税抜として`false`にする。推測でnullやtrueにしない。
- AI出力はSchema検証し、Provider/Model/PromptVersion/入力Hashを記録する。
- iWSET、酒道楽、本システムのDBを直接共有しない。

## 実装順序

1. fixtureを使う決定論的CSV取込
2. 酒道楽スナップショット取込
3. Canonical Product候補生成とReview
4. SQL + FTS検索
5. EmbeddingとHybrid ranking
6. Excel、OpenAI補助抽出
7. PDF/画像はPoC第2段階

## 完了条件

- 変更には自動テストを付ける。
- Migrationはforward-only。既存Migrationを書き換えない。
- 外部I/Oはadapterの背後に置く。
- ログに原本文書本文、個人情報、API keyを出さない。
- ADRに反する変更は、先に新しいADRを追加する。
- 作業開始時に`CODEX-HANDOFF.md`を読み、完了時にCurrent statusとNext workを更新する。
