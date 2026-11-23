# 品種シノニムの検索条件化（2026-11-24候補）

基点は `86000a2c829780bc11797e9e79411db8492695bf`。本候補は未配備。原本・商品名・価格・在庫・地域・DBスキーマを変更しない。

## 今回の解釈

`ピノグリもしくはピノグリージョを含む白ワイン` は `ProductType=白 AND Grape={Name:ピノ・グリ, Mode:contains}` の一条件に束ねる。LLMを呼ばず規則で解釈する。`主体` は別の `predominant` 条件を維持する。画面で明示した条件の優先も維持する。

`wine-search.ja.json` の品種知識は296 canonical、表記は601→605。ピノ・グリの既存2表記に4表記を加え、次の6表記を同じcanonicalへ束ねた。

- ピノ・グリ、ピノグリ、Pinot Gris
- ピノ・グリージョ、ピノグリージョ、Pinot Grigio

同義語の出典は親担当が確認した [Plantgrape: Pinot gris](https://www.plantgrape.fr/en/varieties/fruit-varieties/217)。実装担当による公式ページ再取得は0。この品種の同義語関係は、個別商品の含有や味わいの証明ではない。

辞書の内容SHA256は `63306008896df523cb1f708fb604de88f0d672e3e7858b0eb5341e133993e405`。従来の `JapaneseWineDictionary.Revision` が実JSON bytesから算出し、検索snapshotにも結合する。辞書schema Version=1は変更しない。

## コードと辞書の境界

品種名と別名はApplicationのJSON。`GrapeRegistry` が承認辞書内だけでcanonicalを解決する。Webの `ComposedSearchRules` は、同一canonicalに登録された隣接2表記のORに共通の明示述語（`を含む` または `主体`）がある場合だけ束ねる。品種名をC#へ直接列挙しない。

OR接続語（もしくは／または／あるいは／or）、否定、述語の形、残文の検証はC#の閉じた規則。別品種OR・別品種AND・国/価格/在庫をまたぐOR・否定・3項のORは検索実行へ落とさず確認待ちにする。OR文字だけを一律に削除しない。右岸は従来の `ReviewedRightBankScope` が先に適用され、地理の複合条件を勝手に拡張しない。

ローカル規則で解釈できない文は既存LLM経路へ進めるが、`Options.Grapes` は原文中に実在する承認済み別名から得たcanonicalだけに制限する。右岸のscope構築後もこの制限を維持する。原文に確認済み別名がなければ候補は空で、LLMが品種を発明した応答は契約検証で拒否する。未登録表記の自動学習や、モデル応答への新たな別名補正は今回行わない。

商品ごとの採否は既存 `CatalogQuery`／`SemanticGrapeMatcher` が原本や採用済み公式補完の根拠から判定する。`contains` と `predominant` を混同せず、50%は主体にしない。不明な原本から含有を推測しない。plain品種リストでは従来の英語否定substring `not ` が `Pinot ` に誤反応していたため、not/withoutを単語境界に限定した。日本語否定の扱いは維持する。

## ローカル確認

DB・実モデル・公式fetchを使わない。ローカルNuGetキャッシュで復元し、単一MSBuildで実行する。

```powershell
dotnet restore tests/CatalogSearch.FreeText.WebChecks/CatalogSearch.FreeText.WebChecks.csproj --source C:/Users/kensu/.nuget/packages --disable-parallel -m:1 -p:BuildInParallel=false
dotnet run --project tests/CatalogSearch.FreeText.WebChecks/CatalogSearch.FreeText.WebChecks.csproj --no-restore -p:BuildInParallel=false -m:1
dotnet restore tests/CatalogSearch.Domain.Tests/CatalogSearch.Domain.Tests.csproj --source C:/Users/kensu/.nuget/packages --disable-parallel -m:1 -p:BuildInParallel=false
dotnet test tests/CatalogSearch.Domain.Tests/CatalogSearch.Domain.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false --filter "FullyQualifiedName~SemanticCatalogEvidenceTests|FullyQualifiedName~GrapeEvidenceTests|FullyQualifiedName~SemanticSearchContractTests|FullyQualifiedName~FinesseDescriptionGrapeTests"
```

WebChecksは実サービス層とin-memory store/interpreter、domainは原本根拠判定の限定回帰。実DB検索・実機・本番受入を代用しない。独立QA、commit/push、正式リリースは親担当へ引き継ぐ。
