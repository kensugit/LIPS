using System.Security.Cryptography;
using System.Text;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class ImporterEnrichmentStoreTests : IDisposable
{
    private readonly string root;
    private readonly string prefix;
    static readonly DateTimeOffset Now = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    static readonly ImporterProductIdentity Id = new("TEST", "SKU", "Producer", "Product", "2022", 750, "standard");
    static readonly byte[] Page = Encoding.UTF8.GetBytes("Product 2022 standard: Merlot 80%");
    static readonly string Fingerprint = new('a', 64);
    public ImporterEnrichmentStoreTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "CatalogSearch.Application"))) dir = dir.Parent;
        Assert.NotNull(dir);
        prefix = Path.GetFullPath(Path.Combine(dir!.Parent!.Parent!.FullName, "importer-enrichment-fixtures"));
        Directory.CreateDirectory(prefix);
        root = Path.Combine(prefix, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    }
    static ImporterEnrichmentContent Content() => new("lips-importer-enrichment-content/v1", Id, "source-fixture",
        Fingerprint, "https://importer.example/sku", "https://importer.example/sku", Now.AddSeconds(-5),
        Convert.ToHexStringLower(SHA256.HashData(Page)), null, null,
        new("deterministic", "fixture-fetch/1", "fixture-extractor/1", null, null, null), [new("Merlot", "Merlot 80%")]);
    static ValidatedImporterProductEvidence Proof() => ImporterEvidenceValidator.Validate(
        new(ImporterEvidenceValidator.Schema, Content().RequestedUrl, Content().FinalUrl, Content().FetchedAtUtc,
            Content().BodySha256, Fingerprint, Id, "EXACT", false, Content().Grapes),
        new(Id, Fingerprint, []), new ImporterEvidenceHostPolicy([new("TEST", "importer.example")]), Page, Now,
        (_, _) => false, (_, _, _) => true);
    FileImporterEnrichmentWriter Writer() => new(root, new(100, 4 * 1024 * 1024));
    ImporterEnrichmentCommitResult Commit(long generation = 0, string status = "verified", string kind = "200",
        DateTimeOffset? at = null) => Writer().Commit(Content(), status, at ?? Now, (at ?? Now).AddDays(30), kind,
            generation, at ?? Now, status is "verified" or "conflict" ? Proof() : null);
    ImporterEnrichmentReadResult Read(DateTimeOffset? now = null) => new FileImporterEnrichmentReader(root)
        .ReadVerifiedForProduct(Id, Fingerprint, "fixture-extractor/1", now ?? Now);
    public void Dispose()
    {
        Assert.StartsWith(prefix + Path.DirectorySeparatorChar, Path.GetFullPath(root));
        Assert.False((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0);
        Directory.Delete(root, recursive: true);
    }
    [Fact] public void VerifiedRecordRoundTripsAndReaderDoesNotWrite()
    {
        var result = Commit(); var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray();
        var read = Read(); Assert.Equal("verified", read.Status); Assert.Equal(1, read.Generation);
        Assert.Equal(result.ContentRecordSha256, read.ContentRecordSha256); Assert.Equal("Merlot", Assert.Single(read.Content!.Grapes).Grape);
        Assert.Equal(before, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.False(File.Exists(Path.Combine(root, ".writer.lock")));
    }
    [Fact] public void SameContentWithinFreshWindowUsesNewEventAndHistory()
    {
        var first = Commit(); var next = Commit(1, kind: "200", at: Now.AddSeconds(10));
        Assert.True(next.ContentAlreadyPresent); Assert.Equal(first.ContentRecordSha256, next.ContentRecordSha256);
        Assert.NotEqual(first.VerificationRecordSha256, next.VerificationRecordSha256);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "records")).Length);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "history")).Length);
        Assert.Equal("verified", Read(Now.AddSeconds(10)).Status);
    }
    [Fact] public void ExpiryAndChangedOriginalOrMeaningVersionExcludeEnrichment()
    {
        Commit(); Assert.Equal("EXPIRED", Read(Now.AddDays(30)).Status);
        var reader = new FileImporterEnrichmentReader(root);
        Assert.Equal("BINDING_CHANGED", reader.ReadVerifiedForProduct(Id, new string('b', 64), "fixture-extractor/1", Now).Status);
        Assert.Equal("BINDING_CHANGED", reader.ReadVerifiedForProduct(Id, Fingerprint, "fixture-extractor/2", Now).Status);
        Assert.Equal("BINDING_CHANGED", reader.ReadVerified(new(Id, Fingerprint, "https://importer.example/new-url", "fixture-extractor/1", Now)).Status);
        Assert.Equal("NOT_FOUND", reader.ReadVerifiedForProduct(Id with { Vintage = "2021" }, Fingerprint, "fixture-extractor/1", Now).Status);
    }
    [Theory] [InlineData("candidate")] [InlineData("conflict")] [InlineData("stale")] [InlineData("unavailable")] [InlineData("withdrawn")]
    public void NonVerifiedStatesNeverSupplyFacts(string state)
    { Commit(status: state, kind: state == "withdrawn" ? "404" : "manual"); var r = Read(); Assert.Equal(state, r.Status); if (state == "conflict") Assert.NotNull(r.Content); else Assert.Null(r.Content); }
    [Fact] public void ExistingWriterStopsConcurrentWriterAndReader()
    {
        Commit(); using var owned = new FileStream(Path.Combine(root, ".writer.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Assert.Throws<IOException>(() => Commit(1)); Assert.Equal("WRITER_STATE_UNKNOWN", Read().Status);
    }
    [Fact] public void StaleGenerationPreservesUnknownMarkerAndPreviousIndex()
    {
        Commit(); var old = File.ReadAllBytes(Path.Combine(root, "index.json"));
        Assert.Throws<InvalidDataException>(() => Commit(0));
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(root, "index.json")));
        Assert.Equal("WRITER_STATE_UNKNOWN", Read().Status);
    }
    [Fact] public void CorruptRecordAndIndexAreNotUsed()
    {
        var c = Commit(); File.AppendAllText(Path.Combine(root, "records", c.ContentRecordSha256 + ".json"), " ");
        Assert.Equal("CORRUPT_OR_UNAVAILABLE", Read().Status);
        File.WriteAllText(Path.Combine(root, "index.json"), "{}"); Assert.Equal("CORRUPT_OR_UNAVAILABLE", Read().Status);
    }
    [Fact] public void PendingIndexAndHistoryDamageAreExcluded()
    {
        Commit(); var pending = Path.Combine(root, ".index-fixture.tmp"); File.WriteAllText(pending, "incomplete");
        Assert.Equal("WRITER_STATE_UNKNOWN", Read().Status); File.Delete(pending);
        File.WriteAllText(Directory.GetFiles(Path.Combine(root, "history"))[0], "{}");
        Assert.Equal("CORRUPT_OR_UNAVAILABLE", Read().Status);
    }
    [Fact] public void FutureOrOversizedTtlAndUnverifiedProofAreRejectedBeforeWrite()
    {
        Assert.Throws<InvalidDataException>(() => Writer().Commit(Content(), "verified", Now.AddSeconds(1), Now.AddDays(1), "200", 0, Now, Proof()));
        Assert.Throws<InvalidDataException>(() => Writer().Commit(Content(), "verified", Now, Now.AddDays(31), "200", 0, Now, Proof()));
        Assert.Throws<InvalidDataException>(() => Writer().Commit(Content(), "verified", Now, Now.AddDays(1), "200", 0, Now, null));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    [Theory] [InlineData("304")] [InlineData("unavailable")] [InlineData("manual")] [InlineData("404")]
    public void VerifiedRequiresImplementedFresh200Acquisition(string kind)
    { Assert.Throws<InvalidDataException>(() => Commit(kind: kind)); Assert.False(File.Exists(Path.Combine(root, ".writer.lock"))); }
    [Fact] public void OldProofCannotRenewValidity()
    { Assert.Throws<InvalidDataException>(() => Commit(at: Now.AddSeconds(56))); Assert.Empty(Directory.GetFileSystemEntries(root)); }
    [Fact] public void WithdrawalKeepsHistoryAndCannotReturnPastVerifiedFacts()
    {
        var before = Commit(); Commit(1, "withdrawn", "404"); Assert.Equal("withdrawn", Read().Status);
        Assert.True(File.Exists(Path.Combine(root, "records", before.ContentRecordSha256 + ".json")));
    }
    [Fact] public void StorageLimitStopsWithoutDeletingOldData()
    {
        Commit(); var old = File.ReadAllBytes(Path.Combine(root, "index.json"));
        var writer = new FileImporterEnrichmentWriter(root, new(1, 1));
        Assert.Throws<InvalidDataException>(() => writer.Commit(Content(), "verified", Now.AddSeconds(10), Now.AddDays(1), "200", 1, Now.AddSeconds(10), Proof()));
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(root, "index.json")));
    }
}
