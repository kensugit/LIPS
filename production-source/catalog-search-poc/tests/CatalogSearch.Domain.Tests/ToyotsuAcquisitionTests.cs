using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class ToyotsuAcquisitionTests
{
    sealed class LocalPage(byte[] body):IOfficialImporterPageFetcher
    {
        public int Calls;
        public Task<OfficialImporterPage> FetchAsync(string supplier,string url,ImporterEvidenceHostPolicy hosts,CancellationToken ct=default)
        {Assert.True(hosts.Allows(supplier,url));Calls++;return Task.FromResult(new OfficialImporterPage(url,url,DateTimeOffset.UtcNow,Convert.ToHexStringLower(SHA256.HashData(body)),body));}
    }
    [Theory][InlineData("FE4113","")][InlineData("FE4218","")][InlineData("FE4113","Pinot Noir 100%")]
    public async Task ActualAcquisitionStoreReaderPreserveSourceAndRejectCompleteCompositionConflict(string sku,string originalGrapes)
    {
        var root=Path.Combine(Path.GetTempPath(),"lips-toyotsu-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var id=ToyotsuProductPageIdentityTests.Identity(sku);var source=Guid.NewGuid();
        var raw=JsonSerializer.Serialize(new{SupplierProductCode=id.Sku,ProducerNameJa=id.Producer,ProductNameJa=id.Product,VintageRaw=id.Vintage,VolumeMl=id.VolumeMl,ProductType=id.Variant,Grapes=originalGrapes,Price=1234,Inventory="1"});
        var fp=SourceFingerprint.Create(source,raw);var before=raw;var fetch=new LocalPage(ToyotsuProductPageIdentityTests.Fixture(sku));
        var policy=new ImporterPageAdapterPolicy(reviewedToyotsuSuppliers:new Dictionary<string,IReadOnlyDictionary<string,string>>{{"TOYOTSU",new Dictionary<string,string>{{id.Producer,id.Producer}}}});
        var hosts=new ImporterEvidenceHostPolicy([new("TOYOTSU","toyotsu-wine.com")]);var version=policy.BoundVersion(hosts);
        var service=new ImporterEnrichmentAcquisition(fetch,new(root,new(100,4000000)),hosts,policy);
        var saved=await service.AcquireAsync(new(id,fp,[]),source,raw,"https://toyotsu-wine.com/shop/g/g"+sku+"/",0);
        Assert.Equal(originalGrapes.Length==0?"VERIFIED_SAVED":"CONFLICT_SAVED_NOT_ADOPTED",saved.Status);Assert.Equal(1,fetch.Calls);
        var reader=new FileImporterEnrichmentReader(root);var result=reader.ReadVerifiedForProduct(id,fp,version,DateTimeOffset.UtcNow);
        Assert.Equal(originalGrapes.Length==0?"verified":"conflict",result.Status);Assert.Equal(before,raw);Assert.Equal(fp,SourceFingerprint.Create(source,raw));
        Assert.DoesNotContain("Price",JsonSerializer.Serialize(result.Content));Assert.DoesNotContain("Inventory",JsonSerializer.Serialize(result.Content));
        Assert.Equal("BINDING_CHANGED",reader.ReadVerifiedForProduct(id,new('f',64),version,DateTimeOffset.UtcNow).Status);
    }
}
