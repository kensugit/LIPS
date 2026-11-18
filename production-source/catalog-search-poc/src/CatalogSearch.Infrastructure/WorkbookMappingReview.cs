using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

// Reconstruct questions from the preserved workbook for both intake and publication.
// Supplier detection must agree with the folder; a rule cannot change that identity.
public static class WorkbookMappingReview
{
    public static IReadOnlyList<IntakeMappingQuestion> Inspect(byte[] content)
    {
        WorkbookParserSelection selected;
        try { selected = WorkbookParserRegistry.Detect(content); }
        catch (UnsupportedWorkbookException) { return []; }
        catch (AmbiguousWorkbookException) { return []; }
        return selected.SupplierCode switch
        {
            "LUC" => LucVolumeReview.Inspect(content),
            "LA_VINEE" => LaVineeMappingReview.Inspect(content),
            _ => []
        };
    }
}
