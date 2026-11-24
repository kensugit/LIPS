using System.Text.Json;
using System.Text.Json.Nodes;
using CatalogSearch.Application;
using CatalogSearch.EnrichmentCli;
using Xunit;

public sealed class PolicyV3Tests
{
    static byte[] Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value,AcquisitionInput.Json);
    static AcquisitionRequest Request()
    {
        const string raw="{\"SupplierProductCode\":\"S1\",\"ProducerNameJa\":\"P\",\"ProductNameJa\":\"Wine\",\"VintageRaw\":\"2023\",\"VolumeMl\":750,\"ProductType\":\"赤\"}";
        var source=Guid.NewGuid();return new("lips-importer-acquisition-request/v1","toyotsu-test",new("TOYOTSU","S1","P","Wine","2023",750,"赤"),source,raw,SourceFingerprint.Create(source,raw),"https://toyotsu-wine.com/shop/g/gS1/",0);
    }
    static AcquisitionPolicyV3 Policy()=>new("lips-importer-acquisition-policy/v3",[new("TOYOTSU","toyotsu-wine.com")],100,4000000,
        new Dictionary<string,IReadOnlyDictionary<string,string>>(),new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>>(),
        new Dictionary<string,IReadOnlyDictionary<string,string>>{{"TOYOTSU",new Dictionary<string,string>{{"P","P"}}}});
    [Fact]public void V3AddsExplicitToyotsuAndDoesNotChangeLegacyPolicyVersions()
    {
        var parsed=AcquisitionInput.Validate(Bytes(Request()),Bytes(Policy()));Assert.StartsWith("toyotsu-product/1+",parsed.Adapters.Select("TOYOTSU").Version);
        Assert.Equal(parsed.Adapters.BoundVersion(parsed.Hosts),parsed.ExpectedExtractorVersion);
        Assert.Equal("official-product-jsonld/1+"+parsed.Hosts.Version,new ImporterPageAdapterPolicy().BoundVersion(parsed.Hosts));
        var legacy=new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>>{{"NL",new Dictionary<string,YourCellarProductPageIdentity.ProducerRule>{{"P",new("P","P")}}}};
        Assert.Equal(new ImporterPageAdapterPolicy(null,legacy).BoundVersion(parsed.Hosts),new ImporterPageAdapterPolicy(null,legacy,null).BoundVersion(parsed.Hosts));
    }
    [Theory][InlineData("host")][InlineData("missing")][InlineData("null")][InlineData("unknown")][InlineData("collision")]
    public void WrongHostAndUnreviewedOrAmbiguousMappingsAreRejected(string kind)
    {
        var node=JsonNode.Parse(Bytes(Policy()))!;
        if(kind=="host")node["rules"]![0]!["host"]="evil.example";
        if(kind=="missing")node.AsObject().Remove("reviewedToyotsuSuppliers");
        if(kind=="null")node["reviewedToyotsuSuppliers"]=null;
        if(kind=="unknown")node["extra"]=true;
        if(kind=="collision")node["reviewedJetSuppliers"]!["TOYOTSU"]=new JsonObject{{"brand","P"}};
        Assert.ThrowsAny<Exception>(()=>AcquisitionInput.Validate(Bytes(Request()),System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
