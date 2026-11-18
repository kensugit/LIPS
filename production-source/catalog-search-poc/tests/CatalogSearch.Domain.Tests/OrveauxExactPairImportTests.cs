using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

public sealed class OrveauxExactPairImportTests
{
    public sealed class ImportFactAttribute : FactAttribute
    { public ImportFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIPS_AOC_IMPORT_CONNECTION"))) Skip = "Dedicated isolated AOC import database required."; } }
    [ImportFact]
    public async Task ImportsOnlyConfirmedPairWithTypedIdentityAndSeparateSourceEvidence()
    {
        var connection = Environment.GetEnvironmentVariable("LIPS_AOC_IMPORT_CONNECTION")!;
        var config = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("lips_aoc_import_20261013", config.Database); Assert.Equal("127.0.0.1", config.Host); Assert.Equal(55993, config.Port);
        // The two additive columns use the explicit forward-only SQL below, not an EF-generated migration.
        CatalogSearchDbContext Db() => new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)).Options);
        await using (var setup = Db())
        {
            await setup.Database.MigrateAsync();
            foreach (var file in new[] { "20261013010000_ExactSourcePairLinks.sql", "20261013020000_ExactPairCatalogImport.sql" })
                await setup.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("LIPS_AOC_MIGRATIONS")!, file)));
        }
        var paths = new[] { "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS" }.Select(k => Environment.GetEnvironmentVariable(k)!).ToArray();
        var bytes = paths.Select(File.ReadAllBytes).ToArray();
        static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
        var hashes = bytes.Select(Hash).ToArray();
        var candidate = new OrveauxPairCandidate("ORVEAUX",
            new(hashes[0], "xlsx", "新着 /66", "Château Cesseras Cuvée Olric AOC Minervois", "シャトー・セスラ　キュヴェ・オルリック　ミネルヴォワ", 2024, null, 60),
            new(hashes[1], "pdf", "15/Château Cesseras Cuvée Orlic Minervois/2024/750/60本", "Château Cesseras Cuvée Orlic Minervois", "シャトー・セスラス　キュヴェ・オルリック　ミネルヴォワ", 2024, 750, 60),
            1980, 3300, 3630, 1880, @"12本価格\1,880");
        var date = new DateTimeOffset(2026,9,30,0,0,0,TimeSpan.Zero); var op = Guid.NewGuid();
        async Task<ExactPairImportReceipt> Import(Guid id, OrveauxPairCandidate? row = null)
        { await using var db = Db(); return await new OrveauxExactPairImportStore(db).Import(id, row ?? candidate, bytes[0], bytes[1], bytes[2], date); }
        bool wasImported; await using (var beforeImport = Db()) wasImported = await new OrveauxExactPairImportStore(beforeImport).Get() is not null;
        var receipts = await Task.WhenAll(Import(op), Import(Guid.NewGuid()));
        Assert.Equal(receipts[0].SupplierProductId, receipts[1].SupplierProductId); Assert.Equal(wasImported ? 0 : 1, receipts.Sum(r => r.AddedProducts));
        var retry = await Import(op); Assert.Equal(receipts[0].SupplierProductId, retry.SupplierProductId); Assert.True(retry.AlreadyImported); Assert.Equal(0, retry.AddedProducts);
        var repeated = await Import(Guid.NewGuid()); Assert.True(repeated.AlreadyImported); Assert.Equal(0, repeated.AddedProducts);
        await Assert.ThrowsAsync<ExactPairConflictException>(() => Import(Guid.NewGuid(), candidate with { Pdf = candidate.Pdf with { Vintage = 2023 } }));
        await using var check = Db();
        var read = await new OrveauxExactPairImportStore(check).GetOperation(op); Assert.Equal(receipts[0], read);
        var firstReceipt = (await new OrveauxExactPairImportStore(check).Get())!;
        Assert.Equal(1, firstReceipt.AddedProducts);
        Assert.Equal(0, (await Import(firstReceipt.OperationId)).AddedProducts);
        Assert.Equal(firstReceipt, await new OrveauxExactPairImportStore(check).GetOperation(firstReceipt.OperationId));
        var p = await check.SupplierProducts.SingleAsync(); Assert.Equal("InternalIdentity", p.CodeKind); Assert.Null(p.OriginalSupplierProductCode);
        Assert.Equal("internal:" + read!.InternalIdentityId.ToString("D"), p.SupplierProductCode); Assert.Null(p.ProductId);
        Assert.Contains(candidate.Excel.JapaneseName, p.SupplierProductName); Assert.Contains(candidate.Pdf.JapaneseName, p.SupplierProductName);
        Assert.Null(p.CurrentPrice); Assert.Equal(60, p.CurrentInventoryQuantity); Assert.Equal(750, p.VolumeMl); Assert.Equal(0, await check.Products.CountAsync());
        var evidence = await check.SourceEvidence.OrderBy(e => e.SheetName).ToListAsync(); Assert.Equal(2, evidence.Count);
        var rows = evidence.Select(e => JsonSerializer.Deserialize<SupplierInventoryRow>(e.ExtractedRowJson)!).ToArray();
        Assert.All(rows, r => { Assert.Null(r.ReferenceRetailPrice); Assert.All(r.PriceComponents, price => Assert.Equal("Unknown", price.Unit)); });
        Assert.Equal(4, rows.Single(r => r.SourceSheet == "新着 ").PriceComponents.Count);
        Assert.Equal(2, rows.Single(r => r.SourceSheet == "PDF").PriceComponents.Count);
        Assert.Equal(new[] { candidate.Excel.LatinName, candidate.Pdf.LatinName }.Order(), rows.Select(r => r.ProductNameEn).Order());
        Assert.All(await check.InventoryItems.ToListAsync(), i => Assert.Equal(60, i.Quantity));
        Assert.All(await check.PriceHistories.ToListAsync(), price => Assert.Null(price.Amount));
        Assert.All(await check.ExtractionJobs.ToListAsync(), job => Assert.Equal("Partial", job.Status));
        Assert.Single(await check.ProductSearchDocuments.ToListAsync());
        await using (var normal = Db())
            await Assert.ThrowsAsync<FolderImportHeldException>(() => new PostgresInventoryStore(normal, true).SaveAsync(
                new("original.xlsx", bytes[0], date, "ORVEAUX", "オルヴォー"), hashes[0].ToUpperInvariant(), [], default));
        // The default remains SupplierSku for old-style inserts; no fictitious supplier SKU is added for this pair.
        await using (var raw = new NpgsqlConnection(connection))
        {
            await raw.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT column_default FROM information_schema.columns WHERE table_name='SupplierProducts' AND column_name='CodeKind'",raw);
            Assert.Contains("SupplierSku", (string)(await cmd.ExecuteScalarAsync())!);
        }
        Assert.Equal("SupplierSku", new SupplierProduct { SupplierId = Guid.NewGuid(), SupplierProductCode = "fixture", SupplierProductName = "fixture" }.CodeKind);
        Assert.Equal(hashes, paths.Select(path => Hash(File.ReadAllBytes(path))));
        if (Environment.GetEnvironmentVariable("LIPS_AOC_IMPORT_PROOF") is string proof)
            await File.WriteAllTextAsync(proof, JsonSerializer.Serialize(new { receipt = read, productCount = 1, canonicalProductCount = 0, evidenceCount = 2,
                originalHashesUnchanged = true, previouslyImported = wasImported, concurrentAdded = receipts.Sum(r => r.AddedProducts), operationReceiptUnchanged = true, repeatedAdded = 0,
                currentPrice = (decimal?)null, inventoryQuantity = 60, wholeSourceImportGuard = true, supplierSkuDefaultPreserved = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
