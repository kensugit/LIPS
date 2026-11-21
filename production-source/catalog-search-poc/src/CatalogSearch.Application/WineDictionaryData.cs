using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatalogSearch.Application;

internal sealed record WineDictionaryPlace(string Label, string[] Countries, string[] Names, string[] Members);

internal sealed record WineDictionaryData(int Version, Dictionary<string, string[]> Grapes, WineDictionaryPlace[] Places,
    string[] Sweetness, Dictionary<string, string[]> Flavors)
{
    internal static WineDictionaryData Current { get; }
    internal static string Revision { get; }

    static WineDictionaryData()
    {
        using var source = typeof(WineDictionaryData).Assembly.GetManifestResourceStream("wine-search.ja.json")
            ?? throw new InvalidDataException("Wine dictionary resource is missing.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();
        Current = Parse(bytes);
        Revision = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static string GrapeKey(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c) && c != '・'));

    private static WineDictionaryData Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > 262144) throw new InvalidDataException("Invalid wine dictionary size.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        void VerifyUniqueProperties(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate wine dictionary entry.");
                    VerifyUniqueProperties(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var child in value.EnumerateArray()) VerifyUniqueProperties(child);
        }
        VerifyUniqueProperties(document.RootElement);
        var data = document.RootElement.Deserialize<WineDictionaryData>(new JsonSerializerOptions {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("Wine dictionary data is missing.");
        static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
            value == value.Trim() && !value.Any(char.IsControl);
        static bool Names(string[]? values, int maximum, bool mayBeEmpty = false) =>
            values is not null && values.Length <= maximum && (mayBeEmpty || values.Length > 0) &&
            values.All(Text) && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length;
        if (data.Version != 1 || data.Grapes is null || data.Grapes.Count is 0 or > 500 ||
            data.Grapes.Any(g => !Text(g.Key) || !Names(g.Value, 20) || !g.Value.Contains(g.Key, StringComparer.Ordinal)) ||
            data.Places is null || data.Places.Length is 0 or > 1000 ||
            data.Places.Any(p => p is null || !Text(p.Label) || !Names(p.Countries, 3) || !Names(p.Names, 25) || !Names(p.Members, 128, true)) ||
            !Names(data.Sweetness, 20) || data.Flavors is null || data.Flavors.Count > 50 ||
            data.Flavors.Any(f => !Text(f.Key) || !Names(f.Value, 20)) ||
            data.Flavors.Keys.Intersect(data.Sweetness, StringComparer.Ordinal).Any())
            throw new InvalidDataException("Wine dictionary validation failed.");
        var aliasOwners = data.Grapes.SelectMany(g => g.Value.Select(a => (Key: GrapeKey(a), Name: g.Key)))
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase);
        if (aliasOwners.Any(g => g.Key.Length == 0 || g.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new InvalidDataException("Wine dictionary grape aliases are ambiguous.");
        var countryGroups = data.Places.GroupBy(place => place.Countries[0], StringComparer.Ordinal);
        if (countryGroups.Any(group => group.Any(place => !place.Countries.SequenceEqual(group.First().Countries))) ||
            countryGroups.SelectMany(group => group.First().Countries).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Wine dictionary country aliases are ambiguous.");
        return data;
    }
}
