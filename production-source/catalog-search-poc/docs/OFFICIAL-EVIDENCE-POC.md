# 公式品種補完・味わい条件の隔離PoC

基点は master `db533dbacd2555635d4abe44c8c33244479c04af`。本番・DB・HTTP・モデルを使用しない候補です。価格・在庫・原本の上書き、Migrationはありません。

## 本人が試す入口

このディレクトリをカレントにし、.NET 10 SDKで次を実行します。

```powershell
dotnet restore tests/CatalogSearch.OfficialEvidence.Poc --source C:/Users/kensu/.nuget/packages --disable-parallel -m:1 -p:BuildInParallel=false
dotnet build tests/CatalogSearch.OfficialEvidence.Poc --no-restore -m:1 -p:BuildInParallel=false
dotnet run --project tests/CatalogSearch.OfficialEvidence.Poc --no-build
```

これは任意の自由文を受け付けるWebではなく、**既定サンプル検証CLI**です。2商品を合成原本として、実acquisition→writer→reader→dispatcher→品種matcherを通します。OFFは旧readerのみ、ONは新route追加。新しい一時rootにだけ保存し、結果JSONにrootを返して保全します。元のYourCellar方式も合成cacheで作り、全file指紋の不変を確認します。本番NL cache原本の再受入ではありません。

親が取得固定した実HTMLを使う追加検証（HTTP取得は行わない）:

```powershell
dotnet run --project tests/CatalogSearch.OfficialEvidence.Poc --no-build -- --observed-pages-dir ../../../observed-pages-r105-network
dotnet run --project tests/CatalogSearch.Toyotsu.ObservedChecks --no-build -- --observed-pages-dir ../../../observed-pages-r105-network
```

ObservedChecksは別途restore/buildが必要です。実HTML2件のSHAはTOYOTSU-ADAPTER-VALIDATION.json参照。埋込最小fixtureはDOM骨格・同定欄・必要最短抜粋へ縮約し、関連商品毒入れは合成です。実full HTMLはGitに含めません。

## 自由文の入力例と期待条件

実サービス層の入力例（上の固定CLIへの引数ではありません）:

- 「半甘口の白ワイン」: 白 AND sweetness=やや甘口。甘口へ縮約しない。元入力保持。
- 「ロワールのカベルネ・フラン主体の赤ワイン」: ロワール AND 赤 AND カベルネ・フラン predominant。Toyotsuの公式100%根拠を採用できるのはidentity/原本指紋/期限が一致する商品だけ。
- 「辛口の白ワイン」: 白 AND 辛口。原本「辛口ではありません」は一致させない。
- 「柑橘系の白ワイン」: 白 AND 柑橘系。「柑橘の香りは感じられません」は一致させない。

この候補のWebサービス条件テストはFreeText.WebChecks（108 checks）です。任意入力Webの実動・SQL CatalogQueryの公式OFF/ON・固定12問比較は独立QAの別試験です。matcherを通した本CLIだけをSQL検索成功と呼びません。

## 結合APIと版

1. `ImporterPageAdapterPolicy(reviewedToyotsuSuppliers: mapping)`。mappingは承認済supplier code→原本producer→公式日本語producer。同定はSKU/銘柄/生産者/年号/容量/色、URLは明示hostとSKUに束縛。今回のTOYOTSU codeと原本は合成であり、実DB code/ExtractedRowJson/SourceFPの採用は未確認です。
2. `toyotsuVersion = policy.BoundVersion(hosts)`。旧policy/maps/Root/ExtractorVersionを変更せず、新版と別rootを追加します。旧recordのversion書換はしません。
3. `new RoutedImporterEnrichmentReader(legacyReader, routes)`。routesはsupplier→`new Route(new FileImporterEnrichmentReader(root), toyotsuVersion)`。`new CatalogQuery(db, mixedReader, legacyVersion)`へ注入します。元supplier identityだけでrouteを選び、miss時に旧verifiedへ戻りません。旧有効conflictは新missでも保持。旧bindingchangedは二重verifiedではありません。
4. Web設定は旧 `ImporterEnrichment:Root/ExtractorVersion` を維持し、`ImporterEnrichment:SupplierRoutes:TOYOTSU:Root/ExtractorVersion` の2leafを追加。別の非包含絶対root必須、未登録supplierは旧readerです。
5. `AcquireAsync(candidate, sourceDocumentId, exactOriginalJson, url, expectedGeneration)`。rootは事前作成のみ、writerはCreateNewと世代CASを保持。初回0、次回Saved.Generation。SQLを使う場合はjsonb保存**後に再読した実文字列**からSourceFingerprintを作ります。本CLIの原本は非SQL合成です。
6. Fetcher/AcquisitionはDateTimeOffset.UtcNow。CLIはlocal bytesを返すmock fetcherの時刻であり、fresh実HTTP検証の代用ではありません。期限回帰はreaderのnow引数+31日でEXPIREDを確認。TTLや価格在庫を変更しません。

CLI policy/v3はreviewedToyotsuSuppliersを追加、v1/v2の版計算は保持します。実運用policy例を確定・配備する工程は含みません。

## 辞書と固定規則の詳細

`Dictionaries/wine-search.ja.json` が属性名とSweetnessAliasesの正本です。半甘口→やや甘口は本人回答 `HALF-SWEET-DECISION-r107.json` に従います。5 canonical/6表記、重複・未登録canonical・正規化衝突はロード拒否。甘口は別条件、甘い香りは甘辛度根拠ではありません。

`WineTasteEvidence.cs` の否定、料理/参照範囲、直接商品断定の文法、肯定修飾語はC#の閉じた規則です。辞書の語を任意文のsubstringで拾いません。「フレッシュな酸を伴う辛口の白ワイン」「すっきりした辛口」「洋梨や柑橘の香りが広がる」は対象。未知の複数節・広い自然文のRecallは未測定です。Description/Sweetnessの異常JSON型は不採用、null/欠損はunknownで他の適正欄を無条件には遮断しません。否定/不確実/明示欄との競合は除外します。

公式adapterは商品領域の品種根拠だけを補い、公式味わい属性は未補完です。100%同士の異品種矛盾はconflict、原本の単なる品種listを完全配合とは推定しません。一般知識・生産者説明・関連商品から個別商品の含有を推定しません。

## 残る受入

独立QAの実CatalogQuery+専用合成PGで、旧cache互換、公式2件OFF/ON、原本FP再読、競合/期限/route missを確認します。固定12問は別の同一14商品で比較し変更しません。実本番のsupplier code/SourceFP/identity、fresh公式取得とcache配置、権限、最新本番との統合・独立QA・配布承認は別工程です。
