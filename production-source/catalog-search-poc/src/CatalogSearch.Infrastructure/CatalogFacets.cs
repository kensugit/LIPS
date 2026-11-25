using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed record CatalogFacetRegionSource(string Country, string Region);
public sealed record CatalogFacetValue(string Value, string Label, IReadOnlyList<string> RawValues);
public sealed record CatalogRegionFacet(string Country, string CountryLabel, string Value, string Label, IReadOnlyList<string> RawValues);
public sealed record CatalogFacetOptions(IReadOnlyList<CatalogFacetValue> Countries, IReadOnlyList<CatalogRegionFacet> Regions,
    IReadOnlyList<int> VolumesMl, IReadOnlyList<CatalogFacetValue> Vintages)
{
    public IReadOnlyList<CatalogFacetValue> ProductTypes { get; init; } = [];
    public TypeAttributeOptions TypeAttributes { get; init; } = ProductTypeVocabulary.Options([]);
    public static CatalogFacetOptions Build(IEnumerable<CatalogFacetRegionSource> source, IEnumerable<int> volumes, IEnumerable<string> vintages, IEnumerable<string>? productTypes = null)
    {
        var pairs = source.OrderBy(p => p.Country, StringComparer.Ordinal).ThenBy(p => p.Region, StringComparer.Ordinal).ToArray();
        static string[] Raw(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        static string Representative(string canonical, string[] raw) => raw.Contains(canonical, StringComparer.Ordinal) ? canonical :
            raw.OrderBy(x => CatalogFacetVocabulary.ExactKey(x).Equals(canonical, StringComparison.Ordinal) ? 0 : 1).ThenBy(x => x, StringComparer.Ordinal).First();
        var countries = pairs.Select(p => p.Country).Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(CatalogFacetVocabulary.Country, StringComparer.OrdinalIgnoreCase)
            .Select(group => { var raw = Raw(group); return new CatalogFacetValue(Representative(group.Key, raw), group.Key, raw); })
            .OrderBy(x => x.Label, StringComparer.Ordinal).ToArray();
        var representatives = countries.ToDictionary(x => x.Label, x => x, StringComparer.OrdinalIgnoreCase);
        var regions = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Region))
            .GroupBy(p => (Country: CatalogFacetVocabulary.Country(p.Country), Region: CatalogFacetVocabulary.Region(p.Country, p.Region)), RegionKeyComparer.Instance)
            .Select(group => {
                var raw = Raw(group.Select(p => p.Region));
                return new CatalogRegionFacet(group.Key.Country.Length == 0 ? "" : representatives[group.Key.Country].Value,
                    group.Key.Country.Length == 0 ? "" : representatives[group.Key.Country].Label,
                    Representative(group.Key.Region, raw), group.Key.Region, raw);
            }).OrderBy(x => x.Label, StringComparer.Ordinal).ThenBy(x => x.CountryLabel, StringComparer.Ordinal).ToArray();
        var volumeOptions = volumes.Where(x => x != 0).Distinct().Order().ToArray();
        if (volumeOptions.Length > 100 || volumeOptions.Any(x => x is < 1 or > 100000)) throw new InvalidOperationException("Volume options exceed contract bounds.");
        var vintageOptions = vintages.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(VintageNotation.Normalize, StringComparer.Ordinal)
            .Select(group => new CatalogFacetValue(group.Key, group.Key, Raw(group))).OrderByDescending(x => x.Label, StringComparer.Ordinal).ToArray();
        var rawTypes=(productTypes??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        var groupedTypes=rawTypes.GroupBy(ProductTypeVocabulary.GroupKey,StringComparer.Ordinal)
            .Select(g=>{var raw=Raw(g);var label=ProductTypeVocabulary.Label(raw[0]);return new CatalogFacetValue(Representative(label,raw),label,raw);})
            .OrderBy(x=>x.Label,StringComparer.Ordinal).ToArray();
        return new(countries, regions, volumeOptions, vintageOptions) {ProductTypes=groupedTypes,TypeAttributes=ProductTypeVocabulary.Options(rawTypes)};
    }
    private sealed class RegionKeyComparer : IEqualityComparer<(string Country, string Region)>
    {
        public static readonly RegionKeyComparer Instance = new();
        public bool Equals((string Country, string Region) x, (string Country, string Region) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Country, y.Country) && StringComparer.OrdinalIgnoreCase.Equals(x.Region, y.Region);
        public int GetHashCode((string Country, string Region) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Country), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Region));
    }
}
