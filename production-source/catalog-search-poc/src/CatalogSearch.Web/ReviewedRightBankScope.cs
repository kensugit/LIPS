using CatalogSearch.Application;

namespace CatalogSearch.Web;

// User selected the narrower Saint-Emilion/Pomerol/Fronsac surroundings, not all right-bank Bordeaux.
// Membership: CIVB /fr/appellations/saint-emilion-pomerol-fronsac/ and Saint-Emilion's two AOCs.
// Exact aliases only. Unknown regions are not assigned membership; coverage is disclosed explicitly.
public static class ReviewedRightBankScope
{
    public const string Version = "libournais-user-selected/20261120-v2";
    public const string CoverageNotice = "サンテミリオン・ポムロール・フロンサック周辺の、照合できた登録産地のみを対象としています。未同定の産地は含みません。";
    public static readonly IReadOnlyList<string> Sources = Array.AsReadOnly(new[] {
        "https://www.bordeaux.com/fr/appellations/saint-emilion-pomerol-fronsac/",
        "https://www.bordeaux.com/en/designations/saint-emilion-pomerol-fronsac/saint-emilion/" });
    static readonly string[][] Members = [
        ["Canon Fronsac", "Canon-Fronsac", "カノン・フロンサック", "カノン フロンサック"],
        ["Castillon-Côtes de Bordeaux", "Castillon-Cotes de Bordeaux", "カスティヨン・コート・ド・ボルドー", "AOC カスティヨン－コート・ド・ボルドー", "カスティヨン コート ド ボルドー"],
        ["Francs Côtes de Bordeaux", "Francs Cotes de Bordeaux", "フラン・コート・ド・ボルドー"],
        ["Fronsac", "フロンサック"],
        ["Lalande de Pomerol", "Lalande-de-Pomerol", "ラランド・ド・ポムロール"],
        ["Lussac Saint-Emilion", "Lussac-Saint-Émilion", "リュサック・サンテミリオン", "リュサック サンテミリオン"],
        ["Montagne Saint-Emilion", "Montagne-Saint-Émilion", "モンターニュ・サンテミリオン", "モンターニュ サンテミリオン"],
        ["Pomerol", "ポムロール", "AOC ポムロール", "AOCポムロール"],
        ["Puisseguin Saint-Emilion", "Puisseguin-Saint-Émilion", "ピュイスガン・サンテミリオン", "ピュイスガン サン・テミリオン", "ピュイスガン サンテミリオン"],
        ["Saint-Emilion", "Saint-Émilion", "サンテミリオン", "サン・テミリオン", "AOC サンテミリオン"],
        ["Saint-Georges Saint-Emilion", "Saint-Georges-Saint-Émilion", "サン・ジョルジュ・サンテミリオン"],
        ["Saint-Emilion Grand Cru", "Saint-Émilion Grand Cru", "サンテミリオン・グラン・クリュ", "AOC サンテミリオングランクリュ", "AOCサンテミリオングランクリュ", "サンテミリオン GC", "サンテミリオンGC", "サン・テミリオン グラン クリュ", "サンテミリオン グランクリュ"]
    ];
    static readonly HashSet<string> Outside = new(StringComparer.Ordinal) {
        "Blaye", "ブライ", "Bourg", "ブール", "Côtes de Bourg", "Cotes de Bourg", "コート・ド・ブール",
        "Blaye-Côtes de Bordeaux", "Blaye-Cotes de Bordeaux", "ブライ・コート・ド・ボルドー",
        "Cadillac Côtes de Bordeaux", "Cadillac Cotes de Bordeaux", "カディヤック・コート・ド・ボルドー"
    };
    public static object Definition => new { Version, Sources, Members, Outside = Outside.Order(StringComparer.Ordinal).ToArray() };
    public static bool Requested(string query) => query.Contains("右岸", StringComparison.Ordinal);
    public sealed record Binding(IReadOnlyList<SemanticRegionOption> Regions, IReadOnlyList<string> Errors,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? RawAliases = null, int UnmappedRegionCount = 0, IReadOnlyList<string>? RawCountries = null);
    public static Binding Bind(string query, SemanticSearchOptions options, string? explicitCountry = null)
    {
        if (!query.Contains("ボルドー", StringComparison.Ordinal) && !query.Contains("Bordeaux", StringComparison.OrdinalIgnoreCase)) return new([], ["right_bank_named_area_required"]);
        if (new[] { "または", "又は", "あるいは", "以外", "左岸", " or ", " OR " }.Any(query.Contains)) return new([], ["right_bank_compound_geography_unresolved"]);
        if (explicitCountry is not null && explicitCountry is not ("France" or "フランス")) return new([], ["right_bank_country_unresolved"]);
        var rawCountries = options.Countries.Where(x => x is "France" or "フランス").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (rawCountries.Length == 0 || (explicitCountry is not null && !rawCountries.Contains(explicitCountry))) return new([], ["right_bank_country_unresolved"]);
        var selected = new List<(SemanticRegionOption Option, string Canonical)>(); var unmapped = 0;
        foreach (var option in options.Regions)
        {
            var matching = Members.Where(m => m.Contains(option.Name, StringComparer.Ordinal)).ToArray();
            if (matching.Length > 1) return new([], ["right_bank_alias_ambiguous"]);
            var inside = (option.Country is "フランス" or "France") && matching.Length == 1;
            if (inside) selected.Add((option, matching[0][0]));
            else
            {
                var coarse = option.Name is "ボルドー" or "Bordeaux" or "Libournais" or "リブルネ" or "リブルヌ" or "Côtes de Bordeaux" or "Cotes de Bordeaux" or "コート・ド・ボルドー";
                if (!coarse && query.Contains(option.Name, StringComparison.Ordinal)) return new([], ["right_bank_compound_geography_unresolved"]);
                if (option.Country is "フランス" or "France") unmapped++;
            }
        }
        if (selected.Count == 0) return new([], ["right_bank_no_eligible_regions"]);
        if (selected.Count > 128) return new([], ["right_bank_region_capacity_exceeded"]);
        var country = explicitCountry ?? (rawCountries.Contains("フランス") ? "フランス" : "France");
        var map = selected.GroupBy(x => x.Canonical, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Option.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return new(map.Keys.Select(k => new SemanticRegionOption(k, country)).ToArray(), [], map, unmapped, rawCountries);
    }
    public static IReadOnlyList<string> Check(string query, ExplicitSearchUi ui, SemanticSearchPlan plan, SemanticSearchOptions options)
    {
        if (!Requested(query)) return [];
        // Explicit UI is the user-authorized replacement; never clear UnparsedTerms here.
        if (!string.IsNullOrWhiteSpace(ui.Region)) return [];
        var binding = Bind(query, options, ui.Country);
        if (binding.Errors.Count != 0) return binding.Errors;
        if (!query.Contains("ボルドー", StringComparison.Ordinal) && !query.Contains("Bordeaux", StringComparison.OrdinalIgnoreCase)) return ["right_bank_named_area_required"];
        if (plan.Filter.Country is not ("フランス" or "France")) return ["right_bank_country_unresolved"];
        if (binding.Regions.Any(r => r.Country != plan.Filter.Country)) return ["right_bank_country_alias_split"];
        var eligible = binding.Regions.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        if (!eligible.SetEquals(plan.Regions)) return ["right_bank_expansion_incomplete_or_outside"];
        if (plan.Keywords.Any(k => Requested(k) || k is "ボルドー" or "Bordeaux")) return ["right_bank_keyword_duplicate"];
        return [];
    }
}
