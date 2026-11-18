# Intake追加2表のread-only構造検証

`check_intake_schema.py` は新しい受付・再利用テーブルの**構造だけ**をPostgreSQLカタログから検査する。業務行、原本bytes、回答JSON、接続文字列/秘密値は出力しない。DDL/DML/一時テーブル作成は行わない。クエリは `BEGIN READ ONLY` 内で実行し、その状態も確認する。

既存CLI `schema-check` はEF Migrationのみを比較するため、本ツールと併用する。どちらかが失敗したら本番移行・機能有効化を完了扱いにしない。本ツール合格は、バックアップ復元、旧Web互換、実行権限、原典描画、業務データの受入を保証しない。

## 実行

Python 3標準ライブラリーとpsqlを使用する。PostgreSQL 17の合成DBで検証済み。既存の承認済み管理者経路・認証ファイル/secretを利用する。パスワードを引数に入れない。`-w` でパスワード入力を要求しないため、認証不足ならfailする。子プロセス専用の環境コピーへ `PGCONNECT_TIMEOUT=5` を設定し、psql全体も25秒で打ち切る。SQL自体は15秒statement_timeout。親環境は変更しない。timeout時は接続情報を表示せず終了値2。

```bash
python3 tools/production-review/check_intake_schema.py \
  --psql /usr/bin/psql --host 127.0.0.1 --port '<確認済みDBポート>' \
  --user '<確認済みDBユーザー>' --database '<確認済みLIPS専用DB名>'
```

出力はJSON。`status: Passed` / 終了値0が合格。構造相違は `status: Failed` / 終了値1、接続/読取/設定不明は終了値2。`--schema` は既定public。英数字/underscoreのDB/schema/user名だけを受け付け、接続URIやSQLは受け付けない。対象/ポート/ユーザーは必須。パスワード/ホスト設定をファイルへ保存しない。

同フォルダーの `check-intake-schema.sql` はカタログmetadata収集専用で、SQL単独の終了値は構造一致を意味しない。必ずPythonラッパーの判定を使用する。配布時はこのフォルダーと、リポジトリの同じ相対位置の2 SQLを含める。

- `DB/Migrations/20261012010000_IntakeReview.sql`
- `DB/Migrations/20261012070000_IntakeReusableRules.sql`

期待DDLのUTF-8 BOM/CRLFを正規化したSHAを検証器に固定する。SQLが変われば検証器のレビューを要求してfailする。実ファイルSHAも結果へ記録する。新しいDDLに合わせるために、hashだけ更新して期待構造を据え置かない。

## 検査項目

- 2表の存在、通常の永続table、RLS/強制RLS・継承・ユーザーtriggerなし。
- 全列の順序/名前/型/nullability/default/identity/generated。列の追加・欠落もfail。
- PK、unique、checkの完全な定義、検証済み、非deferred。制約名の違いは許容するが、制約の追加/欠落/弱化はfail。
- 全indexのmethod/unique/PK/valid/ready/live/immediate/exclusion/nulls-not-distinct、キー数/列/式/部分predicate。余分なindexも期待DDLとの差としてfailする。

本番の意図した追加index等でも厳格にfailする。差異がある場合は実機に変更を加える前に理由を確認し、承認済みDDL/期待構造へ正式に反映する。検査を通すために既存index/tableを削除しない。

## 合成検証の再現

`test_intake_schema_guard.py` は**書込みを行うテスト専用**。検証器とは分離している。127.0.0.1:55993/user postgres固定で、既存 `lips_migration_restore_20261012` はmetadata読取のみ。ランダム名の新規 `lips_schema_guard_<12桁>` DBを作成し、そのDB内にのみ合成schema/空tableを作る。名前衝突時は既存DBを上書きしない。テスト後もfixture DBを保持し、削除/cleanupはしない。本番には実行しない。

```powershell
python tools/production-review/test_intake_schema_guard.py `
  --pg-bin '<隔離PostgreSQLのbin絶対パス>' `
  --output '<未存在のproof JSON出力先>'
```

2026-10-12検証: `schema-guard-proof-20261012.json`。15条件合格。正常DDL/復元DBを許可し、表欠損、誤型、nullable、誤default、unique欠落、check欠落/弱化、PK順序差、余分なindex、deferred unique、未検証check、RLSを拒否した。既存復元DBのmetadataは前後同一。合成fixtureは `lips_schema_guard_eeba14ba7632`、本番未操作。否定ケースの `status: Failed` は期待どおりの拒否であり、試験の失敗ではない。

CLIの実終了値も正常0・構造欠損1・存在しないDBへの接続2を確認した（`schema-guard-cli-proof-20261012.json`）。接続/全体timeoutの設定・環境非変更・エラー非開示は `test_checker_timeout.py` でsubprocessを置換して確認する。ネットワーク遅延の実時間試験とは区別する。
