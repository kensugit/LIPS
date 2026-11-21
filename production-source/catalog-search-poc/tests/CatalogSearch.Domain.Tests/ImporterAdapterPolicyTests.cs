using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public sealed class ImporterAdapterPolicyTests
{
    static ImporterPageAdapterPolicy Adapters(string producer="Maker") => new(new Dictionary<string,IReadOnlyDictionary<string,string>> {
        ["JET_FIXTURE"] = new Dictionary<string,string>{{"sample",producer}} });
    static readonly ImporterEvidenceHostPolicy Hosts = new(new[] { KeyValuePair.Create("JET_FIXTURE","www.jetlc.co.jp"),KeyValuePair.Create("JSON_FIXTURE","example.com") });
    static ImporterProductIdentity Identity(string supplier) => new(supplier,"SKU1","Maker","Wine","2023",750,"赤");
    static string Original => JsonSerializer.Serialize(new { SupplierProductCode="SKU1",ProducerNameJa="Maker",ProductNameJa="Wine",VintageRaw="2023",VolumeMl=750,ProductType="赤",Grapes="Sangiovese" });
    static string JetHtml => "<div class=\"productAbout\"><p class=\"imgLogo\"><a href=\"https://www.jetlc.co.jp/wine/brand/sample/\"><img alt=\"Wine\"></a></p><table class=\"productTbl\">" +
        string.Concat(new[]{("商品コード","SKU1"),("VINTAGE","2023"),("色","赤"),("容量/入数","750ml/12"),("ブドウ品種","Sangiovese 80%")}.Select(x=>$"<tr><th>{x.Item1}</th><td>{x.Item2}</td></tr>"))+"</table></div>";
    static string JsonBody => JsonSerializer.Serialize(new Dictionary<string,object>{["@context"]="https://schema.org",["@type"]="Product",["sku"]="SKU1",["name"]="Wine",["brand"]=new{name="Maker"},["description"]="Sangiovese 80%",
        ["additionalProperty"]=new[]{("vintage","2023"),("volumeMl","750"),("variant","赤"),("grapes","Sangiovese"),("grapeEvidence","Sangiovese 80%")}.Select(x=>new Dictionary<string,string>{{"@type","PropertyValue"},{"name",x.Item1},{"value",x.Item2}}).ToArray()});
    sealed class Fake(string body) : IOfficialImporterPageFetcher
    {
        public int Calls;
        public Task<OfficialImporterPage> FetchAsync(string supplier,string url,ImporterEvidenceHostPolicy policy,CancellationToken ct=default)
        { Calls++;var bytes=Encoding.UTF8.GetBytes(body);return Task.FromResult(new OfficialImporterPage(url,url,DateTimeOffset.UtcNow,Convert.ToHexStringLower(SHA256.HashData(bytes)),bytes)); }
    }
    [Fact]
    public async Task MixedAdaptersShareOneStrictWebVersionAndKeepSourceBinding()
    {
        var root=Path.Combine(Path.GetTempPath(),"lips-mixed-adapters-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var policy=Adapters();var source=Guid.NewGuid();var fingerprint=SourceFingerprint.Create(source,Original);var version=policy.BoundVersion(Hosts);
        Assert.InRange(version.Length,1,100);
        foreach(var (supplier,body,url,generation) in new[]{("JET_FIXTURE",JetHtml,"https://www.jetlc.co.jp/wine/999999/",0L),("JSON_FIXTURE",JsonBody,"https://example.com/wine",1L)})
        {
            var fake=new Fake(body);var identity=Identity(supplier);
            var acquisition=new ImporterEnrichmentAcquisition(fake,new(root,new(100,1000000)),Hosts,policy);
            var result=await acquisition.AcquireAsync(new(identity,fingerprint,[]),source,Original,url,generation);
            Assert.Equal("VERIFIED_SAVED",result.Status);Assert.Equal(1,fake.Calls);
            var reader=new FileImporterEnrichmentReader(root);
            Assert.Equal("verified",reader.ReadVerifiedForProduct(identity,fingerprint,version,DateTimeOffset.UtcNow).Status);
            Assert.Null(reader.ReadVerifiedForProduct(identity,fingerprint,new ImporterPageAdapterPolicy().BoundVersion(Hosts),DateTimeOffset.UtcNow).Content);
        }
    }
    [Fact]
    public void PolicyChangesInvalidateAndNeverFallbackFromSelectedJet()
    {
        var p=Adapters();Assert.NotEqual(p.BoundVersion(Hosts),Adapters("Different Maker").BoundVersion(Hosts));
        var adapter=p.Select("JET_FIXTURE");
        Assert.False(adapter.TryExtract(Identity("JET_FIXTURE"),"https://www.jetlc.co.jp/wine/999999/",Encoding.UTF8.GetBytes(JsonBody),out _));
        Assert.False(adapter.TryExtract(Identity("JET_FIXTURE"),"https://example.com/wine",Encoding.UTF8.GetBytes(JetHtml),out _));
        Assert.Equal("official-product-jsonld/1+"+Hosts.Version,new ImporterPageAdapterPolicy().BoundVersion(Hosts));
    }
}
