using System.Text.RegularExpressions;
namespace CatalogSearch.Infrastructure;
public static class SearchTerms
{
    public static string[] Parse(string? text) => string.IsNullOrWhiteSpace(text) ? [] : Regex.Split(text.Trim(), @"\s+").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Pattern(string term) => "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
