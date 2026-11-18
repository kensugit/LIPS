namespace CatalogSearch.Domain;

public enum InventoryStatus { Unknown, InStock, LowStock, OutOfStock, Expected }
public enum LinkStatus { Unmatched, Candidate, Confirmed, Rejected }
public enum SourceSystemKind { Shudoraku, Iwset, Other }
public enum SearchEntityType { Product, SupplierProduct, InternalProduct }

public sealed class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CanonicalName { get; set; }
    public Guid? ProducerId { get; set; }
    public string Category { get; set; } = "OTHER";
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public int? Vintage { get; set; }
    public int? VolumeMl { get; set; }
    public string AttributesJson { get; set; } = "{}";
}

public sealed class SupplierProduct
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SupplierId { get; init; }
    public Guid? ProductId { get; set; }
    public required string SupplierProductCode { get; init; }
    public string CodeKind { get; set; } = "SupplierSku";
    public string? OriginalSupplierProductCode { get; set; }
    public required string SupplierProductName { get; set; }
    public string ProductNameEn { get; set; } = "";
    public string ProducerNameJa { get; set; } = "";
    public string ProducerNameEn { get; set; } = "";
    public string Country { get; set; } = "";
    public string Region { get; set; } = "";
    public string ProductType { get; set; } = "";
    public int? Vintage { get; set; }
    public string VintageRaw { get; set; } = "";
    public int VolumeMl { get; set; }
    public bool TaxIncluded { get; set; }
    public Guid? CurrentSourceDocumentId { get; set; }
    public decimal? CurrentPrice { get; set; }
    public int? CurrentInventoryQuantity { get; set; }
    public InventoryStatus CurrentInventoryStatus { get; set; } = InventoryStatus.Unknown;
    public DateTimeOffset? CurrentObservedAt { get; set; }
}

public sealed class SourceSystem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Code { get; init; }
    public required string Name { get; set; }
    public SourceSystemKind Kind { get; init; }
}

public sealed class InternalProductRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SourceSystemId { get; init; }
    public required string SourceKey { get; init; }
    public required string ProductName { get; set; }
    public string? ProductNameKana { get; set; }
    public string? JanCode { get; set; }
    public string? SupplierCode { get; set; }
    public int? VolumeMl { get; set; }
    public string? CategoryCode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ObservedAt { get; set; }
    public string SourcePayloadJson { get; set; } = "{}";
    public string RecordHash { get; set; } = string.Empty;
}

public sealed class InternalProductLink
{
    public Guid InternalProductRecordId { get; init; }
    public Guid? ProductId { get; set; }
    public LinkStatus Status { get; set; }
    public decimal? Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class ProductSearchDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SearchEntityType EntityType { get; init; }
    public required Guid SourceRecordId { get; init; }
    public Guid? ProductId { get; set; }
    public required string SearchText { get; set; }
    public string StructuredDataJson { get; set; } = "{}";
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }
    public int IndexVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record InventoryValue(int? Quantity, InventoryStatus Status, string RawValue)
{
    public static bool IsAbundant(string? raw) => raw?.Trim() is "〇" or "○" or "◯" or "◎";
}

public static class InventoryParser
{
    public static InventoryValue Parse(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return new(null, InventoryStatus.Unknown, value);
        if (InventoryValue.IsAbundant(value) || value is "有" or "あり" or "OK") return new(null, InventoryStatus.InStock, value);
        if (value is "△" or "僅少" or "少量") return new(null, InventoryStatus.LowStock, value);
        if (value is "×" or "欠品" or "完売") return new(null, InventoryStatus.OutOfStock, value);
        if (value is "入荷待ち") return new(null, InventoryStatus.Expected, value);

        var match = System.Text.RegularExpressions.Regex.Match(value, @"^(?:残)?([0-9]+)(?:本)?$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var quantity))
            return new(quantity,
                quantity == 0 ? InventoryStatus.OutOfStock :
                value.StartsWith("残", StringComparison.Ordinal) ? InventoryStatus.LowStock : InventoryStatus.InStock,
                value);

        return new(null, InventoryStatus.Unknown, value);
    }
}
