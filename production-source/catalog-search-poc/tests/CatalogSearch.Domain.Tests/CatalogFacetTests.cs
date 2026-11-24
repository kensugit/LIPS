using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class CatalogFacetTests
{
    private static readonly CatalogFacetRegionSource[] Pairs = [
        new("France", "Bordeaux"), new("フランス", "ボルドー"), new("ﾌﾗﾝｽ", "ボルドー地方"),
        new("France", "Pomerol"), new("チリ", "Pomerol"), new("チリ", "ボルドー"),
        new("フランス", "Chinon"), new("フランス", "ロワール"), new("", "Pomerol"),
        new("New Zealand", "Unknown"), new("ニュジーランド", "Unknown"), new("ニュージーランド", "Unknown"),
        new("XYZ", "Test"), new("xyz", "test")
    ];

    [Theory]
    [InlineData("France", "フランス")]
    [InlineData(" france ", "フランス")]
    [InlineData("ﾌﾗﾝｽ", "フランス")]
    [InlineData("ｲﾀﾘｱ", "イタリア")]
    [InlineData("ﾁﾘ", "チリ")]
    [InlineData("Deutschland", "ドイツ")]
    [InlineData("Japan", "日本")]
    [InlineData("New Zealand", "ニュージーランド")]
    [InlineData("ニュジーランド", "ニュージーランド")]
    [InlineData("Portugal", "ポルトガル")]
    [InlineData("ミナミアフリカ", "南アフリカ")]
    [InlineData("Unknown country", "Unknown country")]
    public void CountryAliasesAreReviewedExactNames(string raw, string expected) => Assert.Equal(expected, CatalogFacetVocabulary.Country(raw));

    [Fact]
    public void CanonicalCountriesRetainRawRepresentativesAndDoNotAlterSource()
    {
        var before = Pairs.ToArray(); var facets = CatalogFacetOptions.Build(Pairs, [0, 750, 750, 1500], ["2023", "2023年", "ＭＶ"]);
        var france = Assert.Single(facets.Countries, c => c.Label == "フランス");
        Assert.Equal("フランス", france.Value); Assert.Equal(3, france.RawValues.Count);
        Assert.Single(facets.Countries, c => c.Label == "ニュージーランド");
        Assert.Equal([750, 1500], facets.VolumesMl);
        Assert.Equal(before, Pairs);
    }

    [Fact]
    public void RegionAliasesStayWithinCountryAndMembershipDoesNotBecomeSynonym()
    {
        var facets = CatalogFacetOptions.Build(Pairs, [750], []);
        Assert.Single(facets.Regions, r => r.CountryLabel == "フランス" && r.Label == "ボルドー");
        Assert.Equal(2, facets.Regions.Count(r => r.Label == "Pomerol"));
        Assert.Single(facets.Regions, r => r.CountryLabel == "フランス" && r.Label == "ポムロール");
        Assert.Single(facets.Regions, r => r.Label == "シノン");
        Assert.Single(facets.Regions, r => r.Label == "ロワール");
        Assert.Single(facets.Regions, r => r.Label == "Test");
        Assert.Equal("Chinon", CatalogFacetOptions.Build([new("France", "Chinon")], [], []).Regions[0].Value);
    }

    [Fact]
    public void ReviewedRightBankAliasesMergeOnlyInsideEachCountryAndAppellation()
    {
        string[] pomerol = ["Pomerol", "ポムロール", "AOC ポムロール", "AOCポムロール"];
        var pairs = pomerol.Select(name => new CatalogFacetRegionSource("France", name)).Concat([new("チリ", "Pomerol")]);
        var facets = CatalogFacetOptions.Build(pairs, [], []);
        Assert.Equal(4, Assert.Single(facets.Regions, r => r.CountryLabel == "フランス").RawValues.Count);
        Assert.Single(facets.Regions, r => r.CountryLabel == "チリ" && r.Label == "Pomerol");
        Assert.Equal(pomerol, CatalogFacetVocabulary.MatchingRegions(pairs.Select(p => (p.Country, p.Region)), "France", ["ポムロール"]));
        Assert.Equal("サンテミリオン", CatalogFacetVocabulary.Region("France", "Saint-Emilion"));
        Assert.Equal("サンテミリオン・グラン・クリュ", CatalogFacetVocabulary.Region("France", "Saint-Emilion Grand Cru"));
        Assert.NotEqual(CatalogFacetVocabulary.Region("France", "Saint-Emilion"), CatalogFacetVocabulary.Region("France", "Saint-Emilion Grand Cru"));
        Assert.Equal(12, CatalogFacetVocabulary.RightBankAliasGroups.Count);
    }

    [Fact]
    public void UnknownCountryCaseVariantsShareTheSameLabelForDifferentRegions()
    {
        var facets = CatalogFacetOptions.Build([new("XYZ", "A"), new("xyz", "B")], [], []);
        var country = Assert.Single(facets.Countries);
        Assert.Equal(2, facets.Regions.Count);
        Assert.All(facets.Regions, region => {
            Assert.Equal(country.Value, region.Country);
            Assert.Equal(country.Label, region.CountryLabel);
        });
    }

    [Fact]
    public void SearchExpandsKnownCountryAndScopedRegionToAllRawValues()
    {
        Assert.Equal(["France", "フランス", "ﾌﾗﾝｽ"], CatalogFacetVocabulary.MatchingCountries(Pairs.Select(p => p.Country), "フランス"));
        var rows = Pairs.Select(p => (p.Country, p.Region));
        Assert.Equal(["Bordeaux", "ボルドー", "ボルドー地方"], CatalogFacetVocabulary.MatchingRegions(rows, "France", ["Bordeaux"]));
        Assert.Equal(["Pomerol"], CatalogFacetVocabulary.MatchingRegions(rows, "France", ["Pomerol"]));
        Assert.Empty(CatalogFacetVocabulary.MatchingRegions(rows, "チリ", ["Bordeaux"]));
        Assert.Equal(["ボルドー"], CatalogFacetVocabulary.MatchingRegions(rows, "チリ", ["ボルドー"]));
        Assert.Equal(["Bordeaux"], CatalogFacetVocabulary.MatchingRegions(rows, null, ["Bordeaux"]));
        Assert.Equal(["ロワール"], CatalogFacetVocabulary.MatchingRegions(rows, "France", ["Loire"]));
    }

    [Fact]
    public void VintageDuplicatesMergeButAnnotationsAndUnknownValuesRemainSeparate()
    {
        string[] raw = ["2023", "２０２３年", "(2023年)", "2023年", "ＮＶ", "MV", "NV(2024)", "NV\n(2019)", "NV (2019)", "202", "750", "0", "20年", "2020/2021"];
        var facets = CatalogFacetOptions.Build([], [0, 49, 95, 750], raw);
        var year = Assert.Single(facets.Vintages, v => v.Value == "2023"); Assert.Equal(4, year.RawValues.Count);
        Assert.Equal(2, Assert.Single(facets.Vintages, v => v.Value == "NV").RawValues.Count);
        Assert.Equal(2, Assert.Single(facets.Vintages, v => v.Value == "NV(2019)").RawValues.Count);
        Assert.Contains(facets.Vintages, v => v.Value == "NV(2024)");
        Assert.Contains(facets.Vintages, v => v.Value == "202"); Assert.Contains(facets.Vintages, v => v.Value == "750");
        Assert.Contains(facets.Vintages, v => v.Value == "20年"); Assert.Contains(facets.Vintages, v => v.Value == "2020/2021");
        Assert.Equal([49, 95, 750], facets.VolumesMl);
        Assert.Equal(raw.Take(4), VintageNotation.MatchingRawValues(raw, "2023"));
        Assert.Equal(["NV\n(2019)", "NV (2019)"], VintageNotation.MatchingRawValues(raw, "NV(2019)"));
    }
}
