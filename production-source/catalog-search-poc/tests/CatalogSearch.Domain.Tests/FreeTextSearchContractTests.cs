using System.Text.Json;
using System.Text.Json.Nodes;
using CatalogSearch.Application;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class FreeTextSearchContractTests
{
    static FreeTextSearchOptions Options() => new(["フランス","イタリア"],["シャンパーニュ","ブルゴーニュ"],["赤","白","泡"],[375,750,1500],["NV","2019","2020"],["AMZ","LUC"]);
    static FreeTextSearchRequest Request() => FreeTextSearchContract.CreateRequest(Guid.Parse("f62c3982-8586-4c29-9db1-34cda2657d8a"),"synthetic-catalog-1","容量1500mlで3万円以下、在庫のあるシャンパーニュ",Options());
    static string Response() => JsonSerializer.Serialize(new FreeTextSearchInterpretation(FreeTextSearchContract.Schema,Request().RequestId,Request().CatalogVersion,FreeTextSearchContract.PromptVersion,"fixture-provider","fixture-model",new([],null,null,"シャンパーニュ",null,1500,null,null,30000,true),[]));
    static string Change(string key, JsonNode? value, bool filter=false) { var n=JsonNode.Parse(Response())!; (filter?n["filters"]!:n)[key]=value;return n.ToJsonString(); }
    [Fact] public void FixtureShapeAndChampagneDoNotInventSparkling()
    { var r=FreeTextSearchContract.ParseResponse(Request(),Response()); var p=FreeTextSearchContract.Merge(new(),null,r); Assert.Equal("シャンパーニュ",p.Filter.Region); Assert.Null(p.Filter.ProductType);Assert.Equal(1500,p.Filter.VolumeMl);Assert.Equal(30000m,p.Filter.PriceMax);Assert.True(p.Filter.AvailableOnly);Assert.Null(p.Filter.Text); }
    [Theory] [InlineData("schema","wrong")] [InlineData("promptVersion","wrong")] [InlineData("catalogVersion","stale")] [InlineData("requestId","00000000-0000-0000-0000-000000000000")] [InlineData("provider","")] [InlineData("model"," ")] [InlineData("extra","x")]
    public void RejectsEnvelopeMismatch(string key,string value) => Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),Change(key,JsonValue.Create(value))));
    [Theory] [InlineData("supplier","unknown")] [InlineData("supplier","amz")] [InlineData("country","unknown")] [InlineData("region","unknown")] [InlineData("productType","unknown")] [InlineData("vintage","2000")] [InlineData("extra","x")]
    public void RejectsOutOfOptionsOrUnknown(string key,string value) => Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),Change(key,JsonValue.Create(value),true)));
    [Theory] [InlineData("volumeMl","750.0")] [InlineData("volumeMl","true")] [InlineData("volumeMl","500")] [InlineData("priceMax","1.5")] [InlineData("priceMax","true")] [InlineData("priceMax","-1")] [InlineData("priceMax","1000000001")] [InlineData("availableOnly","1")]
    public void RejectsNumericTypeAndBounds(string key,string json) => Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),Change(key,JsonNode.Parse(json),true)));
    [Fact] public void RejectsDuplicateCaseAndMissingMember()
    { Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),Response().Replace("\"provider\":","\"Provider\":\"other\",\"provider\":")));var n=JsonNode.Parse(Response())!;n.AsObject().Remove("model");Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),n.ToJsonString())); }
    [Fact] public void RejectsDuplicateAndOversizedTerms()
    { foreach(var terms in new[]{new JsonArray("x","x"),new JsonArray(new string('x',101)),new JsonArray(Enumerable.Range(0,11).Select(i=>(JsonNode?)JsonValue.Create(i.ToString())).ToArray())}) Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),Change("keywords",terms,true))); }
    [Fact] public void RejectsBodyAndUtf16Overflow()
    { Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.ParseResponse(Request(),new string(' ',100001)));Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v",string.Concat(Enumerable.Repeat("😀",101)),Options()));Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.Empty,"v","q",Options())); }
    [Fact] public void RejectsInvalidOptionsAndCopiesInput()
    { var countries=new List<string>{"フランス"};var o=Options() with {Countries=countries};var r=FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v","q",o);countries.Add("x");Assert.Single(r.Options.Countries);Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v","q",o with {Suppliers=["AMZ","AMZ"]}));Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v","q",o with {VolumesMl=[0]})); }
    [Fact] public void UiOverridesAndResidualsStayLiteral()
    { var n=JsonNode.Parse(Response())!;n["filters"]!["keywords"]=new JsonArray("赤 1000円以下","x");n["unparsedTerms"]=new JsonArray("x","不明");n["filters"]!["supplier"]="AMZ";var r=FreeTextSearchContract.ParseResponse(Request(),n.ToJsonString());var ui=new CatalogFilter(Country:"イタリア",Region:"ブルゴーニュ",ProductType:"白",PriceMax:40000,AvailableOnly:true,Text:"ignored",VolumeMl:750,VintageRaw:"2019",PriceMin:100,Limit:33);var p=FreeTextSearchContract.Merge(ui,"LUC",r);Assert.Equal(ProductTypeVocabulary.MergeUi(ui,ui with {Text=null}),p.Filter);Assert.Equal("LUC",p.SupplierCode);Assert.Equal(new[]{"赤 1000円以下","x","不明"},p.Keywords); }
    [Fact] public void AvailabilityUncheckedIsUnspecifiedAndInvertedMergedRangeRejects()
    {var r=FreeTextSearchContract.ParseResponse(Request(),Response());Assert.True(FreeTextSearchContract.Merge(new(AvailableOnly:false),null,r).Filter.AvailableOnly);Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.Merge(new(PriceMin:40000),null,r));}
    [Fact] public void WireRequestHasOnlyContractFields()
    { using var d=JsonDocument.Parse(JsonSerializer.Serialize(Request())); Assert.Equal(new[]{"schema","requestId","catalogVersion","promptVersion","query","options"},d.RootElement.EnumerateObject().Select(p=>p.Name)); Assert.Equal(new[]{"countries","regions","productTypes","volumesMl","vintages","suppliers"},d.RootElement.GetProperty("options").EnumerateObject().Select(p=>p.Name)); }
    [Fact] public void RejectsOptionCountAndBodyOverflow()
    { var o=Options(); var huge=Enumerable.Range(0,1001).Select(i=>i.ToString()).ToArray(); Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v","q",o with {Suppliers=huge})); var bounded=Enumerable.Range(0,1000).Select(i=>i.ToString()+new string('x',190)).ToArray(); Assert.Throws<ArgumentException>(()=>FreeTextSearchContract.CreateRequest(Guid.NewGuid(),"v","q",o with {Suppliers=bounded})); }
    [Fact] public void NullAvailabilityAndBoundedPricesAreValid()
    { var n=JsonNode.Parse(Response())!; n["filters"]!["priceMin"]=0; n["filters"]!["priceMax"]=1000000000; n["filters"]!["availableOnly"]=null; var r=FreeTextSearchContract.ParseResponse(Request(),n.ToJsonString()); Assert.False(FreeTextSearchContract.Merge(new(),null,r).Filter.AvailableOnly); Assert.True(FreeTextSearchContract.Merge(new(AvailableOnly:true),null,r).Filter.AvailableOnly); }
}
