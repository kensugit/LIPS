using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public class ProductTypeVocabularyTests
{
    [Theory]
    [InlineData("白甘口")][InlineData("白甘")][InlineData("白・甘")][InlineData("白・甘口")]
    public void ReviewedSweetWhiteAliasesSharePredicate(string raw)
    {
        var a=ProductTypeVocabulary.Classify(raw);Assert.Equal("ワイン",a.Category);Assert.Equal("白",a.Color);Assert.Equal("甘口",a.Sweetness);Assert.Null(a.Effervescence);
        Assert.Equal(ProductTypeVocabulary.GroupKey("白甘口"),ProductTypeVocabulary.GroupKey(raw));
    }
    [Theory][InlineData("泡・白")][InlineData("白/泡")][InlineData("白泡")][InlineData("泡白")]
    public void WhiteSparklingIsNotExcludedByColor(string raw)
    {
        var a=ProductTypeVocabulary.Classify(raw);Assert.Equal("白",a.Color);Assert.Equal("スパークリング",a.Effervescence);
        Assert.True(ProductTypeVocabulary.Matches(raw,ProductTypeVocabulary.Resolve(new(Color:"白"))));
        Assert.False(ProductTypeVocabulary.Matches(raw,ProductTypeVocabulary.Resolve(new(Color:"白",Effervescence:"スティル"))));
    }
    [Theory][InlineData("発泡酒")][InlineData("白ビール")][InlineData("非ワイン")][InlineData("甘口ではありません")][InlineData("redwood")]
    public void UnknownResidualDoesNotBecomeWine(string raw)
    {var a=ProductTypeVocabulary.Classify(raw);Assert.Null(a.Category);Assert.Null(a.Color);Assert.Null(a.Effervescence);Assert.Null(a.Sweetness);Assert.NotEmpty(a.UnknownTokens);}
    [Fact]public void UnreviewedMediumDryRemainsUnknown(){var a=ProductTypeVocabulary.Classify("白中辛");Assert.Equal("白",a.Color);Assert.Null(a.Sweetness);Assert.Null(a.Category);Assert.Contains("中辛",a.UnknownTokens);}
    [Fact]public void FruitWineCategoryDoesNotClaimSweet(){var a=ProductTypeVocabulary.Classify("赤・甘味果実酒");Assert.Equal("甘味果実酒",a.Category);Assert.Equal("赤",a.Color);Assert.Null(a.Sweetness);}
    [Fact]public void AmbiguousColorsRemainConflict(){var a=ProductTypeVocabulary.Classify("赤白");Assert.Null(a.Color);Assert.Contains("color",a.Conflicts);}
    [Fact]public void SparklingSweetDoesNotGuessColor(){var a=ProductTypeVocabulary.Classify("泡甘");Assert.Null(a.Color);Assert.Equal("甘口",a.Sweetness);Assert.Equal("スパークリング",a.Effervescence);}
    [Fact]public void DistilledAliasesShareCategory(){Assert.Equal(ProductTypeVocabulary.GroupKey("蒸留"),ProductTypeVocabulary.GroupKey("蒸留酒"));Assert.Equal("蒸留酒",ProductTypeVocabulary.Classify("蒸留").Category);}
    [Fact]public void BubblesStaySeparate(){Assert.Equal("微発泡",ProductTypeVocabulary.Classify("微発泡").Effervescence);Assert.Equal(ProductTypeVocabulary.GroupKey("非発泡"),ProductTypeVocabulary.GroupKey("非発泡性"));Assert.Equal(ProductTypeVocabulary.GroupKey("発泡"),ProductTypeVocabulary.GroupKey("発泡性"));}
    [Fact]public void IndependentUiAxisOverridesOnlySameTextAxis(){var f=ProductTypeVocabulary.MergeUi(new(Color:"赤"),new(ProductType:"白泡",Sweetness:"甘口"));Assert.Equal("赤",f.Color);Assert.Equal("スパークリング",f.Effervescence);Assert.Equal("甘口",f.Sweetness);}
    [Fact]public void ConflictingTwoUiInputsAreRejected(){Assert.Throws<ArgumentException>(()=>ProductTypeVocabulary.Resolve(new(ProductType:"白甘口",Color:"赤")));}
    [Fact]public void TypeSweetnessAndDescriptionConflictIsNotOr(){Assert.True(WineTasteEvidence.Matches("{}","甘口","白甘"));Assert.False(WineTasteEvidence.Matches("{\"Description\":\"辛口\"}","甘口","白甘"));Assert.False(WineTasteEvidence.Matches("{\"Description\":\"甘口の香り\"}","甘口","白"));}
    [Fact]public void UnknownOpaqueTypesKeepSeparators(){Assert.NotEqual(ProductTypeVocabulary.GroupKey("A/B"),ProductTypeVocabulary.GroupKey("AB"));Assert.False(ProductTypeVocabulary.Matches("AB",ProductTypeVocabulary.Resolve(new(ProductType:"A/B"))));Assert.True(ProductTypeVocabulary.Matches("Ａ/Ｂ",ProductTypeVocabulary.Resolve(new(ProductType:"A/B"))));}
    [Theory][InlineData("微発泡の白ワイン","微発泡")][InlineData("非発泡性の白ワイン","スティル")][InlineData("スパークリングの白ワイン","スパークリング")]
    public void NaturalQueryConsumesTypeTermsWithoutNameResidual(string text,string eff){var p=NaturalSearch.Parse(new(Text:text));Assert.Equal(eff,p.Filter.Effervescence);Assert.Equal("白",p.Filter.Color);Assert.True(string.IsNullOrWhiteSpace(p.Filter.Text));}
    [Fact]public void QuerySweetWhiteKeepsPriceAndStock(){var p=NaturalSearch.Parse(new(Text:"白甘口 上代1000円以内 在庫あり"));Assert.Equal("甘口",p.Filter.Sweetness);Assert.Equal("白",p.Filter.Color);Assert.Equal(1000,p.Filter.PriceMax);Assert.True(p.Filter.AvailableOnly);Assert.True(string.IsNullOrWhiteSpace(p.Filter.Text));}
    [Theory][InlineData("微発泡")][InlineData("蒸留酒")][InlineData("甘口")][InlineData("非発泡性")]
    public void StandaloneAttributeIsAParsedCondition(string text){var p=NaturalSearch.Parse(new(Text:text));Assert.NotEmpty(p.Conditions);Assert.True(string.IsNullOrWhiteSpace(p.Filter.Text));}
    [Theory][InlineData("赤のワインで白")][InlineData("白で甘口と辛口")][InlineData("微発泡でスパークリング")]
    public void SeparatePhraseConflictsDoNotOverwrite(string text){Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:text)));}
    [Fact]public void ExplicitUiResolvesOnlyItsOwnConflictingAxis(){var p=NaturalSearch.Parse(new(Text:"赤のワインで白",Color:"白"));Assert.Equal("白",p.Filter.Color);}
    [Theory][InlineData("甘口ではありません")][InlineData("白ワインではありません")]
    public void NegativeTypePhraseNeverBecomesAffirmative(string text)=>Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:text)));
    [Fact]public void SamePhraseConflictCanOnlyBeResolvedByItsUiAxis(){Assert.Equal("白",NaturalSearch.Parse(new(Text:"赤白",Color:"白")).Filter.Color);Assert.Throws<ArgumentException>(()=>NaturalSearch.Parse(new(Text:"赤白",Sweetness:"甘口")));}
}
