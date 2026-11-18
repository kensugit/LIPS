# Linux 原典Web受入の依存と候補準備（2026-10-13）

現行ソースは `2c65f0a` 時点で再確認。P14sの56019統合受入は完了しているが、LinuxでのHTTP受入を証明するものではない。旧76fee5e時点の調査を以下で更新する。

## 現行の実経路

`GET /api/import/reviews/{id}/render-info`、`/render`、`/render-image` は、CatalogSearchDbContextから接続文字列を取得し、IntakeReviewStore.GetでIntakeReviews.Body、Contentで同表Contentを読み、保存hashと照合する。その後、AppContext.BaseDirectory/tools/original-render/render.pyを子プロセスとして起動し、JSON/PNGをHTTP応答する。親timeout85秒、dispatcher60秒、Gate待機5秒。保存値から原典を再構成する経路には差し替えない。

このendpointを変更せず受入するには、.NET 10 ASP.NET Runtime、Webのコンパイル済み依存一式、IntakeReviewsだけを持つ隔離PostgreSQLと合成原本1件が必要。全Webソース・本番DB複製・全業務schemaや業務データは不要。Programは起動時Migration/EnsureCreatedを行わない。最小schemaは既存 `DB/Migrations/20261012010000_IntakeReview.sql`。合成seedはContent、対応SHA、Body、Versionの一致が必要で、実原本・実回答を持ち込まない。

Linux XLSX描画は既存LibreOffice backendへ明示選択する。`LIPS_ORIGINAL_XLSX_BACKEND=libreoffice`、検証した実体への `LIPS_ORIGINAL_SOFFICE` と `LIPS_ORIGINAL_UNO_PYTHON` が必要。dispatcher側Pythonにはpypdfium2/Pillow、UNO側にはLibreOffice/UNOの一致した環境が必要。数式付き原典は再計算と保存cacheの一致を保証できないためLinuxでは明示保留。Windowsの既存Excel描画範囲を狭めない。

## 今回ローカルだけで準備できる範囲

- 対象ソースcommitを固定し、別出力先にframework-dependentのWeb候補を生成する。現行csprojはrender.py、excel-render.ps1、libreoffice_backend.pyをpublish先へコピーする。動的依存を手作業で削った「最小DLL集合」を推測せず、正式publish出力から必要性を確認する。
- 出力の全ファイル名・サイズ・SHA256と対象commitをmanifestへ記録する。設定・秘密・実原本・実回答・DB・キャッシュ・既存runtimeを混入させない。既存 `linux-dispatch-runtime` は旧成果物であり、最新候補として流用しない。
- 合成seedの生成手順、上記SQL、三つのGETの期待JSON/PNG/hash不変と失敗時の停止手順を準備する。起動候補はloopback限定、`CatalogDemo:Enabled=true`、`CatalogDemo:WatchEnabled=false`、`IntakeReview:Enabled=true`。接続文字列・OriginalRender:Python/Cacheは宛先側の承認済み専用値を使い、パッケージへ秘密を埋め込まない。HTTP試験は描画GETのみで、取込・answer・resumeを呼ばない。

これらのローカル準備は配布・導入の実行とは別である。追記：親担当の続行指示で、74d2340を固定したローカルRelease publish候補と合成xlsx/schema/seedを `artifacts/folder-ui-20261011/linux-http-candidate-20261013` に生成した。appsettings.jsonは候補から除外し、正式publish依存は維持した。実DB適用・起動・転送・インストール・Linux HTTP確認は未実施。DBなしreaderへの差替えは不要な別実装となるため行わない。

## 実値manifestの不足と担当間確認

BASE担当のschema `F0C8HUHK6R3` は親担当が `linux-http-acceptance-manifest.schema.json` へ取得済み。実ファイルSHAを持つ `manifest.draft.json` を作成し、PowerShell 7 Test-Jsonで検証結果を記録する。BASEの返信に基づきruntime候補10.0.12・公式URL・専用配置先/回収手順とPostgreSQL候補17.11を反映した。runtime SHA256は未取得のため省略し、schema不合格として扱う。sourceSHAで配布binarySHAを代用していない。

| 必須内容 | 現時点で確定できるもの | 追加根拠が必要なもの |
|---|---|---|
| 対象commit・最小ファイルSHA | ローカルソース/renderer/既存SQLのSHAを採取可能 | 最終publish候補を作成して全依存ファイルを固定 |
| 配置先・起動設定 | loopback限定、監視取込停止、GET描画のみ | BASE上の専用絶対パス・既存port・起動ユーザー |
| Runtime公式版/hash | net10.0要求、ASP.NET Runtimeが必要 | 採用版・公式入手先・配布物SHAと既存導入状況。推測しない |
| 合成schema/seed | IntakeReview.sqlと原本hash/Body一致契約 | 専用DBの承認済み接続先、最終seedのhash |
| 3経路 | render-info / render / render-image | 実Linux HTTPのJSON・PNG・原本不変証拠 |
| 停止・回収 | 専用に起動したプロセスだけ対象、共有/本人プロセスに触れない | 宛先実PID/専用ディレクトリ・保持/削除対象の確定 |

Linux担当の4ファイル受領とネイティブCLI描画は合格済み。追加Web候補の転送・runtime導入・専用DB適用・起動は未実施。新規runtime導入、サービス、Firewall、権限、公開、本番変更はこの準備から派生して実行しない。schemaと実値を揃えた後、親担当が具体的な範囲・宛先・復旧手順を照合して次工程を決める。

受入Webには他のPOST実装も含まれる。受入接続のDB役割はIntakeReviewsへのSELECTだけに制限する計画とし、schema/seed適用者と分離する。今回GRANT/役割作成は行わない。Body.hashは大文字SHAでContent照合契約に合わせ、seed pathは安全な合成相対パスとする。

## BASE返信の反映（2026-10-13）

専用rootは `/home/openai/liquor-work/lips-linux-render-20261012/http-acceptance-001`、候補DBは `lips_http_acceptance_20261013_001`。portは承認後のpreflight一回で選定するため未確定。実コードのdispatcher位置は `web/tools/original-render/render.py` であり、案の `web/dispatcher` は採用しない。停止は所有PID限定でWeb→専用DB、証拠保持後に専用rootだけ回収する。

.NET/ASP.NET Core 10.0.12の公式URLとmetadata SHA512は `artifact-candidates.json` に保持した。artifact未取得のためSHA256を生成・推定していない。残る配布準備の阻害要因は、runtime実体のSHA256、二つのruntime artifactを一つのruntime欄で扱うschema表現の合意、PostgreSQL 17.11 Linux導入artifact/入手元/hash/隔離導入手順。これらの確定や承認なしでダウンロード・配布・導入へ進めない。既存 `prepare.py` は初期草案生成用で、再実行するとBASE返信追記を上書きするため、現manifestとsidecarを保持して使用する。
