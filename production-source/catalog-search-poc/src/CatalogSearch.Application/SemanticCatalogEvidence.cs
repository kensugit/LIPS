using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CatalogSearch.Application;

public static class GrapeRegistry
{
    private static readonly Dictionary<string, string[]> Entries = WineDictionaryData.Current.Grapes;
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(Entries.Keys.ToArray());
    public static bool TryGetAliases(string name, out IReadOnlyList<string> aliases)
    {
        if (Entries.TryGetValue(name, out var values)) { aliases = Array.AsReadOnly(values); return true; }
        aliases = []; return false;
    }

    public static string? Resolve(string input)
    {
        var key = WineDictionaryData.GrapeKey(input);
        var matches = Entries.Where(entry => entry.Value.Any(alias => WineDictionaryData.GrapeKey(alias).Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Key).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string[] MentionedNames(string query)
    {
        var spans = new List<(int Start, int Length, string Name)>();
        foreach (var entry in Entries)
        foreach (var alias in entry.Value)
        {
            var latin = alias.All(c => c < 128);
            for (var offset = 0; offset < query.Length;)
            {
                var start = query.IndexOf(alias, offset, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                offset = start + alias.Length;
                if (latin && ((start > 0 && char.IsAsciiLetterOrDigit(query[start - 1])) ||
                    (offset < query.Length && char.IsAsciiLetterOrDigit(query[offset])))) continue;
                spans.Add((start, alias.Length, entry.Key));
            }
        }
        return spans.Where(match => !spans.Any(other => other.Length > match.Length &&
            other.Start <= match.Start && other.Start + other.Length >= match.Start + match.Length))
            .Select(match => match.Name).Distinct(StringComparer.Ordinal).ToArray();
    }
}

public static class SourceFingerprint
{
    public static string Create(Guid sourceDocumentId, string extractedRowJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceDocumentId.ToString("D") + "\n" + extractedRowJson)));
}

public static class SourceIdentityBinding
{
    public static bool Verify(ImporterProductIdentity identity, string json)
    {
        if (json.Length > 262144) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var row = doc.RootElement;
            if (row.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (row.EnumerateObject().Any(p => !names.Add(p.Name))) return false;
            bool Same(string key, string value) => row.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == value;
            return Same("SupplierProductCode", identity.Sku) && Same("ProducerNameJa", identity.Producer) &&
                Same("ProductNameJa", identity.Product) && Same("VintageRaw", identity.Vintage) && Same("ProductType", identity.Variant) &&
                row.TryGetProperty("VolumeMl", out var volume) && volume.ValueKind == JsonValueKind.Number &&
                volume.TryGetInt32(out var ml) && ml == identity.VolumeMl;
        }
        catch (JsonException) { return false; }
    }
}

public sealed record SemanticCatalogSearchResult(IReadOnlyList<CatalogResult> Items,
    int EvidenceUnavailableCount, int EvidenceConflictCount, bool Complete);

public sealed record ImporterSearchEvidence(string SourceUrl, DateTimeOffset FetchedAtUtc,
    string Grape, string SourceExcerpt, string RecordSha256);

public static class SemanticGrapeMatcher
{
    public static string? ReadOriginal(string json)
    {
        if (json.Length > 262144) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var fields = doc.RootElement.EnumerateObject().Where(p => p.Name == "Grapes").ToArray();
            return fields.Length == 1 && fields[0].Value.ValueKind == JsonValueKind.String ? fields[0].Value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public static string? ReadFinesseDescriptionClaim(string json, string productName)
    {
        if (json.Length > 262144 || string.IsNullOrWhiteSpace(productName)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                doc.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != doc.RootElement.EnumerateObject().Count())
                return null;
            var row = doc.RootElement;
            if (!row.TryGetProperty("ProductNameJa", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != productName ||
                !row.TryGetProperty("Description", out var description) || description.ValueKind != JsonValueKind.String) return null;
            // Finesse puts the product name immediately before the composition at the start of this field.
            var prefix = "フィネス Finesse " + productName + " ";
            var value = description.GetString()!;
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var separator = value.IndexOfAny(['。', '\r', '\n'], prefix.Length);
            var claim = (separator < 0 ? value[prefix.Length..] : value[prefix.Length..separator]).Trim();
            return claim.Length is > 0 and <= 100 ? claim : null;
        }
        catch (JsonException) { return null; }
    }

    public static GrapeEvidenceDecision Match(string? grapes, SemanticGrapePredicate predicate)
    {
        if (!GrapeRegistry.TryGetAliases(predicate.Name, out var aliases)) return new(false, "UNKNOWN_GRAPE");
        var majority = GrapeEvidence.FromGrapes(grapes, aliases);
        if (predicate.Mode == "predominant") return majority;
        if (predicate.Mode != "contains") return new(false, "UNKNOWN_MODE");
        if (majority.Included || majority.ExplicitPercent > 0) return new(true, "EXPLICIT_CONTAINS", majority.ExplicitPercent);
        if (string.IsNullOrWhiteSpace(grapes) || grapes.Length > 4096) return new(false, "GRAPES_MISSING_OR_BOUND");
        // A plain list is sufficient for contains, never for predominant. Qualified prose is not.
        var parts = grapes.Normalize(NormalizationForm.FormKC).Replace("サンジョヴェーゼ", "サンジョベーゼ", StringComparison.Ordinal)
            .Split([',', '、', ';', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 20 && parts.All(p => p.Length <= 100 && !p.Any(char.IsControl) &&
            !p.Any(c => char.IsDigit(c) || c is '%' or '：' or ':') &&
            !new[] { "不使用", "なし", "無し", "以外", "除く", "ない", "主体", "not ", "without " }.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))) &&
            parts.Any(p => aliases.Contains(p, StringComparer.OrdinalIgnoreCase))) return new(true, "EXPLICIT_GRAPE_LIST");
        return new(false, "GRAPE_EVIDENCE_INSUFFICIENT");
    }
}
