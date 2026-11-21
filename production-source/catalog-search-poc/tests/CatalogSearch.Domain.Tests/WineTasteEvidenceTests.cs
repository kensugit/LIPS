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
    public void UsesOnlyDirectTasteEvidence(string description, string taste, bool expected) =>
        Assert.Equal(expected, WineTasteEvidence.Matches(JsonSerializer.Serialize(new { Description = description }), taste));
}
