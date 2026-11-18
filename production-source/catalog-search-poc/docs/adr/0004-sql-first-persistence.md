# ADR 0004: SQL-first persistence milestone
Status: Accepted (2026-09-17)

Next workを優先しWine to Style Excel永続化を先行する。iWSETソリューションに参照を追加せず、catalog-search-pocを独立ソリューションとして同じGit worktreeへ追加する。
初回MigrationはSQL/FTSに必要なテーブルを作成する。未使用のvector extensionの作成はEmbedding導入時の新規Migrationへ延期する。Docker構成はpgvector/pg17のまま維持し、Dockerがない開発機では同じPostgreSQL 17のポータブル版でSQLを検証できる。
原本はプロジェクトにコピーせず、SourceDocumentのbyteaとSHA-256で保存する。Rawセル・正規化値・Sheet/RowをEvidenceに保持する。
取込は仕入先単位のtransaction advisory lock、仕入先+hash一意制約、SKU一意制約で直列化・重複防止する。過去資料を後から投入しても現在値を巻き戻さない。資料日付はCLIの--observed-atで明示する。
単一トランザクションで原典・抽出・SKU・履歴・検索文書・Reviewを保存する。自動Canonical Linkはしない。
独立DBのOperationLogsへ取込概要を保存する。iWSET DBへのログ書込はDB非共有契約に反するため行わず、将来API連携で集約する。
