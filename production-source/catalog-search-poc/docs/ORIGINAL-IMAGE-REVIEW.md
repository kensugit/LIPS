## 画面の分かりにくさを修正（2026-10-12）

本人のスクリーンショットは空の1行目を指定していたため、黄色い枠しか出ていなかった。初期行を固定1から「最初に文字・数値がある可視行」へ修正し、自動表示する。初期表示は対象を判定したものではないため強調OFF。空行指定時は内容のある開始行を案内し、空枠を出さない。

今回の資料は画像表示の確認用サンプルで、実際の仕入先在庫ではない。画像だけ確認するリンクはこちら:
http://127.0.0.1:56001/intake-review.html?id=f2036333-4d2c-4815-b226-346dd13545d4&view=original&sheet=1&row=95

開くと95行が自動表示される。この画面は日付回答・商品の取込を求めない。通常の回答画面では「この在庫リストは、いつ時点の情報ですか？」と目的を明示し、分からない場合は保留を選ぶ手順を説明する。

検証: ブラウザー14項目、空行/初期行の追加回帰1試験合格。専用Excelプロセスの終了も確認。証拠: original-clear-verification.json / original-clear-review.png。旧環境・商品DB・本番は変更していない。
# 原典画像の本人確認（2026-10-12）

確認PC: THINKPAD-P14S。新しい隔離Web: http://127.0.0.1:56001/workspace.html
旧56000・55994～55999は保持。DBは合成専用 lips_questions_20261012 / localhost55993。本番操作なし。

## 確認手順

合成原典4件は既に受付済みです。再アップロード不要です。

1. Excel: http://127.0.0.1:56001/intake-review.html?id=f2036333-4d2c-4815-b226-346dd13545d4
   「原典レイアウト」シート、行番号95を入力して「原典画像を表示」。原本の青い見出し・結合セル・罫線・赤い文字・図形を保持し、95行目だけ黄色と赤枠で強調します。「対象行の強調を表示」を外すと原典の画像を確認できます。
2. PDF: http://127.0.0.1:56001/intake-review.html?id=90df181c-7a42-4e40-9ee1-d00c3259848d
   PDFの元ページ画像が表示されます。行候補「NATIVE-95…」を選択して対象位置の枠を確認してください。候補はPDF文字の座標に基づくもので、業務上の対象を自動決定しません。画像上でドラッグして範囲を選ぶこともできます。
3. スキャンPDF: http://127.0.0.1:56001/intake-review.html?id=d7a05091-c0fa-4c42-8c68-c05f0156ca73
   テキスト候補がない原典もページ画像を表示します。対象行を画像上でドラッグすると、その範囲を強調します。
4. 複数ページ・回転PDF: http://127.0.0.1:56001/intake-review.html?id=fcdc5733-ea2f-40b6-8aa1-e72e33f59894
   ページ2を指定して「原典画像を表示」。元PDFの回転したページを確認できます。
5. 「確認して回答入力へ」→「保留を継続する」→判断根拠入力→内容確認→回答確定。シート/行またはPDFページ/画像座標/選択方法が回答履歴に残ります。合成レイアウト資料なので日付確定による商品取込は行わないでください。

画像部分は縦横スクロールでき、回答へのボタンをページ最下部まで追いかける必要がありません。前回の4段階画面を維持しています。

## 実装方式

- Excel .xlsx: 元bytesを別の一時ファイルへコピーし、独立Excel16プロセスで読み取り専用Open、ExportAsFixedFormatでPDF出力後に画像化。選択行の前後3行・使用列のネイティブ印刷画像。書式・セル幅・結合・図形を保持し、印刷範囲と倍率のみ表示用に設定。原本Saveなし。強調はメモリー上の一時図形で描画し、強調なし版も保持。
- PDF: 元ページをPDFiumで直接ラスタライズ。再組版・保存値からの描き直しはしない。画面上の透明な強調枠は原典の画像と分離。
- 表示操作は原本hashを照合。画像cacheはhash＋描画方式版＋シート/行/ページで分離。UIの選択位置を回答履歴へ保存。画像化だけで商品DBへ書き込まない。
- Excelマクロ・外部接続・リンク更新を実行しない。疑わしいactive-content付きxlsxはネイティブOpen前に拒否。原本は書換えず、既存Excelプロセスを使わない。描画処理は直列化・時間制限し、失敗時に自動再送しない。
- 原典そのものの描画を提供できない形式で、保存値描画に黙って代替しない。

## 対応範囲・残項目

.xlsx と .pdf対応。旧.xls、暗号化・外部接続付きExcel、非表示行、200列を超えるシートは原典ダウンロードへ誘導。Excel利用可能なWindowsローカル環境で検証済み。Linux/Docker用のExcel代替レンダラー・本番配置は未実施です。
PDF行候補はテキスト位置のまとまりで、複数行の商品・複雑な段組みは画像で範囲を選択してください。スキャンPDFのOCRによる自動行特定は未実装です。
今回の4件は合成原典。実原典全形式の受入・業務疑義との対象位置自動接続は別の残項目です。PDFの原典表示対応とPDFの商品取込対応は別です。

## 検証証拠

.NET10 build警告0/エラー0。Edge/API21項目＋最終UI4項目合格。描画回帰8試験合格。Excel95行目のネイティブ書式・強調差分、原典hash不変、PDF元ページのレンダリングと画像画素一致、回転ページの座標、スキャン、範囲外拒否、active-content拒否を検証。
合成商品数は試験前後とも3件。原典参照・保留回答は商品登録を増やしていません。

artifact root: LIPS/artifacts/folder-ui-20261011
- native-render-verification.json / verify-native-render.cjs
- native-excel-ui.png / native-pdf-ui.png / native-scan-ui.png / native-mobile-ui.png
- original-render-fixtures/LUC（合成4原典）
- questions-native-runtime / questions-native-web.pid / original-render-cache

回帰試験: 環境変数 ORIGINAL_RENDER_FIXTURES に合成LUCフォルダーを指定し、tools/original-render/test_render.py をbundled Pythonで実行。既存実原典・本番DBには実行しません。
ランタイムの OriginalRender__Python はbundled Python絶対パス、OriginalRender__Cache は上記cacheに設定。Python依存はpypdfium2/Pillow（既存bundled runtime）。外部送信・追加インストールなし。

方式の一次資料: [Excel Workbooks.Open](https://learn.microsoft.com/en-us/office/vba/api/Excel.Workbooks.Open)、[PDFium Python API](https://pypdfium2.readthedocs.io/en/stable/python_api.html)。
