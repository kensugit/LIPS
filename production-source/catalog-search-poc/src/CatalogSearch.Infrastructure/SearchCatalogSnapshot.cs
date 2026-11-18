using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using CatalogSearch.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CatalogSearch.Infrastructure;

public sealed record SearchSupplierOption(string Code, string Name);
public sealed record SearchCatalogSnapshot(string Version, FreeTextSearchOptions Options, IReadOnlyList<SearchSupplierOption> Suppliers);

/// <summary>Actual search-data identity. No source content, credentials or row data is logged.</summary>
public sealed class CatalogSearchSnapshotProvider(CatalogSearchDbContext db)
{
    public static readonly IReadOnlyList<string> DependencyTables = Array.AsReadOnly(new[]
        { "SupplierProducts", "Suppliers", "SourceEvidence", "ProductSearchDocuments" });

    public async Task<SearchCatalogSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        var current = db.Database.CurrentTransaction;
        if (current is not null && current.GetDbTransaction().IsolationLevel is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
            throw new InvalidOperationException("Search snapshot requires RepeatableRead or Serializable caller transaction.");
        await using var owned = current is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        if (owned is not null) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        using var digest = new SearchCatalogDigest();
        foreach (var table in DependencyTables)
        {
            digest.BeginTable(table);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction;
            // Table names are a fixed allowlist, never request or stored input.
            command.CommandText = $"SELECT row_to_json(t)::text FROM public.\"{table}\" t ORDER BY t.\"Id\"";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await reader.ReadAsync(ct)) digest.AppendRecord(reader.GetString(0));
            digest.EndTable();
        }
        var countries = await db.SupplierProducts.Select(x => x.Country).Distinct().ToListAsync(ct);
        var regions = await db.SupplierProducts.Select(x => x.Region).Distinct().ToListAsync(ct);
        var types = await db.SupplierProducts.Select(x => x.ProductType).Distinct().ToListAsync(ct);
        var volumes = await db.SupplierProducts.Select(x => x.VolumeMl).Distinct().ToListAsync(ct);
        var vintages = await db.SupplierProducts.Select(x => x.VintageRaw).Distinct().ToListAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().Select(x => new SearchSupplierOption(x.Code, x.Name)).ToListAsync(ct);
        var options = CanonicalOptions(countries, regions, types, volumes, vintages, suppliers);
        var result = new SearchCatalogSnapshot(digest.Finish(), options, suppliers.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray());
        if (owned is not null) await owned.CommitAsync(ct);
        return result;
    }

    public static FreeTextSearchOptions CanonicalOptions(IEnumerable<string> countries, IEnumerable<string> regions,
        IEnumerable<string> types, IEnumerable<int> volumes, IEnumerable<string> vintages, IEnumerable<SearchSupplierOption> suppliers)
    {
        static string[] Text(IEnumerable<string> source, int limit)
        {
            var result = source.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (result.Length > limit || result.Any(x => x.Length > 200)) throw new InvalidOperationException("Search options exceed contract bounds.");
            return result;
        }
        var supplierArray = suppliers.ToArray();
        if (supplierArray.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)) || supplierArray.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != supplierArray.Length)
            throw new InvalidOperationException("Supplier codes must be unique and named.");
        // VolumeMl=0 is the existing unknown-capacity marker, not a selectable volume.
        // Keep those products and their source evidence; exclude only the marker from model options.
        var volumeArray = volumes.Where(x => x != 0).Distinct().Order().ToArray();
        if (volumeArray.Length > 100 || volumeArray.Any(x => x is < 1 or > 100000)) throw new InvalidOperationException("Volume options exceed contract bounds.");
        return new FreeTextSearchOptions(Text(countries, 250), Text(regions, 1000), Text(types, 100), volumeArray,
            Text(VintageNotation.Options(vintages), 500), Text(supplierArray.Select(x => x.Code), 1000));
    }
}

/// <summary>Versioned, typed, 64-bit-length framed SHA256; records arrive in database Id order.</summary>
public sealed class SearchCatalogDigest : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private int tableIndex;
    private bool inTable, finished;
    public SearchCatalogDigest() => Frame(0, "search-catalog/v1");
    private void Frame(byte kind, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Span<byte> prefix = stackalloc byte[9]; prefix[0] = kind;
        BinaryPrimitives.WriteInt64BigEndian(prefix[1..], bytes.LongLength);
        hash.AppendData(prefix); hash.AppendData(bytes);
    }
    public void BeginTable(string table)
    {
        if (finished || inTable || tableIndex >= CatalogSearchSnapshotProvider.DependencyTables.Count || table != CatalogSearchSnapshotProvider.DependencyTables[tableIndex])
            throw new InvalidOperationException("Catalog table order differs.");
        Frame(1, table); inTable = true;
    }
    public void AppendRecord(string json)
    {
        if (!inTable || finished) throw new InvalidOperationException("Catalog table is not open.");
        Frame(2, json);
    }
    public void EndTable()
    {
        if (!inTable || finished) throw new InvalidOperationException("Catalog table is not open.");
        Frame(3, ""); inTable = false; tableIndex++;
    }
    public string Finish()
    {
        if (finished || inTable || tableIndex != CatalogSearchSnapshotProvider.DependencyTables.Count) throw new InvalidOperationException("Catalog dependency set incomplete.");
        finished = true; return "search-catalog/v1:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public void Dispose() => hash.Dispose();
}
