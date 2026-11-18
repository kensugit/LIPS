# 容量回答・再利用: 本番DB移行と復旧の管理者手順案

2026-10-12。調査対象ソースは `80ef896dd04d553699d375912d321ae651d7047f`。本書は管理者作業のドラフトであり、実行記録ではない。本人から本番適用までの指示は受領済みだが、最新inspect、正式パッケージ、旧版互換・復元リハーサル・本番Linux原典描画の受入が未確定。本書のコマンドは実行していない。

根拠: [DELL-DOCKER-GUIDE.md](DELL-DOCKER-GUIDE.md)、[ADR0006](adr/0006-dell-linux-container-boundary.md)、[行取込のリリースゲート](ROW-INTAKE-RELEASE-GATES.md)。LIPS作業ルートの `docs/CODEX-DEPLOY-V2.md` を優先し、9月時点のVM未構築という旧文書の記載を現状と扱わない。現行の値は最新inspectで埋める。

## 1. 今回のDB変更と適用経路

| 対象 | 内容 | 注意点 |
|---|---|---|
| `DB/Migrations/20261012010000_IntakeReview.sql` | `IntakeReviews`: 原本bytea、hash/path一意、回答/実行状態JSON、CAS版 | 原本・回答を含むためバックアップは機密データとして管理 |
| `DB/Migrations/20261012070000_IntakeReusableRules.sql` | `IntakeReusableRuleVersions`: ルール版、操作ID一意、要求hash、期待版、JSON | 既存履歴を保持し、新版追記。scope・元回答・原典hashを保存 |
| 既存業務テーブル | 上記SQLによる列変更・削除なし | 新Webでの取込・再開は既存商品、在庫、Evidence、ExtractionJob等へ書く |

2 SQLはいずれも `CREATE TABLE IF NOT EXISTS` の独立DDL。EF Migrationとして登録されておらず、`migrate` だけでは作成されない。`schema-check` はEFの既知/適用済みMigration集合だけを比較するため、新2テーブルの存在・構造検証の代用にならない。`__EFMigrationsHistory` に手書きの適用行を足さない。

Web起動時のDDLはない。`IntakeReview__Enabled=true` と `IntakeRules__Enabled=true` を本番で有効にするには、2テーブルと機能設定・依存を別途検証する。現在の標準composeにはこの2設定もOriginalRender設定もない。単に同梱composeを使うだけでは今回の目標は有効化されない。

Web-onlyの `tools/remote-deploy/build-payload.py` は `DB/Migrations` を含む指紋差を拒否し、`catalogctl deploy` も指紋差を拒否する。これは正常な停止。本リリースを通常prepare/deployへ流さない。SQL削除・指紋偽装・guard書換え・initialize再利用をしない。DB移行はDellの既存LIPS Linux環境で管理者が行う。iWSET JEA、iWSET DB、Windowsサービス、Firewall/鍵/許可元を変更しない。

## 2. 既存証拠と未通過条件

| 項目 | 現時点の証拠 / 不足 |
|---|---|
| 新DB機能 | `MAPPING-ANSWER-REUSE-REVIEW.md` にbuild 0警告/0エラー、通常321合格/環境依存15skip、ルール専用DB3項目skipなしの記録 |
| API/DB | `../../../mapping-api-proof.json`: 隔離DB/56006で12項目。部分回答、CAS、永続化、明示再開、次回適用、原本hash、重複、列変更保留 |
| 原典/UI | `../../../mapping-ui-live-proof.json`、`../../../mapping-folder-live-proof.json`、`../../../mapping-ui-harness-proof.json`。Windows Excelによる実画像/合成帳票。運用資料の業務受入とは区別 |
| 既存本番DBへのDDL | 未実施。現在の同名テーブル・既存schema・Migration状況は最新inspect/管理者のread-only検証待ち |
| 旧Web + 追加2テーブル | 実行互換試験なし。追加DDLだけなら既存列を変えないが、無条件の旧版互換を保証しない |
| 新Webが保存した業務データ + 旧Web | 未受入。`SourceEvidence.Occurrences` と `ExtractionJob.Partial` が問題。旧版が部分保存資料を全体取込済みと扱う可能性を既存release-gatesに記載 |
| 復旧 | 合成専用lips_rules_review_20261012から新規lips_migration_restore_20261012へ実dump/restore成功。資料4/履歴19/回答4/ルール14版8ID/商品2、原本hashと各digest一致、元DB不変。2 SQL各2回再適用で不変。証拠../../../migration-restore-20261012/proof.json。本番相当コピー・旧image起動は未検証であり、この合成検証を代用しない |
| 本番Linux原典描画 | 未対応・未検証。現 `render.py` はExcelでPowerShell/Excel COMを使用。Dockerfileはcurlだけを導入し、Python/PDFium/Pillow/Excelを提供しない。実行ファイル・書込可能cacheも未設定 |
| 正式基点/パッケージ/受渡し | 親担当が最新inspect照合中。新package SHA、image ID、転送先、転送後SHA、Docker実起動は本書作成時未確認 |

**このソースのままで、本番Linux上の「疑義と原典画像の同画面確認」を完成済みと扱わない。** 原典ダウンロードを画像表示の受入へ代用しない。別Windowsサービスの設置、ホストマウント、通信経路、Office導入をこの手順から追加しない。原典描画方式を確定・実証してから全機能を有効化する。

## 3. 作業前に固定する値・検証

管理者が次を記録する。秘密値、原本内容、JSON Body、接続文字列は記録へ出さない。

- 最新inspect取得時刻、現行package/source/Web image/DB image/schema指紋とhealthy、対象sourceの差分。
- 正式target package SHA/サイズ、immutable image ID、真正な新schema指紋、2 SQLのSHA/サイズ、設定差分。
- Dell上の受渡しファイル絶対パス、受信サイズ/SHA。相互TLS一括転送の登録対象として転送する。失敗時に旧SSH uploadへ自動切替しない。
- 現行releaseディレクトリ、実際のcompose/project/DB名/DB user/volume、稼働中gateway、監視/インポート処理、復旧用旧パッケージ。
- 停止時間、作業責任者、バックアップ保存先/容量、復旧先、移行後新規受付を開ける条件。

本番相当の隔離コピーで以下を先に実施する。復元コピーを監視フォルダー・本番gatewayへ接続しない。ルール/原本の実内容はローカル保護領域に限定する。

1. 同一時点のDB dumpを空の隔離DBへ復元し、既存業務件数・選定ID集合/値・原本hash・EF履歴を照合。
2. 2 SQLを一括トランザクションで適用。同じSQLの再適用で構造/行数が変わらないことも確認。既存同名テーブルの場合は構造比較後にのみ実施。
3. 旧immutable Web imageを移行後コピーへ接続して検索/詳細/CSVを確認。旧版に新規受付させない。
4. 新Webで質問→部分回答→全回答→別操作再開→次回再利用、訂正時のルール版更新、未知応答時停止を確認。
5. `Partial` を含むデータに対する旧版の動作を確認。旧版取込再開が安全でない場合、切戻し後の取込停止を必須とする。
6. 移行前dumpを別の空DBへ完全復元し、旧imageで検索・件数・Evidence/原本の整合を確認。dumpの目次確認だけをrestore成功としない。

## 4. Dell管理者の手動適用案

以下は同梱標準の `catalog` compose、`catalog_search` DB、`catalog` user、`/opt/catalog` と `/var/lib/catalog-deploy` が最新実機と一致する場合のbash例。一致しない場合は実機に合わせてレビューしてから実行する。`<...>` は未確定値であり、このまま実行しない。

### 4.1 パッケージと書込み停止

正式Linuxパッケージはクリーンな統合ソースから作成し、DB imageはinspectと同一IDを使う。原本や開発DBをイメージに入れない。旧composeのgateway/監視設定を無条件に同梱composeで置き換えない。承認済み機能設定・原典描画対応を含む候補を事前のread-onlyプレビューで検証する。プレビューは監視OFF・DB読み取り専用で、実データへ回答/取込を書かない。

相互TLS受信・hash検証後、管理者が真正なパッケージをroot保護のapprovedへ登録し、通常の安全な展開処理でtarget releaseを準備する。現行 `catalogctl` の `stage` は内包3ファイル/型/hash/image/composeを検査するが、公開CLIは `stage` サブコマンドを持たない。実機の管理者用ステージ手順を確認し、単純な任意tar展開へ置き換えない。新指紋を旧指紋へ変更してstage/deployを通さない。

利用者の新規アップロード・回答・再開を止め、監視/CLI取込を停止する。Running/Unknownの受付を再GET・Evidenceで照合し、未確定処理があれば移行を始めない。その後LIPS Webのみ停止する。DBコンテナーは停止しない。

```bash
set -euo pipefail
umask 077
state_dir=/var/lib/catalog-deploy
old_sha=$(cat "$state_dir/current")
[[ "$old_sha" =~ ^[a-f0-9]{64}$ ]]
old_dir="/opt/catalog/releases/$old_sha"
target_sha='<検証済み新packageの64桁SHA256>'
target_dir="/opt/catalog/releases/$target_sha"
review_sql='<Dell上の検証済み20261012010000_IntakeReview.sql絶対パス>'
rule_sql='<Dell上の検証済み20261012070000_IntakeReusableRules.sql絶対パス>'
exec 9>"$state_dir/deployment.lock"
flock -n 9
# ロック後、currentが今回inspectの期待packageと一致することを再照合する。
old_dc() { docker compose -p catalog --env-file "$old_dir/release.env" -f "$old_dir/compose.yaml" "$@"; }
new_dc() { docker compose -p catalog --env-file "$target_dir/release.env" -f "$target_dir/compose.yaml" "$@"; }
old_dc stop web
backup_file="$state_dir/backups/before-mapping-$(date -u +%Y%m%dT%H%M%S)-$$.dump"
old_dc exec -T postgres pg_dump -U catalog -d catalog_search -Fc > "$backup_file.partial"
test -s "$backup_file.partial"
mv "$backup_file.partial" "$backup_file"
sha256sum "$backup_file" > "$backup_file.sha256"
old_dc exec -T postgres pg_restore --list < "$backup_file" > "$backup_file.list"
```

旧release/設定とstate current/previous、外部inbox原本も同時点の管理者保護バックアップへ保持する。DB内の原本byteaはdumpに含む。原典cacheは再生成可能で正本にしない。`.dump.list` はローカル保護し、会話に出力しない。バックアップ失敗時はDDLへ進まない。停止後の再開可否を旧環境で確認する。

### 4.2 構造照合・DDL

SQLのSHAを正式manifestと照合する。既存テーブル有無は次の読取りで確認し、結果は表名/真偽だけ記録する。

```bash
old_dc exec -T postgres psql -X -U catalog -d catalog_search -v ON_ERROR_STOP=1 -Atc \
 'SELECT to_regclass('\''public."IntakeReviews"'\'') IS NOT NULL, to_regclass('\''public."IntakeReusableRuleVersions"'\'') IS NOT NULL;'
```

既に存在する場合は列型/null/default/primary/unique/checkの定義を正式DDLと照合する。`IF NOT EXISTS` は既存の誤構造を修復しない。相違があれば中止し、既存データを削除・上書きしない。

`tools/production-review/check_intake_schema.py` はこの照合用の読取専用チェッカー。接続先を明示し、BEGIN READ ONLYでPGカタログだけを照合する。業務行を読まない。欠損/型/null/default/PK/unique/check/index等の15条件を合成DBで検証済み。接続5秒/全体25秒/SQL15秒の制限があり、正常0・不一致1・接続/実行失敗2で終了する。詳細は同READMEとproofを参照。本番では未実行。両表不存在の移行前は想定どおり不一致となり、DDL後にPassedを必要とする。

```bash
{ printf 'BEGIN;\nSET LOCAL lock_timeout = '\''5s'\'';\nSET LOCAL statement_timeout = '\''60s'\'';\n';
  cat "$review_sql" "$rule_sql";
  printf '\nCOMMIT;\n';
} | old_dc exec -T postgres psql -X -U catalog -d catalog_search -v ON_ERROR_STOP=1
```

成功後に存在だけでなく構造を照合し、新規作成なら両表0行、既存なら事前件数を保持したことを確認する。既存業務テーブル件数/選定ID集合/値/Evidence、EF履歴を移行前と比較する。DDLは業務値を書かないので、差異があれば新Webを起動せず調査する。接続断/結果不明なら先に構造とtransaction結果を照合し、自動再送しない。

### 4.3 新Web起動と適用記録

2 SQLと構造照合が通り、原典描画・設定・事前リハーサルの全条件が揃った後だけ実施する。

```bash
# EF整合チェックは補助。新2テーブルの検査は4.2で別途済ませる。
new_dc run --rm --no-deps web schema-check
new_dc up -d --no-deps web
curl --fail --silent --max-time 10 http://127.0.0.1:55440/api/options >/dev/null
```

immutable Web image ID・healthy・DB接続・実gateway・既存検索/CSV・原典描画/参照を確認する。取込受付は確認が済むまで再開しない。`/api/import/reviews` 等のレスポンスには原資料の情報が含まれるため全件本文をログ/会話へ出力しない。既存公開/認証設定を変更しない。機能がOFFのままなのを成功扱いにしない。

その後、管理者が最新実機の展開state形式を確認し、同一ロック内でprevious/currentを正式旧/新packageへ原子的に更新する。現行stateが想定と異なる場合は書き換えない。これは手動DB移行の完了記録であり、Web-onlyガードを欺くための事前更新ではない。Web正常性未確認ならcurrentを新SHAへ進めない。起動失敗時は次節へ進み、停止・復旧の実際の結果を残す。

## 5. 失敗時の復旧

### 新Webが業務書込みする前

旧Web+追加2テーブルの互換リハーサルが合格していれば、管理者が新Webを停止し、旧releaseの `up -d --no-deps web` で検索を復旧する。追加テーブルは残し、履歴・原本をDROPしない。DBは移行後のままであることを記録する。この復旧を「DB巻戻し済み」と呼ばない。旧版での取込は互換性確認まで止める。stateは実際の稼働旧packageへ照合・更新する。

### 業務書込み後 / 互換性が不明

Webを旧版へ戻すだけでは復旧完了にならない。新規受付・監視・CLIを停止し、現在DBを別名のdumpへ保存する。移行前backup以降の受付/回答/ルール/取込/変更された商品とEvidenceをローカルで特定する。Unknown/Runningを再送で消さない。書込み済みの実業務変更がある場合は、復元で失う範囲と再適用/照合方法を管理者・本人と確定するまで破壊的復元しない。

完全復元は、隔離リハーサル済みの移行前dumpを**空DB**に復元する。既存DBへ `pg_restore --clean` だけを行うとdumpに存在しない新2テーブルが残り、旧業務状態と新受付状態が混在するため避ける。

以下は全書込み停止・現状保全・復元範囲確定後、対象が専用LIPS `catalog_search` と再確認できた場合だけ管理者が行う例。既存接続が残ればdropdbは失敗させる。`--force` で強行しない。

```bash
# old_dc、backup_fileは本作業で検証済みの値を保持していること。
old_dc stop web
# 保護された最新dumpの取得と、backup_fileのSHA・復元リハーサルを再確認済みであること。
old_dc exec -T postgres dropdb -U catalog catalog_search
old_dc exec -T postgres createdb -U catalog -O catalog catalog_search
old_dc exec -T postgres pg_restore -U catalog -d catalog_search \
  --exit-on-error --single-transaction --no-owner < "$backup_file"
old_dc run --rm --no-deps web schema-check
old_dc up -d --no-deps web
```

この段階でsource原本/DBの時点、旧image ID、件数/選定値/Evidence/EF履歴、healthy、gateway検索を再照合する。復元したDBに新2テーブルが無い場合は新機能を起動しない。新Webで受け付けた原本・回答は退避dumpから調査できるよう保持し、既存商品への無条件再取込をしない。原本/inboxの復旧もDBと同じ時点へ整合させる。復旧失敗時は取込停止を維持し管理者対応とする。`down -v`、volume削除、Docker pruneは行わない。

## 6. 完了報告に必要な記録

転送・DB移行・Web切替・実業務受入を別々に記録する。Dell受信絶対パス/サイズ/SHA、移行前後schema、SQLのSHA/実行結果、バックアップ絶対パス/SHA/復元リハーサル結果、旧/新image、稼働package、原典画像、本人が承認した実資料の初回→回答→再開→次回再利用を根拠として残す。取込なしのhealthだけで本番目標完了にしない。未通過条件は未完として残す。
