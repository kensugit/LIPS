using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

public static class CatalogSearchVocabulary
{
    private sealed record Vocabulary(int Version, Dictionary<string, string[]> ProductTypes,
        string[] Sparkling, string[] SparklingSourceTypes, string[] InStock, string[] NonVintage);

    private static readonly Vocabulary Data;
    private static readonly Dictionary<string, string> Types;
    public static string Revision { get; }
    public static string ProductTypePattern { get; }
    public static string SparklingPattern { get; }
    public static string InStockPattern { get; }
    public static string NonVintagePattern { get; }
    public static IReadOnlyList<string> SparklingSourceTypes { get; }

    static CatalogSearchVocabulary()
    {
        using var input = typeof(CatalogSearchVocabulary).Assembly.GetManifestResourceStream("catalog-search-elements.ja.json")
            ?? throw new InvalidDataException("Catalog vocabulary resource is missing.");
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        if (buffer.Length is 0 or > 16384) throw new InvalidDataException("Catalog vocabulary size is invalid.");
        var bytes = buffer.ToArray();
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 5 });
        var fields = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
        var typeFields = document.RootElement.GetProperty("ProductTypes").EnumerateObject().Select(x => x.Name).ToArray();
        if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Length ||
            typeFields.Distinct(StringComparer.Ordinal).Count() != typeFields.Length)
            throw new InvalidDataException("Duplicate catalog vocabulary entry.");
        Data = document.RootElement.Deserialize<Vocabulary>(new JsonSerializerOptions {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("Catalog vocabulary data is missing.");
        static bool Valid(string[]? names, bool raw = false) => names is { Length: > 0 and <= 40 } &&
            names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 40 && n == n.Trim() && !n.Any(char.IsControl)) &&
            (raw ? names : names.Select(Key)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length;
        if (Data.Version != 1 || !typeFields.ToHashSet(StringComparer.Ordinal).SetEquals(["赤","白","ロゼ","オレンジ"]) ||
            !Data.ProductTypes.Values.All(aliases => Valid(aliases)) || !Valid(Data.Sparkling) || !Valid(Data.SparklingSourceTypes, raw: true) ||
            !Valid(Data.InStock) || !Valid(Data.NonVintage))
            throw new InvalidDataException("Invalid catalog vocabulary.");
        Types = Data.ProductTypes.SelectMany(group => group.Value.Select(alias => (Alias: Key(alias), Type: group.Key)))
            .ToDictionary(pair => pair.Alias, pair => pair.Type, StringComparer.OrdinalIgnoreCase);
        ProductTypePattern = Pattern(Types.Keys);
        SparklingPattern = Pattern(Data.Sparkling.Where(alias => alias != "泡"));
        InStockPattern = Pattern(Data.InStock);
        NonVintagePattern = Pattern(Data.NonVintage);
        SparklingSourceTypes = Array.AsReadOnly(Data.SparklingSourceTypes);
        Revision = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string Key(string value) => value.Normalize(NormalizationForm.FormKC);
    private static string Pattern(IEnumerable<string> values) =>
        string.Join("|", values.Select(Key).OrderByDescending(value => value.Length).Select(Regex.Escape));
    public static string ProductType(string alias) => Types[Key(alias)];
    public static bool IsNonVintage(string value) => Data.NonVintage.Contains(value, StringComparer.OrdinalIgnoreCase);
}
