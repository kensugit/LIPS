# ADR 0005: local ingestion and review
Status: Accepted, 2026-09-17

ローカル確認を拡張する。共有認証未導入のためloopback限定を維持し、変更APIは専用ヘッダーと同一Originを必須化する。酒道楽fixtureはSHUDORAKU_FIXTUREで実マスターと区別する。Fixtureは実在商品と誤認しない名称を使う。
CSVは指定された標準ヘッダーのUTF-8/CP932を対象とし、仕入先コードは投入時に明示する。ExcelはWine to Style専用。Raw/原本/旧snapshotを保持し、DB書戻しは独立Catalog DBだけに限定する。
候補生成は正規化名称・容量・Vintage一致を根拠として提示するが自動Linkはしない。利用者が確認した場合のみCanonical Product作成/紐付け、却下、解除を行い、操作履歴を残す。同時操作はtransaction lockで直列化し、現在のLinkと異なる版の操作は409とする。
フォルダー監視はPoC専用inboxだけを対象とし、ファイル名の日付YYYY-MM-DDを資料日付とする。失敗は記録し有限回の再試行と手動再試行を提供する。
操作の正本は独立DBのOperationLogs。原文や資格情報をログに書かない。SQLによる部分一致検索を提供する。日本語FTSの評価・Embeddingは後続工程とし、意味検索と同一視しない。
