using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class ImporterAcquisitionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lips-acquisition-test-" + Guid.NewGuid().ToString("N"));
    private static readonly ImporterProductIdentity Identity = new("TEST", "SKU1", "Maker", "Wine", "2023", 750, "red");
    private static readonly ImporterEvidenceHostPolicy Policy = new(new[] { KeyValuePair.Create("TEST", "example.com") });
    public ImporterAcquisitionTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static string Row(string grapes) => JsonSerializer.Serialize(new { SupplierProductCode="SKU1", ProducerNameJa="Maker",
        ProductNameJa="Wine", VintageRaw="2023", VolumeMl=750, ProductType="red", Grapes=grapes });

    private sealed class FakePage(string vintage = "2023") : IOfficialImporterPageFetcher
    {
        public int Calls;
        public Task<OfficialImporterPage> FetchAsync(string supplier, string url, ImporterEvidenceHostPolicy policy, CancellationToken ct = default)
        {
            Calls++;
            var json = JsonSerializer.Serialize(new Dictionary<string, object> {
                ["@context"]="https://schema.org", ["@type"]="Product", ["sku"]="SKU1", ["name"]="Wine",
                ["brand"]=new { name="Maker" }, ["description"]="Sangiovese 80%",
                ["additionalProperty"]=new[] { ("vintage",vintage), ("volumeMl","750"), ("variant","red"),
                    ("grapes","Sangiovese"), ("grapeEvidence","Sangiovese 80%") }.Select(x=>new Dictionary<string,string> { ["@type"]="PropertyValue",["name"]=x.Item1,["value"]=x.Item2 }).ToArray()
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            return Task.FromResult(new OfficialImporterPage(url,url,DateTimeOffset.UtcNow,Convert.ToHexStringLower(SHA256.HashData(bytes)),bytes));
        }
    }

    [Fact]
    public async Task FetchVerifySaveAndReadAreBoundToCurrentSource()
    {
        var id=Guid.NewGuid(); var row=Row("Sangiovese");
        var original=new ImporterOriginalProduct(Identity,SourceFingerprint.Create(id,row),["Sangiovese"]);
        var page=new FakePage();
        var service=new ImporterEnrichmentAcquisition(page,new(root,new(100,1000000)),Policy);
        var result=await service.AcquireAsync(original,id,row,"https://example.com/wine",0);
        Assert.Equal("VERIFIED_SAVED",result.Status); Assert.Equal(1,page.Calls);
        var reader=new FileImporterEnrichmentReader(root);
        Assert.Equal("verified",reader.ReadVerifiedForProduct(Identity,original.Fingerprint,"official-product-jsonld/1+"+Policy.Version,DateTimeOffset.UtcNow).Status);
        Assert.Null(reader.ReadVerifiedForProduct(Identity,SourceFingerprint.Create(Guid.NewGuid(),row),"official-product-jsonld/1+"+Policy.Version,DateTimeOffset.UtcNow).Content);
        Assert.Null(reader.ReadVerifiedForProduct(Identity,original.Fingerprint,"changed-policy",DateTimeOffset.UtcNow).Content);
    }

    [Fact]
    public async Task WrongVintageDoesNotSave()
    {
        var id=Guid.NewGuid(); var row=Row("");
        var service=new ImporterEnrichmentAcquisition(new FakePage("2022"),new(root,new(100,1000000)),Policy);
        var result=await service.AcquireAsync(new(Identity,SourceFingerprint.Create(id,row),[]),id,row,"https://example.com/wine",0);
        Assert.Equal("PAGE_REQUIRES_REVIEW_NOT_SAVED",result.Status); Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public async Task ConflictingPercentCannotBecomeVerified()
    {
        var id=Guid.NewGuid(); var row=Row("Sangiovese 40%");
        var service=new ImporterEnrichmentAcquisition(new FakePage(),new(root,new(100,1000000)),Policy);
        // Caller-provided grape claims cannot override the captured original JSON.
        var result=await service.AcquireAsync(new(Identity,SourceFingerprint.Create(id,row),["Sangiovese 80%"]),id,row,"https://example.com/wine",0);
        Assert.Equal("CONFLICT_SAVED_NOT_ADOPTED",result.Status);
        Assert.Equal("conflict",new FileImporterEnrichmentReader(root).ReadVerifiedForProduct(Identity,
            SourceFingerprint.Create(id,row),"official-product-jsonld/1+"+Policy.Version,DateTimeOffset.UtcNow).Status);
    }

    [Fact]
    public async Task IdentityCannotBeInventedBesideFingerprint()
    {
        var id=Guid.NewGuid();var row=Row("Sangiovese");var page=new FakePage();
        var service=new ImporterEnrichmentAcquisition(page,new(root,new(100,1000000)),Policy);
        await Assert.ThrowsAsync<ArgumentException>(()=>service.AcquireAsync(new(Identity with { Vintage="2022" },
            SourceFingerprint.Create(id,row),[]),id,row,"https://example.com/wine",0));
        Assert.Equal(0,page.Calls);Assert.Empty(Directory.GetFiles(root));
    }
}
