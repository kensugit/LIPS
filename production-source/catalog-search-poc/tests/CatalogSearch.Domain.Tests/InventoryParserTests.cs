using CatalogSearch.Domain;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class InventoryParserTests
{
    [Theory]
    [InlineData("24", 24, InventoryStatus.InStock)]
    [InlineData("24本", 24, InventoryStatus.InStock)]
    [InlineData("○", null, InventoryStatus.InStock)]
    [InlineData("△", null, InventoryStatus.LowStock)]
    [InlineData("×", null, InventoryStatus.OutOfStock)]
    [InlineData("残3", 3, InventoryStatus.LowStock)]
    [InlineData("僅少", null, InventoryStatus.LowStock)]
    [InlineData("入荷待ち", null, InventoryStatus.Expected)]
    [InlineData("OK", null, InventoryStatus.InStock)]
    [InlineData("少量", null, InventoryStatus.LowStock)]
    [InlineData("0", 0, InventoryStatus.OutOfStock)]
    [InlineData("", null, InventoryStatus.Unknown)]
    public void ParsesKnownValues(string raw, int? quantity, InventoryStatus status)
    {
        var actual = InventoryParser.Parse(raw);
        Assert.Equal(quantity, actual.Quantity);
        Assert.Equal(status, actual.Status);
        Assert.Equal(raw, actual.RawValue);
    }
}
