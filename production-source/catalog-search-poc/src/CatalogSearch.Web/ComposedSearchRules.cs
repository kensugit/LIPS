using System.Text;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;

namespace CatalogSearch.Web;

internal static class ComposedSearchRules
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Flags = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly Regex Jan = new(@"(?<![a-z0-9])(?:JAN(?:コード)?|GTIN)\s*[:：]?\s*(?<code>\d{8}|\d{12,14})(?!\d)", Flags, Timeout);
    private static readonly Regex ScreenColor = new(@"\s*\(画面では(?<color>赤|白|ロゼ|オレンジ)(?:ワイン)?を指定\)\s*$", Flags, Timeout);
    private static readonly Regex RightBank = new(@"(?:ボルドー|Bordeaux)\s*(?:の)?右岸", Flags, Timeout);
    private static readonly Regex Unsupported = new(@"税込|卸価格|仕入(?:れ)?価格|納入価格|納価|以外|除く|ではない|ではなく|または|もしくは|あるいは|左岸|\bor\b", Flags, Timeout);

    public static SemanticSearchPlan? Interpret(string query, ExplicitSearchUi ui, SemanticCatalogSnapshot snapshot,
        ReviewedRightBankScope.Binding? bank)
    {
        try
        {
            var text = query.Normalize(NormalizationForm.FormKC);
            if (Unsupported.IsMatch(text)) return null;
            var screen = ScreenColor.Match(text);
            if (screen.Success && ui.ProductType == screen.Groups["color"].Value) text = text[..screen.Index];
            if (text.Contains('(') || text.Contains(')')) return null;
            if (ReviewedRightBankScope.Requested(text))
            {
                if (bank is null || bank.Errors.Count != 0 || RightBank.Matches(text).Count != 1) return null;
                text = RightBank.Replace(text, "");
            }

            string? code = null;
            var codes = Jan.Matches(text);
            if (codes.Count > 1) return null;
            if (codes.Count == 1)
            {
                code = codes[0].Groups["code"].Value;
                text = Jan.Replace(text, " ");
                text = Regex.Replace(text, @"\s*(?:を)?\s*(?:探して(?:ください)?|検索して(?:ください)?)\s*$", "", Flags, Timeout);
            }

            SemanticGrapePredicate? grape = null;
            foreach (var name in GrapeRegistry.MentionedNames(text))
            {
                if (!GrapeRegistry.TryGetAliases(name, out var aliases)) continue;
                var aliasPattern = string.Join("|", aliases.OrderByDescending(a => a.Length).Select(Regex.Escape));
                var pattern = new Regex(@"(?<![a-z0-9])(?:" + aliasPattern +
                    @")(?![a-z0-9])\s*(?:が\s*)?(?<mode>主体|を含む)(?=$|[\s、,。]|の|で|かつ)", Flags, Timeout);
                var claims = pattern.Matches(text);
                if (claims.Count == 0) continue;
                if (claims.Count != 1 || grape is not null) return null;
                var claim = claims[0];
                grape = new(name, claim.Groups["mode"].Value == "主体" ? "predominant" : "contains");
                text = text.Remove(claim.Index, claim.Length);
                text = Regex.Replace(text, @"^\s*(?:の|で)\s*", "", Flags, Timeout);
            }

            var parsed = NaturalSearch.Parse(new CatalogFilter(Text: text));
            if (parsed.MinExclusive || parsed.MaxExclusive || parsed.SupplierId is not null) return null;
            var filter = CatalogQuery.MergeRulesWithUi(parsed, ui.Filter).Filter with { Text = null, Region = null };
            var countries = Array.Empty<string>();
            var regions = Array.Empty<string>();
            string[] keywords = [];
            var leftover = (parsed.Filter.Text ?? "").Trim(' ', '、', '。', ',');
            if (bank is not null)
            {
                if (leftover.Length != 0) return null;
                var country = bank.Regions.Select(r => r.Country).Distinct(StringComparer.Ordinal).SingleOrDefault();
                if (country is null || filter.Country is not null && filter.Country != country) return null;
                filter = filter with { Country = country };
                regions = bank.Regions.Select(r => r.Name).ToArray(); // canonical names, translated by Execute
            }
            else if (parsed.Champagne || leftover.Length > 0)
            {
                var explicitName = Regex.Match(leftover, @"^(?<name>[^\s、,。()]{2,80})という(?:銘柄|商品名)$", Flags, Timeout);
                if (explicitName.Success && !parsed.Champagne) keywords = [explicitName.Groups["name"].Value];
                else
                {
                    var options = snapshot.Options with { Regions = snapshot.RegionPairs };
                    if (filter.Country is { } country)
                        options = options with { Regions = options.Regions.Where(r => r.Country == country).ToArray() };
                    var place = JapaneseWineDictionary.Interpret(parsed.Champagne ? "シャンパーニュ" : leftover, options);
                    if (place is not { PlaceLabel: not null, Regions.Length: > 0 and <= 128, Grape: null, ProductType: null, Taste: null }
                        || parsed.Champagne && leftover.Length != 0) return null;
                    // An explicitly selected country/type overrides the corresponding text condition.
                    if (filter.Country is null || place.Countries.Contains(filter.Country))
                    {
                        countries = filter.Country is null ? place.Countries : [];
                        filter = filter with { Country = filter.Country ?? countries.FirstOrDefault() };
                        regions = place.Regions;
                    }
                    else if (parsed.Filter.Country is not null || ui.Country is null) return null;
                }
            }
            if (ui.Region is not null) regions = [ui.Region];
            if (parsed.Conditions.Length == 0 && grape is null && code is null && regions.Length == 0 && keywords.Length == 0) return null;
            return new(filter, ui.Supplier, keywords, regions, grape, [], false)
            {
                CountryAliases = countries,
                Sparkling = (parsed.Sparkling || parsed.Champagne) && ui.ProductType is null,
                Jan = code
            };
        }
        catch (Exception e) when (e is ArgumentException or OverflowException or RegexMatchTimeoutException) { return null; }
    }
}
