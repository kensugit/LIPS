using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class ImporterProductEvidenceTests
{
    static readonly byte[] Page = Encoding.UTF8.GetBytes("<p>Merlot 80%, Cabernet Franc 20%</p>");
    static readonly DateTimeOffset Now = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    static readonly ImporterProductIdentity Id = new("TEST", "SKU1", "Producer", "Product", "2022", 750, "standard");
    static readonly ImporterOriginalProduct Original = new(Id, new string('a', 64), []);
    static readonly ImporterEvidenceHostPolicy Policy = new([new("TEST", "importer.example")]);
    static ImporterProductEvidence Evidence() => new(ImporterEvidenceValidator.Schema,
        "https://importer.example/product/sku1", "https://importer.example/product/sku1",
        Now.AddHours(-1), Convert.ToHexStringLower(SHA256.HashData(Page)), Original.Fingerprint,
        Id, "EXACT", false, [new("Merlot", "Merlot 80%, Cabernet Franc 20%")]);
    static ValidatedImporterProductEvidence Validate(ImporterProductEvidence e) =>
        ImporterEvidenceValidator.Validate(e, Original, Policy, Page, Now, (_, _) => false, (_, _, _) => true);
    [Fact] public void BoundExcerptAcceptedWithoutInventingMajorityFact()
    {
        var result = Validate(Evidence());
        Assert.Equal(Original.Fingerprint, result.OriginalFingerprint);
        Assert.Equal("Merlot 80%, Cabernet Franc 20%", Assert.Single(result.Grapes).SourceExcerpt);
        Assert.Null(typeof(ValidatedImporterProductEvidence).GetProperty("Price"));
        Assert.Null(typeof(ValidatedImporterProductEvidence).GetProperty("Majority"));
    }
    [Theory]
    [InlineData("http://importer.example/p")]
    [InlineData("https://importer.example.evil.invalid/p")]
    [InlineData("https://evil.invalid/p")]
    [InlineData("https://user@importer.example/p")]
    [InlineData("https://importer.example:8443/p")]
    [InlineData("https://127.0.0.1/p")]
    [InlineData("https://importer.example/p#fragment")]
    [InlineData("https://importer.example/p?token=x")]
    public void BothRequestedAndFinalUrlsMustMatchPolicy(string url)
    {
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { RequestedUrl = url }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { FinalUrl = url }));
    }
    [Fact] public void EveryIdentityDimensionIsBound()
    {
        ImporterProductIdentity[] changed = [Id with { SupplierCode = "OTHER" }, Id with { Sku = "OTHER" },
            Id with { Producer = "OTHER" }, Id with { Product = "OTHER" }, Id with { Vintage = "2021" },
            Id with { VolumeMl = 1500 }, Id with { Variant = "reserve" }];
        foreach (var identity in changed)
            Assert.Throws<ArgumentException>(() => Validate(Evidence() with { Identity = identity }));
    }
    [Theory] [InlineData("unknown")] [InlineData("不明")] [InlineData("?")] [InlineData("*")] [InlineData("")]
    public void UnknownIdentityCannotBeMatchedBySelfDeclaration(string value)
    {
        var identity = Id with { Variant = value };
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Validate(Evidence() with { Identity = identity },
            Original with { Identity = identity }, Policy, Page, Now, (_, _) => false, (_, _, _) => true));
    }
    [Fact] public void FingerprintBodyAndTimeAreBound()
    {
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { OriginalFingerprint = new string('b', 64) }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { ContentSha256 = new string('b', 64) }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { FetchedAtUtc = Now.AddSeconds(1) }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { FetchedAtUtc = Now.ToOffset(TimeSpan.FromHours(9)) }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { FetchedAtUtc = default }));
    }
    [Fact] public void UnboundOrInventedExcerptIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { Grapes = [new("Merlot", "Merlot 100%")] }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { Grapes = [new("Chardonnay", "Merlot 80%, Cabernet Franc 20%")] }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { Grapes = [] }));
    }
    [Fact] public void ConflictGateIsMandatoryAndSemanticNotLiteral()
    {
        var original = Original with { KnownGrapes = ["Merlot", "Cabernet Franc"] };
        Assert.NotNull(ImporterEvidenceValidator.Validate(Evidence(), original, Policy, Page, Now, (_, _) => false, (_, _, _) => true));
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Validate(Evidence(), original, Policy, Page, Now, (_, _) => true, (_, _, _) => true));
        Assert.Throws<ArgumentNullException>(() => ImporterEvidenceValidator.Validate(Evidence(), original, Policy, Page, Now, null!, (_, _, _) => true));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { OriginalConflict = true }));
        Assert.Throws<ArgumentException>(() => Validate(Evidence() with { IdentificationStatus = "LIKELY" }));
    }
    [Fact] public void JsonRejectsExtraPriceAndDuplicateFields()
    {
        var json = JsonSerializer.Serialize(Evidence(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(ImporterEvidenceValidator.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Throws<JsonException>(() => ImporterEvidenceValidator.Parse(Encoding.UTF8.GetBytes(json.Insert(1, "\"price\":123,"))));
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Parse(Encoding.UTF8.GetBytes(json.Insert(1, "\"schema\":\"other\","))));
    }
    [Fact] public void OutputIsCopiedFromMutableInputList()
    {
        var grapes = new List<ImporterGrapeEvidence> { new("Merlot", "Merlot 80%, Cabernet Franc 20%") };
        var result = Validate(Evidence() with { Grapes = grapes }); grapes.Clear(); Assert.Single(result.Grapes);
    }
    [Fact] public void ExactInputClaimRequiresIndependentPageIdentityVerification()
    {
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Validate(Evidence(), Original, Policy,
            Page, Now, (_, _) => false, (_, _, _) => false));
        Assert.Throws<ArgumentNullException>(() => ImporterEvidenceValidator.Validate(Evidence(), Original, Policy,
            Page, Now, (_, _) => false, null!));
        var called = false;
        Assert.NotNull(ImporterEvidenceValidator.Validate(Evidence(), Original, Policy, Page, Now, (_, _) => false,
            (expected, excerpts, body) => { called = true; Assert.Equal(Id, expected); Assert.Equal(Page, body.ToArray()); return true; }));
        Assert.True(called);
    }
    [Fact] public void RelatedProductExcerptMustNotBindToTargetProduct()
    {
        var bytes = Encoding.UTF8.GetBytes("<article id='SKU1'>2022 standard Merlot 40%</article><article id='SKU2'>Merlot 80%</article>");
        var e = Evidence() with { ContentSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Grapes = [new("Merlot", "Merlot 80%")] };
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Validate(e, Original, Policy, bytes, Now,
            (_, _) => false, (identity, excerpts, page) =>
            {
                Assert.Equal("SKU1", identity.Sku);
                var targetSection = Encoding.UTF8.GetString(page.Span).Split("</article>", StringSplitOptions.None)[0];
                return excerpts.All(x => targetSection.Contains(x.SourceExcerpt, StringComparison.Ordinal));
            }));
    }
    [Fact] public void OriginalGrapeTextHasSame4096BoundAsSemanticParser()
    {
        Assert.NotNull(ImporterEvidenceValidator.Validate(Evidence(), Original with { KnownGrapes = [new string('x', 4096)] },
            Policy, Page, Now, (_, _) => false, (_, _, _) => true));
        Assert.Throws<ArgumentException>(() => ImporterEvidenceValidator.Validate(Evidence(), Original with { KnownGrapes = [new string('x', 4097)] },
            Policy, Page, Now, (_, _) => false, (_, _, _) => true));
    }
}
