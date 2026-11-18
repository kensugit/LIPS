using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using Xunit;

public class OrveauxAocPairTests
{
    public sealed class ReceivedPairFactAttribute : FactAttribute
    {
        public ReceivedPairFactAttribute()
        {
            if (new[] { "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS" }.Any(k => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k))))
                Skip = "Opt in with the three received source paths; no database is used.";
        }
    }

    [ReceivedPairFact]
    public void ReceivedOriginalsResolveThroughPublicFixedManifestWithoutChangingFiles()
    {
        var paths = new[] { "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS" }.Select(k => Environment.GetEnvironmentVariable(k)!).ToArray();
        var bytes = paths.Select(File.ReadAllBytes).ToArray();
        var c = Candidate(); c = c with { Excel = c.Excel with { Hash = Hash(bytes[0]) }, Pdf = c.Pdf with { Hash = Hash(bytes[1]) } };
        var result = OrveauxAocPairPolicy.Resolve(c, bytes[0], bytes[1], bytes[2]);
        Assert.Equal("ResolvedEvidenceOnly", result.Status);
        Assert.False(result.BusinessImportAllowed);
        Assert.Equal(bytes.Select(Hash), paths.Select(p => Hash(File.ReadAllBytes(p))));
    }

    private static readonly byte[] Excel = Encoding.UTF8.GetBytes("synthetic Excel original"), Pdf = Encoding.UTF8.GetBytes("synthetic PDF original");
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static byte[] Answers(string? binding = null, int version = 1, string? text = null) => JsonSerializer.SerializeToUtf8Bytes(new {
        schema = "lips-source-review-answers/v1", items = new Dictionary<string, object> {
            [OrveauxAocPairPolicy.ItemId] = new { binding = binding ?? OrveauxAocPairPolicy.Binding, version,
                text = text ?? OrveauxAocPairPolicy.AnswerText, choice = "回答", history = Array.Empty<object>() }
        }
    });
    private static OrveauxPairCandidate Candidate() => new("ORVEAUX",
        new(Hash(Excel), "xlsx", "新着 /66", "Château Cesseras Cuvée Olric AOC Minervois", "シャトー・セスラ　キュヴェ・オルリック　ミネルヴォワ", 2024, null, 60),
        new(Hash(Pdf), "pdf", "15/Château Cesseras Cuvée Orlic Minervois/2024/750/60本", "Château Cesseras Cuvée Orlic Minervois", "シャトー・セスラス　キュヴェ・オルリック　ミネルヴォワ", 2024, 750, 60),
        1980, 3300, 3630, 1880, @"12本価格\1,880");
    private static OrveauxPairResolution Resolve(OrveauxPairCandidate c, byte[]? answers = null)
    {
        answers ??= Answers();
        return OrveauxAocPairPolicy.ResolveVerified(c, Excel, Pdf, answers, new(Hash(Excel), Hash(Pdf), Hash(answers)));
    }

    [Fact]
    public void PreservesBothNamesAndIndependentPricesWithoutAuthorizingImport()
    {
        var c = Candidate(); var r = Resolve(c);
        Assert.Equal("ResolvedEvidenceOnly", r.Status); Assert.Same(c, r.Pair);
        Assert.False(r.BusinessImportAllowed); Assert.Equal(1, r.AnswerVersion);
        Assert.NotEqual(r.Pair!.Excel.LatinName, r.Pair.Pdf.LatinName);
        Assert.Null(r.Pair.Excel.VolumeMl); Assert.Equal(750, r.Pair.Pdf.VolumeMl);
        Assert.Equal(new decimal[] { 1980, 3300, 3630, 1880 }, r.Prices.Select(p => p.Amount));
        Assert.Equal(new[] { false, false, true, false }, r.Prices.Select(p => p.TaxIncluded));
        Assert.All(r.Prices, p => Assert.Equal("Unknown", p.Unit));
        Assert.Equal(@"12本価格\1,880", r.Prices.Single(p => p.Role == "ConditionalPrice").Raw);
    }

    [Theory]
    [InlineData("supplier")][InlineData("hash")][InlineData("sheet")][InlineData("row")]
    [InlineData("page")][InlineData("vintage")][InlineData("2023row")][InlineData("volume")]
    [InlineData("stock")][InlineData("normalizedName")][InlineData("japaneseName")]
    [InlineData("inventedExcelCapacity")][InlineData("conditionalAsWholesale")][InlineData("grossAsNet")]
    public void RejectsScopeExpansionAndPriceContamination(string change)
    {
        var c = Candidate(); c = change switch {
            "supplier" => c with { Supplier = "OTHER" },
            "hash" => c with { Excel = c.Excel with { Hash = new string('0', 64) } },
            "sheet" => c with { Excel = c.Excel with { Location = "新着/66" } },
            "row" => c with { Excel = c.Excel with { Location = "新着 /67" } },
            "page" => c with { Pdf = c.Pdf with { Location = "16/Château Cesseras Cuvée Orlic Minervois/2024/750/60本" } },
            "vintage" => c with { Excel = c.Excel with { Vintage = 2023 } },
            "2023row" => c with { Pdf = c.Pdf with { Vintage = 2023, Stock = 70 } },
            "volume" => c with { Pdf = c.Pdf with { VolumeMl = 1500 } },
            "stock" => c with { Pdf = c.Pdf with { Stock = 70 } },
            "normalizedName" => c with { Excel = c.Excel with { LatinName = c.Pdf.LatinName } },
            "japaneseName" => c with { Pdf = c.Pdf with { JapaneseName = c.Excel.JapaneseName } },
            "inventedExcelCapacity" => c with { Excel = c.Excel with { VolumeMl = 750 } },
            "conditionalAsWholesale" => c with { Wholesale = 1880 },
            _ => c with { ReferenceRetail = 3630 }
        };
        Assert.Equal("Held", Resolve(c).Status);
    }

    [Fact]
    public void BytesMustMatchTrustedManifestAndCannotBeSubstitutedBySelfReportedHash()
    {
        var answers = Answers(); var manifest = new OrveauxAocPairPolicy.Manifest(Hash(Excel), Hash(Pdf), Hash(answers));
        Assert.Equal("Held", OrveauxAocPairPolicy.ResolveVerified(Candidate(), [1], Pdf, answers, manifest).Status);
        Assert.Equal("Held", OrveauxAocPairPolicy.ResolveVerified(Candidate(), Excel, [2], answers, manifest).Status);
        Assert.Equal("Held", OrveauxAocPairPolicy.ResolveVerified(Candidate(), Excel, Pdf, [3], manifest).Status);
        Assert.Equal("Held", OrveauxAocPairPolicy.Resolve(Candidate(), Excel, Pdf, answers).Status);
    }

    [Fact]
    public void RejectsDifferentBindingVersionOrBroaderHumanTextEvenWithVerifiedExportHash()
    {
        Assert.Equal("Held", Resolve(Candidate(), Answers(binding: new string('a', 64))).Status);
        Assert.Equal("Held", Resolve(Candidate(), Answers(version: 2)).Status);
        Assert.Equal("Held", Resolve(Candidate(), Answers(text: "全商品からAOCを削除")).Status);
    }
}
