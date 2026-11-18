using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class NaturalSearchTests
{
    [Theory]
    [InlineData("容量1500mlの３万円以下のシャンパーニュ")]
    [InlineData("シャンパンで1.5L、30,000円以内")]
    [InlineData("１５００ｍｌの３００００円までのＣｈａｍｐａｇｎｅを探してください")]
    [InlineData("150cl 3万円以下 シャンパーニュ地方")]
    public void RequestedSearch(string text)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Equal(1500, result.Filter.VolumeMl);
        Assert.Equal(30000m, result.Filter.PriceMax);
        Assert.True(result.Champagne);
        Assert.Equal("", result.Filter.Text);
    }

    [Fact] public void CombinesPriceAndAvailabilityAndKeepsProducer()
    {
        var result = NaturalSearch.Parse(new(Text: "在庫ありの1500mlの1万5000円以上3万円以下のシャンパーニュ ボランジェ"));
        Assert.Equal(15000m, result.Filter.PriceMin);
        Assert.Equal(30000m, result.Filter.PriceMax);
        Assert.True(result.Filter.AvailableOnly);
        Assert.Equal("ボランジェ", result.Filter.Text);
    }

    [Theory]
    [InlineData("Pinot Noir")]
    [InlineData("FINESSE-0001")]
    [InlineData("ルイのワイン")]
    [InlineData("a%_\\")]
    public void PreservesPlainKeywords(string text) => Assert.Equal(text, NaturalSearch.Parse(new(Text: text)).Filter.Text);

    [Theory]
    [InlineData("1500ml", 750, null, null)]
    [InlineData("1500ml 750ml", null, null, null)]
    [InlineData("3万円以下", null, 40000, null)]
    [InlineData("4万円以上", null, null, 30000)]
    [InlineData("0ml", null, null, null)]
    [InlineData("0.0001L", null, null, null)]
    [InlineData("税込3万円以下", null, null, null)]
    [InlineData("仕入価格3万円以下", null, null, null)]
    [InlineData("シャンパーニュ以外", null, null, null)]
    public void RejectsConflictsAndUnsupportedConditions(string text, int? volume, int? min, int? max) =>
        Assert.Throws<ArgumentException>(() => NaturalSearch.Parse(new(Text: text, VolumeMl: volume, PriceMin: min, PriceMax: max)));

    [Fact] public void IntersectsManualPrices()
    {
        var result = NaturalSearch.Parse(new(Text: "1万円以上3万円以下", PriceMin: 15000, PriceMax: 25000));
        Assert.Equal(15000m, result.Filter.PriceMin);
        Assert.Equal(25000m, result.Filter.PriceMax);
    }

    [Fact] public void CountryTypeAndRemainingRegion()
    {
        var result = NaturalSearch.Parse(new(Text: "フランスのブルゴーニュの750mlの5000円以下の赤ワイン"));
        Assert.Equal("フランス", result.Filter.Country);
        Assert.Equal("赤", result.Filter.ProductType);
        Assert.Equal("ブルゴーニュ", result.Filter.Text);
    }

    [Theory]
    [InlineData("ブルゴーニュの白で15000円以内", "白")]
    [InlineData("ブルゴーニュの赤で15000円以内", "赤")]
    [InlineData("ブルゴーニュのロゼで15000円以内", "ロゼ")]
    [InlineData("ブルゴーニュのオレンジで15000円以内", "オレンジ")]
    [InlineData("ブルゴーニュ 白 15000円以内", "白")]
    [InlineData("15000円以内のブルゴーニュの白", "白")]
    [InlineData("ブルゴーニュの白ワインで15000円以内", "白")]
    [InlineData("ブルゴーニュの白を15000円以内で", "白")]
    public void ShortTypeNamesInPhrases(string text, string type)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Equal(type, result.Filter.ProductType);
        Assert.Equal(15000m, result.Filter.PriceMax);
        Assert.Equal("ブルゴーニュ", result.Filter.Text);
    }

    [Theory]
    [InlineData("白馬")]
    [InlineData("赤坂")]
    [InlineData("白い花")]
    [InlineData("赤い果実")]
    [InlineData("オレンジピール")]
    public void ColorInsideNamesIsNotAType(string text)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Null(result.Filter.ProductType);
        Assert.Equal(text, result.Filter.Text);
    }

    [Fact] public void ShortTypeConflictsAreRejected() =>
        Assert.Throws<ArgumentException>(() => NaturalSearch.Parse(new(Text: "ブルゴーニュの白で15000円以内", ProductType: "赤")));

    [Fact] public void BareWhiteIsAType() => Assert.Equal("白", NaturalSearch.Parse(new(Text: "白")).Filter.ProductType);

    [Theory]
    [InlineData("イタリアの赤ワインで2500円から4000円")]
    [InlineData("イタリアの赤で２５００円から４０００円まで")]
    [InlineData("イタリアの赤ワインで2,500円から4,000円以内")]
    [InlineData("イタリアの赤で2500〜4000円")]
    [InlineData("イタリアの赤で2500円～4000円")]
    [InlineData("イタリアの赤で0.25万円から0.4万円")]
    [InlineData("2500円から4000円のイタリアの赤ワイン")]
    public void PriceRanges(string text)
    {
        var result = NaturalSearch.Parse(new(Text: text));
        Assert.Equal("イタリア", result.Filter.Country);
        Assert.Equal("赤", result.Filter.ProductType);
        Assert.Equal(2500m, result.Filter.PriceMin);
        Assert.Equal(4000m, result.Filter.PriceMax);
        Assert.Equal("", result.Filter.Text);
    }

    [Theory]
    [InlineData("4000円から2500円")]
    [InlineData("2500円から4000円以上")]
    [InlineData("税込2500円から4000円")]
    public void InvalidRangesAreRejected(string text) => Assert.Throws<ArgumentException>(() => NaturalSearch.Parse(new(Text: text)));

    [Fact] public void RangeIntersectsManualFilters()
    {
        var result = NaturalSearch.Parse(new(Text: "2500円から4000円", PriceMin: 3000, PriceMax: 3500));
        Assert.Equal(3000m, result.Filter.PriceMin);
        Assert.Equal(3500m, result.Filter.PriceMax);
        Assert.Throws<ArgumentException>(() => NaturalSearch.Parse(new(Text: "2500円から4000円", PriceMin: 5000)));
    }

    [Fact] public void RangeRetainsResidualKeywords()
    {
        var result = NaturalSearch.Parse(new(Text: "イタリアの赤ワインで2500円から4000円 トスカーナ"));
        Assert.Equal("トスカーナ", result.Filter.Text);
    }
}
