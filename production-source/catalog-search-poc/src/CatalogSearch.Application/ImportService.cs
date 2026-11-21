using System.Security.Cryptography;
using CatalogSearch.Domain;
namespace CatalogSearch.Application;

public sealed record SourceInput(string FileName, byte[] Content, DateTimeOffset ObservedAt, string SupplierCode = "WINE_TO_STYLE", string SupplierName = "Wine to Style", string ParserVersion = "WineToStyle/2")
{
    public IReadOnlyList<string>? SelectedSkus { get; init; }
    public IReadOnlyList<ImportRowHold>? PendingHolds { get; init; }
}
public interface IWorkbookSource { Task<SourceInput> ReadAsync(string path, DateTimeOffset observedAt, CancellationToken ct); }
public interface IWorkbookContentParser { IReadOnlyList<SupplierInventoryRow> Parse(byte[] content, CancellationToken ct); }
public sealed record ImportResult(Guid SourceDocumentId, int Products, bool AlreadyImported,
    IReadOnlyList<ImportRowHold>? Holds = null)
{
    public int AddedProducts { get; init; }
}
public interface IInventoryStore
{
    Task<ImportResult> SaveAsync(SourceInput source, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct);
}
public sealed class ImportService(IWorkbookSource source, IWorkbookContentParser parser, IInventoryStore store)
{
    public async Task<ImportResult> ImportAsync(string path, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        var input = await source.ReadAsync(path, observedAt.ToUniversalTime(), ct);
        var rows = parser.Parse(input.Content, ct);
        if (rows.Count == 0) throw new InvalidDataException("Workbook contains no product rows.");
        if (rows.Any(x => string.IsNullOrWhiteSpace(x.SupplierProductCode)) ||
            rows.Select(x => x.SupplierProductCode).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("Workbook contains empty or duplicate SKUs.");
        return await store.SaveAsync(input, Convert.ToHexString(SHA256.HashData(input.Content)), rows, ct);
    }
}
public sealed record CatalogFilter(string? Country = null, string? Region = null, string? ProductType = null,
    decimal? PriceMax = null, bool AvailableOnly = false, string? Text = null, int Limit = 100, decimal? PriceMin = null, int? VolumeMl = null, string? VintageRaw = null);
public sealed record CatalogResult(string Sku, string ProductNameJa, string ProductNameEn,
    string ProducerNameJa, string ProducerNameEn, int? Vintage, string VintageRaw, int VolumeMl,
    decimal? ReferenceRetailPrice, bool TaxIncluded, InventoryStatus Inventory, int? Quantity,
    string SourceSheet, int SourceRow, Guid SourceDocumentId, string Country, string Region, string ProductType)
{
    public ImporterSearchEvidence? ImporterEvidence { get; init; }
    public bool AbundantInventory { get; init; }
    public Guid SupplierProductId { get; init; }
    public string CodeKind { get; init; } = "SupplierSku";
    public string? OriginalSupplierProductCode { get; init; }
    public bool DuplicateMinimumSelected { get; init; }
    public string VintageLabel => VintageNotation.Normalize(VintageRaw);
    public string SupplierName { get; init; } = "";
    public decimal? SpecialPurchasePrice { get; init; }
}
