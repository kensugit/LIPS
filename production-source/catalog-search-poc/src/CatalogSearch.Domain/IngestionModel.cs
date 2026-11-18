namespace CatalogSearch.Domain;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
}
public sealed class SourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierId { get; set; }
    public required string FileName { get; set; }
    public required string FileHash { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
public sealed class ExtractionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDocumentId { get; set; }
    public string ParserVersion { get; set; } = "WineToStyle/2";
    public string Status { get; set; } = "Completed";
    public int RowCount { get; set; }
}
public sealed class PriceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierProductId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public decimal? Amount { get; set; }
    public required string RawValue { get; set; }
    public string PriceType { get; set; } = "REFERENCE_RETAIL";
    public string Currency { get; set; } = "JPY";
    public bool TaxIncluded { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
public sealed class InventorySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDocumentId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
public sealed class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InventorySnapshotId { get; set; }
    public Guid SupplierProductId { get; set; }
    public int? Quantity { get; set; }
    public InventoryStatus Status { get; set; }
    public required string RawValue { get; set; }
}
public sealed class SourceEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDocumentId { get; set; }
    public Guid SupplierProductId { get; set; }
    public required string SheetName { get; set; }
    public int RowNumber { get; set; }
    public required string RawCellsJson { get; set; }
    public required string ExtractedRowJson { get; set; }
}
public sealed class ReviewTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierProductId { get; set; }
    public string Status { get; set; } = "Pending";
    public string Reason { get; set; } = "Canonical link requires human review";
}
public sealed class OperationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SourceDocumentId { get; set; }
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
    public string Action { get; set; } = "WineToStyleImport";
    public required string Status { get; set; }
    public int RowCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
