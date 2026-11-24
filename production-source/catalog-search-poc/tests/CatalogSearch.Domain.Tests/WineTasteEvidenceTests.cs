using System.Text.Json;
using CatalogSearch.Application;
using Xunit;

public sealed class WineTasteEvidenceTests
{
    [Theory]
    [InlineData("甘口の白ワイン。果実味がある。", "甘口", true)]
    [InlineData("極甘口の白ワイン。", "甘口", false)]
    [InlineData("辛口ではない。", "辛口", false)]
    [InlineData("甘口。辛口。", "甘口", false)]
    [InlineData("洋ナシ、柑橘の香り。", "柑橘系", true)]
    [InlineData("樽香はない。", "樽香", false)]
    [InlineData("リースリング。", "甘口", false)]
    [InlineData("甘口ではありません。", "甘口", false)]
    [InlineData("柑橘の香りはありません。", "柑橘系", false)]
    [InlineData("半甘口です。", "甘口", false)]
    [InlineData("中甘口です。", "甘口", false)]
    [InlineData("甘口のワインと合わせる料理です。", "甘口", false)]
    [InlineData("他の商品の柑橘の香り。", "柑橘系", false)]
    [InlineData("甘口がお好きな方におすすめ。", "甘口", false)]
    [InlineData("このワインは甘口です。", "甘口", true)]
    [InlineData("甘口です。甘口ではありません。", "甘口", false)]
    [InlineData("柑橘の香り。柑橘の香りはありません。", "柑橘系", false)]
    [InlineData("柑橘の香りです。", "柑橘系", true)]
    [InlineData("果実味がある。", "果実味", true)]
    [InlineData("樽の香り。", "樽香", true)]
    [InlineData("スパイシーな味わい。", "スパイシー", true)]
    [InlineData("柑橘系に似ています。", "柑橘系", false)]
    [InlineData("フレッシュな酸を伴う辛口の白ワイン。", "辛口", true)]
    [InlineData("すっきりした辛口。", "辛口", true)]
    [InlineData("洋梨や柑橘の香りが広がる。", "柑橘系", true)]
    [InlineData("柑橘の香りは感じられません。", "柑橘系", false)]
    public void UsesOnlyDirectTasteEvidence(string description, string taste, bool expected) =>
        Assert.Equal(expected, WineTasteEvidence.Matches(JsonSerializer.Serialize(new { Description = description }), taste));

    [Theory]
    [InlineData("辛口", "甘口です。", "辛口", false)]
    [InlineData("辛口", "甘口です。", "甘口", false)]
    [InlineData("辛口", "辛口です。", "辛口", true)]
    [InlineData("甘口", "甘口ではありません。", "甘口", false)]
    [InlineData("甘口", "非常に豊かな甘口のワイン", "甘口", true)]
    [InlineData("甘口", "非甘口", "甘口", false)]
    [InlineData("半甘口", "半甘口ではありません", "やや甘口", false)]
    [InlineData("甘口", "甘口とはいえません。", "甘口", false)]
    [InlineData("甘口", "甘口とは言い切れません。", "甘口", false)]
    [InlineData("半甘口", "", "甘口", false)]
    [InlineData("辛口", "料理には甘口のワインを合わせます。", "辛口", true)]
    public void ExplicitFieldCannotHideDirectConflict(string sweetness, string description, string taste, bool expected) =>
        Assert.Equal(expected, WineTasteEvidence.Matches(JsonSerializer.Serialize(new { Sweetness = sweetness, Description = description }), taste));

    [Fact]
    public void DuplicateEvidenceFieldsRemainUnknown() => Assert.False(WineTasteEvidence.Matches(
        "{\"Sweetness\":\"辛口\",\"Sweetness\":\"甘口\"}", "辛口"));

    [Theory]
    [InlineData("Sweetness", "123")][InlineData("Sweetness", "true")]
    [InlineData("Sweetness", "[]")][InlineData("Sweetness", "{}")]
    [InlineData("Description", "123")][InlineData("Description", "true")]
    [InlineData("Description", "[]")][InlineData("Description", "{}")]
    public void InvalidRelatedFieldTypeCannotBecomeMissing(string field, string value)
    {
        var json=field=="Sweetness"?"{\"Sweetness\":"+value+",\"Description\":\"甘口です\"}":
            "{\"Sweetness\":\"甘口\",\"Description\":"+value+"}";
        Assert.False(WineTasteEvidence.Matches(json,"甘口"));
    }

    [Theory]
    [InlineData("{\"Sweetness\":null,\"Description\":\"甘口です\"}")]
    [InlineData("{\"Sweetness\":\"甘口\",\"Description\":null}")]
    public void ExplicitNullIsUnknownAndDoesNotInvalidateOtherDirectEvidence(string json) => Assert.True(WineTasteEvidence.Matches(json,"甘口"));
}
