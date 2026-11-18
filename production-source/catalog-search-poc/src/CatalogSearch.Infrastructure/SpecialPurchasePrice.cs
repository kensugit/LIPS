using System.Text.Json;
namespace CatalogSearch.Infrastructure;

/// <summary>Typed purchase-price evidence is separate from reference retail and never inferred from free text.</summary>
public static class SpecialPurchasePrice
{
    public static decimal? Read(string supplierCode, string rawCellsJson)
    {
        if (supplierCode != "FINES") return null;
        try
        {
            using var doc = JsonDocument.Parse(rawCellsJson);
            if (!doc.RootElement.TryGetProperty("SpecialPurchasePrice", out var p) || p.ValueKind != JsonValueKind.Object ||
                !p.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var v) || v != 1 ||
                !p.TryGetProperty("SupplierCode", out var supplier) || supplier.GetString() != supplierCode ||
                !p.TryGetProperty("Currency", out var currency) || currency.GetString() != "JPY" ||
                !p.TryGetProperty("TaxIncluded", out var tax) || tax.ValueKind != JsonValueKind.False ||
                !p.TryGetProperty("Amount", out var value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDecimal(out var amount) || amount <= 0) return null;
            return amount;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
