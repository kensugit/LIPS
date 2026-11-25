using System.Text;
using System.Text.Json;

namespace CatalogSearch.Application;

public sealed record SemanticRegionOption(string Name, string Country);
public sealed record SemanticSearchOptions(IReadOnlyList<string> Countries, IReadOnlyList<SemanticRegionOption> Regions,
    IReadOnlyList<string> ProductTypes, IReadOnlyList<int> VolumesMl, IReadOnlyList<string> Vintages,
    IReadOnlyList<string> Suppliers, IReadOnlyList<string> Grapes);
public sealed record SemanticSearchRequest(string Schema, Guid RequestId, string CatalogVersion, string PromptVersion,
    string Query, SemanticSearchOptions Options);
public sealed record SemanticGrapePredicate(string Name, string Mode);
public sealed record SemanticSearchFilters(IReadOnlyList<string> Keywords, string? Supplier, string? Country,
    IReadOnlyList<string> Regions, string? ProductType, int? VolumeMl, string? Vintage, int? PriceMin, int? PriceMax,
    bool? AvailableOnly, SemanticGrapePredicate? Grape);
public sealed record SemanticSearchInterpretation(string Schema, Guid RequestId, string CatalogVersion,
    string PromptVersion, string Provider, string Model, SemanticSearchFilters Filters, IReadOnlyList<string> UnparsedTerms);
/// <summary>Regions are OR; all other predicates are AND. NeedsClarification forbids execution.</summary>
public sealed record SemanticSearchPlan(CatalogFilter Filter, string? SupplierCode, IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Regions, SemanticGrapePredicate? Grape, IReadOnlyList<string> UnparsedTerms, bool NeedsClarification)
{
    // Internal reviewed-geography expansion only; not part of the LLM wire schema.
    public IReadOnlyList<string> CountryAliases { get; init; } = [];
    public string? Taste { get; init; }
    public string? Jan { get; init; }
    public bool Sparkling { get; init; }
    public void RequireExecutable()
    {
        if (NeedsClarification || UnparsedTerms.Count != 0) throw new ArgumentException("semantic_clarification_required");
    }
}

/// <summary>Versioned pure boundary. Registry membership is not proof of geographic or grape evidence.</summary>
public static class SemanticSearchContract
{
    public const string Schema = "lips-free-text-search/v2";
    public const string PromptVersion = "catalog-search-interpretation/2";
    public const int MaxBodyBytes = 100_000;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static void Need(bool value, string code) { if (!value) throw new ArgumentException(code); }
    static bool Text(string? s, int max = 200) => !string.IsNullOrWhiteSpace(s) && s.Length <= max;
    static void Strings(IReadOnlyList<string>? values, int count, int length = 200)
    {
        Need(values is not null && values.Count <= count && values.All(s => Text(s, length)) &&
            values.Distinct(StringComparer.Ordinal).Count() == values.Count, "semantic_list_invalid");
    }
    static void Options(SemanticSearchOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        Strings(o.Countries, 250); Strings(o.ProductTypes, 100); Strings(o.Vintages, 500);
        Strings(o.Suppliers, 1000); Strings(o.Grapes, 500);
        Need(o.Regions is not null && o.Regions.Count <= 1000 && o.Regions.All(r => r is not null && Text(r.Name) &&
            o.Countries.Contains(r.Country)) && o.Regions.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count() == o.Regions.Count,
            "semantic_regions_registry_invalid");
        Need(o.VolumesMl is not null && o.VolumesMl.Count <= 100 && o.VolumesMl.All(n => n is >= 1 and <= 100000) &&
            o.VolumesMl.Distinct().Count() == o.VolumesMl.Count, "semantic_volume_registry_invalid");
    }
    static void Request(SemanticSearchRequest r)
    {
        ArgumentNullException.ThrowIfNull(r); Options(r.Options);
        Need(r.Schema == Schema && r.PromptVersion == PromptVersion && r.RequestId != Guid.Empty &&
            Text(r.CatalogVersion, 128) && Text(r.Query), "semantic_request_identity");
        Need(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r, JsonOptions)) <= MaxBodyBytes, "semantic_request_bound");
    }
    public static SemanticSearchRequest CreateRequest(Guid id, string version, string query, SemanticSearchOptions o)
    {
        Options(o);
        var snapshot = new SemanticSearchOptions(o.Countries.ToArray(), o.Regions.ToArray(), o.ProductTypes.ToArray(),
            o.VolumesMl.ToArray(), o.Vintages.ToArray(), o.Suppliers.ToArray(), o.Grapes.ToArray());
        var r = new SemanticSearchRequest(Schema, id, version, PromptVersion, query, snapshot); Request(r); return r;
    }
    static void DuplicateCheck(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in e.EnumerateObject()) { Need(names.Add(p.Name), "semantic_duplicate_member"); DuplicateCheck(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) DuplicateCheck(x);
    }
    static void Exact(JsonElement e, params string[] names) => Need(e.ValueKind == JsonValueKind.Object &&
        e.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names), "semantic_member_set");
    static string Str(JsonElement e, int max = 200)
    { Need(e.ValueKind == JsonValueKind.String && Text(e.GetString(), max), "semantic_string"); return e.GetString()!; }
    static string? NullableString(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : Str(e);
    static int? Integer(JsonElement e, int min, int max)
    {
        if (e.ValueKind == JsonValueKind.Null) return null;
        Need(e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var _), "semantic_integer");
        var n = e.GetInt32(); Need(n >= min && n <= max, "semantic_integer_range"); return n;
    }
    static string[] List(JsonElement e, int max, int length = 200)
    { Need(e.ValueKind == JsonValueKind.Array, "semantic_array"); var a = e.EnumerateArray().Select(x => Str(x, length)).ToArray(); Strings(a, max, length); return a; }
    static void Member(string? value, IReadOnlyList<string> options) => Need(value is null || options.Contains(value), "semantic_outside_registry");
    static void RegionMembership(string? country, IReadOnlyList<string> regions, SemanticSearchOptions o)
    {
        Strings(regions, 20);
        Need(regions.All(name => o.Regions.Any(r => r.Name == name && r.Country == country)), "semantic_region_country_mismatch");
    }
    static void ValidateFilters(SemanticSearchFilters f, SemanticSearchOptions o)
    {
        Strings(f.Keywords, 10, 100); Member(f.Supplier, o.Suppliers); Member(f.Country, o.Countries);
        Member(f.ProductType, o.ProductTypes); Member(f.Vintage, o.Vintages); RegionMembership(f.Country, f.Regions, o);
        Need(f.VolumeMl is null || o.VolumesMl.Contains(f.VolumeMl.Value), "semantic_volume_outside_registry");
        Need(!(f.PriceMin > f.PriceMax) && !(f.PriceMin < 0) && !(f.PriceMax < 0), "semantic_price_range");
        if (f.Grape is not null)
            Need(o.Grapes.Contains(f.Grape.Name) && f.Grape.Mode is "contains" or "predominant", "semantic_grape_predicate");
    }
    public static SemanticSearchInterpretation ParseResponse(SemanticSearchRequest request, string json)
    {
        Request(request); Need(json is not null && Encoding.UTF8.GetByteCount(json) <= MaxBodyBytes, "semantic_response_bound");
        using var doc = JsonDocument.Parse(json!, new JsonDocumentOptions { MaxDepth = 32 }); var e = doc.RootElement; DuplicateCheck(e);
        Exact(e, "schema", "requestId", "catalogVersion", "promptVersion", "provider", "model", "filters", "unparsedTerms");
        Need(Str(e.GetProperty("schema")) == Schema && Str(e.GetProperty("promptVersion")) == PromptVersion &&
            Guid.TryParse(Str(e.GetProperty("requestId")), out var id) && id == request.RequestId &&
            Str(e.GetProperty("catalogVersion"), 128) == request.CatalogVersion, "semantic_response_identity");
        var f = e.GetProperty("filters");
        Exact(f, "keywords", "supplier", "country", "regions", "productType", "volumeMl", "vintage", "priceMin", "priceMax", "availableOnly", "grape");
        var available = f.GetProperty("availableOnly"); Need(available.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False, "semantic_boolean");
        var g = f.GetProperty("grape"); SemanticGrapePredicate? grape = null;
        if (g.ValueKind != JsonValueKind.Null) { Exact(g, "name", "mode"); grape = new(Str(g.GetProperty("name")), Str(g.GetProperty("mode"), 20)); }
        var filters = new SemanticSearchFilters(List(f.GetProperty("keywords"), 10, 100), NullableString(f.GetProperty("supplier")),
            NullableString(f.GetProperty("country")), List(f.GetProperty("regions"), 20), NullableString(f.GetProperty("productType")),
            Integer(f.GetProperty("volumeMl"), 1, 100000), NullableString(f.GetProperty("vintage")), Integer(f.GetProperty("priceMin"), 0, 1000000000),
            Integer(f.GetProperty("priceMax"), 0, 1000000000), available.ValueKind == JsonValueKind.Null ? null : available.GetBoolean(), grape);
        ValidateFilters(filters, request.Options);
        return new(Schema, request.RequestId, request.CatalogVersion, PromptVersion, Str(e.GetProperty("provider"), 100),
            Str(e.GetProperty("model")), filters, List(e.GetProperty("unparsedTerms"), 10, 100));
    }
    public static SemanticSearchPlan Merge(SemanticSearchRequest request, CatalogFilter ui, string? uiSupplier, SemanticSearchInterpretation response)
    {
        // Revalidate even typed callers; never accept an interpretation for another snapshot.
        var r = ParseResponse(request, JsonSerializer.Serialize(response, JsonOptions)); var f = r.Filters;
        string? Prefer(string? explicitValue, string? model) => string.IsNullOrWhiteSpace(explicitValue) ? model : explicitValue;
        var country = Prefer(ui.Country, f.Country);
        var regions = string.IsNullOrWhiteSpace(ui.Region) ? f.Regions.ToArray() : new[] { ui.Region };
        var filter = ui with { Country = country, Region = null, ProductType = Prefer(ui.ProductType, f.ProductType),
            VolumeMl = ui.VolumeMl ?? f.VolumeMl, VintageRaw = Prefer(ui.VintageRaw, f.Vintage), PriceMin = ui.PriceMin ?? f.PriceMin,
            PriceMax = ui.PriceMax ?? f.PriceMax, AvailableOnly = ui.AvailableOnly || f.AvailableOnly == true, Text = null };
        var supplier = Prefer(uiSupplier, f.Supplier);
        ValidateFilters(new(f.Keywords, supplier, country, regions, filter.ProductType, filter.VolumeMl, filter.VintageRaw,
            f.PriceMin, f.PriceMax, filter.AvailableOnly, f.Grape), request.Options);
        Need(filter.Limit is >= 1 and <= 2000 && !(filter.PriceMin < 0) && !(filter.PriceMax < 0) &&
            !(filter.PriceMin > filter.PriceMax), "semantic_merged_range");
        filter=ProductTypeVocabulary.MergeUi(ui,filter with {ProductType=f.ProductType}) with {ProductType=filter.ProductType};
        return new(filter, supplier, f.Keywords.ToArray(), regions, f.Grape, r.UnparsedTerms.ToArray(), r.UnparsedTerms.Count != 0);
    }
}
