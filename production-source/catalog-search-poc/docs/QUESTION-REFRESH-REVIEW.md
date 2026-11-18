# 保留資料の質問更新

確認PC: THINKPAD-P14S
URL: http://127.0.0.1:56014/intake-review.html?id=6530aa42-e2c0-44d9-aa85-2bcdf3a025ac
専用合成DB: lips_question_refresh_20261012@55993。実資料を複製していません。

旧版で保留になった資料にも、新しく追加された質問を明示的に取り込めます。原典hash・回答版を照合し、現在の質問と完全一致する保存回答を保持します。一致しない回答も履歴から消しません。質問更新は商品取込・再利用ルール登録を実行しません。

1. 上記URLで原典画像と確認項目を確認します。旧4問から納価単位2問が追加され、6問になっています。
2. 保存済み回答3件と未回答の地域を確認します。合成地域XXは未確定のままです。
3. 「質問を更新」を押すと、この現行版では変更なしになり、版5と履歴が増えません。
4. 回答を編集すると、未保存中は質問更新を押せません。保存・再判定は別の明示操作です。

通信結果が不明なら自動再送せず、GET履歴の操作IDで照合できるまで保存・再開・次資料への移動を止めます。Unknown/Running/Completedには質問更新できません。

検証: Store単体10＋専用DB1成功。UI合成35＋既存19成功。実Edge/APIは変更なし14、旧4問→新6問16成功。保存3件・原本不変・商品0を確認。質問照合をID辞書化した後、単体10と最終runtimeのhealth/GET/変更なしPOST/GETを確認。build0警告0エラー。
証拠: question-refresh-store-proof.json、refresh-questions-real-56014-upgrade/proof.json、question-refresh-final-runtime-proof.json（いずれもartifacts/folder-ui-20261011配下）。既存56012実資料・56013合成経路を変更せず、本番操作なし。
