using System.Text.Json;
using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION"))) Skip = "Set CATALOG_TEST_CONNECTION to an isolated PostgreSQL database."; }
}
public sealed class PersistenceTests
{
    private static byte[] Workbook(string sku, int price, string inventory, bool tax = false)
    {
        using var w = new XLWorkbook(); var s = w.AddWorksheet("ワインリスト");
        var headers = new[] { "SKU", "生産者名／ブランド名", "Producer / Brand", "商品名", "Wine", "VTG", "容量", "入数", tax ? "希望小売（税込）" : "希望小売", "在庫", "タイプ", "国", "産地", "JAN" };
        for (var i = 0; i < headers.Length; i++) s.Cell(2, i + 1).Value = headers[i];
        var values = new[] { sku, "生産者", "Producer", "試験ワイン", "Test Wine", "NV", "750", "6", "", inventory, "赤", "フランス", "ブルゴーニュ", "0" };
        for (var i = 0; i < values.Length; i++) s.Cell(3, i + 1).Value = values[i];
        s.Cell(3, 9).Value = price; s.Cell(3, 9).Style.NumberFormat.Format = "¥#,##0";
        using var stream = new MemoryStream(); w.SaveAs(stream); return stream.ToArray();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CurrencyFormattedPriceAndExplicitTaxAreParsed(bool tax)
    {
        var row = Assert.Single(new WineToStyleInventoryParser().Parse(Workbook("TEST", 4800, "", tax), default));
        Assert.Equal(4800m, row.ReferenceRetailPrice); Assert.Equal(tax, row.TaxIncluded);
        Assert.Equal(InventoryStatus.Unknown, row.Inventory.Status); Assert.Contains("I3", row.RawCellsJson);
    }
    private static CatalogSearchDbContext Db() => new(new DbContextOptionsBuilder<CatalogSearchDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options);
    private static async Task<ImportResult> Save(byte[] bytes, DateTimeOffset time)
    {
        await using var db = Db();
        var rows = new WineToStyleInventoryParser().Parse(bytes, default);
        return await new PostgresInventoryStore(db).SaveAsync(new SourceInput("fixture.xlsx", bytes, time), Convert.ToHexString(SHA256.HashData(bytes)), rows, default);
    }
    [PostgresFact]
    public async Task RealPostgresConcurrentImportsHistoryFiltersAndRollback()
    {
        await using (var initial = Db()) await initial.Database.MigrateAsync();
        var sku = "TEST-" + Guid.NewGuid().ToString("N");
        var bytes = Workbook(sku, 4800, "OK"); var time = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(Save(bytes, time), Save(bytes, time));
        Assert.Single(results, x => x.AlreadyImported);
        Assert.Equal(results[0].SourceDocumentId, results[1].SourceDocumentId);
        await Save(Workbook(sku, 6000, "少量"), time.AddDays(1));
        await Save(Workbook(sku, 1000, ""), time.AddDays(-1));
        await using var db = Db();
        var product = await db.SupplierProducts.SingleAsync(x => x.SupplierProductCode == sku);
        Assert.Equal(6000m, product.CurrentPrice); Assert.Equal(InventoryStatus.LowStock, product.CurrentInventoryStatus);
        Assert.Null(product.ProductId);
        Assert.Equal(3, await db.PriceHistories.CountAsync(x => x.SupplierProductId == product.Id));
        Assert.Equal(3, await db.SourceEvidence.CountAsync(x => x.SupplierProductId == product.Id));
        Assert.Equal(3, await db.InventoryItems.CountAsync(x => x.SupplierProductId == product.Id));
        Assert.Single(await db.ReviewTasks.Where(x => x.SupplierProductId == product.Id).ToListAsync());
        var saved = await db.SourceDocuments.FindAsync(results[0].SourceDocumentId);
        Assert.Equal(bytes, saved!.Content);
        var hits = await new CatalogQuery(db).SearchAsync(new("フランス", "ブルゴーニュ", "赤", 10000, true, "Test", 2000));
        Assert.Contains(hits, x => x.Sku == sku && x.SourceRow == 3 && x.ReferenceRetailPrice == 6000);
        Assert.DoesNotContain(await new CatalogQuery(db).SearchAsync(new(PriceMax: 5000, Limit: 2000)), x => x.Sku == sku);
        var countsBefore = await db.SourceDocuments.CountAsync();
        var bad = new WineToStyleInventoryParser().Parse(Workbook(sku, 9000, "OK"), default).Single() with { RawCellsJson = "invalid json" };
        await using (var badDb = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => new PostgresInventoryStore(badDb).SaveAsync(
                new SourceInput("invalid.xlsx", [1,2,3], time.AddDays(2)), Guid.NewGuid().ToString("N"), [bad], default));
        Assert.Equal(countsBefore, await db.SourceDocuments.CountAsync());
        await db.Entry(product).ReloadAsync(); Assert.Equal(6000m, product.CurrentPrice);
    }
}
