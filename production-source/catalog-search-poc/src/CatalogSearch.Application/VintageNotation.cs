using System.Text;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

/// <summary>Search/display notation only. Original supplier evidence is never changed.</summary>
public static class VintageNotation
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var value = Regex.Replace(raw.Normalize(NormalizationForm.FormKC), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
        // Keep annotations, but do not show whitespace-only duplicates such as NV\n(2019) / NV (2019).
        value = Regex.Replace(value, @"\s*([\(\[])\s*", "$1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"\s*([\)\]])", "$1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var unwrapped = value;
        while (unwrapped.Length >= 2 && ((unwrapped[0] == '(' && unwrapped[^1] == ')') ||
               (unwrapped[0] == '[' && unwrapped[^1] == ']')))
            unwrapped = unwrapped[1..^1].Trim();
        if (Regex.IsMatch(unwrapped, @"^[0-9]{4}(?:年)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return unwrapped.TrimEnd('年');
        if (CatalogSearchVocabulary.IsNonVintage(unwrapped) ||
            Regex.IsMatch(unwrapped, @"^[NM]\s*\.?\s*V\s*\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return "NV";
        return value; // Preserve age/blend/unknown meaning and annotations; normalize notation only.
    }

    public static string[] Options(IEnumerable<string> rawValues) => rawValues.Select(Normalize)
        .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(x => x, StringComparer.Ordinal).ToArray();

    public static string[] MatchingRawValues(IEnumerable<string> rawValues, string requested)
    {
        var normalized = Normalize(requested);
        return rawValues.Where(x => Normalize(x) == normalized).Distinct(StringComparer.Ordinal).ToArray();
    }
}
