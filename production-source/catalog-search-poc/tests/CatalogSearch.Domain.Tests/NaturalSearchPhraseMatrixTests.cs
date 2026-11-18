using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class NaturalSearchPhraseMatrixTests
{
    public static IEnumerable<object[]> ItalianRanges => new[]
    {
        "イタリアの赤ワインで2500円から4000円",
        "イタリア産の赤で２５００円～４０００円まで",
        "イタリアの赤で2,500-4,000円",
        "国はイタリア、タイプは赤、価格は2500円から4000円",
        "イタリアの赤で二千五百円から四千円",
        "イタリアの赤で2千500円から4千円",
        "2500円から4000円でイタリアの赤を探してください",
        "イタリアの赤を2500円から4000円でお願いします",
        "イタリア産、赤、2500〜4000円",
        "2500円以上4000円以下のイタリアの赤",
    }.Select(x => new object[] { x });

    [Theory, MemberData(nameof(ItalianRanges))]
    public void EquivalentItalianPhrases(string text)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.Equal("イタリア", p.Filter.Country);
        Assert.Equal("赤", p.Filter.ProductType);
        Assert.Equal(2500m, p.Filter.PriceMin);
        Assert.Equal(4000m, p.Filter.PriceMax);
        Assert.Equal("", p.Filter.Text);
    }

    [Theory]
    [InlineData("ブルゴーニュの白で15000円以内")]
    [InlineData("ブルゴーニュ産の白ワインで1万5千円以下")]
    [InlineData("産地はブルゴーニュ、白、予算15000円")]
    [InlineData("ブルゴーニュの白で一万五千円以下")]
    [InlineData("ブルゴーニュ地方の白を15000円以下で探してください")]
    [InlineData("予算は1.5万円でブルゴーニュの白")]
    public void EquivalentBurgundyPhrases(string text)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.Equal("白", p.Filter.ProductType);
        Assert.Equal(15000m, p.Filter.PriceMax);
        Assert.Equal("ブルゴーニュ", p.Filter.Text);
    }

    [Theory]
    [InlineData("容量は1500mlのみのシャンパンを3万円以下で探してください")]
    [InlineData("1.5リットルのシャンパーニュを予算3万円でお願いします")]
    [InlineData("シャンパン、150cl、3万円以内")]
    [InlineData("容量:1500ml シャンパーニュ 30,000円以下")]
    public void CapacityAndRequestWords(string text)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.True(p.Champagne);
        Assert.Equal(1500, p.Filter.VolumeMl);
        Assert.Equal(30000m, p.Filter.PriceMax);
        Assert.Equal("", p.Filter.Text);
    }

    [Theory]
    [InlineData("在庫ありのフランスの白で5000円以下")]
    [InlineData("在庫のあるフランス産の白で5000円以内")]
    [InlineData("フランスの白で5000円以下、在庫有りのみ")]
    [InlineData("フランスの白で5000円以下で在庫があるもの")]
    public void AvailabilityPhrases(string text)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.True(p.Filter.AvailableOnly);
        Assert.Equal("フランス", p.Filter.Country);
        Assert.Equal("白", p.Filter.ProductType);
        Assert.Equal(5000m, p.Filter.PriceMax);
        Assert.Equal("", p.Filter.Text);
    }

    [Theory]
    [InlineData("白馬", "白馬")]
    [InlineData("赤坂", "赤坂")]
    [InlineData("白い花", "白い花")]
    [InlineData("Pinot Noir", "Pinot Noir")]
    [InlineData("ルイのワイン", "ルイのワイン")]
    [InlineData("a%_\\", "a%_\\")]
    public void UnstructuredNamesArePreserved(string text, string expected)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.Equal(expected, p.Filter.Text);
        Assert.Null(p.Filter.ProductType);
    }

    public static IEnumerable<object[]> PermutedConditions
    {
        get
        {
            var tokens = new[] { "イタリア産", "赤ワイン", "2500円から4000円" };
            foreach (var a in Enumerable.Range(0, 3))
            foreach (var b in Enumerable.Range(0, 3).Where(x => x != a))
            foreach (var separator in new[] { "の", "で", " ", "、" })
                yield return new object[] { string.Join(separator, tokens[a], tokens[b], tokens[3 - a - b]) };
        }
    }

    [Theory, MemberData(nameof(PermutedConditions))]
    public void WordOrderDoesNotChangeFilters(string text) => EquivalentItalianPhrases(text);

    [Theory]
    [InlineData("3000円未満", false, true)]
    [InlineData("3000円より安い", false, true)]
    [InlineData("3000円超", true, false)]
    [InlineData("3000円より高い", true, false)]
    [InlineData("2500円から3000円未満", false, true)]
    public void ExclusiveBounds(string text, bool lower, bool upper)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.Equal(lower, p.MinExclusive);
        Assert.Equal(upper, p.MaxExclusive);
        Assert.Equal("", p.Filter.Text);
    }

    [Fact] public void StrongerInclusiveBoundOverridesWeakerExclusiveBound()
    {
        var p = NaturalSearch.Parse(new(Text: "2500円超4000円未満", PriceMin: 3000, PriceMax: 3500));
        Assert.False(p.MinExclusive);
        Assert.False(p.MaxExclusive);
        Assert.Equal(3000m, p.Filter.PriceMin);
        Assert.Equal(3500m, p.Filter.PriceMax);
        Assert.Throws<ArgumentException>(() => NaturalSearch.Parse(new(Text: "3000円以上3000円未満")));
    }

    [Theory]
    [InlineData("イタリアの泡で5000円以下")]
    [InlineData("イタリアのスパークリングワインで5000円以下")]
    public void SparklingAliases(string text)
    {
        var p = NaturalSearch.Parse(new(Text: text));
        Assert.True(p.Sparkling);
        Assert.Equal("イタリア", p.Filter.Country);
        Assert.Equal(5000m, p.Filter.PriceMax);
        Assert.Equal("", p.Filter.Text);
    }

    [Fact] public void SparklingAndColorAreCombined()
    {
        var p = NaturalSearch.Parse(new(Text: "フランスのロゼのスパークリングで5000円以下"));
        Assert.True(p.Sparkling);
        Assert.Equal("ロゼ", p.Filter.ProductType);
        Assert.Equal("", p.Filter.Text);
    }
}
