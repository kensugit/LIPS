using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

// Trusted configuration only. Route by the original supplier identity, never URL/model output.
// A routed miss is not a request to fall back to a different root or version.
public sealed class RoutedImporterEnrichmentReader : IImporterEnrichmentReader
{
    private static bool Text(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum&&value==value.Trim()&&!value.Any(char.IsControl);
    public sealed record Route(IImporterEnrichmentReader Reader,string ExtractorVersion);
    private readonly IImporterEnrichmentReader legacy;
    private readonly IReadOnlyDictionary<string,Route> routes;
    public RoutedImporterEnrichmentReader(IImporterEnrichmentReader legacyReader,IReadOnlyDictionary<string,Route> reviewedRoutes)
    {
        ArgumentNullException.ThrowIfNull(legacyReader);ArgumentNullException.ThrowIfNull(reviewedRoutes);
        if(reviewedRoutes.Count>100)throw new ArgumentException("ENRICHMENT_ROUTES_INVALID");
        var copy=new Dictionary<string,Route>(StringComparer.Ordinal);
        foreach(var (supplier,route)in reviewedRoutes)
            if(!Text(supplier,100)||route is null||route.Reader is null||
                !Text(route.ExtractorVersion,200)||ReferenceEquals(route.Reader,legacyReader)||!copy.TryAdd(supplier,route))throw new ArgumentException("ENRICHMENT_ROUTES_INVALID");
        legacy=legacyReader;routes=copy;
    }
    public ImporterEnrichmentReadResult ReadVerifiedForProduct(ImporterProductIdentity identity,string originalFingerprint,string extractorVersion,DateTimeOffset nowUtc)
        =>ReadVerified(new(identity,originalFingerprint,null,extractorVersion,nowUtc));
    public ImporterEnrichmentReadResult ReadVerified(ImporterProductIdentity identity,string originalFingerprint,string extractorVersion,DateTimeOffset nowUtc)
        =>ReadVerified(new(identity,originalFingerprint,null,extractorVersion,nowUtc));
    public ImporterEnrichmentReadResult ReadVerified(ImporterEnrichmentLookup lookup)
    {
        if(!routes.TryGetValue(lookup.Identity.SupplierCode,out var route))return legacy.ReadVerified(lookup);
        var old=legacy.ReadVerified(lookup);
        var selected=route.Reader.ReadVerified(lookup with{ExtractorVersion=route.ExtractorVersion});
        // A miss never resolves a still source-bound conflict; exclusion is not fact fallback.
        if(old.Status=="conflict")return old;
        // BINDING_CHANGED/expired/unavailable is not a second verified record. Never adopt it.
        if(old.Status=="verified" && (selected.Status is "verified" or "conflict"))return new("conflict",null,null,null);
        if(old.Status is "CORRUPT_OR_UNAVAILABLE" or "WRITER_STATE_UNKNOWN" or "SNAPSHOT_CHANGED")return new("ROUTE_STATE_UNKNOWN",null,null,null);
        return selected;
    }
}
