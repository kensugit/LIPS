using System.Text;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

/// <summary>Reviewed names and exact aliases. Geography never supplies a product's grape composition.</summary>
public static class JapaneseWineDictionary
{
    private static readonly WineDictionaryPlace[] Places = WineDictionaryData.Current.Places;
    private static readonly string[][] CountryGroups = Places.Select(p => p.Countries)
        .DistinctBy(names => names[0], StringComparer.Ordinal).ToArray();
    public static string Revision => WineDictionaryData.Revision;
    public static bool AreCompatibleCountryAliases(IReadOnlyList<string> names) => names.Count <= 1 ||
        CountryGroups.Any(group => names.All(name => group.Contains(name, StringComparer.Ordinal)));

    private static readonly Regex Form = new(
        "^(?:(?<country>" + string.Join("|", CountryGroups.Select(names => Regex.Escape(names[0]))) +
        @")の)?(?<place>[^の]+?)(?:の(?:(?<grape>[^の、。]+?)(?:が)?(?<mode>主体|を含む)(?:の)?(?:(?<type>赤|白|ロゼ)?ワイン)?|(?<type>赤|白|ロゼ)?ワイン))?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly string Tastes = string.Join("|", WineDictionaryData.Current.Sweetness
        .Concat(WineDictionaryData.Current.Flavors.Keys).OrderByDescending(t => t.Length).Select(Regex.Escape));
    private static readonly Regex TasteForm = new(
        "^(?:(?<place>[^の]+?)の)?(?<taste>" + Tastes + ")(?:の風味)?(?:の)?" +
        "(?:(?<sparkling>" + CatalogSearchVocabulary.SparklingPattern + "|泡)|(?<typeAlias>" +
        CatalogSearchVocabulary.ProductTypePattern + ")|(?<type>赤|白|ロゼ)?(?:ワイン)?)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
    public static bool MentionsTaste(string query) => Regex.IsMatch(query, Tastes, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public sealed record Match(string? PlaceLabel, string[] Countries, string[] Regions, string? ProductType, SemanticGrapePredicate? Grape, string? Taste = null, bool Sparkling = false);

    public static Match? Interpret(string query, SemanticSearchOptions options)
    {
        var normalized = query.Normalize(NormalizationForm.FormKC).Trim();
        var taste = TasteForm.Match(normalized);
        if (taste.Success)
        {
            var countryOrPlace = taste.Groups["place"].Value.Trim();
            var color = taste.Groups["typeAlias"].Success ? CatalogSearchVocabulary.ProductType(taste.Groups["typeAlias"].Value)
                : taste.Groups["type"].Success ? taste.Groups["type"].Value : null;
            var sparkling = taste.Groups["sparkling"].Success;
            if (countryOrPlace.Length == 0) return new(null, [], [], color, null, taste.Groups["taste"].Value, sparkling);
            var countryAliases = CountryGroups.SingleOrDefault(c => Matches(c, countryOrPlace));
            if (countryAliases is not null)
            {
                var registered = countryAliases.Where(c => options.Countries.Contains(c, StringComparer.Ordinal)).ToArray();
                return registered.Length == 0 ? null : new(null, registered, [], color, null, taste.Groups["taste"].Value, sparkling);
            }
            var placeMatch = InterpretPlace(countryOrPlace, options);
            return placeMatch is null ? null : placeMatch with { ProductType = color, Taste = taste.Groups["taste"].Value, Sparkling = sparkling };
        }
        return InterpretPlace(normalized, options);
    }

    private static Match? InterpretPlace(string text, SemanticSearchOptions options)
    {
        var match = Form.Match(text);
        if (!match.Success) return null;
        var input = match.Groups["place"].Value.Trim();
        var places = Places.Where(p => Matches(p.Names, input) &&
            (!match.Groups["country"].Success || p.Countries[0] == match.Groups["country"].Value)).Take(2).ToArray();
        if (places.Length > 1) return null;
        var country = match.Groups["country"].Success ? match.Groups["country"].Value : null;
        if (places.Length == 0 && Places.Any(p => Matches(p.Names, input))) return null;
        SemanticGrapePredicate? grape = null;
        if (match.Groups["grape"].Success)
        {
            var name = GrapeRegistry.Resolve(match.Groups["grape"].Value);
            if (name is null || !options.Grapes.Contains(name, StringComparer.Ordinal)) return null;
            grape = new(name, match.Groups["mode"].Value == "主体" ? "predominant" : "contains");
        }
        var productType = match.Groups["type"].Success ? match.Groups["type"].Value : null;
        if (places.Length == 0)
        {
            var exact = options.Regions.Where(r => (country is null || r.Country == country) &&
                r.Name.Equals(input, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (exact.Length != 1) return null;
            return new(exact[0].Country + "の" + exact[0].Name, [exact[0].Country], [exact[0].Name], productType, grape);
        }
        var place = places[0];
        var countries = place.Countries.Where(c => options.Countries.Contains(c, StringComparer.Ordinal)).ToArray();
        var regions = options.Regions.Where(r => countries.Contains(r.Country, StringComparer.Ordinal) &&
            (Matches(place.Names, r.Name) || Matches(place.Members, r.Name))).Select(r => r.Name).ToArray();
        return new(place.Label, countries, regions, productType, grape);
    }

    private static bool Matches(string[] names, string input) => names.Any(name =>
        name.Equals(input.Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase));
}
