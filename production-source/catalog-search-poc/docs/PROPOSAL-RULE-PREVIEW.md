# 日付候補・取引先別ルールの隔離確認

THINKPAD-P14Sで稼働。合成資料のみ。DB書込み、BASE実接続、実回答登録、業務再判定・取込は行わない。

## 日付候補

http://127.0.0.1:56005/date-proposal-review.html

1. 「合成の候補を表示」を押す。
2. 日付2026-10-01、原典位置「合成原典!A1」、記載「在庫日2026年10月1日」を確認する。
3. 「別の応答を確認」で準備画面へ戻る。
4. BASE担当との合成契約確認には「合成確認用の要求JSONを保存」を使い、返された応答JSONを選択して検証する。

固定の合成requestId/hash/回答版だけを受け付ける。未知の根拠、不一致の版・hash、不正JSON・追加属性、上限超過は拒否し前の候補を消す。Unknownは候補なし・本人判断必要と表示。合格は業務承認ではない。回答保存や取込再開ボタンは設けない。

## 再利用条件

http://127.0.0.1:56005/rule-preview.html

「同じ取引先・同じ形式」を選び「適用条件を確認」すると容量単位Lの候補。「別の取引先」「列構造が変わった」「取消済み」「競合」は保留。元回答版と合成根拠を表示する。

ApplicationのIntakeReusableRulesは取引先・資料種別・列fingerprint・必要な商品Identity/VTを完全一致で比較する純粋関数。変更は新しい版、取消済みの旧版へのfallbackなし。同一履歴の重複受領と同版矛盾を区別する。実値の日付・価格・在庫型は拒否する。商品名や地域の対応は本人の明示確認が前提。

## 検証と限界

Webビルド0警告0エラー。日付候補ブラウザー16項目、ルールブラウザー11項目、ルール単体14件合格。artifact直下verify-date-proposal-ui.cjs、verify-rule-preview.cjsとproof JSONに記録。検証は合成データのみ。

DateProposalReview:Enabled=trueでのみAPIを登録。既存loopback・Host・Origin・action header制約の下で動作。DB保存・業務適用なし。既存Webを維持するためproposal-rules-runtimeへビルド、port56005/PIDはproposal-rules-web.pid。

残項目：本人回答JSON/UI内容確認、実ルールの保存・中断再開・CAS/冪等登録、疑義一覧→原典→型付き回答→次項目への接続、適用条件の実帳票fingerprint生成、業務再判定への接続。BASE起動契約・クラウド.NET10/PostgreSQL17・Linux Excel描画は別依存。公開/本番操作は未実施。
