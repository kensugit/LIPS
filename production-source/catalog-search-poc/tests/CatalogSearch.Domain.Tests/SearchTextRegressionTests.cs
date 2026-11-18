using System.Security.Cryptography;
using System.Text;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class SearchTextRegressionTests
{
    private static CatalogSearchDbContext Db() => new(new DbContextOptionsBuilder<CatalogSearchDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options);

    [PostgresFact]
    public async Task SingleImportAndSharedSearchFieldsRoundTripThroughRealPostgres()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var supplier = "REG" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var time = DateTimeOffset.UtcNow;
        byte[] Csv(string name, string price) => Encoding.UTF8.GetBytes(
            "SKU,ProductNameJa,ProductNameEn,ProducerNameJa,ProducerNameEn,Vintage,VolumeMl,Price,Tax,Inventory,Type,Country,Region\n" +
            $"BASE,{name},originalalpha,検証生産者,producertoken,2023,750,{price},,3,白,フランス,ロワール\n");
        var bytes = Csv("通常単票確認", "2300");
        var imported = await new ContentImportService(db).ImportAsync("sample.csv", bytes, time, supplier, supplier, default);
        var query = new CatalogQuery(db);
        foreach (var term in new[] { "通常単票確認", "originalalpha", "producertoken", "ロワール" })
            Assert.Contains(await query.SearchAsync(new(Text: term)), x => x.SourceDocumentId == imported.SourceDocumentId);
        Assert.Contains(await query.SearchAsync(new(Text: "originalalpha", VolumeMl: 750, VintageRaw: "2023", PriceMin: 2200, PriceMax: 2400)), x => x.SourceDocumentId == imported.SourceDocumentId);
        Assert.DoesNotContain(await query.SearchAsync(new(Text: "originalalpha", PriceMax: 2000)), x => x.SourceDocumentId == imported.SourceDocumentId);
        var repeated = await new ContentImportService(db).ImportAsync("renamed.csv", bytes, time, supplier, supplier, default);
        Assert.True(repeated.AlreadyImported);
        Assert.Equal(imported.SourceDocumentId, repeated.SourceDocumentId);

        // Both folder and non-folder callers share the search-document writer.
        foreach (var folder in new[] { false, true })
        {
            var code = supplier + (folder ? "F" : "S");
            var rows = new StandardCsvParser().Parse(bytes, default);
            var row = rows.Single() with { Description = "香り検証専用語 cedarunique", OriginalSupplierProductCode = "ORIGINAL-9917" };
            var saved = await new PostgresInventoryStore(db, folder).SaveAsync(new("fixture.csv", bytes, time, code, code, "fixture"), Convert.ToHexString(SHA256.HashData(bytes)), [row], default);
            foreach (var term in new[] { "香り検証専用語", "cedarunique", "ORIGINAL-9917", "originalalpha" })
                Assert.Contains(await query.SearchAsync(new(Text: term)), x => x.SourceDocumentId == saved.SourceDocumentId);
            // A later snapshot rebuilds SearchText, removing stale extension terms.
            var newer = bytes.Concat(new byte[] { 10 }).ToArray();
            var latest = await new PostgresInventoryStore(db, folder).SaveAsync(new("new.csv", newer, time.AddDays(1), code, code, "fixture"), Convert.ToHexString(SHA256.HashData(newer)), [row with { Description = "freshuniqueterm", OriginalSupplierProductCode = "REPLACED-9928" }], default);
            Assert.Contains(await query.SearchAsync(new(Text: "freshuniqueterm")), x => x.SourceDocumentId == latest.SourceDocumentId);
            Assert.DoesNotContain(await query.SearchAsync(new(Text: "cedarunique")), x => x.SourceDocumentId == latest.SourceDocumentId);
            Assert.DoesNotContain(await query.SearchAsync(new(Text: "ORIGINAL-9917")), x => x.SourceDocumentId == latest.SourceDocumentId);
        }
        // Ordinary single-file intake retains its existing older-snapshot policy.
        await new ContentImportService(db).ImportAsync("older.csv", Csv("古い単票", "1000"), time.AddDays(-1), supplier, supplier, default);
        Assert.Contains(await query.SearchAsync(new(Text: "通常単票確認", PriceMin: 2300)), x => x.SourceDocumentId == imported.SourceDocumentId);
        Assert.DoesNotContain(await query.SearchAsync(new(Text: "古い単票")), x => x.SourceDocumentId == imported.SourceDocumentId);
        Assert.Equal(bytes, (await db.SourceDocuments.SingleAsync(x => x.Id == imported.SourceDocumentId)).Content);
    }
}
