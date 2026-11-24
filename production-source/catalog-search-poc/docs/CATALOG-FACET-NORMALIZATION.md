# 検索条件の表記統合（2026-11-24・未配備候補）

国・産地・容量・ヴィンテージの選択肢を整理する。商品、原本のCountry/Region/VolumeMl/VintageRaw、価格、在庫を書き換える処理はない。未知の類似名をAIや編集距離で同一視しない。

## データと検索の契約

- `/api/free-text-search/options` の既存 `options` はモデルと検証用の元の登録値を保つ。表示用の `facets` を追加し、ラベルと検索に渡す値を分ける。旧 `/api/options` の元の国・産地・容量フィールドは互換のため変更していない。
- 国と産地の `value` は、スナップショットに存在する原表記の代表値。`label` は正規表記、`rawValues` は統合した原表記。ヴィンテージの `value` は従来のモデル契約と同じ正規化VTで、原表記を `rawValues` に残す。
- 国はNFKC・空白表記差と、`src/CatalogSearch.Application/Dictionaries/catalog-facets.ja.json` の明示的な7グループで正規化する。France/フランス、Deutschland/ドイツ、Japan/日本、New Zealand/ニュージーランド等を統合する。未登録の近似綴りを推測で追加しない。
- 産地は国ごとに扱う。既存 `wine-search.ja.json` の `Places.Names` を同義表記として使う。`Places.Members` は地理的包含を表すため同義語に昇格させない。シノンをロワールへ潰さない。
- 右岸の既存の12同義語グループは、同じfacet JSONへ移し、`ReviewedRightBankScope` も同じ定義を参照する。各内側配列は一つの産地の別名、外側は右岸の所属集合。Version/Sources/Members/Outsideのシリアライズ結果は旧定義と一致するテストを付けた。Pomerol/ポムロール/AOC ポムロール/AOCポムロールを統合する一方、Saint-EmilionとSaint-Emilion Grand Cruを別に保つ。
- 国指定時だけ、その国の登録産地別名に検索を展開する。国未指定ではNFKC・空白同値の同じ産地名だけを照合し、他国の別名へは展開しない。国のない産地選択肢は「国指定なし・同名を含む」と表示し、空国の商品だけに絞る意味を持たせない。
- 容量は既存の数値mlをDistinctし、未設定の0だけを選択肢から除く。容量0の商品は通常検索から削除しない。49mlや95ml等の正の値を推測で補正しない。負値と契約上限超過の既存拒否は保持する。
- VTは従来のNV/MV、括弧付き単独4桁年、NFKCに加え、今回の表記統合に含める「単独4桁＋年」を同じ4桁へ統一する。前後・改行空白と注記括弧周囲の空白も表示/Matchingの双方で統一する。`NV(2024)` をplain NVへ落とさず、`20年`、`202`、`750`、`0`、ブレンド年などを別の年やNVへ推測変換しない。

`CatalogQuery` は通常検索・structured検索・v2検索で同じ原表記集合を使う。国の別名をまとめた画面だけを作って英語表記の商品を取りこぼすことがないよう、UIとSQLの対応を同時に変更した。国・価格・在庫・容量・年号等のAND条件を保持する。

## 画面と互換性

国を変更すると産地をリセットする。国を指定しない一覧では同名別国をラベルで区別し、国付きの産地を選ぶと表示された国を明示選択する。価格・在庫・容量・年号・選択商品の状態は保持する。商品行の国・産地は元の値のまま、詳細では年号の原表記を確認できる。

`facets` のmissing/nullだけを旧サーバー互換として元の選択肢へフォールバックする。presentのfalse/0/空文字/配列/必須項目欠損/異常型は検索前に拒否する。結果に使わない追加メタデータだけでは止めず、DOMへ任意反映しない。失敗で条件を「すべて」に落として本処理を試さない。

最終HTMLは `/catalog.js?v=facets-20261124-r2` を参照する。既存のno-storeに加えて版を明示し、HTML・JS・APIを同じbundleとして受け入れる。旧サーバーへの互換フォールバックは、正規化機能の本番受入完了を意味しない。更新後の通常ブラウザー再読み込みと両サービスのペア受入は別工程。

## ローカル検証と限界

対象OSはWindows、.NET 10、Edge。DB Migration・実DB・公式取得・モデル推論・本番更新は行っていない。

```powershell
dotnet test tests/CatalogSearch.Domain.Tests/CatalogSearch.Domain.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false --filter "FullyQualifiedName~CatalogFacetTests|FullyQualifiedName~VintageNotationTests|FullyQualifiedName~SearchCatalogSnapshotTests|FullyQualifiedName~SearchVocabularyTests"
dotnet run --project tests/CatalogSearch.FreeText.WebChecks/CatalogSearch.FreeText.WebChecks.csproj --no-restore -m:1 -p:BuildInParallel=false
```

r2の作者局所試験はdomain52件成功・既存PostgresFact1件は未接続のためskip、Webサービス層100項目成功。後者は旧右岸定義との一致を含む。JSの型・必須項目境界10件と最終HTML/JSのEdge UI13項目も成功した。UIはHTTP境界を架空APIにした試験で、実ネットワーク0。選択CSVの保持、価格/在庫/容量/VTの保持、同名別国、旧サーバー、余分メタデータ、不正DTOを確認した。

r1では13商品の新規合成DBで実SQL18件と通常/v2サービス4件を実行し、原本ダイジェストの前後一致を確認した。初回initdbタイムアウト後の子終了確認が次の試験開始より遅れたため、その実行を「全所有権ゲート合格」とは扱わない。元2領域は保持し、後の正常な限定列挙と親の独立照合で残存initdb/postgres候補0を確認した。r2の追加12同義語群は局所試験・共有定義一致・UIで確認し、新しいPGは起動していない。r2の独立SQL再検証と統合・本番DB比較は親担当へ引き継ぐ。

表示産地の全660件について、未知の意味的な同義関係がすべて解決したとは主張しない。統合範囲は確認済みの別名と表記同値に限る。既存の固定14商品・12問は変更していない。本番の最新source/config・カタログ担当の明示引渡しと独立QAを揃えた後に正式リリースへ進む。

## r3差分

未知国名の `XYZ` と `xyz` が別の産地を持つ場合も、国選択肢の代表ラベルと両産地の国ラベルを一致させる。意味を推測して国名を追加せず、既存の大小文字同値グループ内の代表だけを共用する。変更は `CatalogFacets.cs` と対応unitの2ファイル。作者の局所unit18件と実DTO純関数probeが成功し、その他13 runtime/testのr2 pinsは不変。追加PG起動は0、独立差分QAと統合受入は親担当が実施する。
