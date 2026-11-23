# Current status: 品種シノニム・同義OR検索候補（2026-11-24）

最新master `86000a2c829780bc11797e9e79411db8492695bf` 基点の隔離worktree。ピノ・グリへピノグリ／ピノ・グリージョ／ピノグリージョ／Pinot Grigioを追加し、原文の隣接する同一canonical別名2つ＋明示contains/主体だけを一条件へ束ねる。白ANDcontains、画面条件優先、別品種/別条件OR・否定の保留を維持。地理scopeでのLLM品種候補も原文の確認済みalias由来だけに制限する。plain原本の`Pinot `を英語`not `否定と誤検出する既存不具合を単語境界へ修正。商品原本・価格・在庫・地域・スキーマ変更なし、実機/DB/公式fetchなし、未commit・未配備。

詳細と実行手順は `docs/GRAPE-SYNONYM-SEARCH.md`。Webサービス層99 checks、限定domain112試験（skip0）が合格。辞書296canonical/605表記、内容SHA `63306008896df523cb1f708fb604de88f0d672e3e7858b0eb5341e133993e405`。

## Next work

親担当による独立QA、最終差分採用、commit/push・正式リリース判断。本番精度の改善確認や配布成功を、ローカル合格から推測しない。モデル追加・未登録aliasの自動学習・一般boolean式の導入は対象外。

---

# Previous status: 日本語の品種・産地・味覚・検索要素辞書候補（2026-11-20）

日本語辞書r3に、商品タイプ・在庫あり・NVの表記辞書を追加。自由文の条件をすべて確定できる場合は既存の規則で実行し、採用条件を表示する。表記データはJSONで管理する。DBスキーマの変更はない。本番切替は未実施。

## Next work

Dellでの正規搬入、現行版の再照合、Web/llm-linkの同時切替と画面受入。公式商品補完キャッシュの接続は別途確認する。

---

# 品種・産地v2検索／公式商品情報の再利用候補（2026-11-20）

本候補は保存済みproduction-source manifestの327ファイルを照合して派生した作業コピー。原本は変更していない。本番AI v1は有効化済みだが、赤条件欠落・右岸語の過剰keyword適用の意味受入は未解決だったためv2を別設定・既定無効で追加。

地域OR/他条件AND、原本>50%又は主体明記による品種判定、公式情報の正確な商品/年号照合、LIMIT前の根拠判定、補完JSONの不変版・世代索引・期限・矛盾保留・原本fingerprintを候補実装。価格と在庫は原本維持。Webは読取専用の補完readerだけを使用。取得保存は別EnrichmentCliで既定validate-only、実取得には明示modeと固定入力が必要。

現行本番package 93c044bbff3e40188004a5b14be775baed4e81ee1e356e639c53c501bbe17aad への配備はまだ行っていない。v2 bridgeが現行v1と互換ではないため、BASE原本と正規v2契約を受渡し・照合し、Web/link同時切替の検証済みパッケージを作る必要がある。旧単回展開は再実行しない。

## Next work

- BASE静的原本・契約ZIPは本人指定の非Slack経路（既存固定SSH/SFTP）で受渡し、SHA・受領回答を確認済み。失敗したSlack記録は保全し再送していない。
- 合成PG r3は単回実SQL試験合格、rollback後4表空と所有サーバー停止を独立照合済み。BASEの合成Linux owned-group試験も同一実行内の結果を独立受入済み。実Linuxの補完保存所有者/権限/永続mount/dirfsync、新イメージ、v2実LLM・画面受入は未実施。
- JSON-LDと明示設定のJETアダプターを候補実装。JET実商品同定は未成立で公開設定は未採用。別の公式商品とカタログの一致を調査中。非対応サイト/同定不明は採用せず、公式ページ一般を対応済みとしない。
- 右岸は本人指定のサンテミリオン・ポムロール・フロンサック周辺のみ。ブライ・ブール除外、登録済みの確認済み産地表記だけに展開し不明産地は含めない。左岸等は未対応。原本だけから品種比率を推測しない。
- 原本exportはDB読戻し文字列のfingerprintを用い、保存前JSONを代用しない。独立CLIの公開入口はvalidate-only。保護保存部品は候補実装済みだが実actor/所有者/資格供給の前提確認と本番入口の統合は未完。
- 304更新は将来仕様。初版verified保存はfresh200限定。補完版にhost policy SHAを結合し変更時失効。

---

# Current status: いろはわいん旧Excel一括取込候補（2026-10-17）

検索500修正・アルカン対応済み本番source0883adbbへ統合。IROHAフォルダー判定と指定.xlsの172商品読取りを追加。本番展開指示は受領済み。今回版の固定パッケージ・相互TLS受信・prepare/deploy・公開検索と一括取込検証が残る。検索容量候補からだけ0を除く修正と原データ0保持を維持する。

---

# Current status: 検索500の修正候補（2026-10-17）

現本番a5e2f56を基点に、自由文検索のsnapshot選択肢生成を修正。既存データのVolumeMl=0は未設定マーカーだが、AI検索の正整数容量候補契約へ渡して全リクエストが例外になっていた。0を候補からだけ除外し、商品・原本・在庫・価格・DBを変更しない。負容量・上限超過は従来どおり拒否。アルカンPDF対応を保持。

## Next work

修正・専用DB/HTTP/実ブラウザー検証と正式候補を親outputs/arcan-search500-20261017に記録。本番適用の成否は同フォルダーdeployment-proof.jsonで照合。コードだけで適用済みと判断しない。

---

# Current status: アルカン通常価格表・酒類のみ（2026-10-17）

現本番inspectのsource 82aa442177b1173b32d1c4e84d3ee5717d1562cdと同一基点。アルカン通常価格表を既存一括PDF取込へ追加。酒類394掲載行を392商品へ変換、完全一致再掲載2行は全位置をEvidenceに保持。品番606020718の異なる2商品は本人決定により内部識別子を分け、原品番・JAN・価格・重複確認事項を保持。ジュース・ソーダ・グッズは対象外。年号10と12桁JANは補正せず原文保持。未記載価格はnull、在庫数量は全件null、入荷予定のみExpected。資料は2026年10月で日の記載なし。一括取込の既存日付確認を維持し、月初は仮定しない。

## Next work

今回の本番一括取込対応・展開はユーザーが明示指示済み。テストと正式パッケージ、最新期待版照合、対象登録済み相互TLS転送、prepare・deploy・本番確認の証跡は親outputs/arcan-202610に保持する。旧本番変更や他案件を消さない。現状の展開成否は同フォルダーのdeployment-proof.jsonを参照し、コードの存在から本番適用済みと推測しない。

---

# Combined PDF + AI + file selection candidate

PDF source 401c2cd549cb12e0e81832071567b66158603a62, AI source 37deb2316ee7289ebc9441af41c14aa87bdbd5c1. Integrated candidate is not deployed. Historical component notes follow; current production state is held by the parent inspect evidence.

## PDF source notes (preserved)

# Current status: 確定160商品更新済み・検索語保持修正（2026-10-17）

Excel優先版0f6ea52/package faebea580e76f54b250b20a3ae332d36a534241b7339c835d40b5a6fc3bf420aを本番展開、backup /var/lib/catalog-deploy/backups/20261017T044702-1314967.dump。本人明示再判定で160商品更新、在庫218+新着30=248保留。390ID/総15389維持。最終検証で既存索引の取引先名/説明等を更新処理が置換する不具合を発見。既存索引語を追記保持し、今回原本に受入済みの160商品だけは過去の不変Evidenceから検索語を再生成できる修正を追加。再判定で業務データ・価格/在庫履歴は追記しない。固定hash・Partial160・CASの明示操作限定。専用DBで保持漏れを再現、再処理時に取引先名/説明/葡萄/JAN検索語復元・390件検索・履歴不変を確認。542成功34skip。

## Next work

この修正を現本番faebea58基点で正式再生成し、相互TLS転送・標準展開後、同じ原典を明示再判定して索引のみ復元。160商品の全項目・未更新230商品・390ID・15,389件・PDF90本履歴/Tinto現126本・IROHA/アルカン・実検索を確認。修正検証完了前はサザンクロスへ引き継がない。完成後に最新本番基点で再生成/展開する指示を送る。

---

# Previous status: Excel優先・既存商品確定照合の追加対応（2026-10-17）

前候補b4285b6は本番展開成功、package d5550e9f6468554564e0116cd7ab03035ef9c0770c7daa7d0e9e023da258d99b、backup /var/lib/catalog-deploy/backups/20261017T042134-1305187.dump。実フォームのムスカデ3件、API空検索、IROHA172件/アルカン同一品番2商品の全項目保持、総15389件を検証。本番Excelは同日資料ガードで未保存。

本人の新たな回答「今回Excelを優先し、既存商品との対応を照合して確定した商品から更新する」に基づき、固定hash901FABの原本だけに専用storeを追加。生産者/英語商品名/年/容量/タイプの一致159商品と本人回答Tinto1商品を既存IDへ対応。160更新候補、在庫218商品+新着30行は保留。新商品は作らない。過去原本/在庫/価格/商品ID/JAN等を保持し、今回原本の価格・在庫根拠を追記。同日競合に今回本人指定のExcelを優先するが、より新しい商品情報は上書きしない。補足資料を在庫更新済みと見なさない。専用DBで390商品保持・160証拠追記・再登録0・126本/旧90本保持を検証。回帰542成功34skip。

## Next work

追加対応版を現本番d5550e9f基点で再生成し、同じ相互TLS受信範囲で転送・標準prepare/deploy・公開検証、既存Held原典の明示再判定で確定160商品を更新する。Web展開と業務更新の結果を別記録。未確定248行は保留。完了後、本人指示済みのサザンクロス担当01a147d1へ最新本番・証跡を渡し、最新基点で再生成・展開を指示する。まだ送信していない。

---

# Previous status: いろは本番版へオルヴォーを統合（2026-10-17）

本人の別チャット01a147c6の明示指示「本番展開に成功したらオルヴォーにパッケージ最新で再度パッケージを造り、本番展開するように指示」をread_threadで照合。いろは本番source09af457/package0e00d7eへ統合し、いろは・アルカン・検索500修正を保持。回帰539成功34環境skip、原本428位置/378商品と新着30保留を確認。Web-onlyでMigration不変。本番展開は承認済み、再承認不要。後続の転送/prepare/deploy/公開検証の実績は親outputs/orveaux-202610/latestに記録し、生成だけで展開完了としない。

## Next work

新しい固定パッケージの相互TLS受信対象を登録し、既存と同じ接続元/証明書/192.168.1.5:5987/一時Allow・補完Blockの範囲で転送する。Dellの新しいpowershell.exe -NoProfile -Fileプロセスで起動し、READY後に送信。受信ACKとLinuxincoming配置を別確認。最新inspect一致と標準prepare読取専用プレビュー・バックアップ・deploy・自由文API/実フォーム/取込/いろは・アルカン保持を検証する。旧方式/同日別原本の保存ガードは維持し、本番業務データの移行・付替えは行わない。

---

# Previous status: オルヴォー在庫Excel対応候補（2026-10-17）

本人回答: 在庫17シートを対象、新着の未確定行は保留。定番品95行の126本を採用、対になるスペイン9行90本も保持。原本hash限定の特例。428位置→378商品、新着30保留を既存一括取込・確認履歴へ接続。実原本7試験/専用DB1試験/HTTP6項目、回帰530成功35skip。本番基点82aa442・package c8adcb8。本番未適用。ローカル56038、DB56037。詳細docs/ORVEAUX-STOCK-20261017.md。

## Next work

本人の個別展開指示後、最新inspect・並行変更照合、正式候補の相互TLS転送・prepare/deploy・本番検証。既存原本が旧方式/同日別原本ガードに該当すれば停止し、既存PDF商品との対応を照合する。ガードだけを解除しない。新着の容量/対応確認と本番データの修正は未実施。

---

# Previous status: PDF・親フォルダー取込修正候補（2026-10-16）

- この隔離候補はPDF取込、原因・対応表示、アップロード後のSHA-256照合と重複表示を追加。親フォルダーは指定の「取引先別」。既存原本・本番DBは未変更。
- b1de349を起点に、保存された66b84ceの原典描画・キャッシュ・価格成分表示の差分を統合。最新本番inspectはSSH timeoutで未取得。この候補を現本番と同一版・本番適用済みと扱わない。
- 木下PDF523件・今回Excel523件、豊通PDF650件の読取りを確認。フィネス489件とワインエクスペリエンス220件の読取りも確認。未対応のPDF帳票と.xlsは個別に原因・対応を表示する。既存パーサーを登録しただけの帳票を今回の実原本で受入済みとしない。
- 本人決定：在庫〇・◎等は潤沢、正確な本数を推測しない。豊通21ページのコード・商品名空欄かつ◎だけの同条件7行を、この原本hash限定で除外。元セルと位置はEvidence保持。docs/PDF-FOLDER-20261016.jsonが決定記録。
- 既存未対応reviewは明示recheckで同一原本を再判定・条件が揃えば登録。Unknown/Running/Partial/保存済み資料への無条件再処理は禁止。通信結果不明を自動再送しない。
- 納入価格は小売価格と分離して価格成分・RawCellsに保持。日付未記載を月初と推測しない。SourceDocument/既存Migrationの変更なし。PythonのPDF依存と明示実行パスが必要。

## Next work

今回の隔離HTTP・DB・ブラウザー・回帰結果は、親ディレクトリーの検証proofを参照。コードと検証成果をレビュー可能な形で提供し、最新本番inspect・期待package・ソースを照合してから個別承認の適用工程へ進む。本番変更はこの実装依頼で実施していない。対応外帳票・旧Excelの個別読取り対応と案件全体の受入は別の残項目。

---

# 現在の引継ぎ（2026-10-13・統合受入後）

この冒頭を現況として参照する。以下の「履歴」では各時点の未実装・承認待ちを保存しているが、現在の未完一覧ではない。最新の操作目標・証拠・手順は [GOAL-ACCEPTANCE.md](docs/GOAL-ACCEPTANCE.md) を正とする。

## 完了した操作経路

- P14sの共通確認URLは http://127.0.0.1:56019/workspace.html 。専用DB `lips_goal_integrated_20261013`（55993）。対応済みの親フォルダー取込、原典画像と質問、回答永続化、明示再開、同取引先・帳票・対象条件限定の次回再利用を統合確認済み。`goal-integrated-56019/proof.json` 25項目と `coexistence-proof.json` 7項目が新しい統合証拠。旧環境の試験件数をこの合格数へ加算しない。
- LUC容量に加え、ラヴィネの容量・地域・納価単位の質問経路、正常行の部分保存と未保存行再開、再利用取消の管理導線は接続済み。受領回答JSONは照合済みで、対象のラヴィネ回答保存も実施済み。24回答すべての業務データへの適用を意味しない。
- AOC固定1組は親フォルダーの受付・保留履歴から対象2原典を選び、原典画像・受領回答JSON照合・明示登録へ接続済み。56018の実保存18項目で確認。内部IDは仕入先SKUと区別し、ProductIdと原典SKUはnull、原文2表記・Evidenceを保持。在庫60を加算せず、未確定価格は採用しない。元ファイル全体を取込完了にはしない。56019ではAPI共存を確認し、この実原典を重複登録する試験はしていない。
- Linux用描画backendの送信許可・受領・CLI検証は完了している。旧履歴の「4ファイル送信許可待ち」は解消済み。Linux WebのHTTP受入とは区別する。

## 実際に残る事項と次工程

1. 実ラヴィネ地域70行・全対象納価単位は本人回答済み。3812239/6e09cb1で原地域と商品区分を分離保持、カルヴァドス/コニャック表示、税別1本価格を適用。重複は少在庫採用とバッジ、今回938/1000行は7本採用・双方原典保持。P14s56023・専用DB lips_lavinee_accepted_v2_20261013で3,783回答、明示取込1,260 SKU、総1,266、32再利用規則。最新手順と受入証拠は [LAVINEE-ACCEPTED-ANSWERS-REVIEW.md](docs/LAVINEE-ACCEPTED-ANSWERS-REVIEW.md)。先行56022の移行不足Unknownは保持し再送せず、新復元DBへ既存移行を適用して検証した。LUC予約欄等の別判断は残る。
2. AOCは指定hash・位置・2024年の1組のみ。一般ORVEAUX parserや他帳票・全取引先への展開は未実施。明示承認済みの同一組・内部ID方針は再質問しない。
3. Linux HTTP受入・BASE実接続・本番配備は別工程。manifest/計画の同2資料共有は本人承認済み・静的定義修正済み。本人は公式.NET/ASP.NET Core Runtime10.0.12 Linux x64の2件とPG17.11 source1件の専用staging取得/hash検証を直接承認しBASE担当へ依頼済み。展開・build・導入・DB起動はこの承認に含めない。BASE Linuxにはcompiler/make/開発headerがなく、既存builderも見つからず、最小toolchain案を読取情報から整理中。本番・権限・ネットワーク変更を行わない。

対応済み範囲のP14s操作目標は受入済み。実資料全件の業務受入や別環境への適用は未完として別管理する。旧Web/DBを不用意に停止しない。

---

# 作業履歴（以下の未完・待ち状態は記録当時のもの）

## 履歴 / Next work（2026-10-13 AOC限定商品の検索接続）

720c738で固定原典ペアを種別付き内部IDとしてSupplierProduct1件/SourceEvidence2件に接続。ProductId=null、原典SKU=null、原文名称併記、在庫60非加算、価格未採用。0a5938fで一覧/詳細/選択/CSV/workspaceの内部IDを仕入先SKUと区別。専用DB lips_aoc_import_20261013@55993、P14s http://127.0.0.1:56017/ 、runtime aoc-import-runtime-final PID14756。実DB統合合格、実HTTP10/実GET Edge7/合成表示10/既存CSV回帰/build0。原本不変、本番なし。API preview/commit/operationGETは明示操作・固定原典bytes照合。docs/ORVEAUX-PAIR-IMPORT-REVIEW.md参照。

残: 一般ORVEAUX parser、親フォルダー保留画面→固定ペア選択/回答JSON接続は未完。実LaVinee地域70/納価単位の本人判断未決。検索/APIの局所成功を全体完了にしない。旧Web/DB維持。内部IDの本人承認は受領済で再質問不要。
## 履歴 / Next work（2026-10-12 統合実操作確認）

a9da08eの56007/復元合成DBでブラウザー実操作から親フォルダー取込→容量疑義→実Excel原典→回答POST→次回ルールPOST→明示resume→翌日資料自動再利用→同一資料再送まで29項目合格。interceptなし。新SKU2件のみ、750/1500mL→次回500/3000mL、価格/在庫は次原典値、原本SHA不変、再送件数不変。artifact mapping-integrated-b36e89d654/integrated-proof.json。既存本人fixture/56006回答は未変更。
追加監査で保存結果不明後の開始/フォルダー選択再開を発見し修正。同ページでロックし履歴照合を案内。35合成通信ブラウザチェック合格、通常保留継続/手動停止後再開維持。永続キュー復元は未実装。56007へ静的反映・GET確認済み。
BASE担当の保存済み合成応答をLIPS .NET CLIで契約照合しCandidates1件合格。追加推論/DB書込なし。再シリアライズでありリモートバイト一致は未主張。
残: 実取込接続はLUC容量単位。全取引先/他疑義回答・実回答JSON/実資料受入、Linux正式backend統合、現行本番inspect/許可経路/DB適用/gateway受入は未完。本番適用の本人一般指示は受領済み。4ファイルBASE送信は自動承認拒否後の宛先別本人回答待ち。
## 履歴 / Next work（2026-10-12 本番適用指示）

追記: 合成専用DBの実dump/restoreが成功し、原本/回答/ルールdigest・件数一致、追加SQL各2回再実行不変、元DB不変を確認。migration-restore-20261012/proof.json。管理者移行/復旧案とゲート更新bba88fc。Linux独立backend/合成fixtureを2997555へ追加（Windows経路未変更、Linux自動有効化なし、数式は再計算未証明のため保留、ローカルguard2件合格）。LIQUOR-BASE担当が専用prefixで公式ツール環境を準備中。renderer4ファイルの同ホストへの送信は自動承認レビューで宛先別承認不足として拒否され、本人へ4ファイルを明記した許可を質問済み。承認前の再送/別経路取得禁止。接続担当の追加証拠: 現VPN送信元10.10.10.3、443到達、22222/5987等timeout、記録上の許可元10.10.10.1/192.168.1.110と異なる。既存許可経路への切替を本人へ確認中。新しいIP許可追加は行っていない。実装準備・合成検証と本番適用を区別する。

本人が「エージェントと協力し本番環境への適用まで進めて」と直接指示。本番適用の一般承認待ちは解除。80ef896の本番準備を開始したが専用inspectは192.168.1.5:22222への接続timeoutで現行基点未取得。接続担当へ読取調査依頼済み。新2テーブルは明示SQLでWeb-only schema guard対象外、LIPS管理者手動移行の準備を進める。Linux Excel描画も未対応、P14sにWSL distro/Docker/LibreOfficeなし。クラウド担当への直接送信はAbsolutePathBufエラーで未到達のためSlackで照会した。独立して合成専用DBのbackup/restore検証と移行手順を準備中。詳細docs/ROW-INTAKE-RELEASE-GATES.md。下記の本番操作なしは各時点の履歴であり現在の承認範囲ではない。本番変更・正式候補転送はまだ実施していない。

## 履歴 / Next work（2026-10-12 回答の取込利用・次回再利用）

本人の親フォルダー一括取込→疑義原典/質問→回答保存/取込利用→同取引先の次回再利用を目標設定。LUC既存在庫形式の単位なし容量に型付き行質問を接続し、保存回答で換算、明示操作でルール永続化、同取引先/帳票/列fingerprint限定で次回適用する縦の経路を実装。CAS/operation冪等/取消履歴/原本・行Evidenceを維持。P14s56006、合成専用lips_rules_review_20261012。通常321合格15環境skip、実API12/実UI6/実親フォルダー4/合成UI17合格。手順docs/MAPPING-ANSWER-REUSE-REVIEW.md。現時点の適用対象はLUC容量単位のみ。全取引先/他の回答種別/管理画面/実回答取得/クラウド・BASE/実資料受入は残る。案件全体完了ではない。本番操作なし。

## 履歴 / Next work（2026-10-12 原典拡大）

追加: 原典確認表25項目にローカル回答入力/保存/再読込/編集履歴/JSON書出しを実装。artifact SOURCE-REVIEW-TABLE.html、tools/cloud-review/source-review-answers.js、docs/SOURCE-TABLE-ANSWERS.md参照。Edge17項目合格。localStorage保存でありサーバーや業務DBへ反映しない。既存46リンク維持。BASE/クラウド/未決資料の残項目は維持。

追加: 56003で合成内容の実PDFをPDFium描画、拡大/範囲/スクロール/回答往復など14項目合格。OriginalRenderEndpointsのscript参照をAppContext.BaseDirectoryへ修正、dotnet runのContentRootとの差を解消。既存Web保持、商品数2件不変。docs/BASE-DATE-PROPOSAL-CONTRACT.mdに既存JSON契約と担当境界を具体化。BASE実接続/クラウドLinux起動は担当受入待ち、未決資料判断は従来どおり。ZIP6218fa3共有は直接本人承認で完了、再承認不要。

56002に原典画像の拡大/縮小（10〜400%）、原寸100%、横幅合わせ、上下左右スクロールを追加。再描画APIを呼ばず表示倍率だけ変更する。回答画面往復で倍率/画像スクロール/参照行/未保存回答/選択SKUを保持。Edge17項目合格（実Excel画像、PDFは合成geometry fixture）。参考Library画像はPage参照不足で取得できず実画面で再現確認。手順docs/IMAGE-ZOOM-REVIEW.md。実PDFでの今回追加倍率受入、既存資料判断/BASE/クラウド/本番準備は未完。本番操作なし。

## 履歴 / Next work（2026-10-12 行単位再判定）

ENEOS専用parser・同日競合の部分保存・未保存SKU選択/回答確定/別操作再判定・原本別数量詳細を実装。P14s56002、合成専用lips_rows_review_20261012。32対象試験/17EdgeAPI、通常280合格15環境skip。実ENEOS3帳票は読取のみ48/139/371行と原本不変確認。詳細docs/ROW-RESUME-REVIEW.md。推論date-proposal/v1検証CLIと合成クラウドパッケージ手順はtools/cloud-review。次はオルヴォーAOC1候補/他20行の判断、他保留形式、クラウド/BASE側受入、本番準備。案件全体完了ではない。本番未操作。

## 画面の分かりにくさを修正（2026-10-12）

本人のスクリーンショットは空の1行目を指定していたため、黄色い枠しか出ていなかった。初期行を固定1から「最初に文字・数値がある可視行」へ修正し、自動表示する。初期表示は対象を判定したものではないため強調OFF。空行指定時は内容のある開始行を案内し、空枠を出さない。

今回の資料は画像表示の確認用サンプルで、実際の仕入先在庫ではない。画像だけ確認するリンクはこちら:
http://127.0.0.1:56001/intake-review.html?id=f2036333-4d2c-4815-b226-346dd13545d4&view=original&sheet=1&row=95

開くと95行が自動表示される。この画面は日付回答・商品の取込を求めない。通常の回答画面では「この在庫リストは、いつ時点の情報ですか？」と目的を明示し、分からない場合は保留を選ぶ手順を説明する。

検証: ブラウザー14項目、空行/初期行の追加回帰1試験合格。専用Excelプロセスの終了も確認。証拠: original-clear-verification.json / original-clear-review.png。旧環境・商品DB・本番は変更していない。
## 履歴 / original-image implementation (2026-10-12)

本人指示により保存値描画を廃し、Excel16の原典印刷画像とPDFiumの元PDFページ画像へ変更。対象行強調・PDF手動範囲・4段階画面。新URL P14s http://127.0.0.1:56001/workspace.html、旧環境保持。Edge/API21項目＋最終UI4項目・描画回帰8試験合格。専用合成DBの商品3件不変。本番未操作。手順 docs/ORIGINAL-IMAGE-REVIEW.md。旧.xls/外部接続付きExcel/実原典受入/対象位置の業務疑義自動接続/本番は未実施。以下の保存値描画の記録は旧版。

## 履歴 / UI update (2026-10-12)

原典行と前後行の確認用画像、資料ごとの原典確認→回答入力→内容確認→結果の画面切替を実装。保存セルからの画像化で原本レイアウトそのものではない。Edge/API16項目合格。URL56000を維持。本人合成確認日10/4、詳細 docs/IMPORT-QUESTION-REVIEW.md。既存残項目と本番未操作を維持。

## 履歴 / Next work (2026-10-12 question workflow implemented)

原本受付/疑義/回答履歴を専用IntakeReviews表に永続化し、原典ダウンロードとCSV/XLSX保存セルプレビュー、日付回答確定、版/hash照合付き対象再開を実装。保留継続は履歴保存のみ。Running/Unknownは再開禁止、同時処理はCASで1回だけ実行。保存不明時のキュー停止を維持。P14s http://127.0.0.1:56000/workspace.html、専用DB lips_questions_20261012、合成資料のみ。詳細/手順は docs/IMPORT-QUESTION-REVIEW.md。

関連単体67合格/既存DB条件1skip、新機能最終18合格/skip0、Edge/API29+再起動等9項目合格。旧確認Web/DB保持、本番未操作。次はENEOS競合/各帳票の疑義を型付き回答方式へ接続、未対応形式・PDFページ参照・LLM候補提示は別工程。今回任意自由文による業務値変更や外部LLMを実装済みとしない。

## Previous plan (2026-10-12 question workflow)

本人Slack1791766575.053619と続行1791766584.429509を受領。取込疑義の原典参照・回答確定・対象処理再開を優先。既存endpoint/FolderImportService/画面との接続を確認し、最小範囲と順序を docs/IMPORT-QUESTION-WORKFLOW.md に保存。取込前の原本保存と回答履歴は未実装、Canonical用ReviewServiceとは別契約にする。次は受付原本/疑義/回答の永続化、基準日回答と保留継続のUI/API、版・hash照合付き再開の実装と隔離検証。ENEOS/AOCの既決定条件は保持。外部LLM/本番操作なし。今回の成果は調査・実装範囲の確定であり実装完了ではない。

## 履歴 / Next work (2026-10-12 decisions)

本人回答を docs/HELD-INTAKE-NEXT.md 冒頭へ保存。3資料の基準日は本人指定2026-10-01、ENEOSは同日非重複併存・価格一致時は数量加算せず原本別Evidence保持・価格不一致保留。数量不一致の単一表示は照会中。オルヴォー数量競合商品は本人指示で保留確定。他商品の別名/VT対応確認は継続。LLM推定日と根拠の一覧表示は将来項目。コード/DB変更・追加試験は今回未実施、既存Nouvelles実装f2f696eと専用DB6435の検証証拠を維持。

Next: 決定済み日付の出典区分を保持する契約とENEOS3形式fixture/parser。数量表示に依存する保存変更は回答後。既存同日guardの無条件解除、原本日付の改変、本番操作はしない。下記旧節の「日付回答待ち」「ENEOS全方針未定」は本節で更新。

## 履歴 / Next work (2026-10-11 Nouvelles intake)

ヌーヴェル専用パーサーを追加。原本A3の更新日2026-09-30を使用し、見積期限を資料日にしない。希望小売Hは固定値のみ、納入価格G/リンクL数式は評価・取得せず原式保持。少/OKの数量はnull。国/産地を推測しない。重なるふりがな情報によるClosedXML読取失敗は専用のメモリ内コピーのsharedStringsからrPh/phoneticPrのみ除去し対応。保存原本は変更しない。外部リンクguard維持。

54単体合格/DB環境依存1skip、Web build 0警告0エラー。実資料1343行のSKU/商品名/容量/上代/在庫/原数式を独立読取と全行照合。専用DB5092→6435、Edge保存/重複時同一原本IDと件数/API検索/画面/原本SHA不変確認。最新P14s http://127.0.0.1:55999/workspace.html。既存Web保持、本番未操作。成功6/保留7/未対応5。

Next: ENEOS同日複数資料の競合方針とLUC予約/オルヴォーロット/ラヴィネの日付は本人回答待ち。独立したオルヴォー現在庫等の調査継続。最新非秘密差分共有・本人受入・本番基点/配備は未完。証拠nouvelles-source-proof.json/nouvelles-review-proof.jsonはローカル、実商品内容を外部共有しない。

## Follow-up 2026-10-11 10:57Z

Slack差分に新規指示なし。確認Web55998で5092商品維持。残保留8件の帳票/日付/数式/競合条件を docs/HELD-INTAKE-NEXT.md に整理。ENEOS3の10/1、ヌーヴェル/オルヴォー現在庫の9/30は原本に根拠あり。LUC予約/オルヴォーロット/ラヴィネの資料日付と、ENEOS同日複数資料のSKU単位競合方針を本人照会中。依存する保存/ガード変更は未実施。独立した解析は継続可能。

## 履歴 / Next work (2026-10-11 Le Caveau intake)

既存 tools/le_caveau_import.py と同じルカヴォー2026-09-29帳票契約をC#一括取込へ追加。全729行の13項目(SKU/商品名/VT/容量/参考上代null/税区分/在庫/国/産地/タイプ/原本行・シート)が既存処理と一致。全角空白を含むSKU生成も互換。卸価格はPriceNote/RawCellsに保持し参考上代へ流用しない。入荷済660/予定69、予定本数は現在庫にしない。H9のTODAYは保存済み日付だけ読取り、再計算しない。対応日付・数式・形式変更は保留。

48単体合格、DB環境依存1skip。専用実資料DB4363→5092、Edge取込/再取込・同一原本ID/件数・API/画面検索・原本SHA不変確認。最新本人確認P14s http://127.0.0.1:55998/workspace.html。旧確認Webは保持。本番未操作。残りは保留8件/未対応5件、成功5件。

途中で55993接続拒否10061、旧PID消失/非listen/pg_ctl停止を確認。既存領域・同一loopback/portで専用DBを起動し、専用DB名と既存4363件を確認してから保存。停止原因未確定。localhostのnode接続EACCESは承認済み隔離検証をsandbox外で実行し成功。いずれも保存POST前、保存不明の自動再送なし。

Next: 残保留8件(ENEOS3/LUC予約1/オルヴォー2/ヌーヴェル1/ラヴィネ1)の帳票/日付/価格仕様確認と対応。実資料・SKU証拠はローカルに限定。未対応PDF2/XLS3は別範囲。最新非秘密差分共有・本人受入・本番基点/配備は未完。

## 履歴 / Next work (2026-10-11 WTS held-file intake)

WTS専用の静的セル読取projectionを追加。外部リンク/ハイパーリンク/印刷設定/定義名/拡張metadataは読取コピーから除外し、原本bytesとSHAは保存。数式・接続・OLE・未知の外部参照は保留、他取引先の外部リンクガードは維持。外部取得・再計算なし。

FolderImport 43合格、DB環境依存1件skip（この試験をDB合格扱いにしない）。実資料WTS1173行を専用 lips_real_20261011 に追加、3190→4363。Edgeで取込/再取込、同一原本IDと商品数不変、API/画面検索、原本SHA不変を確認。最新確認URLはP14s http://127.0.0.1:55997/workspace.html。旧55994/55995/55996保持。18資料全体は成功4/保留9/未対応5。実データ証拠はリポジトリ外wts-review-proof.json等、外部共有禁止。本番未操作。

Next: 本人承認済み保留9件の取引先/形式/日付対応。未対応5件(PDF2/XLS3)は別範囲。日付・不明値の捏造は禁止。最新差分の非秘密証拠共有、本人体験確認、本番基点・配備は未完。

## 履歴 / Next work (2026-10-11 real intake follow-up)

次工程回答を受領。丸数字のNFKC変換による資料日付誤認を修正（区切りなしは8桁のみ、全角数字は維持）。AMZのJAN列を地域の後に置く配置を追加し、原産国/価格/在庫の誤対応を防止。取込関連40テスト合格。実資料専用の隔離確認DBで18件処理：成功3/保留10/未対応5/失敗0。AMZ2件204/203行、LUC1件2985行。登録商品数3190（旧版のみのSKUも保持）。商品コード2例のAPI/Edge検索と原本ID照合済み、原本18件SHA不変。

実資料確認URLはP14s localhost55996。個別検索入力例と全ファイル結果はリポジトリ外のLIPS/artifacts/folder-ui-20261011/REAL-IMPORT-REVIEW.md（実資料由来、外部共有しない）。旧55994/55995は保持。本番未操作。
Next: Wine to Style外部リンクの取得/評価を行わない対応方針検証（ガードは維持）、未知取引先/形式の対応範囲整理、非秘密3成果物のDell共有、本人実受入と本番基点/配備。単なる解析/登録を全資料受入完了としない。

## 履歴 / Next work (2026-10-11 folder UI)

本人が新規改良の一律禁止を撤廃。既存9628d6cから独立したソースコピーで、一括取込を主導線にし、単票の任意取引先標準CSV機能は折りたたみ補助メニューに保持。Excelは既存一括取込へ案内。親フォルダー/下位資料フォルダーは既存の祖先名判定を利用。標準CSVと仕入先独自CSVを明確に区別する。バックエンド/DBスキーマ変更なし。

検証: Web build 0 warnings/errors、Edge/API 31項目、Wine to Styleの下位フォルダー標準CSV保存。FolderImportTestsに5取引先の深い階層判定を追加。本人確認はTHINKPAD-P14S http://127.0.0.1:55995/workspace.html、隔離DB lips_ui_20261011 (port55993)。旧55994環境は保持。

Next work: 本人の実PC/OneDriveフォルダーでの受入、対応外PDF/旧Excel/仕入先独自CSV/未知取引先の扱い。本番基点照合・実gateway受入・本番配備は未実施、個別指示が必要。今回の許可を本番操作へ拡張しない。

## 履歴 / Next work (2026-10-11 review follow-up)

承認済みの保存結果不明時キュー停止を隔離worktreeで修正。HTTP200のHeldでもStopQueue=trueなら停止し、通常入力保留は続行。未処理保持・自動再送なし。SearchTextの共有変更を明記し、通常単票と追加2項目の隔離DB回帰を追加。原本IDは結果JSONまでで画面追加なし。今回の証拠はLIPS/artifacts/folder-upload-20261009/review-fix-20261011/。過去の集計は過去の証拠として保持する。

次は最新本番基点照合、実gateway/PC/OneDrive受入、個別指示後の配備。本番操作は今回未実施。以下の過去の展開指示を今回の新規配備承認として使わない。

# Current status (2026-10-09, local folder upload)

Folder selection and per-file server ingestion are implemented on isolated branch `feature/lips-folder-upload-20261009`, starting from saved distribution source f1732a9. Existing development-tree changes were inspected and not overwritten. Known Excel supplier parsers were reused, with server folder/content matching, explicit document dates, transactional supplier/hash idempotency, same-date/older-version holds, and per-file success/held/unsupported/failed receipts. Browser sends one file at a time within the existing gateway limit. Source evidence, tax, unknown stock, selected CSV and special prices are preserved. No schema migration or configuration change.

Validation: Web build zero warnings/errors; 227 standard tests pass with 10 environment-dependent skips; dedicated folder/DB suite 28 pass, LUC real workbook suite 13 pass (2902 rows), browser/API 21 pass, selected CSV regression passes. Three additional supplier-layout tests are included in the 227 standard tests. No production operations, no push or merge.

## Next work

See `docs/FOLDER-UPLOAD.md` for usage, evidence, baseline provenance, limitations and local reproduction. PDF/legacy Excel, additional suppliers and multi-document composition remain unsupported by this Web path. Production deployment/DB operations/config changes are excluded by the current user instruction. The parent project's watcher/recovery/production adapter/acceptance dependencies remain. A future deployment requires fresh baseline reconciliation and explicit instructions; older deployment approvals below are historical.

# MV/NV update (2026-09-29)

User explicitly requested MV treated as NV for search. Known M.V./fullwidth/wrapped variants normalize to NV. Canonical options merge MV into NV; either input matches both. Original VintageRaw/evidence remains unchanged. 183 tests passed, 7 environment skips; isolated DB vintage/supplier tests 40 passed with no skips. Base deployed source is 17bff0dc39ed1ca72e52419fd9b49bea6604a046. Next: exact mutual-TLS receiver registration and standard LIPS deployment; production is not yet updated for this delta.

# Codex handoff

## Current status (2026-09-29, selected CSV release candidate)

User confirmed selections persist across searches and more products can be added. Search rows now have checkboxes keyed by stable SupplierProductId. The CSV button opens a review dialog with removal actions; only the remaining selection is exported. Pagination/sort/empty and failed searches preserve selection. Re-fetching a selected ID refreshes its displayed data without duplication; different suppliers with the same SKU stay separate. Selection lasts for this page session (refresh/navigation clears it). CSV follows the current sort; existing escaping, original evidence, retail/special price distinction remain intact.

181 .NET tests passed (7 environment tests skipped), vintage and supplier DB integration passed, desktop/mobile browser coverage includes persistence across searches, selection removal, same-SKU suppliers, source refresh, CSV content/decimal prices, failure and empty selection. Existing /api/options remains backward compatible; the UI requests vintageNotation=canonical. Root deployment option comparison remains unchanged.

## Next work (approved production release)

User authorized production deployment of these changes. Package the verified candidate, register exact files for mutual-TLS bulk receiving, transfer and verify, then use standard LIPS prepare/deploy and live acceptance. Bulk receiver currently not listening; Dell administrator setup is required. Do not use iWSET JEA or SSH upload as a fallback. No database migration/data import is included.


## Current status (2026-09-29, supplier natural search candidate)

Explicit handling phrases (e.g. ファインズ取り扱い, ファインズ取扱の, 取扱先は日本リカー) resolve registered supplier names/codes to SupplierId SQL filters. Product/producer keywords alone do not become supplier constraints. Multiple or ambiguous supplier matches fail explicitly. Existing type/price/vintage/availability filters remain conjunctive; previous vintage normalization is retained. No DB/schema changes.

Validation: 181 .NET tests passed, 7 environment tests skipped; all 16 supplier tests including isolated PostgreSQL integration passed. Edge verified API results, interpreted supplier condition, UI and filtered CSV. Not deployed.

## Next work (supplier search)

See LIPS/docs/SUPPLIER-NATURAL-SEARCH.md and docs/VINTAGE-NORMALIZATION.md. Both changes are local candidates; await an explicit deployment instruction, then re-inspect current production and follow guarded release steps.


## Current status (2026-09-29, vintage notation candidate)

Year-only brackets are normalized for search/options/display/CSV: 2021, [2021], (2021) -> 2021. NV/N.V/N.V. (including case/fullwidth variations) -> NV. MV, blends, age statements, 0, dash and unknown remain distinct. Supplier VintageRaw, original files and evidence are preserved. New VintageLabel is display-only. Matching raw variants are selected in SQL before the result limit. No schema or data migration.

Verified against inspected production source f226f0a98d74253af4100e35d0f4d43ae898cb9f using an isolated candidate: 166 unit tests passed (6 environment tests skipped); all 22 vintage tests including PostgreSQL integration passed; Edge API/options/display/sort/CSV/original-evidence checks passed. This is a local implementation, not a production deployment.

## Next work (vintage notation)

See LIPS/docs/VINTAGE-NORMALIZATION.md. Production release requires an explicit instruction for this change, a fresh inspect and standard transfer/deployment gates. Do not deploy the entire development tree containing other work.


## Current status: Fines special purchase price (2026-09-25)

Candidate based on inspected production source 8f86d62e394dea395ecdd127ea8fa1611b7a989d. Adds a nullable SpecialPurchasePrice API field from explicitly versioned FINES current-source evidence, a separate search column/detail item, and a separate CSV column. Reference-retail filtering and prices remain unchanged. No schema/migration changes. Ordinary or malformed evidence returns no special price. A later ordinary inventory document supersedes current-source special price; historical evidence remains available in the database.

Local checks: 145 .NET tests passed (5 unrelated DB/file tests skipped), 331 actual products imported twice with DB/evidence/stock/retail verification, full API comparison and browser/CSV decimal-preservation checks. Data package preserves two originals, applies 322 main rows and 9 extra rows, with extra precedence for 3 duplicates and Open Burgundy excluded per user confirmation.

Next: register the exact package for mutual-TLS bulk receiving on Dell, transfer and verify size/hash, then use the bundled guarded manual data import and standard LIPS prepare/deploy helpers. Do not report production applied before live verification. See LIPS/docs/FINES-SPECIAL-20260924.md.

## Current status: search/CSV refactor (2026-09-22)

Production CSV release e66ac013 was verified and is the baseline of this snapshot. Search request state, result rendering and download handling are now separate named functions. CSV serialization is formatted and structured without changing output (54 edge cases byte-identical). Browser tests verify pagination, sorting, invalid prices, download failure/retry, empty/error responses and cap messages. Only catalog.js and catalog-csv.js runtime files change; backend/schema/deployment infrastructure remain unchanged.

## Next work: refactor production verification

Use the existing dedicated deployment v2 inspect/upload/prepare/deploy pipeline. The user authorized deployment after refactoring. Confirm health, product count, actual CSV download and natural-search regression on the deployed package. See LIPS/docs/CODEX-DEPLOY-V2.md for release evidence and the latest exact package.

Updated: 2026-09-17

## Mission

仕入先カタログ・価格表・在庫表と、酒道楽の商品マスターを横断検索できる独立システムのPoCを完成させる。iWSET・酒道楽とはDBを共有せず、将来APIで疎結合連携する。

## Read first

1. `AGENTS.md` — 実装上の不変条件
2. `docs/ARCHITECTURE.md` — 4層境界とIdentity model
3. `docs/POC-SCOPE.md` — PoC受入条件
4. `docs/WINE-TO-STYLE-ANALYSIS.md` — 実ファイルから確定した取込仕様
5. `docs/IMPLEMENTATION-BACKLOG.md` — 実装順
6. `docs/OPEN-QUESTIONS.md` — 実データ接続前の不足事項

## Fixed decisions

- Stack: .NET 10 / ASP.NET Core / EF Core / PostgreSQL 17 / pgvector
- Data boundary: SOURCE → EXTRACTION → CANONICAL → SEARCH
- Parser First。決定論的に取得できる値にAIを使わない。
- `Product`はCanonical商品、`SupplierProduct`は仕入先SKU、`InternalProductRecord`は酒道楽商品。
- 酒道楽商品はCanonical Productへ任意Link。未Linkでも検索対象から落とさない。
- 酒道楽はread-only source。直接join・書戻し禁止。
- 数値・価格・在庫・容量・日付はSQL filter。意味・味わい・用途はFTS/vector ranking。
- 在庫空欄は`UNKNOWN`であり、`OUT_OF_STOCK`ではない。
- 税区分は「税込」の明記がある場合だけtrue。記載がなければ税抜(false)。
- 同一商品判定はFalse Positive回避を優先し、初期は自動Linkしない。

## Verified Wine to Style facts

Source: `在庫リスト 2026.09.01.xlsx`

- Sheet `ワインリスト`、header row 2、data rows 3–1063
- 1,061商品、SKU重複0
- IN_STOCK 655、LOW_STOCK 399、UNKNOWN 7
- 数量在庫329、オープン価格22、複数JAN 19、JAN欠損扱い208
- `OK`→IN_STOCK、`少量`→LOW_STOCK、数値→Quantity、空欄→UNKNOWN
- `希望小売`はREFERENCE_RETAIL。税込表記がないためTaxIncluded=false
- NV/MVは`Vintage=null`かつ`VintageRaw`を保持
- JAN=`0`は欠損。瓶/箱の複数JANは配列＋Raw Evidence
- 元Excelはプロジェクトへ複製しない

Aggregate verification is stored in `artifacts/wine-to-style-import-report.json`.

## Current status (2026-09-21)

自由文検索v5の配布候補を作成。価格帯、漢数字・万/千混在、予算、未満/超、容量の助詞、国/主要産地、短縮色名、発泡、在庫・依頼表現に対応。自動140件成功・5件skip（自然文関連110件成功）、実データAPI/Edge75項目成功。Dell用の同義表現32件照合スクリプトもローカル検証済み。DB/schema変更なし。現在の本番33459fd7は初版のみ。v5のLinuxコンテナ実行・本番反映はDellビルドと管理者登録後に実施。

## Previous status (2026-09-17)

利用者から動作確認完了の回答あり。複数キーワードAND検索、英字大小を区別しない照合、生産者が異なる候補の除外、候補表示後の資料更新検知を追加。自動35件（skip 0）、ブラウザ118項目（既存82・キーワード5・取込/紐付け31）を検証。DBスキーマ変更なし。
本番Docker環境構築は利用者指定の「本番環境構築」セッションへ委譲済み。このタスクでは本番変更を行わない。利用者確認済み内容のmaster同期・ローカルビルド・正式パッケージ作成を共通ルールに従って進める。実行証跡はチャットoutputsのリリース確認記録を参照。


検索結果を8項目の別カラムに分離し、見出しクリックの昇降順ソートを追加。取得済み全件を並べ替えてからページ分割。自動48＋既存UI34項目成功。詳細: docs/LOCAL-DEMO-GUIDE.md。

Dell Docker準備: deployment/に実機readiness、Hyper-V VM作成（既定plan）、Ubuntu Docker bootstrap、Dockerfile/Compose、SHA承認付きCodex配布経路を追加。CLI schema-checkで未適用/未知migrationを拒否。DB変更は手動境界。docs/DELL-DOCKER-GUIDE.md参照。
OS適合は既存Windows11 Pro記録、現在のJEA状態読取成功。RAM/仮想化/Hyper-VとDocker実起動は未確認。**準備済みだがDell稼働/デプロイ完了ではない**。実機readinessと環境構築の継続は委譲先で管理する。本番未変更。


ローカル確認マイルストーン完了。Excel/CSV取込UI、UTF-8/CP932標準CSV、匿名酒道楽fixture4件の同期、未Linkを含む横断検索、同一名称/容量/Vintage候補の手動Link/保留/解除、共通商品表示、原本/履歴、専用フォルダー監視（最大3回＋手動再試行）を追加。
従来の商品検索（容量・Vintage・税抜価格上下限）とWine to Style実1061件は維持。
build警告0・エラー0、自動28件・ブラウザ62項目成功。新Migration SQL適用/再適用成功・モデル差分なし。利用者用DBに架空検証データは投入せず、専用複製DBで動作検証。
操作手順: docs/WORKFLOWS-GUIDE.md。検証後はWeb/DB停止。本番デプロイなし。master反映は上記の利用者確認後の工程へ移行。
共有利用の認証、酒道楽実接続、日本語FTS/Embedding、汎用Excel/PDF/画像は未実装。意味検索を実装済みと扱わない。

## Next work — execute in this order

LIPS-natural-search-Dell-v5.zipをDellでprepare.sh実行、検証結果とSHAを管理者が確認してapprove後、既に受けたユーザーの本番展開指示に従い専用deploy経路で適用。本番の件数と同義表現32件・画面を再検証。税込/仕入価格・OR/除外・意味検索は対象外。v3/v4は未配布候補として置き換える。

0. 本番Dockerの環境構築・Linux image build・運用確認は「本番環境構築」セッションで継続。このタスクの対象外。
1. 利用者操作確認は完了。共有利用者と認証方式の確定後、認証・権限を実装。
2. 酒道楽の取得方法/schema/資格情報を確認し、read-only adapter・差分watermark・終売方針を実装。現fixture同期と置換可能な境界を維持。
3. 実商品ペアで候補精度を評価し、JANと生産者別名の扱いを確定（同言語の生産者不一致除外は実装済み）。自動Linkはしない。
4. 日本語FTSとgolden query評価。Embeddingは提供元/資格情報/外部送信範囲確定後に別Migrationで実装。
5. 汎用Excel、PDF/画像の抽出は後続。監視の常駐運用/認証監査は共有利用段階。

## 履歴 / Next work — source review filter 2026-10-12
SOURCE-REVIEW-TABLEは通常未回答・保留、全件で回答済み再編集・履歴表示。未保存入力を切替時に保持。隔離Edge表示12＋保存回帰17合格。原本・本人保存領域・本番変更なし。本人UI取得timeoutのため実回答件数/内容未確認、回答JSONの提供で確認可能。BASE契約・クラウド実行環境・未決業務判断は引き続き残項目。

## 履歴 / Next work — proposal and reusable rule preview 2026-10-12
独立工程として日付候補/原典根拠UIと再利用条件プレビューを実装。P14s localhost56005/date-proposal-review.html および /rule-preview.html、合成のみ、DB書込/BASE接続なし。Web build0警告0エラー、Edge16+11、ルール単体14合格。詳細docs/PROPOSAL-RULE-PREVIEW.md。既存Web維持。実回答取得、ルール永続化/CAS・疑義順次導線・実帳票適用は未完。BASE/クラウド/業務判断依存を保持。本番禁止継続。

## 履歴 / Next work — next unresolved review 2026-10-12
資料単位の次未解決導線を追加。未保存/保存不明/Unknown/Runningをガード、移動前にhash/版を照合、保存とresumeを分離。Edge12＋既存56003/専用DBの実ナビゲーション合格（answer/resume書込0）。資料内複数疑義・再利用ルール永続化/実回答取得・BASE/クラウドは引き続き未完。

## Current status — combined original and answer 2026-10-12
本人Slack指定で原典画像と確認結果入力/保存を同一画面へ統合。PC左右/狭い画面上下、原典常時保持、入力保持・履歴・保存とresume分離維持。56003既存合成資料で表示可能。Edge17＋次資料回帰12合格、実画像読取/保存はintercept合成応答、元PDF不変・実DB書込なし。実回答取得/ルール永続化/資料内疑義キュー/BASE/クラウド残。


## 履歴 / Next work — 2026-10-12 回答再利用の実接続

以前の「ルール永続化・資料内疑義順次処理は未実装」は現時点ではLUC容量単位について解消済み。392484bで保存原本の全対象行の最新回答・同じ単位が揃った場合だけAPI再利用登録を許可する。原典画像と質問の同一画面、順次行選択、回答保存、明示的ルール登録、明示的取込再開、次回同じ帳票/対象列への適用を接続。581da8cで実Edge/API/復元合成DB29項目とCompleted重複receipt4項目の受入を記録。56008稼働、手順はdocs/MAPPING-SEQUENCE-REVIEW.md。保存結果不明停止・原本hash・履歴を維持。

目標全体は継続。実取込の再利用はLUC容量単位だけ。列意味/産地略称/商品別名の実取込接続、他帳票の業務未決事項、本人の原典確認表の実回答JSON、Linux描画のLIPS統合、現行本番基点・移行/復旧/gateway/PC受入が残る。汎用プレビューや担当の独立検証を実取込/配備完了と扱わない。本人は本番適用までの直接指示済みだが、現在は隔離作業のみ実施。既存許可経路復旧と内部ソース4ファイル宛先別承認の未解決依存を保持する。

## 履歴 / Next work — reusable rule cancellation
取消管理画面と履歴GET/取消POSTを接続。56010/intake-rules.html、理由を別項目にしてappend-only保存、scope/CAS/operation照合・応答不明時GET復帰。Store11/画面11/実UI-API-DB6合格。取消後の次資料は質問に戻り商品数不変。取消管理画面の残項目は解消。業務未決資料/他種別の実取込/Linux統合/本番基点等は引き続き残る。

## 履歴 / Next work — 受領済み本人回答の確認・訂正

2026-10-12、本人が送信済みと指摘したDownloadsの `LIPS-source-review-answers.json` と `LIPS-source-review-answers (1).json`（各8617bytes）を読み、両者同一バイト・SOURCE-REVIEW-TABLEの全25項目ID/binding一致を検証した。24回答・1保留、24項目第1版／1項目第2版（元第1版履歴あり）。以後「実回答JSON未受領」として停止しない。原JSON/確認表を変更せず、artifact `received-source-answers-verification.json` と `RECEIVED-SOURCE-ANSWERS-20261012.md` に根拠・対応を記録。これは回答受領・対応確認であり、24回答の業務DB適用済みではない。

AOC `source-17a7690c75c1a61f` は「AOCが省略されているが同一商品」と本人明記。対象はオルヴォー新着66行／PDF15頁の Château Cesseras Cuvée Olric (AOC) Minervois 2024、PDF容量750mL。重複質問せずこの原典ペアに限定して次工程へ進める。ラヴィネ容量列L、地域略号10件、個別AOP/IGP/VT対応等も回答原文をbinding別に確認できる。自由文回答を一般ルールとして無条件適用しない。

保留1件は `source-d4342b06f79efb64`、オルヴォー定番品95行／スペイン9行の在庫相違。「該当箇所が不明なため保留とした」であり採用しない。LUC予約回答はタイトル/商品内容/コメント位置の説明で、予約ケース価格の単位・入数・条件への直接回答はまだない。ラヴィネ重複コード2行のidentityも、この回答集合だけで統合しない。

Linux renderer4ファイルは今回の本人直接承認によりリモート担当へ送信成功・受領報告済み（親担当連携情報）。従来の「4ファイル送信承認待ち」は解消。hash照合・統合試験は進行中であり、この記録ではLinux実取込統合完了・本番適用とは扱わない。

## 2026-10-12 残範囲監査
4d81ea9時点でLUC容量の目標経路と取消管理の必須工程は接続・受入済み。他種別は実回答JSON、価格/容量単位・地域略称・商品identity等の未決事項に依存し、値を推測して適用しない。
オルヴォーAOC省略限定1件はPENDING-SOURCE-REVIEW/確認表の「確定」とORVEAUX-PUBLIC-CROSSCHECKの「本人回答原文未確認」が不一致。Slack AOC検索で根拠原文を取得できず、本人へ限定1件の扱いを問い合わせた。回答までは依存実装/採用を止める。在庫相違商品の既決保留や日付は再質問しない。
Linux担当のlive状態を取得し、独立試験完了・4ファイル未受領・blockedを確認。相互ポーリングを進捗として継続しない。heartbeatは4d81ea9/56010/現在の未解決依存へ更新した。

## 2026-10-12 ラヴィネ回答経路とLinux境界修正
ラヴィネ厳密11列Parser/容量Hと地域C原略号別質問/再利用照合/行保留を接続。明示受領済みLと10地域対応だけを扱い、未知地域・重複SKU・納価の単位未確定は保留。納価を希望小売へ転記しない。初回質問→全回答→Rows保留でstore書込0。LaVinee20＋既存容量/Publication22＝42合格、Web build0警告0エラー。画面group19/既存LUC17合格。新runtime lavinee-review-runtimeはbuildのみ、起動・実DB受入は未実施。既存Web維持。
Linuxbackendは非選択sheet除去後range再取得と未対応境界の明示拒否を取り込み。SHA2a385f8cef3df52cd7cc93a830504621aec7c4ad91a5dd4e94209069dd6d4d40はLIQUOR-BASE合格候補一致。第2sheet/隣接結合/並行2process/timeout自身group停止/原典不変を確認。固定profileを付ける既存wrapperは並行競合するため変更せず、検証済み本体パスとbackend一意profileが必要。実暗号化は非ZIP代用品による拒否確認まで、Web接続/実原典全受入未完。
Next: 受領済み回答JSONのbinding/原典hash照合から永続回答履歴へ接続し、本人に再入力させず明示再開/限定再利用を実Web/隔離DBで検証。列意味/別名等の他帳票は継続。現時点のParser/UI試験を目標全体完了と扱わない。本番操作なし。

## 2026-10-12 受領済み回答の実隔離保存
受領JSONは不適合ではない。tools/source-answers CLIでSHA・2項目binding/版/文言・原典SHA・帳票fingerprint・質問scope/choicesを照合し、ラヴィネ1261行2522質問のうち2452回答を隔離DBへ保存、GETのHuman/mapping履歴とversion4を検証。合成HTTP12合格。未知地域70は推測せずHeld維持、商品数6不変、取込再開/再利用登録は未実施。proof received-answers-save-proof.json。review43db0b9a-48a7-4f11-bd5b-ba61f674af56。
56011は原典Python設定漏れを実画面で発見。既存Webを停止せず同runtimeの56012に既存Pythonと専用cacheを指定して起動（PID2712）。56012/workspace.html HTTP200、同隔離DB lips_migration_restore_20261012@55993。原典画像/実画面の確認は進行中。未知の地域+種類表記70行の分解方針は本人へ追加質問、既に回答済み10略号を再質問していない。
56012実Edge読取14項目合格。Excel原典1640x1159画像と選択row2一致、容量/地域scope、desktop/mobileはみ出し修正、変更POST0、review版/hash/商品数不変。proof lavinee-live-readonly-56012/proof.json。手順docs/RECEIVED-ANSWERS-REVIEW.md。既存キャッシュ表示1.49秒を初回ネイティブ描画時間と混同しない。
再利用実接続: 56012で2452保存回答の11グループを明示登録、receipt/historyGET一致。次回形式合成2件で既知容量/地域は質問不要、未知XXのみMapping、既知のみは価格単位Rows保留。商品6不変、元review版4不変。未知70と価格単位は本人回答待ち。既存資料の再開・商品登録は未実施。Linuxdispatcherと対象行スクロール改善は担当作業中。
56012対象行追従: highlightBox検証後、ズーム/Excel行切替で縦表示外の行を戻し、手動倍率/横位置/未保存回答を保持。実原典14項目成功、POST0、版4商品6不変。lavinee-zoom-target-56012参照。従来の手動上端スクロール残項目はこの操作範囲で解消。
Linuxdispatcher: render.pyから明示opt-in時だけLibreOffice backendへ接続、実行ファイル絶対パス検証/60秒worker、Windows/PDF維持。分岐9合格、Web別output build0警告0エラー、backend配布csproj追加。本人承認済み同4ファイルをremote担当へ更新送信、実CLI受入待ち。送信と受入完了を混同しない。新test_render_dispatch.pyはローカル限定。

## 2026-10-12 原典1ペア・Linux dispatcherの追加照合
Linux担当でdispatcher info/render第2sheet行6が実受入合格。4ファイルsize/SHA一致、1page/対象文字/強調box/原本不変/残存processなし。proof SHA79081a37dcc94b4f6e6e98823122deb5963c1dcbd12a8b72e8f80c190a11ab08。Linux側に.NET10/LIPS Web一式/専用DBはread-only確認範囲で見つからず、HTTP Web受入は未完。新規設置/権限/ネットワーク変更はしていない。
AOC1ペアの現原本SHA・値を再照合。PDF実名Orlic、Excel Olricで『AOC差だけ』という旧記録は不正確。当該ペアを同一商品とする本人回答は保持し再質問しない。別VT2023/70本を混ぜず、名称原文保持/通常価格と12本条件価格を分離する。専用supplier/parserと複数原典Evidence、恒久商品キー接続は未実装。artifact ORVEAUX-AOC-PAIR-READONLY.md参照。
ラヴィネJ納価単位を明示Mapping質問へ接続中。本人への実単位質問は未回答。PerBottleを選んだ場合のみ採用する実装を合成専用空DB lips_price_e2e_20261012@55993/Web56013で試験し、実回答DBのルールへ混ぜない。元56012実回答は維持。

## 2026-10-12 旧保留資料への質問追加
旧Mapping Held資料は未回答があると再判定できず、新価格質問へ到達しない欠陥を修正。明示refresh-questionsは原本hash/版CAS/操作ID照合、厳密一致回答保持、旧回答履歴保持、業務保存なし。変更なしはno-op、Unknown/Running等は禁止。辞書化で回答×質問の反復serializeを除去。Store10+専用DB1、UI35+既存19、実Edge14+16合格。56014は最終runtime-v2/PID12732、専用合成DB lips_question_refresh_20261012、旧→新review6530aa42-e2c0-44d9-aa85-2bcdf3a025acは版5/6質問/3回答/商品0。docs/QUESTION-REFRESH-REVIEW.md。実資料56012は未更新。Linux実HTTP経路はIntakeReviews原本取得に依存、全業務DBは不要。最小配布候補/導入承認案は未生成、docs/LINUX-WEB-ACCEPTANCE-DEPENDENCIES.md参照。実資料の未確定地域/価格回答、商品別名接続、Linux HTTP受入が残る。本番操作なし。

## 2026-10-12 LaVinee明示部分保存
正常行のみ保存・残行再開・既確定件数保持・重複receipt今回追加0を実装。専用合成Web56015/DB lips_partial_lavinee_20261012。手順と範囲はdocs/LAVINEE-PARTIAL-REVIEW.md。実資料の価格単位/未知地域判断、本番・Linux HTTPは未完。

## 部分保存から次回再利用の実通し検証
56015完了資料の全回答から4再利用設定を公開し翌日原典へ適用。Edge/API35＋DB/HTTP10合格。対象外地域/列構成変更は業務保存なし。docs/PARTIAL-REUSE-REVIEW.md参照。Completed説明の矛盾と対応取引先案内を修正。実資料の未確定判断は未適用。

## 再利用状態の再読込表示
GETした最新規則で登録済み/取消/相違/未確認を表示、同じ有効回答の重複POSTを抑止。直前GET/CAS/operation照合とUnknown停止維持。docs/REUSE-STATE-REVIEW.md参照。実DB書込なしで検証。

## 複数取引先の親フォルダー受入
56015実Edge/API27合格。5ファイル成功1/保留2/未対応1/失敗1、LAルールLUC非流用、同原本再選択追加0、LUC原典+容量質問表示。docs/MULTI-SUPPLIER-REVIEW.md参照。独立DB価格再検査は反復していない。

## 履歴 / Next work（2026-10-13 Windows Excel原典可読性）
横長Excelの低解像度を元PDFのclip高精細描画へ修正。外側枠のみで対象行pixels保持、16k/8k/32Mpx上限、Windows XLSX cache v2-native-density。既存9+上限3、合成LUC/LaVinee hash/pixel照合、56016実GET Edge9合格/POST0。docs/ORIGINAL-RENDER-READABILITY.md参照。親build0警告0エラー/独立レビュー指摘なし。本番適用とLinux同等可読性受入は未実施。既存Web/DB変更なし。

## 2026-10-13 承認済みAOC1組の実UI接続
本人の内部ID承認を受け、受付履歴から固定2原典・受領回答JSON・明示保存へ接続。56018/orveaux-pair.html?review=244f9b09-86f2-4de4-a861-6444c43f94a3、専用DB lips_aoc_flow_20261013@55993、PID72348。backend6/build0/合成UI18/実保存18合格。初回1・再登録0・総商品1、在庫60非加算・4価格未採用・原文保持・原本SHA不変・両レビューHeld維持。CSPレイアウト修正後左右表示とPDF2024手動枠を目視。docs/ORVEAUX-PAIR-UI-REVIEW.md参照。旧環境保持。本番未適用。LaVinee実地域70/納価単位の判断・一般ORVEAUX全行・LinuxHTTP等はこの1組の完了とは別。

## P14s操作目標の統合受入
56019/workspace.html、DB lips_goal_integrated_20261013@55993、PID74784。56015合成複製＋最新schema/Review/Rules/Pair有効。実UI25＋共存GET7合格、LUC2回答保存→明示再開2商品→scope1公開→翌日新値再利用、総6・旧LA4規則不変・原本SHA不変・旧環境保持。docs/GOAL-ACCEPTANCE.mdで要件照合済み。P14s操作目標は受入完了。実地域70/納価単位等の業務判断・一般未対応形式・LinuxHTTP/本番は別未完として保持。


## AI source notes (preserved)

# Current candidate: free-text LIPS consumer (2026-10-16)

Bulk intake Dell deployment/business acceptance is complete (MetadataFinalized). This isolated source is a new, undeployed candidate derived from the accepted source snapshot; it is not a replacement production record.

User decisions: failure falls back to existing rules with a visible reason; supplier code filtering is included; explicit UI controls win and adopted conditions are displayed. Existing unchecked availability means unspecified. No new AI, credentials, transport, service, or production changes are enabled by this candidate.

New routes: GET /api/free-text-search/options and POST /api/free-text-search/search. Bridge interpretation is off by default. Only a configured literal loopback HTTP URI with fixed /api/free-text-search/interpret path is accepted; redirects/proxies/cookies/retries are disabled. Real BASE/Dell topology still requires joint operations agreement.

The strict v1 contract validates all response keys, types, option membership, duplicate keys and echoed request/version. Residual terms go directly to parameterized literal search, never through NaturalSearch. Supplier code resolves uniquely to SupplierId. The four search dependency tables are hashed in the same RepeatableRead snapshot as options. A change while interpretation is in flight discards it; final search executes within the second read-only snapshot.

Validation: standard Release build has zero warnings/errors. Related suite 194 pass, 2 skip; Web mock 23 checks; browser mock 16 checks. Xml/Pkcs 10.0.13 resolves the prior package audit findings with no warning suppression in candidate settings. Actual isolated HTTP/PostgreSQL results and cleanup are in sibling artifacts, not presumed from mocks. Accepted model interpretation logs store escaped provider/model/prompt/request/version metadata and a UTF8 query SHA256 only; query/body content is not logged. No real model inference or production activation has occurred.

Historical notes below are preserved and do not override this current candidate status. BASE operations inputs and formal deployment remain separate gates.

---

## 2026-10-13 原典画像限定受入・追加承認完了

- 本人がcalcChain既知1件限定修正/合成/実原本比較/停止を明示承認。BASE candidate002 e9a5e9956ad7de2529c332f9e8764f7b411706e7d2da1dd978f38eb4056e593a、元backend維持。
- 静的27負例＋親4負例、合成cache777 runtime1＋実LaVinee4行(2/938/941/1000)成功。原本保存なし/SHA不変、4段階cache保持、所有process/profile/listener0。
- 親Excel基準4PDF/PNG・元bytes不変/所有Excel0。Linux30成果物size/hash一致、数字token4組差分0、親画像比較完了。フォント差・長い地域既存clippingを記録。
- 証拠はLIPS artifacts/folder-ui-20261011/linux-real-acceptance-preparation-20261013/excel-original-baseline/REVIEW.md、parent-final-comparison.json、linux/runtime-proof.safe.json。Slack1791899327.348729。
- 今回範囲完了、反復不要。次はcache分離とbackend候補のWeb統合/隔離HTTPの具体範囲確認。新候補未配布・既存WebDB未変更、本番別工程。

## 最新追記 2026-10-13 原典画像候補とキャッシュ分離

Linux実LaVinee run006業務受入（3783回答/1260商品/32規則/全値一致/終了残0）は完了。限定renderer合成19件+親独立6件/画像/timeout残0も完了、配布済backendは変更なし。実原本画像受入は別未実施。

今回OriginalRenderCacheVersionとendpointを更新し、Linux XLSXだけdispatcher/backend bytes SHAを含むv3世代へ分離。ファイル読取不能時503、旧画像の再利用なし。Windows/PDFの世代は維持。対象テスト4件成功、Web build警告0/エラー0。既存Web/DB再起動・配布なし。実HTTP統合は未実施。候補rendererをこのsourceへ適用したとは扱わない。

残：新規隔離HTTPで候補/キャッシュ統合の受入、同一原本のExcelまたは版対応PDF基準との実画像比較、本番別工程。実原本描画は今回合成承認へ含めない。BASE担当は参照PDFが担当領域にないと確認。本人の決定済み業務意味は再質問しない。
# 現在の引継ぎ（2026-10-13・統合受入後）

この冒頭を現況として参照する。以下の「履歴」では各時点の未実装・承認待ちを保存しているが、現在の未完一覧ではない。最新の操作目標・証拠・手順は [GOAL-ACCEPTANCE.md](docs/GOAL-ACCEPTANCE.md) を正とする。

## 完了した操作経路

- P14sの共通確認URLは http://127.0.0.1:56019/workspace.html 。専用DB `lips_goal_integrated_20261013`（55993）。対応済みの親フォルダー取込、原典画像と質問、回答永続化、明示再開、同取引先・帳票・対象条件限定の次回再利用を統合確認済み。`goal-integrated-56019/proof.json` 25項目と `coexistence-proof.json` 7項目が新しい統合証拠。旧環境の試験件数をこの合格数へ加算しない。
- LUC容量に加え、ラヴィネの容量・地域・納価単位の質問経路、正常行の部分保存と未保存行再開、再利用取消の管理導線は接続済み。受領回答JSONは照合済みで、対象のラヴィネ回答保存も実施済み。24回答すべての業務データへの適用を意味しない。
- AOC固定1組は親フォルダーの受付・保留履歴から対象2原典を選び、原典画像・受領回答JSON照合・明示登録へ接続済み。56018の実保存18項目で確認。内部IDは仕入先SKUと区別し、ProductIdと原典SKUはnull、原文2表記・Evidenceを保持。在庫60を加算せず、未確定価格は採用しない。元ファイル全体を取込完了にはしない。56019ではAPI共存を確認し、この実原典を重複登録する試験はしていない。
- Linux用描画backendの送信許可・受領・CLI検証は完了している。旧履歴の「4ファイル送信許可待ち」は解消済み。Linux WebのHTTP受入とは区別する。

## 実際に残る事項と次工程

1. 実ラヴィネ地域70行・全対象納価単位は本人回答済み。3812239/6e09cb1で原地域と商品区分を分離保持、カルヴァドス/コニャック表示、税別1本価格を適用。重複は少在庫採用とバッジ、今回938/1000行は7本採用・双方原典保持。P14s56023・専用DB lips_lavinee_accepted_v2_20261013で3,783回答、明示取込1,260 SKU、総1,266、32再利用規則。最新手順と受入証拠は [LAVINEE-ACCEPTED-ANSWERS-REVIEW.md](docs/LAVINEE-ACCEPTED-ANSWERS-REVIEW.md)。先行56022の移行不足Unknownは保持し再送せず、新復元DBへ既存移行を適用して検証した。LUC予約欄等の別判断は残る。
2. AOCは指定hash・位置・2024年の1組のみ。一般ORVEAUX parserや他帳票・全取引先への展開は未実施。明示承認済みの同一組・内部ID方針は再質問しない。
3. Linux HTTP受入・BASE実接続・本番配備は別工程。manifest/計画の同2資料共有は本人承認済み・静的定義修正済み。本人は公式.NET/ASP.NET Core Runtime10.0.12 Linux x64の2件とPG17.11 source1件の専用staging取得/hash検証を直接承認しBASE担当へ依頼済み。展開・build・導入・DB起動はこの承認に含めない。BASE Linuxにはcompiler/make/開発headerがなく、既存builderも見つからず、最小toolchain案を読取情報から整理中。本番・権限・ネットワーク変更を行わない。

対応済み範囲のP14s操作目標は受入済み。実資料全件の業務受入や別環境への適用は未完として別管理する。旧Web/DBを不用意に停止しない。

---

# 作業履歴（以下の未完・待ち状態は記録当時のもの）

## 履歴 / Next work（2026-10-13 AOC限定商品の検索接続）

720c738で固定原典ペアを種別付き内部IDとしてSupplierProduct1件/SourceEvidence2件に接続。ProductId=null、原典SKU=null、原文名称併記、在庫60非加算、価格未採用。0a5938fで一覧/詳細/選択/CSV/workspaceの内部IDを仕入先SKUと区別。専用DB lips_aoc_import_20261013@55993、P14s http://127.0.0.1:56017/ 、runtime aoc-import-runtime-final PID14756。実DB統合合格、実HTTP10/実GET Edge7/合成表示10/既存CSV回帰/build0。原本不変、本番なし。API preview/commit/operationGETは明示操作・固定原典bytes照合。docs/ORVEAUX-PAIR-IMPORT-REVIEW.md参照。

残: 一般ORVEAUX parser、親フォルダー保留画面→固定ペア選択/回答JSON接続は未完。実LaVinee地域70/納価単位の本人判断未決。検索/APIの局所成功を全体完了にしない。旧Web/DB維持。内部IDの本人承認は受領済で再質問不要。
## 履歴 / Next work（2026-10-12 統合実操作確認）

a9da08eの56007/復元合成DBでブラウザー実操作から親フォルダー取込→容量疑義→実Excel原典→回答POST→次回ルールPOST→明示resume→翌日資料自動再利用→同一資料再送まで29項目合格。interceptなし。新SKU2件のみ、750/1500mL→次回500/3000mL、価格/在庫は次原典値、原本SHA不変、再送件数不変。artifact mapping-integrated-b36e89d654/integrated-proof.json。既存本人fixture/56006回答は未変更。
追加監査で保存結果不明後の開始/フォルダー選択再開を発見し修正。同ページでロックし履歴照合を案内。35合成通信ブラウザチェック合格、通常保留継続/手動停止後再開維持。永続キュー復元は未実装。56007へ静的反映・GET確認済み。
BASE担当の保存済み合成応答をLIPS .NET CLIで契約照合しCandidates1件合格。追加推論/DB書込なし。再シリアライズでありリモートバイト一致は未主張。
残: 実取込接続はLUC容量単位。全取引先/他疑義回答・実回答JSON/実資料受入、Linux正式backend統合、現行本番inspect/許可経路/DB適用/gateway受入は未完。本番適用の本人一般指示は受領済み。4ファイルBASE送信は自動承認拒否後の宛先別本人回答待ち。
## 履歴 / Next work（2026-10-12 本番適用指示）

追記: 合成専用DBの実dump/restoreが成功し、原本/回答/ルールdigest・件数一致、追加SQL各2回再実行不変、元DB不変を確認。migration-restore-20261012/proof.json。管理者移行/復旧案とゲート更新bba88fc。Linux独立backend/合成fixtureを2997555へ追加（Windows経路未変更、Linux自動有効化なし、数式は再計算未証明のため保留、ローカルguard2件合格）。LIQUOR-BASE担当が専用prefixで公式ツール環境を準備中。renderer4ファイルの同ホストへの送信は自動承認レビューで宛先別承認不足として拒否され、本人へ4ファイルを明記した許可を質問済み。承認前の再送/別経路取得禁止。接続担当の追加証拠: 現VPN送信元10.10.10.3、443到達、22222/5987等timeout、記録上の許可元10.10.10.1/192.168.1.110と異なる。既存許可経路への切替を本人へ確認中。新しいIP許可追加は行っていない。実装準備・合成検証と本番適用を区別する。

本人が「エージェントと協力し本番環境への適用まで進めて」と直接指示。本番適用の一般承認待ちは解除。80ef896の本番準備を開始したが専用inspectは192.168.1.5:22222への接続timeoutで現行基点未取得。接続担当へ読取調査依頼済み。新2テーブルは明示SQLでWeb-only schema guard対象外、LIPS管理者手動移行の準備を進める。Linux Excel描画も未対応、P14sにWSL distro/Docker/LibreOfficeなし。クラウド担当への直接送信はAbsolutePathBufエラーで未到達のためSlackで照会した。独立して合成専用DBのbackup/restore検証と移行手順を準備中。詳細docs/ROW-INTAKE-RELEASE-GATES.md。下記の本番操作なしは各時点の履歴であり現在の承認範囲ではない。本番変更・正式候補転送はまだ実施していない。

## 履歴 / Next work（2026-10-12 回答の取込利用・次回再利用）

本人の親フォルダー一括取込→疑義原典/質問→回答保存/取込利用→同取引先の次回再利用を目標設定。LUC既存在庫形式の単位なし容量に型付き行質問を接続し、保存回答で換算、明示操作でルール永続化、同取引先/帳票/列fingerprint限定で次回適用する縦の経路を実装。CAS/operation冪等/取消履歴/原本・行Evidenceを維持。P14s56006、合成専用lips_rules_review_20261012。通常321合格15環境skip、実API12/実UI6/実親フォルダー4/合成UI17合格。手順docs/MAPPING-ANSWER-REUSE-REVIEW.md。現時点の適用対象はLUC容量単位のみ。全取引先/他の回答種別/管理画面/実回答取得/クラウド・BASE/実資料受入は残る。案件全体完了ではない。本番操作なし。

## 履歴 / Next work（2026-10-12 原典拡大）

追加: 原典確認表25項目にローカル回答入力/保存/再読込/編集履歴/JSON書出しを実装。artifact SOURCE-REVIEW-TABLE.html、tools/cloud-review/source-review-answers.js、docs/SOURCE-TABLE-ANSWERS.md参照。Edge17項目合格。localStorage保存でありサーバーや業務DBへ反映しない。既存46リンク維持。BASE/クラウド/未決資料の残項目は維持。

追加: 56003で合成内容の実PDFをPDFium描画、拡大/範囲/スクロール/回答往復など14項目合格。OriginalRenderEndpointsのscript参照をAppContext.BaseDirectoryへ修正、dotnet runのContentRootとの差を解消。既存Web保持、商品数2件不変。docs/BASE-DATE-PROPOSAL-CONTRACT.mdに既存JSON契約と担当境界を具体化。BASE実接続/クラウドLinux起動は担当受入待ち、未決資料判断は従来どおり。ZIP6218fa3共有は直接本人承認で完了、再承認不要。

56002に原典画像の拡大/縮小（10〜400%）、原寸100%、横幅合わせ、上下左右スクロールを追加。再描画APIを呼ばず表示倍率だけ変更する。回答画面往復で倍率/画像スクロール/参照行/未保存回答/選択SKUを保持。Edge17項目合格（実Excel画像、PDFは合成geometry fixture）。参考Library画像はPage参照不足で取得できず実画面で再現確認。手順docs/IMAGE-ZOOM-REVIEW.md。実PDFでの今回追加倍率受入、既存資料判断/BASE/クラウド/本番準備は未完。本番操作なし。

## 履歴 / Next work（2026-10-12 行単位再判定）

ENEOS専用parser・同日競合の部分保存・未保存SKU選択/回答確定/別操作再判定・原本別数量詳細を実装。P14s56002、合成専用lips_rows_review_20261012。32対象試験/17EdgeAPI、通常280合格15環境skip。実ENEOS3帳票は読取のみ48/139/371行と原本不変確認。詳細docs/ROW-RESUME-REVIEW.md。推論date-proposal/v1検証CLIと合成クラウドパッケージ手順はtools/cloud-review。次はオルヴォーAOC1候補/他20行の判断、他保留形式、クラウド/BASE側受入、本番準備。案件全体完了ではない。本番未操作。

## 画面の分かりにくさを修正（2026-10-12）

本人のスクリーンショットは空の1行目を指定していたため、黄色い枠しか出ていなかった。初期行を固定1から「最初に文字・数値がある可視行」へ修正し、自動表示する。初期表示は対象を判定したものではないため強調OFF。空行指定時は内容のある開始行を案内し、空枠を出さない。

今回の資料は画像表示の確認用サンプルで、実際の仕入先在庫ではない。画像だけ確認するリンクはこちら:
http://127.0.0.1:56001/intake-review.html?id=f2036333-4d2c-4815-b226-346dd13545d4&view=original&sheet=1&row=95

開くと95行が自動表示される。この画面は日付回答・商品の取込を求めない。通常の回答画面では「この在庫リストは、いつ時点の情報ですか？」と目的を明示し、分からない場合は保留を選ぶ手順を説明する。

検証: ブラウザー14項目、空行/初期行の追加回帰1試験合格。専用Excelプロセスの終了も確認。証拠: original-clear-verification.json / original-clear-review.png。旧環境・商品DB・本番は変更していない。
## 履歴 / original-image implementation (2026-10-12)

本人指示により保存値描画を廃し、Excel16の原典印刷画像とPDFiumの元PDFページ画像へ変更。対象行強調・PDF手動範囲・4段階画面。新URL P14s http://127.0.0.1:56001/workspace.html、旧環境保持。Edge/API21項目＋最終UI4項目・描画回帰8試験合格。専用合成DBの商品3件不変。本番未操作。手順 docs/ORIGINAL-IMAGE-REVIEW.md。旧.xls/外部接続付きExcel/実原典受入/対象位置の業務疑義自動接続/本番は未実施。以下の保存値描画の記録は旧版。

## 履歴 / UI update (2026-10-12)

原典行と前後行の確認用画像、資料ごとの原典確認→回答入力→内容確認→結果の画面切替を実装。保存セルからの画像化で原本レイアウトそのものではない。Edge/API16項目合格。URL56000を維持。本人合成確認日10/4、詳細 docs/IMPORT-QUESTION-REVIEW.md。既存残項目と本番未操作を維持。

## 履歴 / Next work (2026-10-12 question workflow implemented)

原本受付/疑義/回答履歴を専用IntakeReviews表に永続化し、原典ダウンロードとCSV/XLSX保存セルプレビュー、日付回答確定、版/hash照合付き対象再開を実装。保留継続は履歴保存のみ。Running/Unknownは再開禁止、同時処理はCASで1回だけ実行。保存不明時のキュー停止を維持。P14s http://127.0.0.1:56000/workspace.html、専用DB lips_questions_20261012、合成資料のみ。詳細/手順は docs/IMPORT-QUESTION-REVIEW.md。

関連単体67合格/既存DB条件1skip、新機能最終18合格/skip0、Edge/API29+再起動等9項目合格。旧確認Web/DB保持、本番未操作。次はENEOS競合/各帳票の疑義を型付き回答方式へ接続、未対応形式・PDFページ参照・LLM候補提示は別工程。今回任意自由文による業務値変更や外部LLMを実装済みとしない。

## Previous plan (2026-10-12 question workflow)

本人Slack1791766575.053619と続行1791766584.429509を受領。取込疑義の原典参照・回答確定・対象処理再開を優先。既存endpoint/FolderImportService/画面との接続を確認し、最小範囲と順序を docs/IMPORT-QUESTION-WORKFLOW.md に保存。取込前の原本保存と回答履歴は未実装、Canonical用ReviewServiceとは別契約にする。次は受付原本/疑義/回答の永続化、基準日回答と保留継続のUI/API、版・hash照合付き再開の実装と隔離検証。ENEOS/AOCの既決定条件は保持。外部LLM/本番操作なし。今回の成果は調査・実装範囲の確定であり実装完了ではない。

## 履歴 / Next work (2026-10-12 decisions)

本人回答を docs/HELD-INTAKE-NEXT.md 冒頭へ保存。3資料の基準日は本人指定2026-10-01、ENEOSは同日非重複併存・価格一致時は数量加算せず原本別Evidence保持・価格不一致保留。数量不一致の単一表示は照会中。オルヴォー数量競合商品は本人指示で保留確定。他商品の別名/VT対応確認は継続。LLM推定日と根拠の一覧表示は将来項目。コード/DB変更・追加試験は今回未実施、既存Nouvelles実装f2f696eと専用DB6435の検証証拠を維持。

Next: 決定済み日付の出典区分を保持する契約とENEOS3形式fixture/parser。数量表示に依存する保存変更は回答後。既存同日guardの無条件解除、原本日付の改変、本番操作はしない。下記旧節の「日付回答待ち」「ENEOS全方針未定」は本節で更新。

## 履歴 / Next work (2026-10-11 Nouvelles intake)

ヌーヴェル専用パーサーを追加。原本A3の更新日2026-09-30を使用し、見積期限を資料日にしない。希望小売Hは固定値のみ、納入価格G/リンクL数式は評価・取得せず原式保持。少/OKの数量はnull。国/産地を推測しない。重なるふりがな情報によるClosedXML読取失敗は専用のメモリ内コピーのsharedStringsからrPh/phoneticPrのみ除去し対応。保存原本は変更しない。外部リンクguard維持。

54単体合格/DB環境依存1skip、Web build 0警告0エラー。実資料1343行のSKU/商品名/容量/上代/在庫/原数式を独立読取と全行照合。専用DB5092→6435、Edge保存/重複時同一原本IDと件数/API検索/画面/原本SHA不変確認。最新P14s http://127.0.0.1:55999/workspace.html。既存Web保持、本番未操作。成功6/保留7/未対応5。

Next: ENEOS同日複数資料の競合方針とLUC予約/オルヴォーロット/ラヴィネの日付は本人回答待ち。独立したオルヴォー現在庫等の調査継続。最新非秘密差分共有・本人受入・本番基点/配備は未完。証拠nouvelles-source-proof.json/nouvelles-review-proof.jsonはローカル、実商品内容を外部共有しない。

## Follow-up 2026-10-11 10:57Z

Slack差分に新規指示なし。確認Web55998で5092商品維持。残保留8件の帳票/日付/数式/競合条件を docs/HELD-INTAKE-NEXT.md に整理。ENEOS3の10/1、ヌーヴェル/オルヴォー現在庫の9/30は原本に根拠あり。LUC予約/オルヴォーロット/ラヴィネの資料日付と、ENEOS同日複数資料のSKU単位競合方針を本人照会中。依存する保存/ガード変更は未実施。独立した解析は継続可能。

## 履歴 / Next work (2026-10-11 Le Caveau intake)

既存 tools/le_caveau_import.py と同じルカヴォー2026-09-29帳票契約をC#一括取込へ追加。全729行の13項目(SKU/商品名/VT/容量/参考上代null/税区分/在庫/国/産地/タイプ/原本行・シート)が既存処理と一致。全角空白を含むSKU生成も互換。卸価格はPriceNote/RawCellsに保持し参考上代へ流用しない。入荷済660/予定69、予定本数は現在庫にしない。H9のTODAYは保存済み日付だけ読取り、再計算しない。対応日付・数式・形式変更は保留。

48単体合格、DB環境依存1skip。専用実資料DB4363→5092、Edge取込/再取込・同一原本ID/件数・API/画面検索・原本SHA不変確認。最新本人確認P14s http://127.0.0.1:55998/workspace.html。旧確認Webは保持。本番未操作。残りは保留8件/未対応5件、成功5件。

途中で55993接続拒否10061、旧PID消失/非listen/pg_ctl停止を確認。既存領域・同一loopback/portで専用DBを起動し、専用DB名と既存4363件を確認してから保存。停止原因未確定。localhostのnode接続EACCESは承認済み隔離検証をsandbox外で実行し成功。いずれも保存POST前、保存不明の自動再送なし。

Next: 残保留8件(ENEOS3/LUC予約1/オルヴォー2/ヌーヴェル1/ラヴィネ1)の帳票/日付/価格仕様確認と対応。実資料・SKU証拠はローカルに限定。未対応PDF2/XLS3は別範囲。最新非秘密差分共有・本人受入・本番基点/配備は未完。

## 履歴 / Next work (2026-10-11 WTS held-file intake)

WTS専用の静的セル読取projectionを追加。外部リンク/ハイパーリンク/印刷設定/定義名/拡張metadataは読取コピーから除外し、原本bytesとSHAは保存。数式・接続・OLE・未知の外部参照は保留、他取引先の外部リンクガードは維持。外部取得・再計算なし。

FolderImport 43合格、DB環境依存1件skip（この試験をDB合格扱いにしない）。実資料WTS1173行を専用 lips_real_20261011 に追加、3190→4363。Edgeで取込/再取込、同一原本IDと商品数不変、API/画面検索、原本SHA不変を確認。最新確認URLはP14s http://127.0.0.1:55997/workspace.html。旧55994/55995/55996保持。18資料全体は成功4/保留9/未対応5。実データ証拠はリポジトリ外wts-review-proof.json等、外部共有禁止。本番未操作。

Next: 本人承認済み保留9件の取引先/形式/日付対応。未対応5件(PDF2/XLS3)は別範囲。日付・不明値の捏造は禁止。最新差分の非秘密証拠共有、本人体験確認、本番基点・配備は未完。

## 履歴 / Next work (2026-10-11 real intake follow-up)

次工程回答を受領。丸数字のNFKC変換による資料日付誤認を修正（区切りなしは8桁のみ、全角数字は維持）。AMZのJAN列を地域の後に置く配置を追加し、原産国/価格/在庫の誤対応を防止。取込関連40テスト合格。実資料専用の隔離確認DBで18件処理：成功3/保留10/未対応5/失敗0。AMZ2件204/203行、LUC1件2985行。登録商品数3190（旧版のみのSKUも保持）。商品コード2例のAPI/Edge検索と原本ID照合済み、原本18件SHA不変。

実資料確認URLはP14s localhost55996。個別検索入力例と全ファイル結果はリポジトリ外のLIPS/artifacts/folder-ui-20261011/REAL-IMPORT-REVIEW.md（実資料由来、外部共有しない）。旧55994/55995は保持。本番未操作。
Next: Wine to Style外部リンクの取得/評価を行わない対応方針検証（ガードは維持）、未知取引先/形式の対応範囲整理、非秘密3成果物のDell共有、本人実受入と本番基点/配備。単なる解析/登録を全資料受入完了としない。

## 履歴 / Next work (2026-10-11 folder UI)

本人が新規改良の一律禁止を撤廃。既存9628d6cから独立したソースコピーで、一括取込を主導線にし、単票の任意取引先標準CSV機能は折りたたみ補助メニューに保持。Excelは既存一括取込へ案内。親フォルダー/下位資料フォルダーは既存の祖先名判定を利用。標準CSVと仕入先独自CSVを明確に区別する。バックエンド/DBスキーマ変更なし。

検証: Web build 0 warnings/errors、Edge/API 31項目、Wine to Styleの下位フォルダー標準CSV保存。FolderImportTestsに5取引先の深い階層判定を追加。本人確認はTHINKPAD-P14S http://127.0.0.1:55995/workspace.html、隔離DB lips_ui_20261011 (port55993)。旧55994環境は保持。

Next work: 本人の実PC/OneDriveフォルダーでの受入、対応外PDF/旧Excel/仕入先独自CSV/未知取引先の扱い。本番基点照合・実gateway受入・本番配備は未実施、個別指示が必要。今回の許可を本番操作へ拡張しない。

## 履歴 / Next work (2026-10-11 review follow-up)

承認済みの保存結果不明時キュー停止を隔離worktreeで修正。HTTP200のHeldでもStopQueue=trueなら停止し、通常入力保留は続行。未処理保持・自動再送なし。SearchTextの共有変更を明記し、通常単票と追加2項目の隔離DB回帰を追加。原本IDは結果JSONまでで画面追加なし。今回の証拠はLIPS/artifacts/folder-upload-20261009/review-fix-20261011/。過去の集計は過去の証拠として保持する。

次は最新本番基点照合、実gateway/PC/OneDrive受入、個別指示後の配備。本番操作は今回未実施。以下の過去の展開指示を今回の新規配備承認として使わない。

# Current status (2026-10-09, local folder upload)

Folder selection and per-file server ingestion are implemented on isolated branch `feature/lips-folder-upload-20261009`, starting from saved distribution source f1732a9. Existing development-tree changes were inspected and not overwritten. Known Excel supplier parsers were reused, with server folder/content matching, explicit document dates, transactional supplier/hash idempotency, same-date/older-version holds, and per-file success/held/unsupported/failed receipts. Browser sends one file at a time within the existing gateway limit. Source evidence, tax, unknown stock, selected CSV and special prices are preserved. No schema migration or configuration change.

Validation: Web build zero warnings/errors; 227 standard tests pass with 10 environment-dependent skips; dedicated folder/DB suite 28 pass, LUC real workbook suite 13 pass (2902 rows), browser/API 21 pass, selected CSV regression passes. Three additional supplier-layout tests are included in the 227 standard tests. No production operations, no push or merge.

## Next work

See `docs/FOLDER-UPLOAD.md` for usage, evidence, baseline provenance, limitations and local reproduction. PDF/legacy Excel, additional suppliers and multi-document composition remain unsupported by this Web path. Production deployment/DB operations/config changes are excluded by the current user instruction. The parent project's watcher/recovery/production adapter/acceptance dependencies remain. A future deployment requires fresh baseline reconciliation and explicit instructions; older deployment approvals below are historical.

# MV/NV update (2026-09-29)

User explicitly requested MV treated as NV for search. Known M.V./fullwidth/wrapped variants normalize to NV. Canonical options merge MV into NV; either input matches both. Original VintageRaw/evidence remains unchanged. 183 tests passed, 7 environment skips; isolated DB vintage/supplier tests 40 passed with no skips. Base deployed source is 17bff0dc39ed1ca72e52419fd9b49bea6604a046. Next: exact mutual-TLS receiver registration and standard LIPS deployment; production is not yet updated for this delta.

# Codex handoff

## Current status (2026-09-29, selected CSV release candidate)

User confirmed selections persist across searches and more products can be added. Search rows now have checkboxes keyed by stable SupplierProductId. The CSV button opens a review dialog with removal actions; only the remaining selection is exported. Pagination/sort/empty and failed searches preserve selection. Re-fetching a selected ID refreshes its displayed data without duplication; different suppliers with the same SKU stay separate. Selection lasts for this page session (refresh/navigation clears it). CSV follows the current sort; existing escaping, original evidence, retail/special price distinction remain intact.

181 .NET tests passed (7 environment tests skipped), vintage and supplier DB integration passed, desktop/mobile browser coverage includes persistence across searches, selection removal, same-SKU suppliers, source refresh, CSV content/decimal prices, failure and empty selection. Existing /api/options remains backward compatible; the UI requests vintageNotation=canonical. Root deployment option comparison remains unchanged.

## Next work (approved production release)

User authorized production deployment of these changes. Package the verified candidate, register exact files for mutual-TLS bulk receiving, transfer and verify, then use standard LIPS prepare/deploy and live acceptance. Bulk receiver currently not listening; Dell administrator setup is required. Do not use iWSET JEA or SSH upload as a fallback. No database migration/data import is included.


## Current status (2026-09-29, supplier natural search candidate)

Explicit handling phrases (e.g. ファインズ取り扱い, ファインズ取扱の, 取扱先は日本リカー) resolve registered supplier names/codes to SupplierId SQL filters. Product/producer keywords alone do not become supplier constraints. Multiple or ambiguous supplier matches fail explicitly. Existing type/price/vintage/availability filters remain conjunctive; previous vintage normalization is retained. No DB/schema changes.

Validation: 181 .NET tests passed, 7 environment tests skipped; all 16 supplier tests including isolated PostgreSQL integration passed. Edge verified API results, interpreted supplier condition, UI and filtered CSV. Not deployed.

## Next work (supplier search)

See LIPS/docs/SUPPLIER-NATURAL-SEARCH.md and docs/VINTAGE-NORMALIZATION.md. Both changes are local candidates; await an explicit deployment instruction, then re-inspect current production and follow guarded release steps.


## Current status (2026-09-29, vintage notation candidate)

Year-only brackets are normalized for search/options/display/CSV: 2021, [2021], (2021) -> 2021. NV/N.V/N.V. (including case/fullwidth variations) -> NV. MV, blends, age statements, 0, dash and unknown remain distinct. Supplier VintageRaw, original files and evidence are preserved. New VintageLabel is display-only. Matching raw variants are selected in SQL before the result limit. No schema or data migration.

Verified against inspected production source f226f0a98d74253af4100e35d0f4d43ae898cb9f using an isolated candidate: 166 unit tests passed (6 environment tests skipped); all 22 vintage tests including PostgreSQL integration passed; Edge API/options/display/sort/CSV/original-evidence checks passed. This is a local implementation, not a production deployment.

## Next work (vintage notation)

See LIPS/docs/VINTAGE-NORMALIZATION.md. Production release requires an explicit instruction for this change, a fresh inspect and standard transfer/deployment gates. Do not deploy the entire development tree containing other work.


## Current status: Fines special purchase price (2026-09-25)

Candidate based on inspected production source 8f86d62e394dea395ecdd127ea8fa1611b7a989d. Adds a nullable SpecialPurchasePrice API field from explicitly versioned FINES current-source evidence, a separate search column/detail item, and a separate CSV column. Reference-retail filtering and prices remain unchanged. No schema/migration changes. Ordinary or malformed evidence returns no special price. A later ordinary inventory document supersedes current-source special price; historical evidence remains available in the database.

Local checks: 145 .NET tests passed (5 unrelated DB/file tests skipped), 331 actual products imported twice with DB/evidence/stock/retail verification, full API comparison and browser/CSV decimal-preservation checks. Data package preserves two originals, applies 322 main rows and 9 extra rows, with extra precedence for 3 duplicates and Open Burgundy excluded per user confirmation.

Next: register the exact package for mutual-TLS bulk receiving on Dell, transfer and verify size/hash, then use the bundled guarded manual data import and standard LIPS prepare/deploy helpers. Do not report production applied before live verification. See LIPS/docs/FINES-SPECIAL-20260924.md.

## Current status: search/CSV refactor (2026-09-22)

Production CSV release e66ac013 was verified and is the baseline of this snapshot. Search request state, result rendering and download handling are now separate named functions. CSV serialization is formatted and structured without changing output (54 edge cases byte-identical). Browser tests verify pagination, sorting, invalid prices, download failure/retry, empty/error responses and cap messages. Only catalog.js and catalog-csv.js runtime files change; backend/schema/deployment infrastructure remain unchanged.

## Next work: refactor production verification

Use the existing dedicated deployment v2 inspect/upload/prepare/deploy pipeline. The user authorized deployment after refactoring. Confirm health, product count, actual CSV download and natural-search regression on the deployed package. See LIPS/docs/CODEX-DEPLOY-V2.md for release evidence and the latest exact package.

Updated: 2026-09-17

## Mission

仕入先カタログ・価格表・在庫表と、酒道楽の商品マスターを横断検索できる独立システムのPoCを完成させる。iWSET・酒道楽とはDBを共有せず、将来APIで疎結合連携する。

## Read first

1. `AGENTS.md` — 実装上の不変条件
2. `docs/ARCHITECTURE.md` — 4層境界とIdentity model
3. `docs/POC-SCOPE.md` — PoC受入条件
4. `docs/WINE-TO-STYLE-ANALYSIS.md` — 実ファイルから確定した取込仕様
5. `docs/IMPLEMENTATION-BACKLOG.md` — 実装順
6. `docs/OPEN-QUESTIONS.md` — 実データ接続前の不足事項

## Fixed decisions

- Stack: .NET 10 / ASP.NET Core / EF Core / PostgreSQL 17 / pgvector
- Data boundary: SOURCE → EXTRACTION → CANONICAL → SEARCH
- Parser First。決定論的に取得できる値にAIを使わない。
- `Product`はCanonical商品、`SupplierProduct`は仕入先SKU、`InternalProductRecord`は酒道楽商品。
- 酒道楽商品はCanonical Productへ任意Link。未Linkでも検索対象から落とさない。
- 酒道楽はread-only source。直接join・書戻し禁止。
- 数値・価格・在庫・容量・日付はSQL filter。意味・味わい・用途はFTS/vector ranking。
- 在庫空欄は`UNKNOWN`であり、`OUT_OF_STOCK`ではない。
- 税区分は「税込」の明記がある場合だけtrue。記載がなければ税抜(false)。
- 同一商品判定はFalse Positive回避を優先し、初期は自動Linkしない。

## Verified Wine to Style facts

Source: `在庫リスト 2026.09.01.xlsx`

- Sheet `ワインリスト`、header row 2、data rows 3–1063
- 1,061商品、SKU重複0
- IN_STOCK 655、LOW_STOCK 399、UNKNOWN 7
- 数量在庫329、オープン価格22、複数JAN 19、JAN欠損扱い208
- `OK`→IN_STOCK、`少量`→LOW_STOCK、数値→Quantity、空欄→UNKNOWN
- `希望小売`はREFERENCE_RETAIL。税込表記がないためTaxIncluded=false
- NV/MVは`Vintage=null`かつ`VintageRaw`を保持
- JAN=`0`は欠損。瓶/箱の複数JANは配列＋Raw Evidence
- 元Excelはプロジェクトへ複製しない

Aggregate verification is stored in `artifacts/wine-to-style-import-report.json`.

## Current status (2026-09-21)

自由文検索v5の配布候補を作成。価格帯、漢数字・万/千混在、予算、未満/超、容量の助詞、国/主要産地、短縮色名、発泡、在庫・依頼表現に対応。自動140件成功・5件skip（自然文関連110件成功）、実データAPI/Edge75項目成功。Dell用の同義表現32件照合スクリプトもローカル検証済み。DB/schema変更なし。現在の本番33459fd7は初版のみ。v5のLinuxコンテナ実行・本番反映はDellビルドと管理者登録後に実施。

## Previous status (2026-09-17)

利用者から動作確認完了の回答あり。複数キーワードAND検索、英字大小を区別しない照合、生産者が異なる候補の除外、候補表示後の資料更新検知を追加。自動35件（skip 0）、ブラウザ118項目（既存82・キーワード5・取込/紐付け31）を検証。DBスキーマ変更なし。
本番Docker環境構築は利用者指定の「本番環境構築」セッションへ委譲済み。このタスクでは本番変更を行わない。利用者確認済み内容のmaster同期・ローカルビルド・正式パッケージ作成を共通ルールに従って進める。実行証跡はチャットoutputsのリリース確認記録を参照。


検索結果を8項目の別カラムに分離し、見出しクリックの昇降順ソートを追加。取得済み全件を並べ替えてからページ分割。自動48＋既存UI34項目成功。詳細: docs/LOCAL-DEMO-GUIDE.md。

Dell Docker準備: deployment/に実機readiness、Hyper-V VM作成（既定plan）、Ubuntu Docker bootstrap、Dockerfile/Compose、SHA承認付きCodex配布経路を追加。CLI schema-checkで未適用/未知migrationを拒否。DB変更は手動境界。docs/DELL-DOCKER-GUIDE.md参照。
OS適合は既存Windows11 Pro記録、現在のJEA状態読取成功。RAM/仮想化/Hyper-VとDocker実起動は未確認。**準備済みだがDell稼働/デプロイ完了ではない**。実機readinessと環境構築の継続は委譲先で管理する。本番未変更。


ローカル確認マイルストーン完了。Excel/CSV取込UI、UTF-8/CP932標準CSV、匿名酒道楽fixture4件の同期、未Linkを含む横断検索、同一名称/容量/Vintage候補の手動Link/保留/解除、共通商品表示、原本/履歴、専用フォルダー監視（最大3回＋手動再試行）を追加。
従来の商品検索（容量・Vintage・税抜価格上下限）とWine to Style実1061件は維持。
build警告0・エラー0、自動28件・ブラウザ62項目成功。新Migration SQL適用/再適用成功・モデル差分なし。利用者用DBに架空検証データは投入せず、専用複製DBで動作検証。
操作手順: docs/WORKFLOWS-GUIDE.md。検証後はWeb/DB停止。本番デプロイなし。master反映は上記の利用者確認後の工程へ移行。
共有利用の認証、酒道楽実接続、日本語FTS/Embedding、汎用Excel/PDF/画像は未実装。意味検索を実装済みと扱わない。

## Next work — execute in this order

LIPS-natural-search-Dell-v5.zipをDellでprepare.sh実行、検証結果とSHAを管理者が確認してapprove後、既に受けたユーザーの本番展開指示に従い専用deploy経路で適用。本番の件数と同義表現32件・画面を再検証。税込/仕入価格・OR/除外・意味検索は対象外。v3/v4は未配布候補として置き換える。

0. 本番Dockerの環境構築・Linux image build・運用確認は「本番環境構築」セッションで継続。このタスクの対象外。
1. 利用者操作確認は完了。共有利用者と認証方式の確定後、認証・権限を実装。
2. 酒道楽の取得方法/schema/資格情報を確認し、read-only adapter・差分watermark・終売方針を実装。現fixture同期と置換可能な境界を維持。
3. 実商品ペアで候補精度を評価し、JANと生産者別名の扱いを確定（同言語の生産者不一致除外は実装済み）。自動Linkはしない。
4. 日本語FTSとgolden query評価。Embeddingは提供元/資格情報/外部送信範囲確定後に別Migrationで実装。
5. 汎用Excel、PDF/画像の抽出は後続。監視の常駐運用/認証監査は共有利用段階。

## 履歴 / Next work — source review filter 2026-10-12
SOURCE-REVIEW-TABLEは通常未回答・保留、全件で回答済み再編集・履歴表示。未保存入力を切替時に保持。隔離Edge表示12＋保存回帰17合格。原本・本人保存領域・本番変更なし。本人UI取得timeoutのため実回答件数/内容未確認、回答JSONの提供で確認可能。BASE契約・クラウド実行環境・未決業務判断は引き続き残項目。

## 履歴 / Next work — proposal and reusable rule preview 2026-10-12
独立工程として日付候補/原典根拠UIと再利用条件プレビューを実装。P14s localhost56005/date-proposal-review.html および /rule-preview.html、合成のみ、DB書込/BASE接続なし。Web build0警告0エラー、Edge16+11、ルール単体14合格。詳細docs/PROPOSAL-RULE-PREVIEW.md。既存Web維持。実回答取得、ルール永続化/CAS・疑義順次導線・実帳票適用は未完。BASE/クラウド/業務判断依存を保持。本番禁止継続。

## 履歴 / Next work — next unresolved review 2026-10-12
資料単位の次未解決導線を追加。未保存/保存不明/Unknown/Runningをガード、移動前にhash/版を照合、保存とresumeを分離。Edge12＋既存56003/専用DBの実ナビゲーション合格（answer/resume書込0）。資料内複数疑義・再利用ルール永続化/実回答取得・BASE/クラウドは引き続き未完。

## Current status — combined original and answer 2026-10-12
本人Slack指定で原典画像と確認結果入力/保存を同一画面へ統合。PC左右/狭い画面上下、原典常時保持、入力保持・履歴・保存とresume分離維持。56003既存合成資料で表示可能。Edge17＋次資料回帰12合格、実画像読取/保存はintercept合成応答、元PDF不変・実DB書込なし。実回答取得/ルール永続化/資料内疑義キュー/BASE/クラウド残。


## 履歴 / Next work — 2026-10-12 回答再利用の実接続

以前の「ルール永続化・資料内疑義順次処理は未実装」は現時点ではLUC容量単位について解消済み。392484bで保存原本の全対象行の最新回答・同じ単位が揃った場合だけAPI再利用登録を許可する。原典画像と質問の同一画面、順次行選択、回答保存、明示的ルール登録、明示的取込再開、次回同じ帳票/対象列への適用を接続。581da8cで実Edge/API/復元合成DB29項目とCompleted重複receipt4項目の受入を記録。56008稼働、手順はdocs/MAPPING-SEQUENCE-REVIEW.md。保存結果不明停止・原本hash・履歴を維持。

目標全体は継続。実取込の再利用はLUC容量単位だけ。列意味/産地略称/商品別名の実取込接続、他帳票の業務未決事項、本人の原典確認表の実回答JSON、Linux描画のLIPS統合、現行本番基点・移行/復旧/gateway/PC受入が残る。汎用プレビューや担当の独立検証を実取込/配備完了と扱わない。本人は本番適用までの直接指示済みだが、現在は隔離作業のみ実施。既存許可経路復旧と内部ソース4ファイル宛先別承認の未解決依存を保持する。

## 履歴 / Next work — reusable rule cancellation
取消管理画面と履歴GET/取消POSTを接続。56010/intake-rules.html、理由を別項目にしてappend-only保存、scope/CAS/operation照合・応答不明時GET復帰。Store11/画面11/実UI-API-DB6合格。取消後の次資料は質問に戻り商品数不変。取消管理画面の残項目は解消。業務未決資料/他種別の実取込/Linux統合/本番基点等は引き続き残る。

## 履歴 / Next work — 受領済み本人回答の確認・訂正

2026-10-12、本人が送信済みと指摘したDownloadsの `LIPS-source-review-answers.json` と `LIPS-source-review-answers (1).json`（各8617bytes）を読み、両者同一バイト・SOURCE-REVIEW-TABLEの全25項目ID/binding一致を検証した。24回答・1保留、24項目第1版／1項目第2版（元第1版履歴あり）。以後「実回答JSON未受領」として停止しない。原JSON/確認表を変更せず、artifact `received-source-answers-verification.json` と `RECEIVED-SOURCE-ANSWERS-20261012.md` に根拠・対応を記録。これは回答受領・対応確認であり、24回答の業務DB適用済みではない。

AOC `source-17a7690c75c1a61f` は「AOCが省略されているが同一商品」と本人明記。対象はオルヴォー新着66行／PDF15頁の Château Cesseras Cuvée Olric (AOC) Minervois 2024、PDF容量750mL。重複質問せずこの原典ペアに限定して次工程へ進める。ラヴィネ容量列L、地域略号10件、個別AOP/IGP/VT対応等も回答原文をbinding別に確認できる。自由文回答を一般ルールとして無条件適用しない。

保留1件は `source-d4342b06f79efb64`、オルヴォー定番品95行／スペイン9行の在庫相違。「該当箇所が不明なため保留とした」であり採用しない。LUC予約回答はタイトル/商品内容/コメント位置の説明で、予約ケース価格の単位・入数・条件への直接回答はまだない。ラヴィネ重複コード2行のidentityも、この回答集合だけで統合しない。

Linux renderer4ファイルは今回の本人直接承認によりリモート担当へ送信成功・受領報告済み（親担当連携情報）。従来の「4ファイル送信承認待ち」は解消。hash照合・統合試験は進行中であり、この記録ではLinux実取込統合完了・本番適用とは扱わない。

## 2026-10-12 残範囲監査
4d81ea9時点でLUC容量の目標経路と取消管理の必須工程は接続・受入済み。他種別は実回答JSON、価格/容量単位・地域略称・商品identity等の未決事項に依存し、値を推測して適用しない。
オルヴォーAOC省略限定1件はPENDING-SOURCE-REVIEW/確認表の「確定」とORVEAUX-PUBLIC-CROSSCHECKの「本人回答原文未確認」が不一致。Slack AOC検索で根拠原文を取得できず、本人へ限定1件の扱いを問い合わせた。回答までは依存実装/採用を止める。在庫相違商品の既決保留や日付は再質問しない。
Linux担当のlive状態を取得し、独立試験完了・4ファイル未受領・blockedを確認。相互ポーリングを進捗として継続しない。heartbeatは4d81ea9/56010/現在の未解決依存へ更新した。

## 2026-10-12 ラヴィネ回答経路とLinux境界修正
ラヴィネ厳密11列Parser/容量Hと地域C原略号別質問/再利用照合/行保留を接続。明示受領済みLと10地域対応だけを扱い、未知地域・重複SKU・納価の単位未確定は保留。納価を希望小売へ転記しない。初回質問→全回答→Rows保留でstore書込0。LaVinee20＋既存容量/Publication22＝42合格、Web build0警告0エラー。画面group19/既存LUC17合格。新runtime lavinee-review-runtimeはbuildのみ、起動・実DB受入は未実施。既存Web維持。
Linuxbackendは非選択sheet除去後range再取得と未対応境界の明示拒否を取り込み。SHA2a385f8cef3df52cd7cc93a830504621aec7c4ad91a5dd4e94209069dd6d4d40はLIQUOR-BASE合格候補一致。第2sheet/隣接結合/並行2process/timeout自身group停止/原典不変を確認。固定profileを付ける既存wrapperは並行競合するため変更せず、検証済み本体パスとbackend一意profileが必要。実暗号化は非ZIP代用品による拒否確認まで、Web接続/実原典全受入未完。
Next: 受領済み回答JSONのbinding/原典hash照合から永続回答履歴へ接続し、本人に再入力させず明示再開/限定再利用を実Web/隔離DBで検証。列意味/別名等の他帳票は継続。現時点のParser/UI試験を目標全体完了と扱わない。本番操作なし。

## 2026-10-12 受領済み回答の実隔離保存
受領JSONは不適合ではない。tools/source-answers CLIでSHA・2項目binding/版/文言・原典SHA・帳票fingerprint・質問scope/choicesを照合し、ラヴィネ1261行2522質問のうち2452回答を隔離DBへ保存、GETのHuman/mapping履歴とversion4を検証。合成HTTP12合格。未知地域70は推測せずHeld維持、商品数6不変、取込再開/再利用登録は未実施。proof received-answers-save-proof.json。review43db0b9a-48a7-4f11-bd5b-ba61f674af56。
56011は原典Python設定漏れを実画面で発見。既存Webを停止せず同runtimeの56012に既存Pythonと専用cacheを指定して起動（PID2712）。56012/workspace.html HTTP200、同隔離DB lips_migration_restore_20261012@55993。原典画像/実画面の確認は進行中。未知の地域+種類表記70行の分解方針は本人へ追加質問、既に回答済み10略号を再質問していない。
56012実Edge読取14項目合格。Excel原典1640x1159画像と選択row2一致、容量/地域scope、desktop/mobileはみ出し修正、変更POST0、review版/hash/商品数不変。proof lavinee-live-readonly-56012/proof.json。手順docs/RECEIVED-ANSWERS-REVIEW.md。既存キャッシュ表示1.49秒を初回ネイティブ描画時間と混同しない。
再利用実接続: 56012で2452保存回答の11グループを明示登録、receipt/historyGET一致。次回形式合成2件で既知容量/地域は質問不要、未知XXのみMapping、既知のみは価格単位Rows保留。商品6不変、元review版4不変。未知70と価格単位は本人回答待ち。既存資料の再開・商品登録は未実施。Linuxdispatcherと対象行スクロール改善は担当作業中。
56012対象行追従: highlightBox検証後、ズーム/Excel行切替で縦表示外の行を戻し、手動倍率/横位置/未保存回答を保持。実原典14項目成功、POST0、版4商品6不変。lavinee-zoom-target-56012参照。従来の手動上端スクロール残項目はこの操作範囲で解消。
Linuxdispatcher: render.pyから明示opt-in時だけLibreOffice backendへ接続、実行ファイル絶対パス検証/60秒worker、Windows/PDF維持。分岐9合格、Web別output build0警告0エラー、backend配布csproj追加。本人承認済み同4ファイルをremote担当へ更新送信、実CLI受入待ち。送信と受入完了を混同しない。新test_render_dispatch.pyはローカル限定。

## 2026-10-12 原典1ペア・Linux dispatcherの追加照合
Linux担当でdispatcher info/render第2sheet行6が実受入合格。4ファイルsize/SHA一致、1page/対象文字/強調box/原本不変/残存processなし。proof SHA79081a37dcc94b4f6e6e98823122deb5963c1dcbd12a8b72e8f80c190a11ab08。Linux側に.NET10/LIPS Web一式/専用DBはread-only確認範囲で見つからず、HTTP Web受入は未完。新規設置/権限/ネットワーク変更はしていない。
AOC1ペアの現原本SHA・値を再照合。PDF実名Orlic、Excel Olricで『AOC差だけ』という旧記録は不正確。当該ペアを同一商品とする本人回答は保持し再質問しない。別VT2023/70本を混ぜず、名称原文保持/通常価格と12本条件価格を分離する。専用supplier/parserと複数原典Evidence、恒久商品キー接続は未実装。artifact ORVEAUX-AOC-PAIR-READONLY.md参照。
ラヴィネJ納価単位を明示Mapping質問へ接続中。本人への実単位質問は未回答。PerBottleを選んだ場合のみ採用する実装を合成専用空DB lips_price_e2e_20261012@55993/Web56013で試験し、実回答DBのルールへ混ぜない。元56012実回答は維持。

## 2026-10-12 旧保留資料への質問追加
旧Mapping Held資料は未回答があると再判定できず、新価格質問へ到達しない欠陥を修正。明示refresh-questionsは原本hash/版CAS/操作ID照合、厳密一致回答保持、旧回答履歴保持、業務保存なし。変更なしはno-op、Unknown/Running等は禁止。辞書化で回答×質問の反復serializeを除去。Store10+専用DB1、UI35+既存19、実Edge14+16合格。56014は最終runtime-v2/PID12732、専用合成DB lips_question_refresh_20261012、旧→新review6530aa42-e2c0-44d9-aa85-2bcdf3a025acは版5/6質問/3回答/商品0。docs/QUESTION-REFRESH-REVIEW.md。実資料56012は未更新。Linux実HTTP経路はIntakeReviews原本取得に依存、全業務DBは不要。最小配布候補/導入承認案は未生成、docs/LINUX-WEB-ACCEPTANCE-DEPENDENCIES.md参照。実資料の未確定地域/価格回答、商品別名接続、Linux HTTP受入が残る。本番操作なし。

## 2026-10-12 LaVinee明示部分保存
正常行のみ保存・残行再開・既確定件数保持・重複receipt今回追加0を実装。専用合成Web56015/DB lips_partial_lavinee_20261012。手順と範囲はdocs/LAVINEE-PARTIAL-REVIEW.md。実資料の価格単位/未知地域判断、本番・Linux HTTPは未完。

## 部分保存から次回再利用の実通し検証
56015完了資料の全回答から4再利用設定を公開し翌日原典へ適用。Edge/API35＋DB/HTTP10合格。対象外地域/列構成変更は業務保存なし。docs/PARTIAL-REUSE-REVIEW.md参照。Completed説明の矛盾と対応取引先案内を修正。実資料の未確定判断は未適用。

## 再利用状態の再読込表示
GETした最新規則で登録済み/取消/相違/未確認を表示、同じ有効回答の重複POSTを抑止。直前GET/CAS/operation照合とUnknown停止維持。docs/REUSE-STATE-REVIEW.md参照。実DB書込なしで検証。

## 複数取引先の親フォルダー受入
56015実Edge/API27合格。5ファイル成功1/保留2/未対応1/失敗1、LAルールLUC非流用、同原本再選択追加0、LUC原典+容量質問表示。docs/MULTI-SUPPLIER-REVIEW.md参照。独立DB価格再検査は反復していない。

## 履歴 / Next work（2026-10-13 Windows Excel原典可読性）
横長Excelの低解像度を元PDFのclip高精細描画へ修正。外側枠のみで対象行pixels保持、16k/8k/32Mpx上限、Windows XLSX cache v2-native-density。既存9+上限3、合成LUC/LaVinee hash/pixel照合、56016実GET Edge9合格/POST0。docs/ORIGINAL-RENDER-READABILITY.md参照。親build0警告0エラー/独立レビュー指摘なし。本番適用とLinux同等可読性受入は未実施。既存Web/DB変更なし。

## 2026-10-13 承認済みAOC1組の実UI接続
本人の内部ID承認を受け、受付履歴から固定2原典・受領回答JSON・明示保存へ接続。56018/orveaux-pair.html?review=244f9b09-86f2-4de4-a861-6444c43f94a3、専用DB lips_aoc_flow_20261013@55993、PID72348。backend6/build0/合成UI18/実保存18合格。初回1・再登録0・総商品1、在庫60非加算・4価格未採用・原文保持・原本SHA不変・両レビューHeld維持。CSPレイアウト修正後左右表示とPDF2024手動枠を目視。docs/ORVEAUX-PAIR-UI-REVIEW.md参照。旧環境保持。本番未適用。LaVinee実地域70/納価単位の判断・一般ORVEAUX全行・LinuxHTTP等はこの1組の完了とは別。

## P14s操作目標の統合受入
56019/workspace.html、DB lips_goal_integrated_20261013@55993、PID74784。56015合成複製＋最新schema/Review/Rules/Pair有効。実UI25＋共存GET7合格、LUC2回答保存→明示再開2商品→scope1公開→翌日新値再利用、総6・旧LA4規則不変・原本SHA不変・旧環境保持。docs/GOAL-ACCEPTANCE.mdで要件照合済み。P14s操作目標は受入完了。実地域70/納価単位等の業務判断・一般未対応形式・LinuxHTTP/本番は別未完として保持。

## 2026-10-13 Linux業務受入候補（未配布・未起動）
固定b1de349再publish、105file SHA/版4DLL一致、DB5SQL/25table/限定Web GRANT、合成10件/期待値/UI matrixをlinux-business-candidate-20261013へ準備。Linux原典HTTP受入は完了済み。次は候補ZIP受渡し・新専用DB/role・合成POST/Firefox headless受入の具体的追加承認。旧P14s/本番変更なし。

## 2026-10-13 空の保護要素限定修正（本人直接承認）
workbookProtectionの完全空要素だけ許可。属性false含む/子/comment/PI/本文空白含む/後続保護要素は拒否。新13＋既存2回帰合格・入力SHA不変。数式/暗号化/シート保護拒否維持。libreoffice_backend.pyとtest_empty_workbook_protection.py。新固定v3はb1de349の4DLL据置＋PythonoverlayのSHAをmanifestへ明記、fixture/SQL/GRANT不変。BASE転送SHA一致、IntakeRules__Enabled=trueを専用Webだけ有効にして新run受入進行中。旧v2/旧run証跡保持、UI成功未確定。Next: BASE A/B実証跡/停止残存0を確認して結果報告。本番/既存DB変更なし。

## 2026-10-13 Linux隔離業務/UI受入完了
空保護タグ限定修正13＋既存2合格。通常SKU原典価格成分詳細を表示するcatalog.js最小修正（合成Edge9合格）。固定v3でA業務/再利用/境界/Unknown確認、B run010取消→次資料Held Passed。v4 UI差分run012はGET限定・server read-only・全25table前後同一・業務POST0で納価1735/1936表示/原典確認Passed。全所有process/listener0。FINAL-ACCEPTANCE-PROOF SHA928d5826afc782803b222757fe6204bbf51ec50174b876d44b134d65251ab390を親取得照合・PNG目視。旧A最終UI失敗は保持し合成証跡で範囲完了。詳細linux-business-candidate-20261013/ACCEPTANCE-COMPLETED.md。Next: 今回BASE待ちは解消、完了試験反復不要。Linux実原本/実AI/AOC等の別範囲と本番は未実施。P14s既存環境未変更。
