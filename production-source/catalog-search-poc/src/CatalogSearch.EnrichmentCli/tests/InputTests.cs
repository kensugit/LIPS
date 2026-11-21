using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.EnrichmentCli;
using Xunit;

public sealed class InputTests
{
    [Theory][InlineData("INPUT_HASH_MISMATCH", "INPUT_HASH_MISMATCH")][InlineData("private/path or secret", "REQUIRES_INSPECTION")]
    public void ErrorReasonClosed(string message, string expected) => Assert.Equal(expected, Program.SafeReason(new IOException(message)));
    static readonly Guid Source = Guid.Parse("c3d0d66e-237c-4db6-9a49-b77840c159dc");
    const string Original = "{\"SupplierProductCode\":\"SKU1\",\"ProducerNameJa\":\"Producer\",\"ProductNameJa\":\"Product\",\"VintageRaw\":\"2020\",\"VolumeMl\":750,\"ProductType\":\"standard\"}";
    static AcquisitionRequest Request() => new("lips-importer-acquisition-request/v1", "synthetic-only", new("TEST", "SKU1", "Producer", "Product", "2020", 750, "standard"), Source, Original, SourceFingerprint.Create(Source, Original), "https://importer.example/product/1", 0);
    static AcquisitionPolicy Policy() => new("lips-importer-acquisition-policy/v1", [new("TEST", "importer.example")], 100, 1000000);
    static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, AcquisitionInput.Json);
    [Fact] public void FixedInputValidates() => Assert.Equal("synthetic-only", AcquisitionInput.Validate(Bytes(Request()), Bytes(Policy())).Request.OperationId);
    [Theory]
    [InlineData("fingerprint")][InlineData("generation")][InlineData("url")][InlineData("operation")][InlineData("vintage")][InlineData("volume")][InlineData("source")]
    public void InvalidRequestRejected(string kind)
    {
        var r = Request(); r = kind switch {
            "fingerprint" => r with { OriginalFingerprint = new string('a', 64) },
            "generation" => r with { ExpectedGeneration = -1 },
            "url" => r with { OfficialUrl = "https://other.example/product/1" },
            "operation" => r with { OperationId = "../escape" },
            "vintage" => r with { Identity = r.Identity with { Vintage = "unknown" } },
            "volume" => r with { Identity = r.Identity with { VolumeMl = 0 } },
            _ => r with { SourceDocumentId = Guid.Empty } };
        Assert.ThrowsAny<Exception>(() => AcquisitionInput.Validate(Bytes(r), Bytes(Policy())));
    }
    [Theory][InlineData("extra")][InlineData("duplicate")][InlineData("missing")][InlineData("oversize")]
    public void MalformedJsonRejected(string kind)
    {
        var s = Encoding.UTF8.GetString(Bytes(Request()));
        var b = kind switch {
            "extra" => Encoding.UTF8.GetBytes(s[..^1] + ",\"secret\":\"never-output\"}"),
            "duplicate" => Encoding.UTF8.GetBytes(s[..^1] + ",\"operationId\":\"second\"}"),
            "missing" => Encoding.UTF8.GetBytes("{}"),
            _ => new byte[AcquisitionInput.RequestLimit + 1] };
        Assert.ThrowsAny<Exception>(() => AcquisitionInput.Validate(b, Bytes(Policy())));
    }
    [Fact] public void PolicyUnknownFieldsRejected() => Assert.ThrowsAny<Exception>(() => AcquisitionInput.Validate(Bytes(Request()), Encoding.UTF8.GetBytes("{\"schema\":\"lips-importer-acquisition-policy/v1\",\"rules\":[],\"maximumFiles\":1,\"maximumTotalBytes\":1,\"approved\":true}")));
    [Fact] public void SelfConsistentWrongIdentityRejected() => Assert.ThrowsAny<Exception>(() => AcquisitionInput.Validate(Bytes(Request() with { Identity = Request().Identity with { Vintage = "2021" } }), Bytes(Policy())));
    [Theory][InlineData(false)][InlineData(true)]
    public async Task FetchEntryUsesFakeOnceAndPreservesGuard(bool fail)
    {
        var root = Path.Combine(Path.GetTempPath(), "lips-cli-synthetic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var rb = Bytes(Request()); var pb = Bytes(Policy());
            var rp = Path.Combine(root, "request.json"); var pp = Path.Combine(root, "policy.json"); File.WriteAllBytes(rp, rb); File.WriteAllBytes(pp, pb);
            string[] args = ["--request", rp, "--policy", pp, "--store-root", root, "--receipt-root", root, "--request-sha256", AcquisitionInput.Sha(rb), "--policy-sha256", AcquisitionInput.Sha(pb), "--fetch-and-save"];
            var calls = 0;
            Task<CatalogSearch.Infrastructure.ImporterAcquisitionResult> Fake(ValidatedAcquisition _, string __) { calls++; if (fail) throw new IOException("secret-raw-error"); return Task.FromResult(new CatalogSearch.Infrastructure.ImporterAcquisitionResult("PAGE_REQUIRES_REVIEW_NOT_SAVED", null)); }
            Assert.Equal(fail ? 1 : 0, await Program.Run(args, Fake)); Assert.Equal(1, calls);
            var result = File.ReadAllText(Path.Combine(root, "synthetic-only.result.safe.json"));
            Assert.DoesNotContain("secret-raw-error", result); Assert.Contains(fail ? "REQUIRES_INSPECTION" : "PAGE_REQUIRES_REVIEW_NOT_SAVED", result);
            Assert.Equal(1, await Program.Run(args, Fake)); Assert.Equal(1, calls);
            Assert.Equal(result, File.ReadAllText(Path.Combine(root, "synthetic-only.result.safe.json")));
        } finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task DefaultValidationWritesNothingAndExistingIntentStops()
    {
        var root = Path.Combine(Path.GetTempPath(), "lips-cli-synthetic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var rb = Bytes(Request()); var pb = Bytes(Policy());
            var rp = Path.Combine(root, "request.json"); var pp = Path.Combine(root, "policy.json"); File.WriteAllBytes(rp, rb); File.WriteAllBytes(pp, pb);
            string[] args = ["--request", rp, "--policy", pp, "--store-root", root, "--receipt-root", root, "--request-sha256", AcquisitionInput.Sha(rb), "--policy-sha256", AcquisitionInput.Sha(pb)];
            Assert.Equal(0, await Program.Main(args)); Assert.Equal(2, Directory.GetFiles(root).Length);
            File.WriteAllText(Path.Combine(root, "synthetic-only.intent.safe.json"), "preserved");
            Assert.Equal(1, await Program.Main(args)); Assert.Equal(3, Directory.GetFiles(root).Length);
            Assert.Equal("preserved", File.ReadAllText(Path.Combine(root, "synthetic-only.intent.safe.json")));
        } finally { Directory.Delete(root, true); }
    }
}
