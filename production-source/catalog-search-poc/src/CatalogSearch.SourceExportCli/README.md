# 原本export候補の実装境界

現在公開するentryは `--validate-only <selector file> <lowercase SHA256>` のみです。
DB接続・資格情報読取り・HTTP・取得済み原本のexport実行は行いません。
ビルドと純mock合格は、実機export成功を意味しません。

`PostgresSourceSnapshot` は将来の接続factory注入用APIです。Repeatable Read + READ ONLY、固定parameterized join、最大2行、現行原本・取引先・6 identityを検査します。
DBから返されたJSON文字列を再serializeせず、source ID + LF + UTF8文字列をhashします。
PostgreSQL/providerによる実際の返却表現、READ ONLYの実機強制、同時更新時動作は未試験です。

`SaveNew` は内部候補部品で、信頼済み `CaptureAsync` / `Bind` 結果を渡す前提です。
public `ExportValue` を外部入力として保存してもDB由来証明にはなりません。
保存は既存enrichment storeの `EnrichmentFiles` 検証・directory fsync・bounded readを `ProtectedExportFiles` 経由で再利用します。
`ExportCustody` は固定selector SHA照合、CreateNew intent、同一snapshot取得、保護exportのflush/読戻し、completion最終保存を順に行い、失敗時は既存証跡を残します。
保護rootの実所有者/ACL確認、資格情報供給、固定actor/実行器との接続は未完です。必須の `IExportExecutionPrerequisites` は信頼済みランチャーの注入契約で、要求JSONによる自己申告ではありません。実運用実装は未提供です。
Linux directory fsyncは既存実装の再利用であり、本候補の実Linux動作は未試験です。Windows試験は同等の所有者/永続性を証明しません。
これらが実装・独立検証されるまで、本番exportコマンドを公開しません。
失敗後の既存ファイル削除・上書き・自動再試行は行いません。

selectorはProductId/SourceId/SupplierCode/Skuの4項目のみ、重複keyも拒否します。
実機の秘密・接続文字列・原本全文をコンソールや診断へ表示しない設計です。

次の限定読取りで確定すべき事項: 実行actorのUID/GID、固定export rootと祖先の種類/所有者/mode、既存保護connection供給機構の有無と権限メタデータ、固定dotnet実行器/版、対象商品ID/現行source IDの取得経路。資格情報本文は対象外です。新規権限・保存先・転送例外が必要なら同一手順へまとめて確認します。
