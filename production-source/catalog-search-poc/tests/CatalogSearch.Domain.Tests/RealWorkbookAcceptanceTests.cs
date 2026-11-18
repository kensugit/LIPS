using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public sealed class WorkbookFactAttribute : FactAttribute
{
    public WorkbookFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_ACCEPTANCE_CONNECTION")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_WORKBOOK")))
            Skip = "Set CATALOG_ACCEPTANCE_CONNECTION and CATALOG_WORKBOOK for the 1061-row acceptance.";
    }
}
public sealed class RealWorkbookAcceptanceTests
{
    [WorkbookFact]
    public async Task RealWorkbookTwiceMatchesDatabaseAndFilteredResults()
    {
        var path = Environment.GetEnvironmentVariable("CATALOG_WORKBOOK")!;
        var bytes = await File.ReadAllBytesAsync(path); var hash = SHA256.HashData(bytes);
        var parser = new WineToStyleInventoryParser(); var rows = parser.Parse(bytes, default);
        Assert.Equal(1061, rows.Count);
        Assert.Equal(655, rows.Count(x => x.Inventory.Status == InventoryStatus.InStock));
        Assert.Equal(399, rows.Count(x => x.Inventory.Status == InventoryStatus.LowStock));
        Assert.Equal(7, rows.Count(x => x.Inventory.Status == InventoryStatus.Unknown));
        Assert.Equal(329, rows.Count(x => x.Inventory.Quantity.HasValue));
        Assert.Equal(22, rows.Count(x => x.ReferenceRetailPrice is null));
        Assert.Equal(19, rows.Count(x => x.JanCodes.Count > 1));
        Assert.Equal(208, rows.Count(x => x.JanCodes.Count == 0));
        Assert.All(rows, x => Assert.False(x.TaxIncluded));
        var options = new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_ACCEPTANCE_CONNECTION")).Options;
        ImportResult? first = null;
        for (var i = 0; i < 2; i++)
        {
            await using var importDb = new CatalogSearchDbContext(options);
            var result = await new ImportService(new FileWorkbookSource(), parser, new PostgresInventoryStore(importDb))
                .ImportAsync(path, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Equal(1061, result.Products);
            if (i == 0) first = result; else { Assert.True(result.AlreadyImported); Assert.Equal(first!.SourceDocumentId, result.SourceDocumentId); }
        }
        await using var db = new CatalogSearchDbContext(options);
        Assert.Equal(1061, await db.SupplierProducts.CountAsync());
        Assert.Equal(1, await db.SourceDocuments.CountAsync());
        Assert.Equal(1, await db.ExtractionJobs.CountAsync());
        Assert.Equal(1061, await db.PriceHistories.CountAsync());
        Assert.Equal(1, await db.InventorySnapshots.CountAsync());
        Assert.Equal(1061, await db.InventoryItems.CountAsync());
        Assert.Equal(1061, await db.SourceEvidence.CountAsync());
        Assert.Equal(1061, await db.ProductSearchDocuments.CountAsync());
        Assert.Equal(1061, await db.ReviewTasks.CountAsync());
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(bytes, (await db.SourceDocuments.SingleAsync()).Content);
        var all = await new CatalogQuery(db).SearchAsync(new(Limit: 2000));
        foreach (var sourceRow in rows)
        {
            var hit = Assert.Single(all, x => x.Sku == sourceRow.SupplierProductCode);
            Assert.Equal(sourceRow.ProductNameJa, hit.ProductNameJa); Assert.Equal(sourceRow.ProductNameEn, hit.ProductNameEn);
            Assert.Equal(sourceRow.ProducerNameJa, hit.ProducerNameJa); Assert.Equal(sourceRow.ProducerNameEn, hit.ProducerNameEn);
            Assert.Equal(sourceRow.Vintage, hit.Vintage); Assert.Equal(sourceRow.VintageRaw, hit.VintageRaw);
            Assert.Equal(sourceRow.VolumeMl, hit.VolumeMl); Assert.Equal(sourceRow.ReferenceRetailPrice, hit.ReferenceRetailPrice);
            Assert.Equal(sourceRow.Inventory.Status, hit.Inventory); Assert.Equal(sourceRow.Inventory.Quantity, hit.Quantity);
            Assert.Equal(sourceRow.SourceRow, hit.SourceRow); Assert.Equal(sourceRow.SourceSheet, hit.SourceSheet);
        }
        var expected = rows.Where(x => x.Country == "フランス" && x.Region == "ブルゴーニュ" && x.ProductType == "赤" &&
            x.ReferenceRetailPrice is not null && x.ReferenceRetailPrice <= 10000 &&
            x.Inventory.Status is InventoryStatus.InStock or InventoryStatus.LowStock).Select(x => x.SupplierProductCode).Order().ToArray();
        var filtered = await new CatalogQuery(db).SearchAsync(new("フランス", "ブルゴーニュ", "赤", 10000, true, Limit: 2000));
        Assert.NotEmpty(expected); Assert.Equal(expected, filtered.Select(x => x.Sku).Order().ToArray());
        var filterCases = new[] {
            new CatalogFilter(VolumeMl: 750, Limit: 2000),
            new CatalogFilter(VintageRaw: "2023", Limit: 2000),
            new CatalogFilter(VintageRaw: "NV", Limit: 2000),
            new CatalogFilter(VintageRaw: "MV", Limit: 2000),
            new CatalogFilter(PriceMin: 5000, Limit: 2000),
            new CatalogFilter(PriceMax: 10000, Limit: 2000),
            new CatalogFilter(PriceMin: 6500, PriceMax: 6500, Limit: 2000),
            new CatalogFilter(VolumeMl: 750, VintageRaw: "2023", PriceMin: 5000, PriceMax: 10000, Limit: 2000)
        };
        foreach (var filter in filterCases)
        {
            var expectedRows = rows.Where(x =>
                (filter.VolumeMl is null || x.VolumeMl == filter.VolumeMl) &&
                (filter.VintageRaw is null || x.VintageRaw == filter.VintageRaw) &&
                (filter.PriceMin is null || (!x.TaxIncluded && x.ReferenceRetailPrice.HasValue && x.ReferenceRetailPrice >= filter.PriceMin)) &&
                (filter.PriceMax is null || (!x.TaxIncluded && x.ReferenceRetailPrice.HasValue && x.ReferenceRetailPrice <= filter.PriceMax)))
                .Select(x => x.SupplierProductCode).Order().ToArray();
            Assert.NotEmpty(expectedRows);
            var actualRows = await new CatalogQuery(db).SearchAsync(filter);
            Assert.Equal(expectedRows, actualRows.Select(x => x.Sku).Order().ToArray());
        }
        await Assert.ThrowsAsync<ArgumentException>(() => new CatalogQuery(db).SearchAsync(new(PriceMin: 10000, PriceMax: 5000)));
        await Assert.ThrowsAsync<ArgumentException>(() => new CatalogQuery(db).SearchAsync(new(VolumeMl: 0)));
        Assert.Empty(await new CatalogQuery(db).SearchAsync(new(Country: "__no_such_country__")));
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }
}
