using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Length < 2 || args.Length > 3 || (args.Length == 3 && args[2] is not ("--persist" or "--verify")))
{
    Console.Error.WriteLine("Usage: CatalogPdfBridge bundle.json original.pdf [--persist|--verify]. CATALOG_CONNECTION required for database operations.");
    return 2;
}
try
{
    var bundle = JsonSerializer.Deserialize<ImportBundle>(await File.ReadAllTextAsync(args[0])) ?? throw new InvalidDataException("Missing bundle");
    var bytes = await File.ReadAllBytesAsync(args[1]);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    if (bundle.SchemaVersion != 1 || bytes.Length is 0 or > 20_000_000 || !bytes.AsSpan().StartsWith("%PDF-"u8) ||
        !hash.Equals(bundle.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PDF/hash/schema mismatch");
    if (!Regex.IsMatch(bundle.SupplierCode, "^[A-Z0-9_]{1,50}$") || string.IsNullOrWhiteSpace(bundle.SupplierName) || bundle.SupplierName.Length > 100 ||
        bundle.SourceFileName != Path.GetFileName(bundle.SourceFileName) || !bundle.SourceFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
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
            if (product.CurrentObservedAt == observed && (product.CurrentSourceDocumentId != source.Id || product.SupplierProductName != row.ProductNameJa ||
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
