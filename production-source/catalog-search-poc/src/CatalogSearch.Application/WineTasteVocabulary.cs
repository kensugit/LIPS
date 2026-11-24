using System.Text;

namespace CatalogSearch.Application;

// The query and evidence paths share this reviewed mapping; original text is never replaced.
public static class WineTasteVocabulary
{
    private static readonly Dictionary<string,string> Sweetness = WineDictionaryData.Current.SweetnessAliases
        .SelectMany(group=>group.Value.Select(alias=>(Alias:alias.Normalize(NormalizationForm.FormKC),Canonical:group.Key)))
        .ToDictionary(pair=>pair.Alias,pair=>pair.Canonical,StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> SweetnessNames { get; }=Array.AsReadOnly(Sweetness.Keys.ToArray());
    public static string? ResolveSweetness(string input)=>Sweetness.TryGetValue(input.Normalize(NormalizationForm.FormKC).Trim(),out var canonical)?canonical:null;
}
