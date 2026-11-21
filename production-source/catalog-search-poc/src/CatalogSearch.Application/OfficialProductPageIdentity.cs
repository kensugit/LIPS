using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

// Deliberately narrow, reviewed adapter: unsupported HTML remains a candidate.
// No LLM classification, fuzzy names, geographic assumptions, or wildcard vintage.
public static class OfficialProductPageIdentity
{
    private static readonly Regex Scripts = new("<script\\b[^>]*>(?<body>.*?)</script\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex ExactOpening = new("^<script\\s+type\\s*=\\s*([\"'])application/ld\\+json\\1\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // Initial extractor intentionally supports one explicitly named grape only.
    public static bool TryExtract(ImporterProductIdentity identity, ReadOnlyMemory<byte> body,
        out IReadOnlyList<ImporterGrapeEvidence> grapes)
    {
        grapes = Array.Empty<ImporterGrapeEvidence>();
        if (body.Length is 0 or > ImporterEvidenceValidator.MaxBodyBytes) return false;
        try
        {
            using var doc = ParsePage(body);
            var root = doc.RootElement;
            if (!Unique(root) || !root.TryGetProperty("additionalProperty", out var props) || props.ValueKind != JsonValueKind.Array) return false;
            string? grape = null, excerpt = null;
            foreach (var prop in props.EnumerateArray())
            {
                if (Text(prop, "name") == "grapes") { if (grape is not null) return false; grape = Text(prop, "value"); }
                if (Text(prop, "name") == "grapeEvidence") { if (excerpt is not null) return false; excerpt = Text(prop, "value"); }
            }
            if (!ImporterEvidenceValidator.ValidText(grape, 100) || !ImporterEvidenceValidator.ValidText(excerpt, 1000) ||
                grape!.IndexOfAny([',', ';', '/', '、', '%']) >= 0) return false;
            var candidate = new[] { new ImporterGrapeEvidence(grape, excerpt!) };
            if (!Verify(identity, candidate, body)) return false;
            grapes = Array.AsReadOnly(candidate);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or RegexMatchTimeoutException or InvalidOperationException) { return false; }
    }

    private static JsonDocument ParsePage(ReadOnlyMemory<byte> body)
    {
        var html = new UTF8Encoding(false, true).GetString(body.Span);
        string json;
        if (html.TrimStart().StartsWith('{')) json = html;
        else
        {
            html = Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
            var candidates = Scripts.Matches(html).Where(x => x.Value.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1 || !ExactOpening.IsMatch(candidates[0].Value)) throw new JsonException();
            json = candidates[0].Groups["body"].Value;
        }
        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
    }

    public static bool Verify(ImporterProductIdentity identity,
        IReadOnlyList<ImporterGrapeEvidence> grapes, ReadOnlyMemory<byte> body)
    {
        if (body.Length is 0 or > ImporterEvidenceValidator.MaxBodyBytes || grapes.Count is 0 or > 20) return false;
        try
        {
            using var doc = ParsePage(body);
            var p = doc.RootElement;
            if (!Unique(p) || ProductCount(p) != 1 || p.ValueKind != JsonValueKind.Object || Text(p, "@type") != "Product" ||
                Text(p, "@context") != "https://schema.org" || p.TryGetProperty("@graph", out _) ||
                Text(p, "sku") != identity.Sku || Text(p, "name") != identity.Product) return false;
            if (!p.TryGetProperty("brand", out var brand) || Text(brand, "name") != identity.Producer) return false;
            if (!p.TryGetProperty("additionalProperty", out var props) || props.ValueKind != JsonValueKind.Array) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in props.EnumerateArray())
            {
                if (Text(prop, "@type") != "PropertyValue" || Text(prop, "name") is not { } key ||
                    Text(prop, "value") is not { } value || !values.TryAdd(key, value)) return false;
            }
            if (!values.TryGetValue("vintage", out var vintage) || vintage != identity.Vintage ||
                !values.TryGetValue("volumeMl", out var volume) || volume != identity.VolumeMl.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !values.TryGetValue("variant", out var variant) || variant != identity.Variant) return false;
            var description = Text(p, "description");
            return description is not null && grapes.All(g => g is not null &&
                !string.IsNullOrWhiteSpace(g.Grape) && !string.IsNullOrWhiteSpace(g.SourceExcerpt) &&
                description.Contains(g.SourceExcerpt, StringComparison.Ordinal) &&
                g.SourceExcerpt.Contains(g.Grape, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or RegexMatchTimeoutException) { return false; }
    }
    private static string? Text(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int ProductCount(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return (Text(value, "@type") == "Product" ? 1 : 0) + value.EnumerateObject().Sum(x => ProductCount(x.Value));
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Sum(ProductCount) : 0;
    }
    private static bool Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in value.EnumerateObject()) if (!seen.Add(p.Name) || !Unique(p.Value)) return false;
        }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (!Unique(item)) return false;
        return true;
    }
}
