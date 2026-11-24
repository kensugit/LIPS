using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;

if(args.Length!=0&&!(args.Length==2&&args[0]=="--observed-pages-dir"))throw new ArgumentException("Only optional --observed-pages-dir <local directory>; no network/DB.");
var root=Path.Combine(Path.GetTempPath(),"lips-official-evidence-poc-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var legacyRoot=Path.Combine(root,"legacy");var toyotsuRoot=Path.Combine(root,"toyotsu");Directory.CreateDirectory(legacyRoot);Directory.CreateDirectory(toyotsuRoot);
var nlPolicy=new ImporterPageAdapterPolicy(reviewedYourCellarSuppliers:new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>>{
    ["NL_SYNTHETIC"]=new Dictionary<string,YourCellarProductPageIdentity.ProducerRule>{{"原本生産者",new("表示生産者","表示生産者（合成地域）")}}});
var nlHosts=new ImporterEvidenceHostPolicy([new("NL_SYNTHETIC","www.yourcellar.jp")]);var legacyVersion=nlPolicy.BoundVersion(nlHosts);
var toyotsuPolicy=new ImporterPageAdapterPolicy(reviewedToyotsuSuppliers:new Dictionary<string,IReadOnlyDictionary<string,string>>{
    ["TOYOTSU"]=new Dictionary<string,string>{{"クーリー デュテイユ","クーリー デュテイユ"}}});
var toyotsuHosts=new ImporterEvidenceHostPolicy([new("TOYOTSU","toyotsu-wine.com")]);var toyotsuVersion=toyotsuPolicy.BoundVersion(toyotsuHosts);
static string Original(ImporterProductIdentity id,string grapes="")=>JsonSerializer.Serialize(new{
    SupplierProductCode=id.Sku,ProducerNameJa=id.Producer,ProductNameJa=id.Product,VintageRaw=id.Vintage,VolumeMl=id.VolumeMl,ProductType=id.Variant,Grapes=grapes,
    Price=1234,Inventory="1",Fixture="synthetic original; not production row export"});
static string FingerprintTree(string path)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
    Directory.GetFiles(path,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file=>new{file=Path.GetRelativePath(path,file),sha=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))}).ToArray())));
var nlId=new ImporterProductIdentity("NL_SYNTHETIC","SKU-A","原本生産者","合成商品","2023",750,"赤");var nlSource=Guid.NewGuid();var nlOriginal=Original(nlId);var nlFp=SourceFingerprint.Create(nlSource,nlOriginal);
const string title="表示生産者 合成商品 2023 750ML";
var nlPage="<html><body><div class='detail'><div class='item-detail'><div class='item-title'><p class='item-category-name'>表示生産者（合成地域）</p><p class='item-name'>"+title+"</p></div><ul class='item-info-list'><li><dl><dt>独自商品コード</dt><dd>SKU-A</dd></dl></li></ul></div><div class='item-description-cont'><div class='item-description'><div class='description-wrap'><div class='desc-content'><p>"+title+"</p></div><div class='desc-content'><p>合成説明</p></div><dl class='desc-spec'><dt>品種</dt><dd>Sangiovese 100%</dd><dt>タイプ</dt><dd>赤ワイン</dd></dl></div></div></div></div></body></html>";
var nlAcquisition=new ImporterEnrichmentAcquisition(new LocalFixtureFetcher(Encoding.UTF8.GetBytes(nlPage)),new(legacyRoot,new(100,4000000)),nlHosts,nlPolicy);
if((await nlAcquisition.AcquireAsync(new(nlId,nlFp,[]),nlSource,nlOriginal,"https://www.yourcellar.jp/view/item/000000001957",0)).Status!="VERIFIED_SAVED")throw new InvalidDataException("LEGACY_FIXTURE_NOT_SAVED");
var legacyBefore=FingerprintTree(legacyRoot);var mixed=new RoutedImporterEnrichmentReader(new FileImporterEnrichmentReader(legacyRoot),new Dictionary<string,RoutedImporterEnrichmentReader.Route>{{"TOYOTSU",new(new FileImporterEnrichmentReader(toyotsuRoot),toyotsuVersion)}});
var results=new List<object>();long generation=0;
foreach(var(sku,product,year,pin)in new[]{("FE4113","シノン レ グラヴィエール","2023","5459cb0a107d6dd0379a66013f3660f0ecfa6553d36ff083560dcbe448c2e868"),("FE4218","シノン クロ ド レコー","2018","043f07b483f2bf4b13df18e4bfe1b31eb6f1e30b5bc05475a50f52033dbf6c8f")})
{
    byte[] page;
    if(args.Length==2){page=File.ReadAllBytes(Path.Combine(Path.GetFullPath(args[1]),sku+".html"));if(Convert.ToHexStringLower(SHA256.HashData(page))!=pin)throw new InvalidDataException("OBSERVED_PAGE_PIN_CHANGED");}
    else{using var stream=typeof(LocalFixtureFetcher).Assembly.GetManifestResourceStream("Toyotsu."+sku+".min.html")!;using var memory=new MemoryStream();stream.CopyTo(memory);page=memory.ToArray();}
    var id=new ImporterProductIdentity("TOYOTSU",sku,"クーリー デュテイユ",product,year,750,"赤");var source=Guid.NewGuid();var raw=Original(id);var fp=SourceFingerprint.Create(source,raw);var rawSha=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    // OFF means the original legacy-only reader, without a Toyotsu route.
    var before=new FileImporterEnrichmentReader(legacyRoot).ReadVerifiedForProduct(id,fp,legacyVersion,DateTimeOffset.UtcNow);
    var acquisition=new ImporterEnrichmentAcquisition(new LocalFixtureFetcher(page),new(toyotsuRoot,new(100,4000000)),toyotsuHosts,toyotsuPolicy);
    var saved=await acquisition.AcquireAsync(new(id,fp,[]),source,raw,"https://toyotsu-wine.com/shop/g/g"+sku+"/",generation);
    if(saved.Status!="VERIFIED_SAVED"||saved.Saved is null)throw new InvalidDataException("TOYOTSU_NOT_SAVED");generation=saved.Saved.Generation;
    var after=mixed.ReadVerifiedForProduct(id,fp,legacyVersion,DateTimeOffset.UtcNow);var adopted=after.Status=="verified"&&after.Content is{}content&&content.Grapes.Count==1&&SemanticGrapeMatcher.Match(content.Grapes[0].SourceExcerpt,new("カベルネ・フラン","predominant")).Included;
    var expired=mixed.ReadVerifiedForProduct(id,fp,legacyVersion,DateTimeOffset.UtcNow.AddDays(31));var wrongSource=mixed.ReadVerifiedForProduct(id,new('f',64),legacyVersion,DateTimeOffset.UtcNow);
    if(before.Status!="NOT_FOUND"||before.Content is not null||!adopted||expired.Status!="EXPIRED"||wrongSource.Status!="BINDING_CHANGED"||rawSha!=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))))throw new InvalidDataException($"POC_POSTCONDITION_FAILED before={before.Status} after={after.Status} adopted={adopted} expired={expired.Status} changed={wrongSource.Status}");
    results.Add(new{sku,originalSynthetic=true,sourceFingerprint=fp,beforeStatus=before.Status,cacheOffMatched=false,cacheOnMatched=adopted,recordSha256=after.ContentRecordSha256,generation,sourceUrl=after.Content!.FinalUrl,excerpt=after.Content.Grapes[0].SourceExcerpt,expired=expired.Status,changedSource=wrongSource.Status,priceStockRawUnchanged=true});
}
var nl=mixed.ReadVerifiedForProduct(nlId,nlFp,legacyVersion,DateTimeOffset.UtcNow);if(nl.Status!="verified"||FingerprintTree(legacyRoot)!=legacyBefore)throw new InvalidDataException("LEGACY_CACHE_CHANGED");
Console.WriteLine(JsonSerializer.Serialize(new{status="passed",root,results,legacySyntheticYourCellarPreserved=true,legacyVersion,toyotsuVersion,layers="actual acquisition/writer/file-reader/dispatcher/grape-matcher; SQL CatalogQuery not executed here",actualDbCalls=0,httpCalls=0,productionCacheWrites=0,clock="mock fetch timestamp and acquisition use DateTimeOffset.UtcNow; expiry tested only by reader now argument"}));

sealed class LocalFixtureFetcher(byte[] bytes):IOfficialImporterPageFetcher
{
    public Task<OfficialImporterPage> FetchAsync(string supplier,string url,ImporterEvidenceHostPolicy policy,CancellationToken ct=default)
    {
        if(!policy.Allows(supplier,url))throw new ArgumentException("POC_HOST_NOT_BOUND");
        return Task.FromResult(new OfficialImporterPage(url,url,DateTimeOffset.UtcNow,Convert.ToHexStringLower(SHA256.HashData(bytes)),bytes));
    }
}
