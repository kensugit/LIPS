using System.Text;
using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class ToyotsuProductPageIdentityTests
{
    public static ToyotsuProductPageIdentity Adapter() => new("TOYOTSU",new Dictionary<string,string>{{"クーリー デュテイユ","クーリー デュテイユ"}});
    public static ImporterProductIdentity Identity(string sku) => new("TOYOTSU",sku,"クーリー デュテイユ",
        sku=="FE4113"?"シノン レ グラヴィエール":"シノン クロ ド レコー",sku=="FE4113"?"2023":"2018",750,"赤");
    public static byte[] Fixture(string sku)
    {
        using var source=typeof(ToyotsuProductPageIdentityTests).Assembly.GetManifestResourceStream("CatalogSearch.Domain.Tests.Fixtures.Toyotsu."+sku+".min.html")!;
        using var memory=new MemoryStream();source.CopyTo(memory);return memory.ToArray();
    }
    [Theory][InlineData("FE4113")][InlineData("FE4218")]
    public void ReducedObservedDomExtractsOnlyProductHundredPercent(string sku)
    {
        var adapter=Adapter();var id=Identity(sku);var page=Fixture(sku);var url="https://toyotsu-wine.com/shop/g/g"+sku+"/";
        Assert.True(adapter.TryExtract(id,url,page,out var grapes));var claim=Assert.Single(grapes);
        Assert.Equal("カベルネ・フラン",claim.Grape);Assert.True(SemanticGrapeMatcher.Match(claim.SourceExcerpt,new("カベルネ・フラン","predominant")).Included);
        Assert.True(adapter.Verify(id,grapes,url,page));Assert.DoesNotContain("Merlot",claim.SourceExcerpt);
    }
    [Theory][InlineData("producer")][InlineData("product")][InlineData("vintage")][InlineData("volume")][InlineData("sku")][InlineData("variant")]
    public void EveryIdentityAxisMustAgree(string axis)
    {
        var id=Identity("FE4113");id=axis switch{"producer"=>id with{Producer="別生産者"},"product"=>id with{Product="別銘柄"},"vintage"=>id with{Vintage="2022"},"volume"=>id with{VolumeMl=1500},"sku"=>id with{Sku="FE4218"},_=>id with{Variant="白"}};
        Assert.False(Adapter().TryExtract(id,"https://toyotsu-wine.com/shop/g/gFE4113/",Fixture("FE4113"),out _));
    }
    [Fact]public void ProducerAndRelatedHundredPercentCannotFillMissingProductClaim()
    {
        var page=Encoding.UTF8.GetString(Fixture("FE4113")).Replace("カベルネ・フラン100%によるキュヴェ。","説明未確認。",StringComparison.Ordinal)
            .Replace("Merlot 100%","カベルネ・フラン100%",StringComparison.Ordinal);
        Assert.False(Adapter().TryExtract(Identity("FE4113"),"https://toyotsu-wine.com/shop/g/gFE4113/",Encoding.UTF8.GetBytes(page),out _));
    }
    [Theory][InlineData("100%ではなく50%による")][InlineData("約100%による")][InlineData("100%による別の商品")]
    public void QualifiedProductDescriptionIsNotAdopted(string claim)
    {
        var page=Encoding.UTF8.GetString(Fixture("FE4113")).Replace("100%による",claim,StringComparison.Ordinal);
        Assert.False(Adapter().TryExtract(Identity("FE4113"),"https://toyotsu-wine.com/shop/g/gFE4113/",Encoding.UTF8.GetBytes(page),out _));
    }
    [Theory][InlineData("http://toyotsu-wine.com/shop/g/gFE4113/")][InlineData("https://toyotsu-wine.com/shop/g/gFE4113/?x=1")][InlineData("https://evil.example/shop/g/gFE4113/")]
    public void OnlyExactCanonicalOfficialUrlIsAccepted(string url) => Assert.False(Adapter().TryExtract(Identity("FE4113"),url,Fixture("FE4113"),out _));

    [Theory]
    [InlineData("second-before")][InlineData("second-after")][InlineData("unsupported-marker")]
    [InlineData("duplicate-class")][InlineData("quoted-only")][InlineData("inside-noscript")]
    [InlineData("noscript-inside")][InlineData("broken-inside")]
    public void ProductBoundaryCannotBeManufacturedOrAmbiguous(string kind)
    {
        var html=Encoding.UTF8.GetString(Fixture("FE4113"));
        html=kind switch{
            "second-before"=>html.Replace("<body>","<body><div class='block-detail'></div>"),
            "second-after"=>html.Replace("</body>","<div class='block-detail'></div></body>"),
            "unsupported-marker"=>html.Replace("</body>","<section class='block-detail'></section></body>"),
            "duplicate-class"=>html.Replace("class=\"block-detail\"","class=\"block-detail\" CLASS=\"block-detail\""),
            "quoted-only"=>html.Replace("class=\"block-detail\"","title=\"<div class='block-detail'>\""),
            "inside-noscript"=>html.Replace("<body>","<body><noscript>").Replace("</body>","</noscript></body>"),
            "noscript-inside"=>html.Replace("<div class=\"block-detail\">","<div class=\"block-detail\"><noscript>hidden</noscript>"),
            _=>html.Replace("</h1>","</span>")};
        Assert.False(Adapter().TryExtract(Identity("FE4113"),"https://toyotsu-wine.com/shop/g/gFE4113/",Encoding.UTF8.GetBytes(html),out _));
    }
    [Fact] public void UnrelatedTrailingClosingTagDoesNotRepairProductTree()
    {
        var html=Encoding.UTF8.GetString(Fixture("FE4113")).Replace("</body>","</div></body>");
        Assert.True(Adapter().TryExtract(Identity("FE4113"),"https://toyotsu-wine.com/shop/g/gFE4113/",Encoding.UTF8.GetBytes(html),out _));
    }
}
