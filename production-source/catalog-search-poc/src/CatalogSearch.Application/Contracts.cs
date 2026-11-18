using CatalogSearch.Domain;

namespace CatalogSearch.Application;

public sealed record InternalProductSourceRow(
    string SourceKey, string ProductName, string? ProductNameKana, string? JanCode,
    string? SupplierCode, int? VolumeMl, string? CategoryCode, string? Notes,
    bool IsActive, DateTimeOffset ObservedAt, string SourcePayloadJson);

public interface IInternalProductSource
{
    string SourceSystemCode { get; }
    IAsyncEnumerable<InternalProductSourceRow> ReadChangedAsync(DateTimeOffset? since, CancellationToken cancellationToken);
}

public interface IEmbeddingProvider
{
    string Provider { get; }
    string Model { get; }
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken);
}

public sealed record SearchPlan(
    string? Text,
    string? SemanticQuery,
    decimal? PriceMax,
    IReadOnlySet<InventoryStatus>? Inventory,
    bool? HasInternalProduct,
    bool IncludeInactiveInternalProducts = false);

public sealed record SearchHit(
    Guid? ProductId,
    Guid? InternalProductRecordId,
    string DisplayName,
    decimal Score,
    string MatchReason);

public interface IHybridSearchService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchPlan plan, CancellationToken cancellationToken);
}

public sealed record SupplierInventoryRow(
    string SupplierProductCode,
    string ProducerNameJa,
    string ProducerNameEn,
    string ProductNameJa,
    string ProductNameEn,
    int? Vintage,
    string VintageRaw,
    int VolumeMl,
    int CaseSize,
    decimal? ReferenceRetailPrice,
    string PriceRaw,
    bool TaxIncluded,
    InventoryValue Inventory,
    string ProductType,
    string Country,
    string Region,
    IReadOnlyList<string> JanCodes,
    string Grapes,
    decimal? AlcoholPercent,
    string AppellationJa,
    string AppellationEn,
    string OrganicCategory,
    string OrganicCertification,
    string PriceNote,
    string Closure,
    string LegacySource,
    int SourceRow,
    string SourceSheet)
{
    public string RawCellsJson { get; init; } = "{}";
    public string Description { get; init; } = "";
    // Source-only labels retained in extraction Evidence, not inferred search classifications.
    public bool DuplicateMinimumSelected { get; init; }
    public string SourceRegionRaw { get; init; } = "";
    public string SourceProductCategoryRaw { get; init; } = "";
    public string OriginalSupplierProductCode { get; init; } = "";
    public IReadOnlyList<SourcePriceComponent> PriceComponents { get; init; } = [];
    public IReadOnlyList<IntakeMappingResolution> MappingEvidence { get; init; } = [];
    public IReadOnlyList<SourceRowOccurrence> Occurrences { get; init; } = [];
}

public sealed record SourcePriceComponent(string Kind, decimal? Amount, bool TaxIncluded, string Unit, string Raw);
public sealed record SourceRowOccurrence(string Sheet, int Row, string RawCellsJson, InventoryValue Inventory);
public sealed record ImportRowHold(string Sku, string Reason, IReadOnlyList<SourceRowOccurrence> Sources);

public interface ISupplierInventoryWorkbookParser
{
    string SupplierCode { get; }
    Task<IReadOnlyList<SupplierInventoryRow>> ParseAsync(string path, CancellationToken cancellationToken);
}
