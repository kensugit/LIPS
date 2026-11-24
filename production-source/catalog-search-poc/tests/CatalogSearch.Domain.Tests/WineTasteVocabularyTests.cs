using System.Reflection;
using System.Text.Json.Nodes;
using CatalogSearch.Application;
using Xunit;

public sealed class WineTasteVocabularyTests
{
    [Theory][InlineData("半甘口")][InlineData("やや甘口")]
    public void ApprovedAliasHasOneCanonicalBothDirections(string alias)
    {
        Assert.Equal("やや甘口",WineTasteVocabulary.ResolveSweetness(alias));
        Assert.True(WineTasteEvidence.Matches("{\"Sweetness\":\""+alias+"\"}","やや甘口"));
        Assert.True(WineTasteEvidence.Matches("{\"Description\":\""+alias+"です。\"}","半甘口"));
        Assert.False(WineTasteEvidence.Matches("{\"Sweetness\":\""+alias+"\"}","甘口"));
    }
    [Theory][InlineData("半甘口です。甘口です。")][InlineData("半甘口です。半甘口ではありません。")][InlineData("甘い香りです。")]
    public void ConflictNegationOrSweetAromaIsNotHalfSweetness(string description)=>Assert.False(WineTasteEvidence.Matches(
        System.Text.Json.JsonSerializer.Serialize(new{Description=description}),"やや甘口"));
    [Theory][InlineData("甘口")][InlineData("果実味")][InlineData("missing-canonical")][InlineData("unknown-canonical")][InlineData("normalized-duplicate")]
    public void InvalidAliasResourceFailsAtLoad(string kind)
    {
        var assembly=typeof(WineTasteVocabulary).Assembly;
        using var stream=assembly.GetManifestResourceStream("wine-search.ja.json")!;var node=JsonNode.Parse(stream)!;
        var map=node["SweetnessAliases"]!;
        if(kind=="missing-canonical")map["やや甘口"]=new JsonArray("半甘口");
        else if(kind=="unknown-canonical")map["未承認"]=new JsonArray("未承認");
        else if(kind=="normalized-duplicate")map["やや甘口"]!.AsArray().Add("半甘口");
        else map["やや甘口"]!.AsArray().Add(kind);
        var parse=assembly.GetType("CatalogSearch.Application.WineDictionaryData")!.GetMethod("Parse",BindingFlags.NonPublic|BindingFlags.Static)!;
        Assert.Throws<TargetInvocationException>(()=>parse.Invoke(null,[System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())]));
    }
}
