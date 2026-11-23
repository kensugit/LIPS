using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class SemanticCatalogEvidenceTests
{
    [Theory]
    [InlineData("ピノグリ")]
    [InlineData("ピノ・グリ")]
    [InlineData("ピノグリージョ")]
    [InlineData("ピノ・グリージョ")]
    [InlineData("Pinot Gris")]
    [InlineData("Pinot Grigio")]
    public void ReviewedSynonymsShareOneCanonical(string alias)
    {
        Assert.Equal("ピノ・グリ", GrapeRegistry.Resolve(alias));
        Assert.Equal(new[] { "ピノ・グリ" }, GrapeRegistry.MentionedNames(alias + "を含む白ワイン"));
        Assert.True(SemanticGrapeMatcher.Match(alias, new("ピノ・グリ", "contains")).Included);
        Assert.False(SemanticGrapeMatcher.Match(alias, new("ピノ・グリ", "predominant")).Included);
    }

    [Theory]
    [InlineData("Pinot Grigio 20%", "contains", true)]
    [InlineData("Pinot Grigio 0%", "contains", false)]
    [InlineData("Pinot Grigio 50%", "predominant", false)]
    [InlineData("Pinot Grigio 51%", "predominant", true)]
    [InlineData("Pinot Grigio 不使用", "contains", false)]
    [InlineData("not Pinot Grigio", "contains", false)]
    [InlineData("not Pinot Gris", "contains", false)]
    [InlineData("Not Pinot Grigio", "contains", false)]
    [InlineData("NOT Pinot Gris", "contains", false)]
    [InlineData("Pinot Gris not used", "contains", false)]
    [InlineData("without Pinot Gris", "contains", false)]
    [InlineData("not, Pinot Grigio", "contains", false)]
    [InlineData("Pinot Gris is fruity", "contains", false)]
    [InlineData(null, "contains", false)]
    [InlineData("Merlot", "contains", false)]
    public void SynonymDoesNotInventProductComposition(string? source, string mode, bool expected) =>
        Assert.Equal(expected, SemanticGrapeMatcher.Match(source, new("ピノ・グリ", mode)).Included);

    [Fact]
    public void SynonymMentionsAreOneGrapeAndRespectLatinBoundaries()
    {
        Assert.Equal(new[] { "ピノ・グリ" }, GrapeRegistry.MentionedNames("ピノグリもしくはPinot Grigioを含む白ワイン"));
        Assert.Empty(GrapeRegistry.MentionedNames("XPinot GrigioZ"));
        Assert.Null(GrapeRegistry.Resolve("架空ピノ"));
    }

    [Theory]
    [InlineData("Sangiovese 80%", "predominant", true)]
    [InlineData("Sangiovese 50%", "predominant", false)]
    [InlineData("Sangiovese 50%", "contains", true)]
    [InlineData("Sangiovese 0%", "contains", false)]
    [InlineData("Sangiovese, Merlot", "predominant", false)]
    [InlineData("Sangiovese, Merlot", "contains", true)]
    [InlineData("Sangiovese 不使用", "contains", false)]
    [InlineData("サンジョヴェーゼ 100%", "predominant", true)]
    public void SourceMeaning(string source, string mode, bool expected) =>
        Assert.Equal(expected, SemanticGrapeMatcher.Match(source, new("サンジョベーゼ", mode)).Included);

    [Fact]
    public void FingerprintBindsSourceAndActualBytes()
    {
        var id = Guid.NewGuid();
        Assert.NotEqual(SourceFingerprint.Create(id, "{}"), SourceFingerprint.Create(Guid.NewGuid(), "{}"));
        Assert.NotEqual(SourceFingerprint.Create(id, "{}"), SourceFingerprint.Create(id, "{ }"));
    }

    [Fact]
    public void DuplicateGrapesCannotSupplyEvidence() =>
        Assert.Null(SemanticGrapeMatcher.ReadOriginal("{\"Grapes\":\"Sangiovese 80%\",\"Grapes\":\"Merlot\"}"));

    private static async IAsyncEnumerable<int> Candidates()
    {
        for (var i = 0; i < 20; i++) { await Task.Yield(); yield return i; }
    }

    [Fact]
    public async Task EvidenceFilteringPrecedesLimit()
    {
        var result = await SemanticResultSelector.SelectAsync(Candidates(), x => x >= 10, 2);
        Assert.Equal(new[] { 10, 11 }, result);
    }

    [Fact]
    public async Task CancellationDoesNotReturnPartialSuccess()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SemanticResultSelector.SelectAsync(Candidates(), _ => true, 2, cancel.Token));
    }
}
