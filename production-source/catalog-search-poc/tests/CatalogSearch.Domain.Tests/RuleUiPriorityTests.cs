using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class RuleUiPriorityTests
{
    [Fact]
    public void ExplicitUiReplacesConflictingParsedFiltersWithoutReparsingResidual()
    {
        var parsed = new NaturalSearchPlan(new CatalogFilter(Country: "フランス", ProductType: "赤", Text: "白%_", PriceMin: 1000, PriceMax: 2000), false, [], true, true);
        var actual = CatalogQuery.MergeRulesWithUi(parsed, new CatalogFilter(Country: "日本", ProductType: "白", PriceMin: 3000, PriceMax: 5000));
        Assert.Equal("日本", actual.Filter.Country);
        Assert.Equal("白", actual.Filter.ProductType);
        Assert.Equal("白%_", actual.Filter.Text);
        Assert.Equal(3000m, actual.Filter.PriceMin);
        Assert.Equal(5000m, actual.Filter.PriceMax);
        Assert.False(actual.MinExclusive);
        Assert.False(actual.MaxExclusive);
    }

    [Fact]
    public void ExplicitColorPreservesParsedSparklingAxis()
    {
        var parsed = new NaturalSearchPlan(new CatalogFilter(ProductType: "泡"), true, [], Sparkling: true);
        var actual = CatalogQuery.MergeRulesWithUi(parsed, new CatalogFilter(ProductType: "白"));
        Assert.True(actual.Sparkling);
        Assert.Equal("スパークリング",actual.Filter.Effervescence);
        Assert.False(actual.Champagne);
        Assert.Equal("白", actual.Filter.ProductType);
    }

    [Fact]
    public void ExplicitStillAxisClearsParsedSparkling()
    {
        var parsed=new NaturalSearchPlan(new CatalogFilter(ProductType:"泡"),false,[],Sparkling:true);
        var actual=CatalogQuery.MergeRulesWithUi(parsed,new CatalogFilter(Effervescence:"スティル"));
        Assert.False(actual.Sparkling);Assert.Equal("スティル",actual.Filter.Effervescence);
    }

    [Fact]
    public void UnspecifiedUiPreservesRuleSemanticsAndExclusivePrice()
    {
        var parsed = new NaturalSearchPlan(new CatalogFilter(Text: "シャトー", PriceMin: 1000, AvailableOnly: true), true, [], true, Sparkling: true);
        var actual = CatalogQuery.MergeRulesWithUi(parsed, new CatalogFilter());
        Assert.True(actual.Champagne);
        Assert.True(actual.Sparkling);
        Assert.True(actual.MinExclusive);
        Assert.True(actual.Filter.AvailableOnly);
        Assert.Equal("シャトー", actual.Filter.Text);
    }
}
