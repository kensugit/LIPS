using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CatalogSearch.Application;
using CatalogSearch.EnrichmentCli;
using Xunit;
public sealed class PolicyV2Tests
{
    static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,AcquisitionInput.Json);
    static AcquisitionRequest Request(string supplier="CELLAR",string host="www.yourcellar.jp")
    {
        var source=Guid.Parse("cd053f50-7045-4c88-989c-c195026ee241");
        const string raw="{\"SupplierProductCode\":\"S1\",\"ProducerNameJa\":\"P\",\"ProductNameJa\":\"Wine\",\"VintageRaw\":\"2023\",\"VolumeMl\":750,\"ProductType\":\"赤\"}";
        return new("lips-importer-acquisition-request/v1","policy-test",new(supplier,"S1","P","Wine","2023",750,"赤"),source,raw,SourceFingerprint.Create(source,raw),"https://"+host+"/view/item/000000001957",4);
    }
    static AcquisitionPolicyV2 Policy()=>new("lips-importer-acquisition-policy/v2",[new("JET","www.jetlc.co.jp"),new("CELLAR","www.yourcellar.jp")],100,1000000,
        new Dictionary<string,IReadOnlyDictionary<string,string>>{["JET"]=new Dictionary<string,string>{{"brand","P"}}},
        new Dictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>>{["CELLAR"]=new Dictionary<string,YourCellarProductPageIdentity.ProducerRule>{{"P",new("表示P","カテゴリP")}}});
    [Fact] public void TypedDispatchAndBoundVersionPreserveV1()
    {
        var parsed=AcquisitionInput.Validate(Bytes(Request()),Bytes(Policy()));
        Assert.StartsWith("yourcellar-product/3+",parsed.Adapters.Select("CELLAR").Version);
        Assert.StartsWith("jet-product-about/1+",parsed.Adapters.Select("JET").Version);
        Assert.Equal("official-product-jsonld/1",parsed.Adapters.Select("OTHER").Version);
        Assert.Equal(parsed.Adapters.BoundVersion(parsed.Hosts),parsed.ExpectedExtractorVersion);Assert.InRange(parsed.ExpectedExtractorVersion.Length,1,100);
        var original=new AcquisitionPolicy("lips-importer-acquisition-policy/v1",[new("CELLAR","www.yourcellar.jp")],100,1000000);
        var v1=AcquisitionInput.Validate(Bytes(Request()),Bytes(original));Assert.Equal("official-product-jsonld/1+"+v1.Hosts.Version,v1.ExpectedExtractorVersion);
        var changed=JsonNode.Parse(Bytes(Policy()))!;changed["reviewedYourCellarSuppliers"]!["CELLAR"]!["P"]!["displayName"]="別P";
        Assert.NotEqual(parsed.ExpectedExtractorVersion,AcquisitionInput.Validate(Bytes(Request()),Encoding.UTF8.GetBytes(changed.ToJsonString())).ExpectedExtractorVersion);
    }
    [Theory]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("missing")][InlineData("null")][InlineData("wrongtype")][InlineData("collision")][InlineData("host")][InlineData("unbound")][InlineData("emptyproducer")][InlineData("nestedunknown")][InlineData("nestedduplicate")][InlineData("oversize")][InlineData("wrongversion")]
    public void InvalidPolicyNeverBecomesExecutable(string kind)
    {
        var node=JsonNode.Parse(Bytes(Policy()))!;
        switch(kind)
        {
            case "unknown":node["unexpected"]=true;break;
            case "missing":node.AsObject().Remove("reviewedJetSuppliers");break;
            case "null":node["reviewedYourCellarSuppliers"]=null;break;
            case "wrongtype":node["reviewedJetSuppliers"]=1;break;
            case "collision":node["reviewedJetSuppliers"]!["CELLAR"]=new JsonObject{{"brand","P"}};break;
            case "host":node["rules"]![1]!["host"]="evil.example";break;
            case "unbound":node["rules"]!.AsArray().RemoveAt(1);break;
            case "emptyproducer":node["reviewedYourCellarSuppliers"]!["CELLAR"]=new JsonObject();break;
            case "nestedunknown":node["reviewedYourCellarSuppliers"]!["CELLAR"]!["P"]!["extra"]=true;break;
            case "wrongversion":node["schema"]="lips-importer-acquisition-policy/v9";break;
        }
        var json=node.ToJsonString();
        if(kind=="duplicate")json=json[..^1]+",\"schema\":\"lips-importer-acquisition-policy/v2\"}";
        if(kind=="nestedduplicate")json=json.Replace("\"displayName\":","\"DisplayName\":\"other\",\"displayName\":");
        var bytes=kind=="oversize"?new byte[AcquisitionInput.PolicyLimit+1]:Encoding.UTF8.GetBytes(json);
        Assert.ThrowsAny<Exception>(()=>AcquisitionInput.Validate(Bytes(Request()),bytes));
    }
    [Fact] public async Task V2DefaultValidationIsNonExecutingAndFetchGetsTypedPolicyOnce()
    {
        var root=Path.Combine(Path.GetTempPath(),"lips-policy-v2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var rb=Bytes(Request());var pb=Bytes(Policy());var rp=Path.Combine(root,"request.json");var pp=Path.Combine(root,"policy.json");File.WriteAllBytes(rp,rb);File.WriteAllBytes(pp,pb);
        string[] args=["--request",rp,"--policy",pp,"--store-root",root,"--receipt-root",root,"--request-sha256",AcquisitionInput.Sha(rb),"--policy-sha256",AcquisitionInput.Sha(pb)];
        int calls=0;
        Task<CatalogSearch.Infrastructure.ImporterAcquisitionResult> Fake(ValidatedAcquisition input,string _){calls++;Assert.StartsWith("yourcellar-product/3+",input.Adapters.Select("CELLAR").Version);return Task.FromResult(new CatalogSearch.Infrastructure.ImporterAcquisitionResult("PAGE_REQUIRES_REVIEW_NOT_SAVED",null));}
        Assert.Equal(0,await Program.Run(args,Fake));Assert.Equal(0,calls);Assert.Equal(2,Directory.GetFiles(root).Length);
        Assert.Equal(0,await Program.Run([..args,"--fetch-and-save"],Fake));Assert.Equal(1,calls);
        Assert.Equal(1,await Program.Run([..args,"--fetch-and-save"],Fake));Assert.Equal(1,calls);
    }
}
