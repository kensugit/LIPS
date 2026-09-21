using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Length < 2 || args.Length > 3 || (args.Length == 3 && args[2] is not ("--persist" or "--verify" or "--check-schema")))
{
    Console.Error.WriteLine("Usage: CatalogPdfBridge bundle.json original.pdf [--persist|--verify|--check-schema]. CATALOG_CONNECTION required for database operations.");
    return 2;
}
try
{
    var bundle = JsonSerializer.Deserialize<ImportBundle>(await File.ReadAllTextAsync(args[0])) ?? throw new InvalidDataException("Missing bundle");
    var bytes = await File.ReadAllBytesAsync(args[1]);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    var xlsxValid = false;
    if (bundle.SourceFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && bytes.AsSpan().StartsWith("PK\x03\x04"u8))
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        xlsxValid = archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry("xl/workbook.xml") is not null &&
            archive.GetEntry("xl/vbaProject.bin") is null;
    }
    var sourceFormatValid = (bundle.SourceFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && bytes.AsSpan().StartsWith("%PDF-"u8)) ||
        (bundle.SourceFileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) && bytes.AsSpan().StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })) || xlsxValid;
    if (bundle.SchemaVersion != 1 || bytes.Length is 0 or > 20_000_000 || !sourceFormatValid ||
        !hash.Equals(bundle.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source format/hash/schema mismatch");
    if (!Regex.IsMatch(bundle.SupplierCode, "^[A-Z0-9_]{1,50}$") || string.IsNullOrWhiteSpace(bundle.SupplierName) || bundle.SupplierName.Length > 100 ||
        bundle.SourceFileName != Path.GetFileName(bundle.SourceFileName) ||
        string.IsNullOrWhiteSpace(bundle.ParserVersion)) throw new InvalidDataException("Invalid source identity");
    var observed = new DateTimeOffset(DateTime.ParseExact(bundle.ObservedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero);
    var rows = bundle.Rows;
    if (rows.Count is 0 or > 10000 || rows.Select(x => x.SupplierProductCode).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
        rows.Any(x => string.IsNullOrWhiteSpace(x.SupplierProductCode) || string.IsNullOrWhiteSpace(x.ProductNameJa) || x.VolumeMl < 0 || x.CaseSize < 0 ||
            x.ReferenceRetailPrice < 0 || x.Inventory.Quantity < 0 || !Enum.IsDefined(x.Inventory.Status) || x.SourceRow <= 0 || string.IsNullOrWhiteSpace(x.SourceSheet)))
        throw new InvalidDataException("Invalid product rows");
    foreach (var row in rows) { using var raw = JsonDocument.Parse(row.RawCellsJson); if (raw.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing raw evidence"); }
    if (args.Length == 2)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { validated = true, products = rows.Count, inventory = rows.GroupBy(x => x.Inventory.Status).ToDictionary(x => x.Key.ToString(), x => x.Count()), numericInventory = rows.Count(x => x.Inventory.Quantity.HasValue), sourceHash = hash }));
        return 0;
    }
    var connection = Environment.GetEnvironmentVariable("CATALOG_CONNECTION") ?? throw new InvalidDataException("CATALOG_CONNECTION missing");
    var settings = new NpgsqlConnectionStringBuilder(connection);
    if (settings.Host is not ("127.0.0.1" or "localhost") || settings.Database is null || !settings.Database.StartsWith("catalog_", StringComparison.Ordinal))
        throw new InvalidDataException("Only local independent catalog databases are supported");
    await using var db = new CatalogSearchDbContext(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
    var expected = db.Database.GetMigrations().ToArray();
    var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    if (expected.Length == 0 || expected.Except(applied).Any() || applied.Except(expected).Any()) throw new InvalidDataException("Database schema mismatch; migration is not performed by this tool");
    if (args.Contains("--check-schema"))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { schemaReady = true, migrations = applied.Length, products = rows.Count }));
        return 0;
    }
    if (args.Contains("--verify"))
    {
        var supplier = await db.Suppliers.SingleAsync(x => x.Code == bundle.SupplierCode);
        var source = await db.SourceDocuments.SingleAsync(x => x.SupplierId == supplier.Id && x.FileHash == hash);
        if (!source.Content.SequenceEqual(bytes) || source.ObservedAt != observed || source.FileName != bundle.SourceFileName) throw new InvalidDataException("Stored source mismatch");
        var products = await db.SupplierProducts.Where(x => x.SupplierId == supplier.Id).ToDictionaryAsync(x => x.SupplierProductCode);
        var evidence = await db.SourceEvidence.Where(x => x.SourceDocumentId == source.Id).ToDictionaryAsync(x => x.SupplierProductId);
        var prices = await db.PriceHistories.Where(x => x.SourceDocumentId == source.Id).ToDictionaryAsync(x => x.SupplierProductId);
        var snapshot = await db.InventorySnapshots.SingleAsync(x => x.SourceDocumentId == source.Id);
        var inventories = await db.InventoryItems.Where(x => x.InventorySnapshotId == snapshot.Id).ToDictionaryAsync(x => x.SupplierProductId);
        if (evidence.Count != rows.Count || prices.Count != rows.Count || inventories.Count != rows.Count) throw new InvalidDataException("Stored row count mismatch");
        foreach (var row in rows)
        {
            var product = products[row.SupplierProductCode];
            var item = evidence[product.Id];
            var storedRow = JsonSerializer.Deserialize<SupplierInventoryRow>(item.ExtractedRowJson)!;
            if (item.SheetName != row.SourceSheet || item.RowNumber != row.SourceRow ||
                JsonSerializer.Serialize(storedRow) != JsonSerializer.Serialize(row) || !JsonElement.DeepEquals(JsonDocument.Parse(item.RawCellsJson).RootElement, JsonDocument.Parse(row.RawCellsJson).RootElement) ||
                prices[product.Id].Amount != row.ReferenceRetailPrice || prices[product.Id].TaxIncluded != row.TaxIncluded ||
                inventories[product.Id].Quantity != row.Inventory.Quantity || inventories[product.Id].Status != row.Inventory.Status ||
                inventories[product.Id].RawValue != row.Inventory.RawValue) throw new InvalidDataException("Stored evidence/history mismatch");
            if (product.CurrentSourceDocumentId == source.Id && (product.CurrentObservedAt != observed || product.SupplierProductName != row.ProductNameJa ||
                product.ProductNameEn != row.ProductNameEn || product.ProducerNameJa != row.ProducerNameJa || product.ProducerNameEn != row.ProducerNameEn ||
                product.VolumeMl != row.VolumeMl || product.VintageRaw != row.VintageRaw || product.CurrentPrice != row.ReferenceRetailPrice ||
                product.TaxIncluded != row.TaxIncluded || product.CurrentInventoryQuantity != row.Inventory.Quantity || product.CurrentInventoryStatus != row.Inventory.Status))
                throw new InvalidDataException("Current product mismatch");
        }
        var productIds = products.Values.Select(x => x.Id).ToArray();
        var indexed = await db.ProductSearchDocuments.CountAsync(x => x.EntityType == SearchEntityType.SupplierProduct && productIds.Contains(x.SourceRecordId));
        if (indexed != products.Count) throw new InvalidDataException("Search index count mismatch");
        Console.WriteLine(JsonSerializer.Serialize(new { verified = true, products = rows.Count, evidence = evidence.Count, prices = prices.Count, inventory = inventories.Count, indexed, sourceHash = hash }));
        return 0;
    }
    var result = await new PostgresInventoryStore(db).SaveAsync(new(bundle.SourceFileName, bytes, observed, bundle.SupplierCode, bundle.SupplierName, bundle.ParserVersion), hash, rows, default);
    // Some compatible production runtimes omit supplier/description terms from the index.
    // Repair only products whose current source is this exact verified document; replay is idempotent.
    db.ChangeTracker.Clear();
    await using var indexTransaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)");
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({bundle.SupplierCode}, 0))");
    var importedSupplier = await db.Suppliers.SingleAsync(x => x.Code == bundle.SupplierCode);
    var importedSource = await db.SourceDocuments.SingleAsync(x => x.SupplierId == importedSupplier.Id && x.FileHash == hash);
    var currentProducts = await db.SupplierProducts.Where(x => x.SupplierId == importedSupplier.Id && x.CurrentSourceDocumentId == importedSource.Id).ToListAsync();
    var currentIds = currentProducts.Select(x => x.Id).ToArray();
    var searchDocuments = await db.ProductSearchDocuments.Where(x => x.EntityType == SearchEntityType.SupplierProduct && currentIds.Contains(x.SourceRecordId)).ToDictionaryAsync(x => x.SourceRecordId);
    var inputRows = rows.ToDictionary(x => x.SupplierProductCode);
    using var inputJson = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    var descriptions = inputJson.RootElement.GetProperty("Rows").EnumerateArray().ToDictionary(
        x => x.GetProperty("SupplierProductCode").GetString()!,
        x => x.TryGetProperty("Description", out var description) ? description.GetString() ?? "" : "");
    foreach (var product in currentProducts)
    {
        var row = inputRows[product.SupplierProductCode];
        var document = searchDocuments[product.Id];
        var searchText = string.Join(" ", bundle.SupplierName, row.ProductNameJa, row.ProductNameEn, row.ProducerNameJa,
            row.ProducerNameEn, row.VintageRaw, row.ProductType, row.Country, row.Region, row.Grapes,
            row.AppellationJa, row.AppellationEn, row.OrganicCategory, descriptions[row.SupplierProductCode], row.SupplierProductCode);
        if (document.SearchText == searchText) continue;
        document.SearchText = searchText;
        document.UpdatedAt = DateTimeOffset.UtcNow;
    }
    await db.SaveChangesAsync();
    await indexTransaction.CommitAsync();
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error is InvalidDataException ? $"Catalog PDF import failed: {error.Message}" : $"Catalog PDF import failed: {error.GetType().Name}. Check source, bundle, runtime and database schema.");
    return 1;
}

internal sealed record ImportBundle(int SchemaVersion, string SourceFileName, string SourceSha256, string ObservedAt,
    string SupplierCode, string SupplierName, string ParserVersion, List<SupplierInventoryRow> Rows);
