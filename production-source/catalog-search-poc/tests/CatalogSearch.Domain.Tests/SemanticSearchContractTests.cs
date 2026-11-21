using System.Text.Json;
using System.Text.Json.Nodes;
using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class SemanticSearchContractTests
{
    static SemanticSearchRequest Request() => SemanticSearchContract.CreateRequest(Guid.NewGuid(), "snapshot", "赤ワイン",
        new(["フランス", "イタリア"], [new("A", "フランス"), new("B", "フランス"), new("C", "イタリア")],
            ["赤", "白"], [750], ["2020"], ["S"], ["Sangiovese"]));
    static string Response(SemanticSearchRequest r) => JsonSerializer.Serialize(new SemanticSearchInterpretation(
        r.Schema, r.RequestId, r.CatalogVersion, r.PromptVersion, "fixture", "fixture",
        new(["商品名"], null, "フランス", ["A", "B"], "赤", null, null, null, 10000, true, null), []), SemanticSearchContract.JsonOptions);
    static string Change(string raw, Action<JsonObject> edit) { var o = JsonNode.Parse(raw)!.AsObject(); edit(o); return o.ToJsonString(); }
    [Fact] public void RegionsRemainOrListAndV1IsUnchanged()
    {
        var r = Request(); var parsed = SemanticSearchContract.ParseResponse(r, Response(r));
        var plan = SemanticSearchContract.Merge(r, new(), null, parsed);
        Assert.Equal(new[] { "A", "B" }, plan.Regions); Assert.Null(plan.Filter.Region);
        Assert.Equal("赤", plan.Filter.ProductType); plan.RequireExecutable();
        Assert.Equal("lips-free-text-search/v1", FreeTextSearchContract.Schema);
    }
    [Fact] public void ExplicitUiReplacesRegionsAndTypeAndPrice()
    {
        var r = Request(); var p = SemanticSearchContract.Merge(r, new(Region: "B", ProductType: "白", PriceMax: 3000), "S", SemanticSearchContract.ParseResponse(r, Response(r)));
        Assert.Equal(new[] { "B" }, p.Regions); Assert.Equal("白", p.Filter.ProductType); Assert.Equal(3000, p.Filter.PriceMax); Assert.Equal("S", p.SupplierCode);
    }
    [Fact] public void UnparsedDoesNotBecomeKeywordsOrDisappearWithUi()
    {
        var r = Request(); var raw = Change(Response(r), o => o["unparsedTerms"] = new JsonArray("右岸"));
        var p = SemanticSearchContract.Merge(r, new(Region: "A"), null, SemanticSearchContract.ParseResponse(r, raw));
        Assert.True(p.NeedsClarification); Assert.Equal(new[] { "商品名" }, p.Keywords);
        Assert.Equal(new[] { "右岸" }, p.UnparsedTerms); Assert.Throws<ArgumentException>(p.RequireExecutable);
    }
    [Theory] [InlineData("contains")] [InlineData("predominant")]
    public void RegisteredGrapeModesAreExplicit(string mode)
    {
        var r = Request(); var raw = Change(Response(r), o => o["filters"]!["grape"] = new JsonObject { ["name"] = "Sangiovese", ["mode"] = mode });
        Assert.Equal(mode, SemanticSearchContract.ParseResponse(r, raw).Filters.Grape!.Mode);
    }
    [Theory]
    [InlineData("unknown-member")] [InlineData("wrong-schema")] [InlineData("wrong-id")]
    [InlineData("wrong-version")] [InlineData("wrong-prompt")] [InlineData("region-country")]
    [InlineData("null-regions")] [InlineData("duplicate-region")] [InlineData("unknown-grape")]
    [InlineData("wrong-grape-mode")] [InlineData("price-string")] [InlineData("bool-string")]
    [InlineData("too-many-regions")]
    public void InvalidResponsesAreRejected(string kind)
    {
        var r = Request(); var raw = Change(Response(r), o => {
            var f = o["filters"]!;
            switch (kind) {
                case "unknown-member": o["unknown"] = true; break;
                case "wrong-schema": o["schema"] = "lips-free-text-search/v1"; break;
                case "wrong-id": o["requestId"] = Guid.NewGuid().ToString(); break;
                case "wrong-version": o["catalogVersion"] = "other"; break;
                case "wrong-prompt": o["promptVersion"] = "other"; break;
                case "region-country": f["regions"] = new JsonArray("C"); break;
                case "null-regions": f["regions"] = null; break;
                case "duplicate-region": f["regions"] = new JsonArray("A", "A"); break;
                case "unknown-grape": f["grape"] = new JsonObject { ["name"] = "Unknown", ["mode"] = "contains" }; break;
                case "wrong-grape-mode": f["grape"] = new JsonObject { ["name"] = "Sangiovese", ["mode"] = "infer" }; break;
                case "price-string": f["priceMax"] = "10000"; break;
                case "bool-string": f["availableOnly"] = "true"; break;
                case "too-many-regions": f["regions"] = new JsonArray(Enumerable.Range(0,21).Select(i => (JsonNode?)JsonValue.Create(i.ToString())).ToArray()); break;
            }
        });
        Assert.Throws<ArgumentException>(() => SemanticSearchContract.ParseResponse(r, raw));
    }
    [Fact] public void DuplicateKeysRejected()
    {
        var r = Request(); var raw = Response(r).Replace("\"country\":", "\"country\":null,\"country\":", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => SemanticSearchContract.ParseResponse(r, raw));
    }
    [Fact] public void CountryOverrideCannotLeaveConflictingRegions()
    {
        var r = Request(); var parsed = SemanticSearchContract.ParseResponse(r, Response(r));
        Assert.Throws<ArgumentException>(() => SemanticSearchContract.Merge(r, new(Country:"イタリア"), null, parsed));
    }
    [Fact] public void NoCountryDoesNotInventRegionMembership()
    {
        var r=Request(); var raw=Change(Response(r),o=>o["filters"]!["country"]=null);
        Assert.Throws<ArgumentException>(()=>SemanticSearchContract.ParseResponse(r,raw));
    }
}
