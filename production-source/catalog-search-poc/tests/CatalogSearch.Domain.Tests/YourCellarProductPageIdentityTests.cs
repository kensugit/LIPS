using System.Text;
using CatalogSearch.Application;
using Xunit;

public sealed class YourCellarProductPageIdentityTests
{
    private static readonly ImporterProductIdentity Identity=new("SYNTHETIC","SKU-A","原本生産者","合成商品","2023",750,"赤");
    private const string Url="https://www.yourcellar.jp/view/item/000000001957?category_page_id=ct12";
    private static YourCellarProductPageIdentity Adapter()=>new("SYNTHETIC",new Dictionary<string,YourCellarProductPageIdentity.ProducerRule>{{"原本生産者",new("表示生産者","表示生産者（合成地域）")}});
    private const string Title="表示生産者 合成商品 2023 750ML";
    internal static string Page()=>"<html><body><div class='detail'><div class='item-detail'><div class='item-title'><p class='item-category-name'><a>表示生産者（合成地域）</a></p><p class='item-name'>"+Title+"</p></div><ul class='item-info-list'><li><dl><dt>独自商品コード</dt><dd>SKU-A</dd></dl></li></ul></div><div class='item-description-cont'><div class='item-description'><div class='description-wrap'><div class='desc-content'><p>"+Title+"<br>Producer Product</p></div><div class='desc-content'><p>合成説明</p></div><dl class='desc-spec'><dt>品種</dt><dd>サンジョヴェーゼ 100%</dd><dt>タイプ</dt><dd>赤ワイン</dd></dl></div></div></div></div></body></html>";
    [Fact] public void ObservedShapeSyntheticFixturePassesWithoutClaimingLiveIdentity()
    {Assert.True(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(Page()),out var result));Assert.Equal(new ImporterGrapeEvidence("サンジョヴェーゼ","サンジョヴェーゼ 100%"),Assert.Single(result));}
    [Theory]
    [InlineData("シュナン・ブラン", false)]
    [InlineData("シュナンブラン", false)]
    [InlineData("Chenin Blanc 50%", false)]
    [InlineData("Chenin Blanc 51%", true)]
    public void CheninGrapeFieldKeepsTheStrengthOfTheSourceClaim(string grape, bool predominant)
    {
        var page = Page().Replace("サンジョヴェーゼ 100%", grape).Replace("赤ワイン", "白ワイン");
        Assert.True(Adapter().TryExtract(Identity with { Variant = "白" }, Url, Encoding.UTF8.GetBytes(page), out var evidence));
        var claim = Assert.Single(evidence);
        Assert.Equal(grape, claim.SourceExcerpt);
        Assert.True(SemanticGrapeMatcher.Match(claim.SourceExcerpt, new("シュナン・ブラン", "contains")).Included);
        Assert.Equal(predominant, SemanticGrapeMatcher.Match(claim.SourceExcerpt, new("シュナン・ブラン", "predominant")).Included);
    }
    [Theory]
    [InlineData("year")][InlineData("volume")][InlineData("sku")][InlineData("color")][InlineData("duplicate")][InlineData("related")][InlineData("description")][InlineData("label")][InlineData("nested")][InlineData("template")][InlineData("percent")]
    public void RejectsContradictoryOrAmbiguousSections(string kind)
    {
        var page=Page();page=kind switch{
            "year"=>page.Replace("2023","2022"),"volume"=>page.Replace("750ML","1500ML"),"sku"=>page.Replace("SKU-A","SKU-B"),"color"=>page.Replace("赤ワイン","白ワイン"),
            "duplicate"=>page.Replace("<dt>品種</dt>","<dt>品種</dt><dd>別品種 80%</dd><dt>品種</dt>"),
            "related"=>page.Replace("<dt>品種</dt><dd>サンジョヴェーゼ 100%</dd>","").Replace("</body>","<aside><dt>品種</dt><dd>サンジョヴェーゼ 100%</dd></aside></body>"),
            "description"=>page.Replace("<p>"+Title,"<p>別生産者 別商品 2023 750ML"),"label"=>page.Replace("独自商品コード","別コード"),
            "nested"=>page.Replace("</body>","<div class='detail'></div></body>"),"template"=>page.Replace("</body>","<template>hidden</template></body>"),"percent"=>page.Replace("100%","101%"),_=>page};
        Assert.False(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(page),out _));
    }
    [Fact] public void SupplierAndUrlAndClaimAreBound()
    {
        var bytes=Encoding.UTF8.GetBytes(Page());var a=Adapter();Assert.False(a.TryExtract(Identity with{SupplierCode="OTHER"},Url,bytes,out _));
        Assert.False(a.TryExtract(Identity,Url.Replace("yourcellar.jp","yourcellar.jp.evil.test"),bytes,out _));
        Assert.False(a.TryExtract(Identity,Url+"&other=x",bytes,out _));
        Assert.False(a.Verify(Identity,new[]{new ImporterGrapeEvidence("メルロー","メルロー 100%")},Url,bytes));
    }
    [Fact] public void InvalidJsonLdDoesNotSupplyEvidence()
    {var page=Page().Replace("</body>","<script type='application/ld+json'>{broken</script></body>");Assert.True(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(page),out _));}

    [Fact] public void OutsideNoScriptIsIgnoredOnlyAfterBalancedBoundaryCheck()
    {
        var page=Page().Replace("<body>","<body><noscript><iframe src='https://example.invalid/tracker'></iframe><img alt='different product 2022 1500ML'></noscript>");
        Assert.True(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(page),out var grapes));
        Assert.Equal("サンジョヴェーゼ 100%",Assert.Single(grapes).SourceExcerpt);
    }
    [Theory]
    [InlineData("inside")][InlineData("wraps")][InlineData("nested")][InlineData("unclosed")][InlineData("crossed")][InlineData("fake-detail")][InlineData("template-outside")]
    public void NoScriptAmbiguousOrProductBoundariesReject(string kind)
    {
        var page=kind switch{
            "inside"=>Page().Replace("<div class='item-detail'>","<noscript>fallback</noscript><div class='item-detail'>"),
            "wraps"=>"<noscript>"+Page()+"</noscript>",
            "nested"=>Page().Replace("<body>","<body><noscript><noscript>fallback</noscript></noscript>"),
            "unclosed"=>Page().Replace("<body>","<body><noscript>fallback"),
            "crossed"=>Page().Replace("<body>","<body><noscript><div></noscript></div>"),
            "fake-detail"=>Page().Replace("<body>","<body><noscript><section class='detail'>fallback</section></noscript>"),
            _=>Page().Replace("<body>","<body><template>fallback</template>")};
        Assert.False(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(page),out _));
    }
    [Fact] public void OutsideNoScriptCannotSupplyMissingGrapes()
    {
        var page=Page().Replace("<dt>品種</dt><dd>サンジョヴェーゼ 100%</dd>","").Replace("<body>","<body><noscript><dl><dt>品種</dt><dd>サンジョヴェーゼ 100%</dd></dl></noscript>");
        Assert.False(Adapter().TryExtract(Identity,Url,Encoding.UTF8.GetBytes(page),out _));
    }
    [Fact] public void UnrelatedDecoratedValueIsNotAdopted()
    {
        var html=Page().Replace("<dt>タイプ</dt>","<dt>評価</dt><dd><a>untrusted unrelated value</a><br>other</dd><dt>タイプ</dt>");
        Assert.True(Adapter().TryExtractDiagnosed(Identity,Url,Encoding.UTF8.GetBytes(html),out var grapes,out var diagnostic));
        Assert.All(diagnostic.Predicates.Values,v=>Assert.True(v));
        Assert.DoesNotContain("untrusted",System.Text.Json.JsonSerializer.Serialize(diagnostic));
        Assert.Single(grapes);
    }
    [Theory]
    [InlineData("grape-link","GRAPE_DD")][InlineData("grape-br","GRAPE_DD")][InlineData("type-link","TYPE_DD")]
    [InlineData("duplicate-other","SPEC_DL_SHAPE")][InlineData("wrong-order","SPEC_DL_SHAPE")]
    public void RequiredValuesAndPairShapeRemainStrict(string kind,string stage)
    {
        var html=kind switch{
            "grape-link"=>Page().Replace("サンジョヴェーゼ 100%","<a>サンジョヴェーゼ 100%</a>"),
            "grape-br"=>Page().Replace("サンジョヴェーゼ 100%","サンジョヴェーゼ<br>100%"),
            "type-link"=>Page().Replace("赤ワイン","<a>赤ワイン</a>"),
            "duplicate-other"=>Page().Replace("<dt>タイプ</dt>","<dt>評価</dt><dd><a>x</a></dd><dt>評価</dt><dd>y</dd><dt>タイプ</dt>"),
            _=>Page().Replace("<dt>タイプ</dt><dd>赤ワイン</dd>","<dd>赤ワイン</dd><dt>タイプ</dt>")};
        Assert.False(Adapter().TryExtractDiagnosed(Identity,Url,Encoding.UTF8.GetBytes(html),out var grapes,out var d));
        Assert.Empty(grapes);Assert.Equal(stage,d.Stage);Assert.False(d.Predicates[stage]);
    }
    [Fact] public void IndependentFailuresAreCollectedWithoutChangingDescriptionRules()
    {
        var html=Page().Replace("SKU-A","wrong").Replace("<p>"+Title,"<p>other product").Replace("赤ワイン","白ワイン");
        Assert.False(Adapter().TryExtractDiagnosed(Identity,Url,Encoding.UTF8.GetBytes(html),out _,out var d));
        Assert.False(d.Predicates["SKU_VALUE"]);Assert.False(d.Predicates["DESCRIPTION_TITLE"]);Assert.False(d.Predicates["TYPE_VALUE"]);
        Assert.True(d.Predicates["GRAPE_VALUE"]);
    }
}
