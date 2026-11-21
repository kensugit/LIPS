using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatalogSearch.Application;

// Input claims are not an authorization or evidence of network provenance.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterProductIdentity(string SupplierCode, string Sku, string Producer,
    string Product, string Vintage, int VolumeMl, string Variant);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterGrapeEvidence(string Grape, string SourceExcerpt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImporterProductEvidence(string Schema, string RequestedUrl, string FinalUrl,
    DateTimeOffset FetchedAtUtc, string ContentSha256, string OriginalFingerprint,
    ImporterProductIdentity Identity, string IdentificationStatus, [property: JsonRequired] bool OriginalConflict,
    IReadOnlyList<ImporterGrapeEvidence> Grapes);

// Supplied by the original catalog, never by the page or LLM.
public sealed record ImporterOriginalProduct(ImporterProductIdentity Identity, string Fingerprint,
    IReadOnlyList<string> KnownGrapes);

// Host rules must come from reviewed importer configuration, not evidence JSON.
public sealed class ImporterEvidenceHostPolicy
{
    private readonly Dictionary<string, HashSet<string>> hosts = new(StringComparer.Ordinal);
    public string Version { get; }
    public ImporterEvidenceHostPolicy(IEnumerable<KeyValuePair<string, string>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var (supplier, host) in rules)
        {
            if (!ImporterEvidenceValidator.ValidText(supplier, 100) ||
                !ValidHost(host)) throw new ArgumentException("INVALID_IMPORTER_HOST_POLICY");
            if (!hosts.TryGetValue(supplier, out var values)) hosts[supplier] = values = new(StringComparer.Ordinal);
            if (!values.Add(host)) throw new ArgumentException("DUPLICATE_IMPORTER_HOST_POLICY");
        }
        if (hosts.Count == 0 || hosts.Count > 100) throw new ArgumentException("INVALID_IMPORTER_HOST_POLICY");
        Version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            hosts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { supplier = x.Key, hosts = x.Value.Order(StringComparer.Ordinal).ToArray() }).ToArray()))));
    }
    private static bool ValidHost(string? host) => host is not null && host.Length is >= 3 and <= 253 &&
        host == host.ToLowerInvariant() && host.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-') &&
        !host.EndsWith('.') && Uri.CheckHostName(host) == UriHostNameType.Dns &&
        host.Contains('.') && host != "localhost";
    public bool Allows(string supplier, string url)
    {
        if (url is null || url.Length > 2048 || url.Any(char.IsControl) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        return parsed.Scheme == Uri.UriSchemeHttps && parsed.Port == 443 &&
            parsed.UserInfo.Length == 0 && parsed.Fragment.Length == 0 && parsed.Query.Length == 0 &&
            !parsed.IsLoopback && parsed.HostNameType == UriHostNameType.Dns &&
            hosts.TryGetValue(supplier, out var allowed) && allowed.Contains(parsed.IdnHost);
    }
}

// Bound source excerpts, not interpreted grape/majority facts. The caller must parse their meaning.
// No price, stock, percentage, dominance or original-field update API.
public sealed class ValidatedImporterProductEvidence
{
    internal ValidatedImporterProductEvidence(ImporterProductEvidence value)
    {
        Identity = value.Identity with { };
        OriginalFingerprint = value.OriginalFingerprint;
        RequestedUrl = value.RequestedUrl; FinalUrl = value.FinalUrl;
        ContentSha256 = value.ContentSha256; FetchedAtUtc = value.FetchedAtUtc;
        Grapes = Array.AsReadOnly(value.Grapes.Select(x => x with { }).ToArray());
    }
    public ImporterProductIdentity Identity { get; }
    public string OriginalFingerprint { get; }
    public string RequestedUrl { get; }
    public string FinalUrl { get; }
    public string ContentSha256 { get; }
    public DateTimeOffset FetchedAtUtc { get; }
    public IReadOnlyList<ImporterGrapeEvidence> Grapes { get; }
}

public static class ImporterEvidenceValidator
{
    public const string Schema = "lips-importer-product-evidence/v1";
    public const int MaxBodyBytes = 2 * 1024 * 1024;
    public const int MaxEvidenceJsonBytes = 32768;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    internal static bool ValidText(string? value, int max) => value is not null &&
        value.Length is > 0 && value.Length <= max && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Need(bool passed, string reason)
    {
        if (!passed) throw new ArgumentException(reason);
    }
    private static void Identity(ImporterProductIdentity? value)
    {
        Need(value is not null, "IDENTITY_MISSING");
        var v = value!;
        Need(ValidText(v.SupplierCode, 100) && ValidText(v.Sku, 100) &&
            ValidText(v.Producer, 200) && ValidText(v.Product, 200) &&
            ValidText(v.Vintage, 32) && ValidText(v.Variant, 100) && v.VolumeMl is > 0 and <= 100000,
            "IDENTITY_UNKNOWN_OR_INVALID");
        // Unknown must not be encoded as a variant/year wildcard.
        Need(!new[] { "unknown", "不明", "?", "*" }.Contains(v.Vintage, StringComparer.OrdinalIgnoreCase) &&
             !new[] { "unknown", "不明", "?", "*" }.Contains(v.Variant, StringComparer.OrdinalIgnoreCase),
             "IDENTITY_UNKNOWN_OR_INVALID");
    }
    private static void RejectDuplicateMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in value.EnumerateObject())
            {
                Need(names.Add(p.Name), "DUPLICATE_JSON_MEMBER"); RejectDuplicateMembers(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicateMembers(child);
    }
    public static ImporterProductEvidence Parse(ReadOnlyMemory<byte> json)
    {
        Need(json.Length is > 0 and <= MaxEvidenceJsonBytes, "EVIDENCE_SIZE");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        RejectDuplicateMembers(doc.RootElement);
        return JsonSerializer.Deserialize<ImporterProductEvidence>(json.Span, Options)
            ?? throw new ArgumentException("EVIDENCE_MISSING");
    }
    public static ValidatedImporterProductEvidence Validate(ImporterProductEvidence evidence,
        ImporterOriginalProduct original, ImporterEvidenceHostPolicy policy,
        ReadOnlyMemory<byte> fetchedPageBytes, DateTimeOffset nowUtc,
        Func<IReadOnlyList<string>, IReadOnlyList<ImporterGrapeEvidence>, bool> originalConflictDetector,
        Func<ImporterProductIdentity, IReadOnlyList<ImporterGrapeEvidence>, ReadOnlyMemory<byte>, bool> pageIdentityVerifier)
    {
        ArgumentNullException.ThrowIfNull(evidence); ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(originalConflictDetector);
        ArgumentNullException.ThrowIfNull(pageIdentityVerifier);
        Need(evidence.Schema == Schema, "SCHEMA_MISMATCH");
        Identity(original.Identity); Identity(evidence.Identity);
        Need(evidence.Identity == original.Identity, "IDENTITY_MISMATCH");
        Need(evidence.IdentificationStatus == "EXACT" && !evidence.OriginalConflict,
            "IDENTIFICATION_NOT_ACCEPTED");
        Need(Hash(original.Fingerprint) && evidence.OriginalFingerprint == original.Fingerprint,
            "ORIGINAL_FINGERPRINT_MISMATCH");
        Need(policy.Allows(original.Identity.SupplierCode, evidence.RequestedUrl) &&
            policy.Allows(original.Identity.SupplierCode, evidence.FinalUrl), "IMPORTER_URL_NOT_ALLOWED");
        Need(nowUtc.Offset == TimeSpan.Zero && nowUtc != default &&
            evidence.FetchedAtUtc.Offset == TimeSpan.Zero && evidence.FetchedAtUtc != default &&
            evidence.FetchedAtUtc <= nowUtc, "FETCH_TIMESTAMP_INVALID");
        Need(fetchedPageBytes.Length is > 0 and <= MaxBodyBytes, "PAGE_SIZE");
        Need(Hash(evidence.ContentSha256) &&
            Convert.ToHexStringLower(SHA256.HashData(fetchedPageBytes.Span)) == evidence.ContentSha256,
            "PAGE_HASH_MISMATCH");
        string body;
        try { body = new UTF8Encoding(false, true).GetString(fetchedPageBytes.Span); }
        catch (DecoderFallbackException) { throw new ArgumentException("PAGE_UTF8_REQUIRED"); }
        Need(evidence.Grapes is { Count: > 0 and <= 20 }, "GRAPE_EVIDENCE_MISSING");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grape in evidence.Grapes)
        {
            Need(grape is not null && ValidText(grape.Grape, 100) &&
                ValidText(grape.SourceExcerpt, 1000), "GRAPE_EVIDENCE_INVALID");
            Need(seen.Add(grape!.Grape), "DUPLICATE_GRAPE");
            Need(body.Contains(grape.SourceExcerpt, StringComparison.Ordinal) &&
                grape.SourceExcerpt.Contains(grape.Grape, StringComparison.Ordinal), "EXCERPT_NOT_BOUND_TO_PAGE");
        }
        // The reviewed importer-specific mapper must bind EVERY excerpt to the SAME product
        // section/variant/year, not a related product elsewhere in the body. EXACT is insufficient.
        // Network acquisition safety is outside this pure validator.
        Need(pageIdentityVerifier(original.Identity, evidence.Grapes, fetchedPageBytes), "PAGE_IDENTITY_NOT_VERIFIED");
        Need(original.KnownGrapes is not null && original.KnownGrapes.Count <= 20 &&
            original.KnownGrapes.All(x => ValidText(x, 4096)), "ORIGINAL_GRAPES_INVALID");
        // The reviewed semantic parser decides substantive conflicts (e.g. 40% versus 80%).
        // Literal spelling/list differences alone are not a contradiction.
        Need(!originalConflictDetector(original.KnownGrapes!, evidence.Grapes), "ORIGINAL_GRAPES_CONFLICT");
        return new ValidatedImporterProductEvidence(evidence);
    }
}
