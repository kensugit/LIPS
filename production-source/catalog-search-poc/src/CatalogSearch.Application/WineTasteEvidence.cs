using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

public static class WineTasteEvidence
{
    private static readonly string[] Sweetness = WineDictionaryData.Current.Sweetness;
    private static readonly Dictionary<string, string[]> Flavors = WineDictionaryData.Current.Flavors;
    private static readonly Regex SweetnessWords = new(string.Join("|", Sweetness.Concat(["中甘口", "中辛口"])
        .OrderByDescending(t => t.Length).Select(Regex.Escape)),
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Uncertain = new("ない|なく|無し|なし|不使用|ではなく|ではない|とは異|抑え|向け|好き|近い|から.*まで",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static bool Supports(string taste) => Sweetness.Contains(taste) || Flavors.ContainsKey(taste);

    public static bool Matches(string json, string taste)
    {
        if (json.Length > 262144 || !Supports(taste)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var row = doc.RootElement;
            if (row.ValueKind != JsonValueKind.Object) return false;
            string Read(string name)
            {
                var fields = row.EnumerateObject().Where(p => p.NameEquals(name)).Take(2).ToArray();
                return fields.Length == 1 && fields[0].Value.ValueKind == JsonValueKind.String
                    ? fields[0].Value.GetString()!.Normalize(NormalizationForm.FormKC) : "";
            }
            var description = Read("Description");
            if (description.Length > 16000) return false;
            var clauses = description.Split(['。', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (Sweetness.Contains(taste))
            {
                var explicitStyle = Read("Sweetness").Trim();
                if (explicitStyle.Length > 0) return explicitStyle == taste;
                var claims = clauses.Where(c => !Uncertain.IsMatch(c))
                    .SelectMany(c => SweetnessWords.Matches(c).Select(m => m.Value)).Distinct(StringComparer.Ordinal).ToArray();
                return claims.Length == 1 && claims[0] == taste;
            }
            return clauses.Any(c => !Uncertain.IsMatch(c) &&
                Flavors[taste].Any(term => c.Contains(term, StringComparison.Ordinal)));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or RegexMatchTimeoutException) { return false; }
    }
}
