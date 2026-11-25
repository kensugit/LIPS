using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

public static class WineTasteEvidence
{
    private static readonly string[] Sweetness = WineDictionaryData.Current.Sweetness;
    private static readonly Dictionary<string, string[]> Flavors = WineDictionaryData.Current.Flavors;
    // Recognize longer unresolved labels atomically, without deciding their mapping.
    private static readonly string StyleNames = string.Join("|", WineTasteVocabulary.SweetnessNames.Concat(["半辛口", "中甘口", "中辛口"])
        .Distinct(StringComparer.Ordinal).OrderByDescending(t => t.Length).Select(Regex.Escape));
    private static readonly Regex SweetnessWords = new(StyleNames,
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex StyleAssertion = new(@"^(?:(?:この|本)(?:ワイン|商品)は|本品は|味わいは)?\s*" +
        @"(?:(?:フレッシュな酸を伴う|すっきりした|すっきりとした|すっきり|爽やかな|さわやかな|しっかりした|軽やかな|豊かな|濃厚な|やさしい|優しい|きれいな|綺麗な|心地よい)\s*)?(?<style>" + StyleNames +
        @")(?:の(?:赤|白|ロゼ|オレンジ)?ワイン|の味わい)?(?:です|である)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex AromaAssertion = new(@"^(?:(?:この|本)ワインは|本品は)?(?<aromas>[^。\r\n]{1,300})の香り(?:です|がある|があります|が広がる|が広がります|が感じられる|が感じられます)?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Uncertain = new("ない|なく|ません|無し|なし|不使用|ではなく|ではない|とは異|抑え|向け|好き|近い|から.*まで",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex NegativeAttribute = new("非\\s*(?:" + StyleNames + "|" + string.Join("|", Flavors.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).OrderByDescending(v => v.Length).Select(Regex.Escape)) + ")", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static bool IsUncertain(string clause) => Uncertain.IsMatch(clause) || NegativeAttribute.IsMatch(clause);

    public static bool Supports(string taste) => WineTasteVocabulary.ResolveSweetness(taste) is not null || Flavors.ContainsKey(taste);

    public static bool Matches(string json, string taste, string? typeColumn = null)
    {
        if (json.Length > 262144 || !Supports(taste)) return false;
        taste=WineTasteVocabulary.ResolveSweetness(taste)??taste;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var row = doc.RootElement;
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != row.EnumerateObject().Count()) return false;
            string Read(string name)
            {
                var fields = row.EnumerateObject().Where(p => p.NameEquals(name)).Take(2).ToArray();
                if (fields.Length == 0 || fields[0].Value.ValueKind == JsonValueKind.Null) return "";
                if (fields[0].Value.ValueKind != JsonValueKind.String) throw new ArgumentException("TASTE_FIELD_TYPE_INVALID");
                return fields[0].Value.GetString()!.Normalize(NormalizationForm.FormKC);
            }
            var description = Read("Description");
            var explicitStyle = Read("Sweetness").Trim();
            if (description.Length > 16000) return false;
            var clauses = description.Split(['。', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (clauses.Length > 256) return false;
            if (Sweetness.Contains(taste))
            {
                var type=ProductTypeVocabulary.Classify(typeColumn);
                if(type.Conflicts.Contains("sweetness")||type.UnknownTokens.Any(t=>t is "中辛" or "中辛口" or "半辛口" or "中甘口"))return false;
                if(explicitStyle.Length>0)explicitStyle=WineTasteVocabulary.ResolveSweetness(explicitStyle)??explicitStyle;
                if (explicitStyle.Length > 0 && !Sweetness.Contains(explicitStyle)) return false;
                var claims = clauses.Where(c => !IsUncertain(c))
                    .Select(c => StyleAssertion.Match(c)).Where(m => m.Success)
                    .Select(m => WineTasteVocabulary.ResolveSweetness(m.Groups["style"].Value)??m.Groups["style"].Value).Distinct(StringComparer.Ordinal).ToArray();
                var styles = claims.Concat(explicitStyle.Length == 0 ? [] : new[] { explicitStyle }).Concat(type.Sweetness is null ? [] : new[] { type.Sweetness }).Distinct(StringComparer.Ordinal).ToArray();
                if (styles.Length != 1 || styles[0] != taste) return false;
                // An affirmative and a negated/qualified statement cannot supply strict evidence.
                return !clauses.Any(c => IsUncertain(c) && SweetnessWords.Matches(c).Any(m => (WineTasteVocabulary.ResolveSweetness(m.Value)??m.Value) == taste));
            }
            var terms = Flavors[taste];
            if (clauses.Any(c => IsUncertain(c) && terms.Any(term => c.Contains(term, StringComparison.Ordinal)))) return false;
            return clauses.Any(c => !IsUncertain(c) && FlavorAssertion(c, terms));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or RegexMatchTimeoutException) { return false; }
    }

    private static bool FlavorAssertion(string clause, IReadOnlyList<string> terms)
    {
        // Full product assertions only; food pairings, other products and free prose stay unknown.
        if (terms.Any(term => clause == term || clause == term + "です" || clause == term + "がある" ||
            clause == term + "があります" || clause == term + "な味わい")) return true;
        var aroma = AromaAssertion.Match(clause);
        if (!aroma.Success) return false;
        var parts = aroma.Groups["aromas"].Value.Split(['、', ',', 'や'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 6 || parts.Any(p => p.Length > 30 || p.Any(char.IsControl) ||
            new[] { "の", "は", "ワイン", "料理", "商品", "よう", "似" }.Any(p.Contains))) return false;
        return parts.Any(p => terms.Any(term => p == term || term.EndsWith("の香り", StringComparison.Ordinal) && p == term[..^3]));
    }
}
