# 本番適用記録に対応するWebソース

`production-source/catalog-search-poc/` は、保存済みの2026-10-17本番適用・引継ぎ証跡に対応するソース全327ファイルの固定スナップショットです。取込ツールだけでなく、Web、Application、Infrastructure、DB migration、テスト、ビルド・配置定義を含みます。

- 元のsource commit: `3cf2a1dcebf2e75c13615356a592a9698433dbb5`
- 対応package: `757b48ee915274f4bbaf2d7b01b5fed6592b179c1da7f00bb64abd00e26d85af`
- 対応Web image: `sha256:29276150f4db0308da9b8a5e72df730186ae530f7d4b815ee79d7723e317a9c3`
- ファイル一覧・サイズ・SHA-256: [manifest](production-source-manifest.json)

元のGitコミットから通常ファイルを抽出し、全ファイルのbytes一致を検証しました。元の履歴は移植せず、本リポジトリの別コミットとして保存します。上記元コミットがこのリポジトリのGit履歴に存在することは意味しません。

本番の秘密設定、認証情報、DBダンプ、原典・取込データは含みません。設定例のパスワードは開発用プレースホルダーです。原本のCODEX-HANDOFF.mdは履歴として変更せず保持しています。

## 確認範囲と次工程

2026-11-18の本作業はソースの公開・同一性確認です。本日の実機inspect、ビルド・回帰試験、本番変更は実施していません。保存済み記録ではAI接続は無効です。ソース内の自由文検索実装の存在は本番AI有効化の証明ではありません。

AI有効化はウサギマスターと既存展開担当で、最新inspect、BASE接続証跡、固定実行kit、操作対象の承認・排他、Web/llm-linkの同時再作成と切戻しを照合して進めます。旧ApplyとUNKNOWNを再実行しません。
