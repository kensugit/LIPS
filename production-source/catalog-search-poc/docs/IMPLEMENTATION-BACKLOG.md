# Implementation backlog

2026-09-17追加: 複数語AND、英字大小無視、同言語の生産者照合、古い候補画面からの確定拒否を完了。既存simple FTSは稼働、日本語正規化/意味検索評価は未完了。本番Dockerは別セッションに委譲。

2026-09-17 ローカル操作確認版: CSV・fixture snapshot・手動Review・未Link横断/共通商品表示・取込画面・フォルダー監視は完了。詳細 docs/WORKFLOWS-GUIDE.md。以下は当初計画も含む。未完了は実接続/watermark、JAN/生産者を含む候補精度評価、日本語FTS/Embedding、認証、汎用抽出。

## Slice 1: deterministic ingestion

- SourceDocument/FileHash/Evidence entities and first Migration
- CSV parser with encoding detection (UTF-8/CP932)
- SupplierProfile column and inventory mappings
- idempotent import by content hash
- inventory parser tests beyond the included baseline

## Slice 2: 酒道楽 snapshot

- fixture adapter first
- source Schema confirmed後にread-only PostgreSQL adapter
- SyncRun, watermark, record hash, deactivate semantics
- InternalProductRecord → ProductSearchDocument generation
- active/inactive filter and provenance display

## Slice 3: canonicalization and review

- normalized text/JAN/vintage/volume candidate generator
- match score evidence
- ReviewTask/ReviewAction
- confirmed/rejected link feedback

## Slice 4: hybrid search

- PostgreSQL FTS with Japanese normalization policy
- embedding provider and pgvector column
- Reciprocal Rank Fusion or weighted rank (evalで決定)
- Product view / Internal-product view
- 未Linkの酒道楽商品を落とさないunion query

## Slice 5: UI and evaluation

- search/filter/results/detail/evidence/review pages
- golden query set and relevance judgments
- false-positive/false-negative matching report
- AI cost/latency/usage logging

## Definition of first demo

代表CSV 1件と匿名化した酒道楽fixtureを投入し、「ブルゴーニュの赤、在庫あり、1万円以下、エレガント」で、Canonical商品・仕入先条件・酒道楽登録の有無が同じ画面に表示されること。
