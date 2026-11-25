using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CatalogSearch.Application;

public sealed record FreeTextSearchOptions([property: JsonPropertyName("countries")] IReadOnlyList<string> Countries, [property: JsonPropertyName("regions")] IReadOnlyList<string> Regions, [property: JsonPropertyName("productTypes")] IReadOnlyList<string> ProductTypes, [property: JsonPropertyName("volumesMl")] IReadOnlyList<int> VolumesMl, [property: JsonPropertyName("vintages")] IReadOnlyList<string> Vintages, [property: JsonPropertyName("suppliers")] IReadOnlyList<string> Suppliers);
public sealed record FreeTextSearchRequest([property: JsonPropertyName("schema")] string Schema, [property: JsonPropertyName("requestId")] Guid RequestId, [property: JsonPropertyName("catalogVersion")] string CatalogVersion, [property: JsonPropertyName("promptVersion")] string PromptVersion, [property: JsonPropertyName("query")] string Query, [property: JsonPropertyName("options")] FreeTextSearchOptions Options);
public sealed record FreeTextSearchFilters([property: JsonPropertyName("keywords")] IReadOnlyList<string> Keywords, [property: JsonPropertyName("supplier")] string? Supplier, [property: JsonPropertyName("country")] string? Country, [property: JsonPropertyName("region")] string? Region, [property: JsonPropertyName("productType")] string? ProductType, [property: JsonPropertyName("volumeMl")] int? VolumeMl, [property: JsonPropertyName("vintage")] string? Vintage, [property: JsonPropertyName("priceMin")] int? PriceMin, [property: JsonPropertyName("priceMax")] int? PriceMax, [property: JsonPropertyName("availableOnly")] bool? AvailableOnly);
public sealed record FreeTextSearchInterpretation([property: JsonPropertyName("schema")] string Schema, [property: JsonPropertyName("requestId")] Guid RequestId, [property: JsonPropertyName("catalogVersion")] string CatalogVersion, [property: JsonPropertyName("promptVersion")] string PromptVersion, [property: JsonPropertyName("provider")] string Provider, [property: JsonPropertyName("model")] string Model, [property: JsonPropertyName("filters")] FreeTextSearchFilters Filters, [property: JsonPropertyName("unparsedTerms")] IReadOnlyList<string> UnparsedTerms);
public sealed record StructuredSearchPlan(CatalogFilter Filter, string? SupplierCode, IReadOnlyList<string> Keywords);

public static class FreeTextSearchContract
{
    public const string Schema = "lips-free-text-search/v1";
    public const string PromptVersion = "catalog-search-interpretation/1";
    public const int MaxBodyBytes = 100_000;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static void Need(bool value, string code) { if (!value) throw new ArgumentException(code); }
    static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    static void Strings(IReadOnlyList<string>? values, int count, int length)
    { Need(values is not null && values.Count <= count && values.All(v => Text(v,length)) && values.Distinct(StringComparer.Ordinal).Count()==values.Count, "Invalid text list"); }
    static void Options(FreeTextSearchOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        Strings(o.Countries,250,200); Strings(o.Regions,1000,200); Strings(o.ProductTypes,100,200); Strings(o.Vintages,500,200); Strings(o.Suppliers,1000,200);
        Need(o.VolumesMl is not null && o.VolumesMl.Count <=100 && o.VolumesMl.All(v=>v is >=1 and <=100000) && o.VolumesMl.Distinct().Count()==o.VolumesMl.Count,"Invalid volumes");
    }
    static void Request(FreeTextSearchRequest r)
    {
        Need(r.Schema==Schema && r.PromptVersion==PromptVersion && r.RequestId!=Guid.Empty && Text(r.CatalogVersion,128) && Text(r.Query,200),"Invalid request identity"); Options(r.Options);
        Need(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r,JsonOptions))<=MaxBodyBytes,"Request body too large");
    }
    public static FreeTextSearchRequest CreateRequest(Guid id, string version, string query, FreeTextSearchOptions options)
    {
        Options(options);
        var snapshot=new FreeTextSearchOptions(options.Countries.ToArray(),options.Regions.ToArray(),options.ProductTypes.ToArray(),options.VolumesMl.ToArray(),options.Vintages.ToArray(),options.Suppliers.ToArray());
        var r=new FreeTextSearchRequest(Schema,id,version,PromptVersion,query,snapshot); Request(r); return r;
    }
    static void Duplicates(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Object) { var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var p in e.EnumerateObject()) { Need(names.Add(p.Name),"Duplicate JSON member"); Duplicates(p.Value); } }
        else if(e.ValueKind==JsonValueKind.Array) foreach(var v in e.EnumerateArray()) Duplicates(v);
    }
    static void Exact(JsonElement e, params string[] names)
    { Need(e.ValueKind==JsonValueKind.Object,"Object required"); Need(e.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names),"Unknown or missing member"); }
    static string Str(JsonElement e,int max) { Need(e.ValueKind==JsonValueKind.String,"String required"); var s=e.GetString()!; Need(Text(s,max),"Invalid string"); return s; }
    static string? NullableString(JsonElement e) => e.ValueKind==JsonValueKind.Null?null:Str(e,200);
    static int? Integer(JsonElement e,int min,int max)
    { if(e.ValueKind==JsonValueKind.Null)return null; Need(e.ValueKind==JsonValueKind.Number && e.TryGetInt32(out _),"Integer required"); var n=e.GetInt32(); Need(n>=min&&n<=max,"Integer out of bounds"); return n; }
    static string[] List(JsonElement e) { Need(e.ValueKind==JsonValueKind.Array,"Array required"); var a=e.EnumerateArray().Select(v=>Str(v,100)).ToArray(); Strings(a,10,100); return a; }
    static void Member<T>(T? value,IReadOnlyList<T> options) { Need(value is null || options.Contains(value),"Value outside options"); }
    public static FreeTextSearchInterpretation ParseResponse(FreeTextSearchRequest request,string json)
    {
        Request(request); Need(json is not null && Encoding.UTF8.GetByteCount(json)<=MaxBodyBytes,"Response body too large");
        using var doc=JsonDocument.Parse(json!); var e=doc.RootElement; Duplicates(e);
        Exact(e,"schema","requestId","catalogVersion","promptVersion","provider","model","filters","unparsedTerms");
        Need(Str(e.GetProperty("schema"),100)==Schema && Str(e.GetProperty("promptVersion"),100)==PromptVersion,"Schema mismatch");
        var id=Str(e.GetProperty("requestId"),100); Need(Guid.TryParse(id,out var guid)&&guid==request.RequestId,"Request ID mismatch");
        var version=Str(e.GetProperty("catalogVersion"),128); Need(version==request.CatalogVersion,"Catalog version mismatch");
        var f=e.GetProperty("filters"); Exact(f,"keywords","supplier","country","region","productType","volumeMl","vintage","priceMin","priceMax","availableOnly");
        var available=f.GetProperty("availableOnly"); Need(available.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False,"Boolean required");
        var filters=new FreeTextSearchFilters(List(f.GetProperty("keywords")),NullableString(f.GetProperty("supplier")),NullableString(f.GetProperty("country")),NullableString(f.GetProperty("region")),NullableString(f.GetProperty("productType")),Integer(f.GetProperty("volumeMl"),1,100000),NullableString(f.GetProperty("vintage")),Integer(f.GetProperty("priceMin"),0,1000000000),Integer(f.GetProperty("priceMax"),0,1000000000),available.ValueKind==JsonValueKind.Null?null:available.GetBoolean());
        var o=request.Options; Member(filters.Supplier,o.Suppliers);Member(filters.Country,o.Countries);Member(filters.Region,o.Regions);Member(filters.ProductType,o.ProductTypes);Member(filters.Vintage,o.Vintages);
        Need(filters.VolumeMl is null || o.VolumesMl.Contains(filters.VolumeMl.Value),"Volume outside options"); Need(!(filters.PriceMin>filters.PriceMax),"Price range invalid");
        return new(Schema,guid,version,PromptVersion,Str(e.GetProperty("provider"),100),Str(e.GetProperty("model"),200),filters,List(e.GetProperty("unparsedTerms")));
    }
    static string? Prefer(string? ui,string? parsed)=>string.IsNullOrWhiteSpace(ui)?parsed:ui;
    public static StructuredSearchPlan Merge(CatalogFilter explicitUi,string? explicitSupplierCode,FreeTextSearchInterpretation response)
    {
        var f=response.Filters; Strings(f.Keywords,10,100);Strings(response.UnparsedTerms,10,100);
        var effective=explicitUi with { Country=Prefer(explicitUi.Country,f.Country),Region=Prefer(explicitUi.Region,f.Region),ProductType=Prefer(explicitUi.ProductType,f.ProductType),VolumeMl=explicitUi.VolumeMl??f.VolumeMl,VintageRaw=Prefer(explicitUi.VintageRaw,f.Vintage),PriceMin=explicitUi.PriceMin??f.PriceMin,PriceMax=explicitUi.PriceMax??f.PriceMax,AvailableOnly=explicitUi.AvailableOnly||f.AvailableOnly==true,Text=null };
        Need(effective.Limit is >=1 and <=2000 && !(effective.PriceMin<0) && !(effective.PriceMax<0) && !(effective.PriceMin>effective.PriceMax) && !(effective.VolumeMl<=0),"Merged filters invalid");
        effective=ProductTypeVocabulary.MergeUi(explicitUi,effective with {ProductType=f.ProductType}) with {ProductType=effective.ProductType};
        return new(effective,Prefer(explicitSupplierCode,f.Supplier),f.Keywords.Concat(response.UnparsedTerms).Distinct(StringComparer.Ordinal).ToArray());
    }
}
