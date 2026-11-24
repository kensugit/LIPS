using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

/// <summary>Reviewed display/search aliases only. Source values and geographical membership stay unchanged.</summary>
public static class CatalogFacetVocabulary
{
    private sealed record RegionAliases(string Country, string Label, string[] Names);
    private sealed record RightBankData(string Version, string[] Sources, RegionAliases[] Groups);
    private sealed record Data(int Version, Dictionary<string, string[]> CountryAliases, RightBankData RightBankAliases);
    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, string>> Regions = new(StringComparer.OrdinalIgnoreCase);
    public static string Revision { get; }
    public const string RightBankSourceVersion = "libournais-user-selected/20261120-v2";
    public static IReadOnlyList<string> RightBankSources { get; }
    public static IReadOnlyList<IReadOnlyList<string>> RightBankAliasGroups { get; }

    static CatalogFacetVocabulary()
    {
        using var stream = typeof(CatalogFacetVocabulary).Assembly.GetManifestResourceStream("catalog-facets.ja.json")
            ?? throw new InvalidDataException("Facet vocabulary resource is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
        if (bytes.Length is 0 or > 32768) throw new InvalidDataException("Invalid facet vocabulary size.");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        CheckDuplicates(json.RootElement);
        var data = json.RootElement.Deserialize<Data>(new JsonSerializerOptions {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("Facet vocabulary is missing.");
        if (data.Version != 1 || data.CountryAliases is null || data.CountryAliases.Count is 0 or > 250)
            throw new InvalidDataException("Invalid facet country aliases.");
        foreach (var (canonical, aliases) in data.CountryAliases)
        {
            if (!Valid(canonical) || aliases is null || aliases.Length is 0 or > 20 || !aliases.Contains(canonical, StringComparer.Ordinal))
                throw new InvalidDataException("Invalid facet country aliases.");
            foreach (var alias in aliases)
            {
                if (!Valid(alias)) throw new InvalidDataException("Invalid facet country alias.");
                var key = ExactKey(alias);
                if (Countries.TryGetValue(key, out var owner) && owner != canonical)
                    throw new InvalidDataException("Ambiguous facet country alias.");
                Countries[key] = canonical;
            }
        }
        // Names are synonyms. Members are geographical children/coverage and MUST NOT be collapsed.
        foreach (var place in WineDictionaryData.Current.Places)
        foreach (var country in place.Countries)
        {
            AddRegionAliases(country, place.Names[0], place.Names);
        }
        var reviewed = data.RightBankAliases;
        if (reviewed is null || reviewed.Version != RightBankSourceVersion || reviewed.Sources is not { Length: > 0 and <= 10 } ||
            reviewed.Sources.Any(url => !Valid(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") ||
            reviewed.Groups is not { Length: 12 }) throw new InvalidDataException("Invalid reviewed right-bank aliases.");
        foreach (var group in reviewed.Groups)
        {
            if (group is null || group.Country != "フランス" || !Valid(group.Label) || group.Names is not { Length: > 0 and <= 25 } ||
                group.Names.Any(name => !Valid(name)) || !group.Names.Contains(group.Label, StringComparer.Ordinal) ||
                group.Names.Distinct(StringComparer.Ordinal).Count() != group.Names.Length)
                throw new InvalidDataException("Invalid reviewed right-bank alias group.");
            AddRegionAliases(group.Country, group.Label, group.Names);
        }
        RightBankSources = Array.AsReadOnly(reviewed.Sources.ToArray());
        RightBankAliasGroups = Array.AsReadOnly(reviewed.Groups.Select(g => (IReadOnlyList<string>)Array.AsReadOnly(g.Names.ToArray())).ToArray());
        Revision = Convert.ToHexStringLower(SHA256.HashData(bytes)) + "+" + WineDictionaryData.Revision;
    }

    private static bool Valid(string? value) => value is { Length: > 0 and <= 200 } && value == value.Trim() && !value.Any(char.IsControl);
    private static void AddRegionAliases(string country, string label, IEnumerable<string> names)
    {
        var countryKey = Country(country);
        if (!Regions.TryGetValue(countryKey, out var aliases)) Regions[countryKey] = aliases = new(StringComparer.OrdinalIgnoreCase);
        var canonical = ExactKey(label);
        foreach (var name in names)
        {
            var key = ExactKey(name);
            if (aliases.TryGetValue(key, out var owner) && owner != canonical) throw new InvalidDataException("Ambiguous facet region alias.");
            aliases[key] = canonical;
        }
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate facet vocabulary entry."); CheckDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) CheckDuplicates(value);
    }

    public static string ExactKey(string? value) => string.IsNullOrWhiteSpace(value) ? "" :
        Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
    public static string Country(string? raw)
    { var key = ExactKey(raw); return Countries.TryGetValue(key, out var canonical) ? canonical : key; }
    public static string Region(string? country, string? raw)
    {
        var key = ExactKey(raw); var owner = Country(country);
        return owner.Length > 0 && Regions.TryGetValue(owner, out var aliases) && aliases.TryGetValue(key, out var canonical) ? canonical : key;
    }
    public static bool SameCountry(string? left, string? right) => Country(left).Equals(Country(right), StringComparison.OrdinalIgnoreCase);
    public static string[] MatchingCountries(IEnumerable<string> values, string requested) => values
        .Where(raw => SameCountry(raw, requested)).Distinct(StringComparer.Ordinal).ToArray();
    public static string[] MatchingRegions(IEnumerable<(string Country, string Region)> pairs, string? country, IEnumerable<string> requested)
    {
        var names = requested.ToArray();
        // Without an explicit country use exact notation only. An alias in France must not expand a Chilean name.
        if (string.IsNullOrWhiteSpace(country))
        {
            var keys = names.Select(ExactKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return pairs.Where(p => keys.Contains(ExactKey(p.Region))).Select(p => p.Region).Distinct(StringComparer.Ordinal).ToArray();
        }
        var regionKeys = names.Select(name => Region(country, name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pairs.Where(p => SameCountry(country, p.Country) && regionKeys.Contains(Region(country, p.Region)))
            .Select(p => p.Region).Distinct(StringComparer.Ordinal).ToArray();
    }
}
