using System.Text.Json;
using CatalogSearch.Application;
using Xunit;

public sealed class FinesseDescriptionGrapeTests
{
    [Theory]
    [InlineData("シノン ルージュ ピエール ド テュフ", "2021", "カベルネ フラン100％")]
    [InlineData("シノン ルージュ レ シヤン シヤン", "2020", "カベルネ フラン100％")]
    [InlineData("シノン ルージュ レ シヤン シヤン", "2021", "カベルネ フラン100％")]
    public void CompositionAtStartOfDescriptionIsUsed(string product, string vintage, string expected)
    {
        var json = JsonSerializer.Serialize(new {
            ProductNameJa = product, VintageRaw = vintage, Grapes = "",
            Description = $"フィネス Finesse {product} {expected}。説明"
        });
        var actual = SemanticGrapeMatcher.ReadFinesseDescriptionClaim(json, product);
        Assert.Equal(expected, actual);
        Assert.True(SemanticGrapeMatcher.Match(actual, new("カベルネ・フラン", "predominant")).Included);
        Assert.Null(SemanticGrapeMatcher.ReadFinesseDescriptionClaim(json, "別の商品"));
    }

    [Theory]
    [InlineData("カベルネ フラン50％")]
    [InlineData("カベルネ フラン不使用")]
    [InlineData("隣の区画ではカベルネ フラン100％")]
    public void UnsupportedDescriptionDoesNotQualify(string description)
    {
        var json = JsonSerializer.Serialize(new {ProductNameJa = "商品", Description = $"フィネス Finesse 商品 {description}。"});
        var claim = SemanticGrapeMatcher.ReadFinesseDescriptionClaim(json, "商品");
        Assert.False(SemanticGrapeMatcher.Match(claim, new("カベルネ・フラン", "predominant")).Included);
    }
}
