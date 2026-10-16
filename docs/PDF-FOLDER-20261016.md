# PDF・親フォルダ取込変更（2026-10-16）

Web実装master: `401c2cd549cb12e0e81832071567b66158603a62`。
LIPSの既存未コミット変更は今回のコミットに含めない。

## 実装・確定仕様

PDF読取り、取込失敗の原因と対応方法表示、親フォルダ配下の一括取込、アップロード後SHA256による重複スキップと一覧表示を実装。
在庫〇・◎は潤沢、正確な数量は推測しない。商品コード・商品名がなく◎だけの豊通行はユーザー承認済みの原本ハッシュと条件に限り除外し、Evidenceを残す。
木下PDF523行、豊通650行をローカル検証。全仕入先のPDFレイアウトを網羅するものではなく、未対応形式は原因と対応方法を表示する。

.NET487成功/31環境依存skip、Python6項目、HTTP14項目とブラウザ検証を実施。Linux依存同梱の検証7項目、Migration指紋契約7項目を確認。DB変更なし。Migration11ファイルの実バイトとGit archiveは現行本番と一致。

## 保存したソース

`tools/releases/pdf-folder-20261016.bundle` にWeb独立Gitのmaster履歴全体を保存。
新しい作業フォルダへ `git clone --branch master <bundleの絶対パス> <新しい復元先>` で復元できる。
`tools/patches/pdf-folder-20261016.patch` は保存済み基点3984b7aからの差分。別の本番基点へ無確認で適用しない。

## 本番適用・検証

2026-10-16、本番へ適用済み。

- package: `751b1bcf4f051b4db8391f0de9420b35d632b4de3f60d48a481e16a721fdf7bb`
- Web image: `sha256:515d3995ae23a9ac3e9cb84b9775656dff2486ce4b3c111cd4e351d439b6ce50`
- source: `401c2cd549cb12e0e81832071567b66158603a62`
- バックアップ: `/var/lib/catalog-deploy/backups/20261016T080948-898679.dump`
- Web/DB healthy。DBイメージ・schema不変、DBコンテナは再作成なし。
- 指定親フォルダの51ファイル選択、実際の取込済み原本の重複スキップ、サーバーSHA256表示・結果書出し、PDF保留画面の原因・対応方法を公開画面で9項目検証。
- 確認前後とも商品11,162件、取込履歴29件で不変。既存原本と同一パスの重複だけを検証し、新規の業務取込・51ファイルの一括登録は実行していない。
- 原本PDFの523行/650行解析・HTTP取込は隔離環境で検証。実Linux候補ではPDF読書き・pdfplumber・PDFium/Pillow・AES256を確認した。

利用URL: http://192.168.1.5:55441/workspace.html

## 展開処理の修正

相互TLSエンジンのpayload区分は5MiB上限のため、転送を実ZIP（30,452,249bytes）/zip区分へ修正。ZIPと内包payloadのSHA256をDellで照合し、専用incomingへ配置。一時5987規則・待受けの削除確認済み。

旧固定ビルダーでは検証時のキャッシュがread-onlyで起動チェックに失敗したため、64MiB tmpfs/noexec/nosuidを追加。既存の制限は保持した。
また、現行composeはWebイメージが固定値だったため、単純コピーでは新イメージへ切り替わらず、最初の適用が安全チェックで停止・旧版へ復旧した。
修正版 `tools/remote-deploy/prepare-web-only-20261016.py` は現行の実効compose全体を保持してWebイメージだけ変更し、Docker Composeで再解決して全項目が一致する場合だけ登録する。9回帰テストと実機roundtrip/prepare/deployに成功。

稼働する固定ヘルパーSHA256: `4c1925b226f7a2868e727582b059ecfd9e2c58c9f76b8e67f0e7585ca26654e9`。
管理者による旧ヘルパー控えと失敗候補の記録は保持し、ガード値の書換えで回避していない。
ローカル `tools/remote-deploy/prepare.py` にある他作業の未コミット変更は上書きせず、検証済み版を別名で保存した。

Bundle SHA256: `88dfe4cab8171232a3ee2846ba22bcbfe63c02b4d8c31179fa200db1b2230399`
