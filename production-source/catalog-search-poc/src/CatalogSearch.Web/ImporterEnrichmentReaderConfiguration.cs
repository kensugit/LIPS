using CatalogSearch.Application;
using CatalogSearch.Infrastructure;

namespace CatalogSearch.Web;

public static class ImporterEnrichmentReaderConfiguration
{
    private static bool Text(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum&&value==value.Trim()&&!value.Any(char.IsControl);
    public static (IImporterEnrichmentReader? Reader,string? LegacyVersion) Create(IConfiguration configuration)
    {
        var children=configuration.GetSection("ImporterEnrichment:SupplierRoutes").GetChildren().ToArray();
        if(configuration["ImporterEnrichment:Root"]is not{Length:>0}root)
        {
            if(children.Length!=0)throw new ArgumentException("enrichment_routes_require_legacy_configuration");
            return(null,null);
        }
        if(!Path.IsPathFullyQualified(root)||root.Any(char.IsControl))throw new ArgumentException("enrichment_root_must_be_absolute");
        var version=configuration["ImporterEnrichment:ExtractorVersion"];
        if(string.IsNullOrWhiteSpace(version)||version.Length>200||version.Any(char.IsControl))throw new ArgumentException("enrichment_extractor_version_required");
        IImporterEnrichmentReader reader=new FileImporterEnrichmentReader(root);
        if(children.Length==0)return(reader,version);
        if(children.Length>100)throw new ArgumentException("enrichment_routes_invalid");
        var routes=new Dictionary<string,RoutedImporterEnrichmentReader.Route>(StringComparer.Ordinal);
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        var legacyPath=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach(var child in children)
        {
            var keys=child.GetChildren().Select(c=>c.Key).ToHashSet(StringComparer.Ordinal);
            var routeRoot=child["Root"];var routeVersion=child["ExtractorVersion"];
            if(child.Value is not null||!keys.SetEquals(["Root","ExtractorVersion"])||
                !Text(child.Key,100)||string.IsNullOrWhiteSpace(routeRoot)||!Path.IsPathFullyQualified(routeRoot)||routeRoot.Any(char.IsControl)||
                string.IsNullOrWhiteSpace(routeVersion)||routeVersion.Length>200||routeVersion.Any(char.IsControl))throw new ArgumentException("enrichment_routes_invalid");
            var selectedPath=Path.TrimEndingDirectorySeparator(Path.GetFullPath(routeRoot));
            if(selectedPath.Equals(legacyPath,comparison)||selectedPath.StartsWith(legacyPath+Path.DirectorySeparatorChar,comparison)||
                legacyPath.StartsWith(selectedPath+Path.DirectorySeparatorChar,comparison))throw new ArgumentException("enrichment_route_roots_must_be_separate");
            if(!routes.TryAdd(child.Key,new(new FileImporterEnrichmentReader(routeRoot),routeVersion)))throw new ArgumentException("enrichment_routes_invalid");
        }
        return(new RoutedImporterEnrichmentReader(reader,routes),version);
    }
}
