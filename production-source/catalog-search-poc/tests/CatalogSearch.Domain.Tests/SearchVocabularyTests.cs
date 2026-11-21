using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

public sealed class SearchVocabularyTests
{
    [Theory]
    [InlineData("在庫が有るレッドワイン", "赤", true)]
    [InlineData("在庫ありのホワイトワイン", "白", true)]
    [InlineData("rosé wine", "ロゼ", false)]
    [InlineData("orange wine", "オレンジ", false)]
    public void ExactTypeAliasesReuseStoredFilters(string text, string type, bool available)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Equal(type, result.Filter.ProductType);
        Assert.Equal(available, result.Filter.AvailableOnly);
        Assert.Equal("", result.Filter.Text);
    }

    [Theory]
    [InlineData("ＮＶの発泡性ワイン")]
    [InlineData("ノン・ヴィンテージのスパークリング")]
    public void NonVintageAndSparklingKeepSeparatePredicates(string text)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Equal("NV", result.Filter.VintageRaw);
        Assert.True(result.Sparkling);
        Assert.Equal("", result.Filter.Text);
        Assert.False(result.Champagne);
    }

    [Fact]
    public void PlainSkuNegationAndUnknownYearAreNotInferred()
    {
        Assert.Null(NaturalSearch.Parse(new(Text: "NV-123")).Filter.VintageRaw);
        Assert.False(NaturalSearch.Parse(new(Text: "在庫有無")).Filter.AvailableOnly);
        Assert.False(NaturalSearch.Parse(new(Text: "在庫なし")).Filter.AvailableOnly);
        Assert.Equal(["ＮＶ", "ノンヴィンテージ"], VintageNotation.MatchingRawValues(
            ["ＮＶ", "ノンヴィンテージ", "", "不明", "202"], "NV"));
        Assert.False(NaturalSearch.Parse(new(Text: "PanoramaSparkling Wine")).Sparkling);
        Assert.True(NaturalSearch.Parse(new(Text: "シャンパン")).Champagne);
    }
}
