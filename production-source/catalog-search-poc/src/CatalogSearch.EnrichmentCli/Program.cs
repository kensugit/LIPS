using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;

namespace CatalogSearch.EnrichmentCli;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcquisitionRequest(string Schema, string OperationId, ImporterProductIdentity Identity,
    Guid SourceDocumentId, string ExtractedJson, string OriginalFingerprint, string OfficialUrl, long ExpectedGeneration);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterHostRule(string SupplierCode, string Host);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcquisitionPolicy(string Schema, IReadOnlyList<ImporterHostRule> Rules,
    int MaximumFiles, long MaximumTotalBytes);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcquisitionPolicyV2(string Schema, IReadOnlyList<ImporterHostRule> Rules,
    int MaximumFiles, long MaximumTotalBytes,
    IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> ReviewedJetSuppliers,
    IReadOnlyDictionary<string,IReadOnlyDictionary<string,YourCellarProductPageIdentity.ProducerRule>> ReviewedYourCellarSuppliers);
public sealed record ValidatedAcquisition(AcquisitionRequest Request, AcquisitionPolicy Policy, ImporterEvidenceHostPolicy Hosts)
{
    public ImporterPageAdapterPolicy Adapters { get; init; } = new();
    public string ExpectedExtractorVersion => Adapters.BoundVersion(Hosts);
}

public static class AcquisitionInput
{
    public const int RequestLimit = 512 * 1024, PolicyLimit = 32768;
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
      UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 };
    public static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok) { if (!ok) throw new ArgumentException("INPUT_NOT_ACCEPTED"); }
    static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        value == value.Trim() && !value.Any(char.IsControl);
    static void Duplicate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var p in element.EnumerateObject()) { Need(seen.Add(p.Name)); Duplicate(p.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var x in element.EnumerateArray()) Duplicate(x);
    }
    static T Parse<T>(byte[] bytes, int limit, params string[] fields)
    {
        Need(bytes.Length is > 0 && bytes.Length <= limit);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 }); Duplicate(doc.RootElement);
        Need(doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(fields));
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new ArgumentException("INPUT_NULL");
    }
    public static ValidatedAcquisition Validate(byte[] requestBytes, byte[] policyBytes)
    {
        var request = Parse<AcquisitionRequest>(requestBytes, RequestLimit, "schema", "operationId", "identity", "sourceDocumentId", "extractedJson", "originalFingerprint", "officialUrl", "expectedGeneration");
        Need(policyBytes.Length is > 0 and <= PolicyLimit);
        using var policyDoc=JsonDocument.Parse(policyBytes,new JsonDocumentOptions{MaxDepth=12});Duplicate(policyDoc.RootElement);
        Need(policyDoc.RootElement.ValueKind==JsonValueKind.Object&&policyDoc.RootElement.TryGetProperty("schema",out _));
        var policySchema=policyDoc.RootElement.GetProperty("schema").GetString();
        AcquisitionPolicy policy;ImporterPageAdapterPolicy adapters=new();
        if(policySchema=="lips-importer-acquisition-policy/v2")
        {
            var v2=Parse<AcquisitionPolicyV2>(policyBytes,PolicyLimit,"schema","rules","maximumFiles","maximumTotalBytes","reviewedJetSuppliers","reviewedYourCellarSuppliers");
            Need(v2.ReviewedJetSuppliers is not null&&v2.ReviewedYourCellarSuppliers is not null&&v2.Rules is not null&&v2.Rules.All(r=>r is not null));
            // Adapter ownership is explicit fixed configuration, never derived from URL/page/model output.
            foreach(var supplier in v2.ReviewedJetSuppliers!.Keys)
                Need(v2.Rules!.Any(r=>r.SupplierCode==supplier)&&v2.Rules.Where(r=>r.SupplierCode==supplier).All(r=>r.Host=="www.jetlc.co.jp"));
            foreach(var supplier in v2.ReviewedYourCellarSuppliers!.Keys)
                Need(v2.Rules!.Any(r=>r.SupplierCode==supplier)&&v2.Rules.Where(r=>r.SupplierCode==supplier).All(r=>r.Host=="www.yourcellar.jp"));
            adapters=new(v2.ReviewedJetSuppliers,v2.ReviewedYourCellarSuppliers);
            policy=new(v2.Schema,v2.Rules!,v2.MaximumFiles,v2.MaximumTotalBytes);
        }
        else policy=Parse<AcquisitionPolicy>(policyBytes,PolicyLimit,"schema","rules","maximumFiles","maximumTotalBytes");
        Need(request.Schema == "lips-importer-acquisition-request/v1" && request.OperationId is not null &&
            Regex.IsMatch(request.OperationId, "^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
            request.SourceDocumentId != Guid.Empty && request.ExpectedGeneration >= 0 && request.Identity is not null);
        var i = request.Identity!;
        Need(Text(i.SupplierCode, 100) && Text(i.Sku, 100) && Text(i.Producer, 200) && Text(i.Product, 200) &&
            Text(i.Vintage, 32) && Text(i.Variant, 100) && i.VolumeMl is > 0 and <= 100000);
        Need(!new[] { "unknown", "不明", "?", "*" }.Contains(i.Vintage, StringComparer.OrdinalIgnoreCase) &&
             !new[] { "unknown", "不明", "?", "*" }.Contains(i.Variant, StringComparer.OrdinalIgnoreCase));
        Need(request.ExtractedJson is not null && Encoding.UTF8.GetByteCount(request.ExtractedJson) is > 0 and <= 262144);
        using var original = JsonDocument.Parse(request.ExtractedJson!, new JsonDocumentOptions { MaxDepth = 32 });
        Duplicate(original.RootElement); Need(original.RootElement.ValueKind == JsonValueKind.Object);
        Need(SourceFingerprint.Create(request.SourceDocumentId, request.ExtractedJson!) == request.OriginalFingerprint);
        Need(SourceIdentityBinding.Verify(i, request.ExtractedJson!));
        Need(policy.Schema is "lips-importer-acquisition-policy/v1" or "lips-importer-acquisition-policy/v2" && policy.Rules is { Count: > 0 and <= 100 } &&
            policy.Rules.All(x => x is not null) && policy.MaximumFiles is > 0 and <= 100000 && policy.MaximumTotalBytes is > 0 and <= 1099511627776);
        var hosts = new ImporterEvidenceHostPolicy(policy.Rules.Select(x => new KeyValuePair<string, string>(x.SupplierCode, x.Host)));
        Need(hosts.Allows(i.SupplierCode, request.OfficialUrl));
        return new(request, policy, hosts){Adapters=adapters};
    }
}

public static class Program
{
    public static string SafeReason(Exception error) => new[] {
        "INPUT_NOT_ACCEPTED", "INPUT_NULL", "ABSOLUTE_DIRECTORY_REQUIRED", "DIRECTORY_NOT_READY",
        "ABSOLUTE_FILE_REQUIRED", "INPUT_FILE_NOT_READY", "INPUT_CHANGED", "RECEIPT_READBACK_FAILED",
        "RECEIPT_DIRECTORY_SYNC_FAILED", "DUPLICATE_MODE", "ARGUMENTS_INVALID", "ARGUMENTS_MISSING",
        "INPUT_HASH_MISMATCH", "PRIOR_OPERATION_PRESERVED"
    }.Contains(error.Message, StringComparer.Ordinal) ? error.Message : "REQUIRES_INSPECTION";
    [DllImport("libc", SetLastError = true)] static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] static extern int close(int fd);
    static void SyncDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var fd = open(path, 0x10000 | 0x20000 | 0x80000);
        if (fd < 0) throw new IOException("RECEIPT_DIRECTORY_SYNC_FAILED");
        try { if (fsync(fd) != 0) throw new IOException("RECEIPT_DIRECTORY_SYNC_FAILED"); }
        finally { close(fd); }
    }
    static void DirectorySafe(string value)
    {
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("ABSOLUTE_DIRECTORY_REQUIRED");
        var d = new DirectoryInfo(value);
        while (d is not null)
        {
            if (!d.Exists || (d.Attributes & FileAttributes.ReparsePoint) != 0 || d.LinkTarget is not null)
                throw new IOException("DIRECTORY_NOT_READY");
            d = d.Parent;
        }
    }
    static byte[] Read(string path, int limit)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("ABSOLUTE_FILE_REQUIRED");
        DirectorySafe(Path.GetDirectoryName(path)!);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > limit ||
            (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.LinkTarget is not null)
            throw new IOException("INPUT_FILE_NOT_READY");
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length != info.Length) throw new IOException("INPUT_CHANGED");
        var bytes = new byte[checked((int)f.Length)]; f.ReadExactly(bytes);
        if (f.ReadByte() != -1 || f.Length != bytes.Length) throw new IOException("INPUT_CHANGED");
        return bytes;
    }
    static void SaveNew(string path, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AcquisitionInput.Json);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(flushToDisk: true); }
        SyncDirectory(Path.GetDirectoryName(path)!);
        if (!Read(path, 32768).AsSpan().SequenceEqual(bytes)) throw new IOException("RECEIPT_READBACK_FAILED");
    }
    public static Task<int> Main(string[] args) => Run(args, null);
    public static async Task<int> Run(string[] args, Func<ValidatedAcquisition, string, Task<ImporterAcquisitionResult>>? testAcquisition)
    {
        var stage = "arguments"; string? intentPath = null, resultPath = null;
        var intentCreated = false; var fetchAttempted = false;
        var intentSaveAttempted = false;
        string? requestSha = null, policySha = null;
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var fetch = false; var modeSeen = false;
            var required = new[] { "--request", "--policy", "--store-root", "--receipt-root", "--request-sha256", "--policy-sha256" };
            for (var n = 0; n < args.Length; n++)
            {
                if (args[n] is "--fetch-and-save" or "--validate-only")
                { if (modeSeen) throw new ArgumentException("DUPLICATE_MODE"); modeSeen = true; fetch = args[n] == "--fetch-and-save"; continue; }
                if (!required.Contains(args[n], StringComparer.Ordinal) || n + 1 >= args.Length || !values.TryAdd(args[n], args[++n]))
                    throw new ArgumentException("ARGUMENTS_INVALID");
            }
            if (values.Count != required.Length) throw new ArgumentException("ARGUMENTS_MISSING");
            stage = "capture";
            var requestBytes = Read(values["--request"], AcquisitionInput.RequestLimit);
            var policyBytes = Read(values["--policy"], AcquisitionInput.PolicyLimit);
            requestSha = AcquisitionInput.Sha(requestBytes); policySha = AcquisitionInput.Sha(policyBytes);
            if (!string.Equals(requestSha, values["--request-sha256"], StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(policySha, values["--policy-sha256"], StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("INPUT_HASH_MISMATCH");
            stage = "validate"; var input = AcquisitionInput.Validate(requestBytes, policyBytes);
            DirectorySafe(values["--store-root"]); DirectorySafe(values["--receipt-root"]);
            intentPath = Path.Combine(values["--receipt-root"], input.Request.OperationId + ".intent.safe.json");
            resultPath = Path.Combine(values["--receipt-root"], input.Request.OperationId + ".result.safe.json");
            if (File.Exists(intentPath) || File.Exists(resultPath)) throw new IOException("PRIOR_OPERATION_PRESERVED");
            if (!fetch)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { schema = "lips-importer-cli/v1", status = "VALIDATED_NOT_EXECUTED",
                    requestSha256 = requestSha, policySha256 = policySha, policyVersion = input.Hosts.Version, expectedExtractorVersion = input.ExpectedExtractorVersion,
                    httpRequests = 0, filesWritten = 0, automaticRetryAllowed = false }, AcquisitionInput.Json)); return 0;
            }
            stage = "intent";
            intentSaveAttempted = true;
            SaveNew(intentPath, new { schema = "lips-importer-cli-intent/v1", status = "STARTED_NOT_OUTCOME", input.Request.OperationId,
                requestSha256 = requestSha, policySha256 = policySha, startedAtUtc = DateTimeOffset.UtcNow,
                maximumAcquisitions = 1, maximumRedirects = 3, automaticRetryAllowed = false }); intentCreated = true;
            stage = "acquisition"; fetchAttempted = true;
            ImporterAcquisitionResult result;
            if (testAcquisition is not null) result = await testAcquisition(input, values["--store-root"]);
            else {
                var acquisition = new ImporterEnrichmentAcquisition(new OfficialImporterPageFetcher(),
                    new FileImporterEnrichmentWriter(values["--store-root"], new(input.Policy.MaximumFiles, input.Policy.MaximumTotalBytes)), input.Hosts,input.Adapters);
                result = await acquisition.AcquireAsync(new(input.Request.Identity, input.Request.OriginalFingerprint, []),
                    input.Request.SourceDocumentId, input.Request.ExtractedJson, input.Request.OfficialUrl, input.Request.ExpectedGeneration);
            }
            stage = "result";
            var report = new { schema = "lips-importer-cli-result/v1", status = result.Status, stage = "complete", requestSha256 = requestSha,
                policySha256 = policySha, fetchAttempted, result.Saved, automaticRetryAllowed = false };
            SaveNew(resultPath, report); Console.WriteLine(JsonSerializer.Serialize(report, AcquisitionInput.Json)); return 0;
        }
        catch (Exception error)
        {
            var name = error.GetType().Name;
            var safeType = new[] { "ArgumentException", "IOException", "InvalidDataException", "JsonException", "UnauthorizedAccessException",
                "HttpRequestException", "TaskCanceledException", "OperationCanceledException", "InvalidOperationException" }.Contains(name) ? name : "OTHER";
            var partialIntentMayExist = intentSaveAttempted && !intentCreated;
            var report = new { schema = "lips-importer-cli-result/v1", status = "STOPPED_OR_UNKNOWN", stage,
                requestSha256 = requestSha, policySha256 = policySha, errorType = safeType, fetchAttempted,
                reasonCode = SafeReason(error), intentSaveAttempted, intentCreated, partialIntentMayExist,
                automaticRetryAllowed = false };
            var failureResultSaved = false;
            var failureResultSaveAttempted = intentSaveAttempted && resultPath is not null && !File.Exists(resultPath);
            if (failureResultSaveAttempted)
                try { SaveNew(resultPath!, report); failureResultSaved = true; } catch { /* Preserve partial receipts; never overwrite. */ }
            Console.WriteLine(JsonSerializer.Serialize(new { report.schema, report.status, report.stage,
                report.requestSha256, report.policySha256, report.errorType, report.fetchAttempted, report.reasonCode,
                report.intentSaveAttempted, report.intentCreated, report.partialIntentMayExist,
                failureResultSaveAttempted, failureResultSaved, failureResultSaveOutcomeUnknown = failureResultSaveAttempted && !failureResultSaved,
                automaticRetryAllowed = false }, AcquisitionInput.Json)); return 1;
        }
    }
}
