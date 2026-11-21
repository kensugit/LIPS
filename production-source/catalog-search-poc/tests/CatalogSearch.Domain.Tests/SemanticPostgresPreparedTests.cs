using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace CatalogSearch.Domain.Tests;

// Preparation only: separate approval plus an already provisioned synthetic database is required.
public sealed class SemanticIsolatedPostgresFactAttribute : FactAttribute
{
    public SemanticIsolatedPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIPS_SEMANTIC_PG_APPROVAL") != "synthetic-only-20261120-r1")
            Skip = "Dedicated synthetic PostgreSQL execution is not authorized/configured.";
    }
}
public sealed class SemanticPostgresPreparedTests
{
    [SemanticIsolatedPostgresFact]
    public async Task RegionsAndPriceStockMajorityBeforeLimitAndEvidenceBinding()
    {
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LIPS_SEMANTIC_PG_CONNECTION") ?? "");
        Assert.Equal("127.0.0.1", cs.Host); Assert.Equal(54339, cs.Port);
        Assert.Equal("lips_synthetic", cs.Username); Assert.Equal("public", cs.SearchPath);
        Assert.Equal("lips_semantic_synthetic_20261120_r1", cs.Database);
        Assert.False(cs.Multiplexing);
        cs.Timeout = 5; cs.CommandTimeout = 15; cs.ApplicationName = "lips-semantic-synthetic-r1";
        await using var db = new CatalogSearchDbContext(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(cs.ConnectionString).Options);
        await using var tx = await db.Database.BeginTransactionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var identity = new NpgsqlCommand("SELECT current_database(), current_user, current_schema()", connection, (NpgsqlTransaction)tx.GetDbTransaction()))
        { await using var dbIdentity = await identity.ExecuteReaderAsync(); Assert.True(await dbIdentity.ReadAsync()); Assert.Equal(cs.Database, dbIdentity.GetString(0)); Assert.Equal("lips_synthetic", dbIdentity.GetString(1)); Assert.Equal("public", dbIdentity.GetString(2)); }
        // No create, migrate, reset, service start or cleanup of preexisting business data.
        Assert.False(await db.SupplierProducts.AnyAsync()); Assert.False(await db.Suppliers.AnyAsync());
        Assert.False(await db.SourceDocuments.AnyAsync()); Assert.False(await db.SourceEvidence.AnyAsync());
        var supplier = new Supplier { Code = "SYNTHETIC", Name = "Synthetic only" };
        var source = new SourceDocument { SupplierId = supplier.Id, FileName = "synthetic.json", FileHash = new string('a', 64), Content = [123,125], ObservedAt = DateTimeOffset.UtcNow, ImportedAt = DateTimeOffset.UtcNow };
        db.AddRange(supplier, source);
        var entries = new List<(SupplierProduct Product, string Json)>();
        void Add(string sku, string region, string grapes, decimal? price = 9000, InventoryStatus stock = InventoryStatus.InStock, bool tax = false, string type = "赤", string country = "フランス")
        {
            var p = new SupplierProduct { SupplierId = supplier.Id, SupplierProductCode = sku, SupplierProductName = sku, ProducerNameJa = "Synthetic producer", Country = country, Region = region, ProductType = type, VintageRaw = "2020", VolumeMl = 750, CurrentSourceDocumentId = source.Id, CurrentPrice = price, TaxIncluded = tax, CurrentInventoryStatus = stock };
            var json = JsonSerializer.Serialize(new { SupplierProductCode = sku, ProducerNameJa = p.ProducerNameJa, ProductNameJa = sku, VintageRaw = "2020", VolumeMl = 750, ProductType = type, Grapes = grapes });
            db.Add(p); db.Add(new SourceEvidence { SupplierProductId = p.Id, SourceDocumentId = source.Id, SheetName = "synthetic", RowNumber = entries.Count + 1, RawCellsJson = "{}", ExtractedRowJson = json }); entries.Add((p, json));
        }
        Add("00-minority", "R1", "Merlot 40%"); Add("01-half", "R2", "Merlot 50%");
        Add("02-list", "R1", "Merlot"); Add("03-majority", "R1", "Merlot 80%");
        Add("04-low", "R2", "Merlot 80%", stock: InventoryStatus.LowStock);
        Add("05-price", "R1", "Merlot 80%", 10001); Add("06-stock", "R2", "Merlot 80%", stock: InventoryStatus.OutOfStock);
        Add("07-tax", "R1", "Merlot 80%", tax: true); Add("08-type", "R1", "Merlot 80%", type: "白");
        Add("09-region", "R3", "Merlot 80%"); Add("10-country", "R1", "Merlot 80%", country: "イタリア");
        Add("11-country-alias", "R2", "Merlot 80%", country: "France");
        await db.SaveChangesAsync();
        SemanticSearchPlan Plan(int limit, string mode = "predominant") => new(new(Country: "フランス", ProductType: "赤", PriceMax: 10000, AvailableOnly: true, Limit: limit), "SYNTHETIC", [], ["R1", "R2"], new("メルロー", mode), [], false);
        var query = new CatalogQuery(db);
        var limited = await query.SearchSemanticAsync(Plan(1)); Assert.Equal("03-majority", Assert.Single(limited.Items).Sku);
        var all = await query.SearchSemanticAsync(Plan(20)); Assert.Equal(new[] { "03-majority", "04-low" }, all.Items.Select(x => x.Sku));
        var expanded = Plan(20) with { Regions = new[] { "R1", "R2" }.Concat(Enumerable.Range(1, 23).Select(n => "unused-" + n)).ToArray() };
        Assert.Equal(new[] { "03-majority", "04-low" }, (await query.SearchSemanticAsync(expanded)).Items.Select(x => x.Sku));
        await Assert.ThrowsAsync<ArgumentException>(() => query.SearchSemanticAsync(Plan(20) with { Regions = Enumerable.Range(1,129).Select(n => "unused-" + n).ToArray() }));
        var countries = Plan(20) with { CountryAliases = ["France", "フランス"] };
        Assert.Equal(new[] { "03-majority", "04-low", "11-country-alias" }, (await query.SearchSemanticAsync(countries)).Items.Select(x => x.Sku));
        await Assert.ThrowsAsync<ArgumentException>(() => query.SearchSemanticAsync(Plan(20) with { CountryAliases = ["フランス", "イタリア"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => query.SearchSemanticAsync(Plan(20) with { CountryAliases = ["France"] }));
        var contains = await query.SearchSemanticAsync(Plan(20, "contains")); Assert.Contains(contains.Items, p => p.Sku == "02-list");
        var originalEntry = entries.Single(x => x.Product.SupplierProductCode == "02-list");
        // jsonb changes textual representation. Bind the same persisted bytes read by CatalogQuery,
        // while independently proving the database did not change the JSON data.
        var persistedJson = await db.SourceEvidence.AsNoTracking()
            .Where(e => e.SourceDocumentId == source.Id && e.SupplierProductId == originalEntry.Product.Id)
            .Select(e => e.ExtractedRowJson).SingleAsync();
        AssertEquivalentSource(originalEntry.Json, persistedJson);
        var reader = new BoundSyntheticReader(source.Id, (originalEntry.Product, persistedJson));
        var enriched = await new CatalogQuery(db, reader, "synthetic/1").SearchSemanticAsync(Plan(1));
        var row = Assert.Single(enriched.Items); Assert.Equal("02-list", row.Sku); Assert.NotNull(row.ImporterEvidence);
        Assert.Equal(9000m, row.ReferenceRetailPrice); Assert.Equal(source.Id, row.SourceDocumentId); Assert.True(reader.IdentityMatched);
        reader.Conflict = true;
        var conflicted = await new CatalogQuery(db, reader, "synthetic/1").SearchSemanticAsync(Plan(20));
        Assert.DoesNotContain(conflicted.Items, p => p.Sku == "02-list"); Assert.Equal(1, conflicted.EvidenceConflictCount);
        await tx.RollbackAsync();
        Assert.False(await db.SupplierProducts.AsNoTracking().AnyAsync()); Assert.False(await db.Suppliers.AsNoTracking().AnyAsync());
        Assert.False(await db.SourceDocuments.AsNoTracking().AnyAsync()); Assert.False(await db.SourceEvidence.AsNoTracking().AnyAsync());
    }
    static void AssertEquivalentSource(string original, string persisted)
    {
        using var a = JsonDocument.Parse(original); using var b = JsonDocument.Parse(persisted);
        Assert.True(JsonElement.DeepEquals(a.RootElement, b.RootElement));
    }
    [Fact]
    public void FingerprintUsesExactPersistedTextNotEquivalentPreInsertJson()
    {
        const string submitted = "{\"Grapes\":\"Merlot 80%\",\"VolumeMl\":750}";
        const string persisted = "{\"Grapes\": \"Merlot 80%\", \"VolumeMl\": 750}";
        var source = Guid.Parse("f04d8935-5d23-44c6-b99b-1fd673549beb");
        AssertEquivalentSource(submitted, persisted);
        Assert.NotEqual(SourceFingerprint.Create(source, submitted), SourceFingerprint.Create(source, persisted));
        Assert.Equal(SourceFingerprint.Create(source, persisted), SourceFingerprint.Create(source, persisted));
        Assert.ThrowsAny<Exception>(() => AssertEquivalentSource(submitted, persisted.Replace("80%", "40%", StringComparison.Ordinal)));
        Assert.NotEqual(SourceFingerprint.Create(source, persisted), SourceFingerprint.Create(Guid.Empty, persisted));
    }
    sealed class BoundSyntheticReader(Guid source, (SupplierProduct Product, string Json) entry) : IImporterEnrichmentReader
    {
        public bool IdentityMatched; public bool Conflict;
        public ImporterEnrichmentReadResult ReadVerified(ImporterEnrichmentLookup l) => ReadVerifiedForProduct(l.Identity, l.OriginalFingerprint, l.ExtractorVersion, l.NowUtc);
        public ImporterEnrichmentReadResult ReadVerified(ImporterProductIdentity i, string f, string v, DateTimeOffset n) => ReadVerifiedForProduct(i,f,v,n);
        public ImporterEnrichmentReadResult ReadVerifiedForProduct(ImporterProductIdentity i, string f, string v, DateTimeOffset n)
        {
            if (i.Sku != entry.Product.SupplierProductCode) return new("missing", null, null, null);
            Assert.True(SourceIdentityBinding.Verify(i, entry.Json)); Assert.Equal(SourceFingerprint.Create(source, entry.Json), f); Assert.Equal("synthetic/1", v); IdentityMatched = true;
            var content = new ImporterEnrichmentContent("lips-importer-enrichment-content/v1", i, source.ToString("D"), f, "https://importer.example/p", "https://importer.example/p", n, new string('b',64), null,null,new("deterministic","synthetic/1",v,null,null,null), [new("Merlot","Merlot 80%")]);
            return new(Conflict ? "conflict" : "verified", 1, new string('c',64), content);
        }
    }
}
