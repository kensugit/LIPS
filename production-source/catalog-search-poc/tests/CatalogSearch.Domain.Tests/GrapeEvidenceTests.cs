using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class GrapeEvidenceTests
{
    [Theory]
    [InlineData("Sangiovese 51%")]
    [InlineData("Sangiovese 100%")]
    [InlineData("sangiovese 60% / Merlot 40%")]
    [InlineData("Ｓａｎｇｉｏｖｅｓｅ　６０％")]
    [InlineData("60% Sangiovese")]
    [InlineData("サンジョベーゼ（80%）")]
    [InlineData("サンジョヴェーゼ 100%")]
    [InlineData("サンジョベーゼ主体")]
    [InlineData("サンジョベーゼが主体")]
    [InlineData("主体：サンジョベーゼ")]
    public void ExplicitSourceEvidenceQualifies(string source) => Assert.True(GrapeEvidence.FromGrapes(source).Included);

    [Theory]
    [InlineData("Sangiovese 50%")]
    [InlineData("Sangiovese 49.9%")]
    [InlineData("Sangiovese")]
    [InlineData("Sangiovese, Merlot")]
    [InlineData("サンジョベーゼ主体ではない")]
    [InlineData("Sangiovese 60%; 主体ではない")]
    [InlineData("not Sangiovese 80%")]
    [InlineData("Sangiovese 50-70%")]
    [InlineData("Sangiovese 60〜80%")]
    [InlineData("Sangiovese 約60%")]
    [InlineData("Sangiovese 60%以上")]
    [InlineData("Sangiovese 50%超")]
    [InlineData("Sangiovese 110%")]
    [InlineData("Sangiovese -80%")]
    [InlineData("OtherSangiovese 80%")]
    [InlineData("Sangiovese clone 80%")]
    [InlineData("非サンジョベーゼ 80%")]
    [InlineData("Sangiovese 60%; Sangiovese 40%")]
    [InlineData("Sangiovese 60%, Merlot 50%")]
    [InlineData("サンジョベーゼ主体、Merlot 50%")]
    [InlineData("サンジョベーゼ主体、メルロー主体")]
    [InlineData("Chianti")]
    [InlineData("Toscana")]
    public void AmbiguousOrInsufficientSourceIsExcluded(string source) => Assert.False(GrapeEvidence.FromGrapes(source).Included);

    [Theory]
    [InlineData("{\"Description\":\"Sangiovese 60%\",\"Region\":\"Chianti\"}")]
    [InlineData("{\"Grapes\":\"Sangiovese 60%\",\"Grapes\":\"Sangiovese 40%\"}")]
    [InlineData("{\"Grapes\":60}")]
    [InlineData("{\"grapes\":\"Sangiovese 60%\"}")]
    [InlineData("[]")]
    [InlineData("{broken")]
    public void OnlyUniqueTypedGrapesFieldIsEvidence(string json) => Assert.False(GrapeEvidence.FromExtractedRowJson(json).Included);

    [Fact]
    public void PercentageIsStrictlyAbove50AndNotRounded()
    {
        var result = GrapeEvidence.FromExtractedRowJson("{\"Grapes\":\"Sangiovese 50.000001%\"}");
        Assert.True(result.Included);
        Assert.Equal(50.000001m, result.ExplicitPercent);
    }

    [Fact]
    public void ParsingHasFixedInputBounds()
    {
        Assert.False(GrapeEvidence.FromGrapes(new string('a', 4097)).Included);
        Assert.False(GrapeEvidence.FromExtractedRowJson(new string('a', 262145)).Included);
    }
    [Theory]
    [InlineData("Sangiovese 60%, Merlot -10%")]
    [InlineData("サンジョベーゼ主体, Merlot -10%")]
    [InlineData("サンジョベーゼ主体, Merlot 1000%")]
    [InlineData("サンジョベーゼ主体, Merlot 1e2%")]
    [InlineData("サンジョベーゼ主体, Merlot 1.2.3%")]
    [InlineData("サンジョベーゼ主体, Merlot 10.0000001%")]
    [InlineData("サンジョベーゼ主体, Merlot 1 0%")]
    [InlineData("サンジョベーゼ主体, Merlot ten%")]
    public void MalformedOtherPercentageCannotBePartiallyParsed(string source) => Assert.False(GrapeEvidence.FromGrapes(source).Included);

    [Fact]
    public void GenericTrustedRegistryAliasesUseSameMajorityRule()
    {
        string[] aliases = ["Cabernet Sauvignon", "カベルネ・ソーヴィニヨン"];
        Assert.True(GrapeEvidence.FromGrapes("Cabernet Sauvignon 60%, Merlot 40%", aliases).Included);
        Assert.True(GrapeEvidence.FromExtractedRowJson("{\"Grapes\":\"カベルネ・ソーヴィニヨン主体\"}", aliases).Included);
        Assert.False(GrapeEvidence.FromGrapes("Cabernet Sauvignon 50%", aliases).Included);
        Assert.False(GrapeEvidence.FromGrapes("Sangiovese 100%", aliases).Included);
        Assert.False(GrapeEvidence.FromGrapes("OtherCabernet Sauvignon 80%", aliases).Included);
    }

    [Fact]
    public void AliasPatternIsLiteralAndBounded()
    {
        Assert.True(GrapeEvidence.FromGrapes("A.* 60%", ["A.*"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("Anything 60%", ["A.*"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", []).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", [" "]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", ["X", "x"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", ["Ｘ", "X"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", ["X\n"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", ["X\u200b"]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", [new string('a', 101)]).Included);
        Assert.False(GrapeEvidence.FromGrapes("X 60%", Enumerable.Range(0, 21).Select(i => "X" + i).ToArray()).Included);
    }
}
