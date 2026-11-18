using CatalogSearch.Domain;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Domain.Tests;

public sealed class OrveauxSourceFactAttribute : FactAttribute
{
    public OrveauxSourceFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORVEAUX_SOURCE"))) Skip = "Explicit original workbook path required."; }
}
public sealed class OrveauxDbFactAttribute : FactAttribute
{
    public OrveauxDbFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ORVEAUX_TEST_CONNECTION"))) Skip = "Dedicated isolated database required."; }
}

public sealed class OrveauxStockTests
{
    private static byte[] Fixture(string stock = "12本", string secondStock = "12本", bool formula = false)
    {
        using var w = new XLWorkbook();
        foreach (var title in new[] { "定番品", "スペイン" })
        {
            var s = w.AddWorksheet(title);
            s.Cell("N6").Value = "株式会社オルヴォー";
            s.Cell("G11").Value = "容量"; s.Cell("I11").Value = "納入価格";
            s.Cell("K11").Value = "参考上代"; s.Cell("N11").Value = "在庫";
            string[] cells = ["Wine", "ワイン", "Producer", "生産者", "赤", "2023", "750", "12", "1000", "900", "2000", "", "", title == "定番品" ? stock : secondStock, "備考"];
            for (var i = 0; i < cells.Length; i++) s.Cell(12, i + 1).Value = cells[i];
            s.Cell("L12").FormulaA1 = "K12*0.1";
            if (formula) s.Cell("K12").FormulaA1 = "I12*2";
        }
        using var stream = new MemoryStream(); w.SaveAs(stream); return stream.ToArray();
    }
    [Fact]
    public void IdenticalRowsKeepBothOccurrencesWithoutSumming()
    {
        var row = Assert.Single(new OrveauxStockParser().Parse(Fixture(), default));
        Assert.Equal(12, row.Inventory.Quantity); Assert.Equal(2, row.Occurrences.Count);
        Assert.Equal(2000m, row.ReferenceRetailPrice); Assert.False(row.TaxIncluded);
        Assert.Contains("=K12*0.1", row.RawCellsJson); Assert.Contains("1000", row.PriceNote);
        Assert.StartsWith("ORV-XLS-", row.SupplierProductCode); Assert.Empty(row.OriginalSupplierProductCode);
    }
    [Fact]
    public void MissingAndOutOfStockRemainDistinct()
    {
        Assert.Equal(InventoryStatus.Unknown, OrveauxStockParser.Stock("").Status);
        Assert.Equal(InventoryStatus.OutOfStock, OrveauxStockParser.Stock("欠品中").Status);
        Assert.Equal(54, OrveauxStockParser.Stock("54個").Quantity);
        Assert.Throws<InvalidDataException>(() => OrveauxStockParser.Stock("少"));
    }
    [Fact]
    public void EquivalentOutOfStockMarksCanMerge()
    {
        var row = Assert.Single(new OrveauxStockParser().Parse(Fixture("欠品中", "×"), default));
        Assert.Null(row.Inventory.Quantity); Assert.Equal(InventoryStatus.OutOfStock, row.Inventory.Status);
        Assert.Equal(2, row.Occurrences.Count);
    }
    [Fact]
    public void ConflictingQuantityIsHeldOutsideExactApprovedSource()
        => Assert.Throws<FolderImportHeldException>(() => new OrveauxStockParser().Parse(Fixture("126本", "90本"), default));
    [Fact]
    public void SemanticFormulaIsNeverEvaluated()
        => Assert.Throws<InvalidDataException>(() => new OrveauxStockParser().Parse(Fixture(formula: true), default));
    [OrveauxSourceFact]
    public void RealSourceAuditWhenExplicitlyProvided()
    {
        var path = Environment.GetEnvironmentVariable("ORVEAUX_SOURCE");
        if (string.IsNullOrEmpty(path)) return;
        var bytes = File.ReadAllBytes(path);
        var rows = new OrveauxStockParser().Parse(bytes, default);
        Assert.Equal(378, rows.Count); Assert.Equal(428, rows.Sum(r => r.Occurrences.Count));
        var conflict = Assert.Single(rows, r => r.ProductNameEn == "Tinto Velasco Vino de la Tierra de Castilla" && r.VintageRaw == "2023" && r.VolumeMl == 750);
        Assert.Equal(126, conflict.Inventory.Quantity); Assert.Contains("90", conflict.RawCellsJson);
        Assert.Equal(OrveauxStockParser.ApprovedOriginalHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Equal("2026-09-30", OrveauxStockParser.DocumentDate(bytes));
        Assert.Equal(30, OrveauxStockParser.NewArrivalHolds(bytes).Count);
    }
    private sealed class Store : IInventoryStore
    {
        public int Calls; public SourceInput? Input;
        public Task<ImportResult> SaveAsync(SourceInput input, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
        { Calls++; Input = input; return Task.FromResult(new ImportResult(Guid.NewGuid(), rows.Count, false, input.PendingHolds)); }
    }
    [OrveauxSourceFact]
    public async Task RealFolderFlowKeepsNewArrivalsPendingAndChecksDate()
    {
        var path = Environment.GetEnvironmentVariable("ORVEAUX_SOURCE"); if (string.IsNullOrEmpty(path)) return;
        var bytes = File.ReadAllBytes(path); var store = new Store(); var service = new FolderImportService(store);
        var result = await service.ImportAsync("取引先別/オルヴォー/202610新着＆現在庫.xlsx", bytes, null, default);
        Assert.Equal("Partial", result.Status); Assert.Equal(378, result.Products); Assert.Equal(30, result.Holds!.Count);
        Assert.Equal("2026-09-30", result.ObservedAt); Assert.Equal(bytes, store.Input!.Content);
        Assert.Equal("ORVEAUX", store.Input.SupplierCode);
        var rejected = await service.ImportAsync("オルヴォー/202610新着＆現在庫.xlsx", bytes, "2026-10-01", default);
        Assert.Equal("Held", rejected.Status); Assert.Equal(1, store.Calls);
    }
    [OrveauxDbFact]
    public async Task PartialSaveRepeatAndLegacyGuardWithRealDatabase()
    {
        var connection = Environment.GetEnvironmentVariable("ORVEAUX_TEST_CONNECTION")!;
        var config = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", config.Host); Assert.Equal(56037, config.Port); Assert.Equal("orveaux_isolated_test", config.Database);
        CatalogSearchDbContext Db() => new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
        await using (var setup = Db()) await setup.Database.EnsureCreatedAsync();
        var bytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("ORVEAUX_SOURCE")!);
        var rows = new OrveauxStockParser().Parse(bytes, default); var holds = OrveauxStockParser.NewArrivalHolds(bytes);
        var input = new SourceInput("original.xlsx", bytes, new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "ORVEAUX", "オルヴォー", "OrveauxStock/1") { PendingHolds = holds };
        var hash = OrveauxStockParser.ApprovedOriginalHash;
        await using (var db = Db())
        {
            var result = await new PostgresInventoryStore(db, true).SaveAsync(input, hash, rows, default);
            Assert.Equal(378, result.Products); Assert.Equal(30, result.Holds!.Count);
        }
        await using (var db = Db())
        {
            var result = await new PostgresInventoryStore(db, true).SaveAsync(input, hash, rows, default);
            Assert.Equal(378, result.Products); Assert.Equal(30, result.Holds!.Count); Assert.False(result.AlreadyImported);
            Assert.Equal(378, await db.SourceEvidence.CountAsync()); Assert.Equal(378, await db.InventoryItems.CountAsync());
            Assert.Equal("Partial", (await db.ExtractionJobs.SingleAsync()).Status);
            Assert.Null(await new FolderDuplicateLookup(db).FindCompleted("ORVEAUX", hash, default));
            Assert.Equal(bytes, (await db.SourceDocuments.SingleAsync()).Content);
            var product = await db.SupplierProducts.SingleAsync(p => p.ProductNameEn == "Tinto Velasco Vino de la Tierra de Castilla" && p.VintageRaw == "2023");
            Assert.Equal(126, product.CurrentInventoryQuantity);
            Assert.Equal(1800, product.CurrentPrice);
            var job = await db.ExtractionJobs.SingleAsync(); job.ParserVersion = "Legacy/1"; await db.SaveChangesAsync();
        }
        await using (var db = Db())
            await Assert.ThrowsAsync<FolderImportHeldException>(() => new PostgresInventoryStore(db, true).SaveAsync(input, hash, rows, default));
        await using (var db = Db()) Assert.Equal(378, await db.SupplierProducts.CountAsync());
    }
}
