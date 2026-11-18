using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class OrveauxPriorityFactAttribute : FactAttribute
{
    public OrveauxPriorityFactAttribute()
    { if (new[] { "ORVEAUX_PRIORITY_CONNECTION", "ORVEAUX_EXISTING_PRODUCTS", "ORVEAUX_SOURCE" }.Any(k => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)))) Skip = "Dedicated test database and read-only source snapshots required."; }
}
public sealed class OrveauxExcelPriorityTests
{
    private static SupplierInventoryRow Row() => new("INTERNAL", "生産者", "Producer", "ワイン", "Wine", 2023, "2023", 750, 12, 1800, "1800", false,
        new(12, InventoryStatus.InStock, "12本"), "赤", "", "", [], "", null, "", "", "", "", "", "", "", 12, "定番品");
    private static SupplierProduct Product() => new() { SupplierId = Guid.NewGuid(), SupplierProductCode = "EXISTING", SupplierProductName = "ワイン", ProductNameEn = "Wine", ProducerNameEn = "Producer", VintageRaw = "2023", VolumeMl = 750, ProductType = "赤" };
    [Fact] public void MatchingIsConservativeAndDoesNotIgnoreProducerVintageVolumeOrType()
    {
        var p = Product(); Assert.True(OrveauxExcelPriorityStore.ExactIdentity(Row(), p));
        Assert.False(OrveauxExcelPriorityStore.ExactIdentity(Row() with { ProducerNameEn = "Other" }, p));
        Assert.False(OrveauxExcelPriorityStore.ExactIdentity(Row() with { VintageRaw = "2024" }, p));
        Assert.False(OrveauxExcelPriorityStore.ExactIdentity(Row() with { VolumeMl = 375 }, p));
        Assert.False(OrveauxExcelPriorityStore.ExactIdentity(Row() with { ProductType = "白" }, p));
        Assert.False(OrveauxExcelPriorityStore.ApprovedTinto(Row(), p));
    }
    [Fact] public void RecheckIsLimitedToUnacceptedKnownOrveauxOriginal()
    {
        var r = new IntakeReview { Hash = OrveauxStockParser.ApprovedOriginalHash, State = "Held", Result = new("オルヴォー/original.xlsx", "Held", "同日別内容または新しい資料が登録済みです。", "ORVEAUX") };
        IntakeReviewStore.PrepareUnsupportedRecheck(r, 1, r.Hash); Assert.Equal("Ready", r.State);
        r.State = "Unknown"; Assert.Throws<IntakeConflictException>(() => IntakeReviewStore.PrepareUnsupportedRecheck(r, 1, r.Hash));
        r.State = "Held"; r.Result = r.Result! with { Products = 1 }; Assert.Throws<IntakeConflictException>(() => IntakeReviewStore.PrepareUnsupportedRecheck(r, 1, r.Hash));
        r.Result = r.Result with { Products = 0 }; r.Hash = "other"; Assert.Throws<IntakeConflictException>(() => IntakeReviewStore.PrepareUnsupportedRecheck(r, 1, r.Hash));
        r.Hash = OrveauxStockParser.ApprovedOriginalHash; r.State = "Partial"; r.Result = r.Result with { Products = 160, SourceDocumentId = Guid.NewGuid(), Status = "Partial" };
        IntakeReviewStore.PrepareUnsupportedRecheck(r, 1, r.Hash); Assert.Equal("Ready", r.State);
    }
    [OrveauxPriorityFact] public async Task ExactExistingIdsAreUpdatedOnceAndPriorEvidenceSurvives()
    {
        var connection = Environment.GetEnvironmentVariable("ORVEAUX_PRIORITY_CONNECTION")!;
        var c = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", c.Host); Assert.Equal(56037, c.Port); Assert.StartsWith("orveaux_priority_test_", c.Database!);
        CatalogSearchDbContext Db() => new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
        var date = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero); Guid priorId;
        var sourceRows = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("ORVEAUX_EXISTING_PRODUCTS")!)).RootElement.EnumerateArray().ToArray();
        await using (var db = Db())
        {
            await db.Database.EnsureCreatedAsync();
            var supplier = new Supplier { Code = "ORVEAUX", Name = "オルヴォー" }; db.Suppliers.Add(supplier);
            var source = new SourceDocument { SupplierId = supplier.Id, FileName = "synthetic-prior.pdf", FileHash = "PRIOR-SYNTHETIC", Content = [1], ObservedAt = date, ImportedAt = date };
            db.SourceDocuments.Add(source); priorId = source.Id;
            db.SourceDocuments.Add(new() { SupplierId = supplier.Id, FileName = "synthetic-later-source-only-lot.xlsx", FileHash = "LATER-SOURCE-ONLY", Content = [2], ObservedAt = date.AddDays(1), ImportedAt = date });
            var snapshot = new InventorySnapshot { SourceDocumentId = source.Id, ObservedAt = date }; db.InventorySnapshots.Add(snapshot);
            foreach (var raw in sourceRows)
            {
                string S(string key) => raw.GetProperty(key).GetString() ?? "";
                var product = new SupplierProduct { SupplierId = supplier.Id, SupplierProductCode = S("sku"), SupplierProductName = S("productNameJa"), ProductNameEn = S("productNameEn"), ProducerNameJa = S("producerNameJa"), ProducerNameEn = S("producerNameEn"), VintageRaw = S("vintageRaw"), VolumeMl = raw.GetProperty("volumeMl").GetInt32(), ProductType = S("productType"), CurrentObservedAt = date, CurrentSourceDocumentId = source.Id, CurrentPrice = raw.GetProperty("referenceRetailPrice").ValueKind == JsonValueKind.Number ? raw.GetProperty("referenceRetailPrice").GetDecimal() : null, CurrentInventoryQuantity = raw.GetProperty("quantity").ValueKind == JsonValueKind.Number ? raw.GetProperty("quantity").GetInt32() : null };
                db.SupplierProducts.Add(product);
                var historical = Row() with { SupplierProductCode = product.SupplierProductCode, ProductNameJa = product.SupplierProductName,
                    ProductNameEn = product.ProductNameEn, ProducerNameJa = product.ProducerNameJa, ProducerNameEn = product.ProducerNameEn,
                    VintageRaw = product.VintageRaw, VolumeMl = product.VolumeMl, ProductType = product.ProductType,
                    Description = "HISTORICAL-DESCRIPTION-MARKER", Grapes = "HISTORICAL-GRAPE", JanCodes = ["OLD-JAN"] };
                db.SourceEvidence.Add(new() { SupplierProductId = product.Id, SourceDocumentId = source.Id, SheetName = "synthetic", RowNumber = 1, RawCellsJson = raw.GetRawText(), ExtractedRowJson = JsonSerializer.Serialize(historical) });
                db.InventoryItems.Add(new() { SupplierProductId = product.Id, InventorySnapshotId = snapshot.Id, Quantity = product.CurrentInventoryQuantity, Status = InventoryStatus.Unknown, RawValue = "synthetic" });
                db.ProductSearchDocuments.Add(new() { SourceRecordId = product.Id, EntityType = SearchEntityType.SupplierProduct, SearchText = "ORIGINAL-SEARCH-MARKER オルヴォー " + product.SupplierProductName, StructuredDataJson = "{\"JanCodes\":[\"PRESERVED\"]}" });
            }
            await db.SaveChangesAsync();
        }
        var bytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("ORVEAUX_SOURCE")!);
        var rows = new OrveauxStockParser().Parse(bytes, default);
        var input = new SourceInput("original.xlsx", bytes, date, "ORVEAUX", "オルヴォー", OrveauxExcelPriorityStore.Version) { PendingHolds = OrveauxStockParser.NewArrivalHolds(bytes) };
        Guid acceptedSource;
        await using (var db = Db())
        {
            var result = await new PostgresInventoryStore(db, true).SaveAsync(input, OrveauxStockParser.ApprovedOriginalHash, rows, default);
            Assert.Equal(160, result.Products); Assert.Equal(160, result.AddedProducts); Assert.Equal(248, result.Holds!.Count); acceptedSource = result.SourceDocumentId;
            Assert.All(await db.ProductSearchDocuments.ToListAsync(), d => Assert.Contains("ORIGINAL-SEARCH-MARKER", d.SearchText));
            // Reproduce the earlier index omission on accepted rows; replay must repair only the index.
            var ids = await db.SourceEvidence.Where(e => e.SourceDocumentId == acceptedSource).Select(e => e.SupplierProductId).ToListAsync();
            foreach (var d in await db.ProductSearchDocuments.Where(d => ids.Contains(d.SourceRecordId)).ToListAsync()) d.SearchText = "INDEX-OMISSION-REPRO";
            await db.SaveChangesAsync();
        }
        await using (var db = Db())
        {
            var result = await new PostgresInventoryStore(db, true).SaveAsync(input, OrveauxStockParser.ApprovedOriginalHash, rows, default);
            Assert.Equal(160, result.Products); Assert.Equal(0, result.AddedProducts); Assert.Equal(248, result.Holds!.Count);
            Assert.Equal(390, await db.SupplierProducts.CountAsync()); Assert.Equal(390, await db.SourceEvidence.CountAsync(e => e.SourceDocumentId == priorId));
            Assert.Equal(160, await db.SourceEvidence.CountAsync(e => e.SourceDocumentId == acceptedSource)); Assert.Equal(550, await db.InventoryItems.CountAsync());
            var tinto = await db.SupplierProducts.SingleAsync(p => p.SupplierProductCode == "ORV-F76228FE2775FF3F4E8C5F46");
            Assert.Equal(126, tinto.CurrentInventoryQuantity); Assert.Equal(1800, tinto.CurrentPrice); Assert.Equal("Finca la Escatada", tinto.ProducerNameEn);
            Assert.All(await db.ProductSearchDocuments.ToListAsync(), d => Assert.Contains("PRESERVED", d.StructuredDataJson));
            Assert.All(await db.ProductSearchDocuments.ToListAsync(), d => Assert.Contains("オルヴォー", d.SearchText));
            var acceptedIds = await db.SourceEvidence.Where(e => e.SourceDocumentId == acceptedSource).Select(e => e.SupplierProductId).ToListAsync();
            Assert.All(await db.ProductSearchDocuments.Where(d => acceptedIds.Contains(d.SourceRecordId)).ToListAsync(), d =>
            { Assert.Contains("HISTORICAL-DESCRIPTION-MARKER", d.SearchText); Assert.Contains("HISTORICAL-GRAPE", d.SearchText); Assert.Contains("OLD-JAN", d.SearchText); });
            Assert.Equal(390, (await new CatalogQuery(db).SearchAsync(new CatalogFilter(Text: "オルヴォー", Limit: 2000))).Count);
            Assert.Equal(new byte[] { 1 }, (await db.SourceDocuments.SingleAsync(s => s.Id == priorId)).Content);
            await Assert.ThrowsAsync<FolderImportHeldException>(() => new OrveauxExcelPriorityStore(db).SaveAsync(input with { ObservedAt = date.AddDays(1) }, OrveauxStockParser.ApprovedOriginalHash, rows, default));
        }
    }
}
