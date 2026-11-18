# 原典画像の拡大確認

## 実PDF追加検証（2026-10-12）

旧56002を保持し、新しい隔離Web56003で実PDFファイル（合成内容）をPDFiumで描画して14項目合格。URL:
http://127.0.0.1:56003/intake-review.html?id=d8ee3030-fff8-4ad9-8fa7-195ebb3a8d23

原寸/125%拡大/手動範囲指定/上下左右スクロール/回答画面往復の倍率・範囲・位置・メモ保持/横幅合わせ/原本hash不変/商品数2件不変/ブラウザー例外なしを確認。回答確定は不要。実業務PDFの全形式受入ではない。証拠real-pdf-zoom-proof.json、verify-real-pdf-zoom.cjs。

cloud start.shのdotnet runはContentRootがWebプロジェクトとなり、コピー先binのtoolsと一致しなかった。参照基準をAppContext.BaseDirectoryへ修正した。Web.csprojのbuild/publishコピー契約を維持。ContentRoot直下scriptなし、bin側scriptありの条件でPDF描画成功。ビルド0警告0エラー。Linux自体の起動は未検証、既存ZIP6218fa3にはこの修正を含まない。

確認PC: THINKPAD-P14S。既存の隔離Web56002へ静的ファイルのみ反映、再起動/DB変更なし。

http://127.0.0.1:56002/intake-review.html?id=8d56e478-7453-46aa-91bd-e117c04b7cbb

1. 再読み込みし、原典画像上の「＋ 拡大」で文字を読める倍率にする（最大400%）。
2. 横長の資料は画像内を左右にスクロールする。「原寸（100%）」は画像1ピクセルを画面の1CSSピクセルで表示。「横幅に合わせる」で全体へ戻る。
3. 回答入力へ進み、未保存商品選択とメモを入力する。原典へ戻っても倍率、画像内スクロール、強調行は保持される。もう一度回答入力へ進んでも選択とメモが残る。
4. 確認だけなら回答確定・処理再開は不要。990001の価格相違は拡大とは無関係で保留を維持する。

検証: Edge 17項目。実際の合成Excel原典画像で原寸/125%/縮小/400%上限/横幅/左右スクロール、回答画面往復の状態保持、追加描画要求0、POST0、ブラウザー例外0。PDFは合成ページgeometry fixtureで拡大時のoverlay一致と参照座標不変を検証。原本や取込データは変更していない。元画像以上の解像度は生成しない。

証拠はartifactルートのverify-image-zoom.cjs / image-zoom-proof.json / image-zoom-review.png。参考Library ID単独からの画像取得ツールはなく、Page参照が未提供のため添付自体は未閲覧。実画面で幅固定を確認した。実PDF追加倍率受入と他の既存残作業は完了扱いにしない。
