using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using Npgsql;

namespace CatalogSearch.SourceExportCli;

public sealed record Selector(Guid ProductId, Guid SourceId, string SupplierCode, string Sku);
public sealed record SnapshotRow(Guid ProductId, Guid ProductSupplierId, Guid SupplierId,
    Guid DocumentSupplierId, Guid CurrentSourceId, Guid SourceId, Guid EvidenceId,
    string SupplierCode, string Sku, string Producer, string Product, string Vintage,
    int VolumeMl, string Variant, string ExtractedJson);
public sealed record ExportValue(Selector Selector, Guid SupplierId, Guid EvidenceId,
    ImporterProductIdentity Identity, string ExtractedJson, string OriginalFingerprint,
    int ExtractedBytes, string ExtractedSha256);
public interface IReadOnlySourceSnapshot
{
    Task<IReadOnlyList<SnapshotRow>> ReadAsync(Selector selector, CancellationToken token);
}

public static class SourceExport
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    public static void Validate(Selector s)
    {
        if (s.ProductId == Guid.Empty || s.SourceId == Guid.Empty ||
            !ValidText(s.SupplierCode, 100) || !ValidText(s.Sku, 100))
            throw new InvalidDataException("SELECTOR_REJECTED");
    }
    static bool ValidText(string? x, int limit) => !string.IsNullOrWhiteSpace(x) &&
        x.Length <= limit && x == x.Trim() && !x.Any(char.IsControl);
    public static ExportValue Bind(Selector s, IReadOnlyList<SnapshotRow> rows)
    {
        Validate(s);
        if (rows.Count != 1) throw new InvalidDataException("SOURCE_NOT_UNIQUE");
        var r = rows[0];
        if (r.ProductId != s.ProductId || r.CurrentSourceId != s.SourceId || r.SourceId != s.SourceId ||
            r.SupplierCode != s.SupplierCode || r.Sku != s.Sku || r.EvidenceId == Guid.Empty ||
            r.SupplierId == Guid.Empty || r.ProductSupplierId != r.SupplierId || r.DocumentSupplierId != r.SupplierId)
            throw new InvalidDataException("SOURCE_BINDING_REJECTED");
        if (!ValidText(r.Producer,200) || !ValidText(r.Product,200) || !ValidText(r.Vintage,32) ||
            !ValidText(r.Variant,100) || r.VolumeMl is <=0 or >100000 ||
            new[]{"unknown","不明","?","*"}.Contains(r.Vintage,StringComparer.OrdinalIgnoreCase) ||
            new[]{"unknown","不明","?","*"}.Contains(r.Variant,StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("IDENTITY_REJECTED");
        var bytes = Utf8.GetBytes(r.ExtractedJson);
        if (bytes.Length is 0 or >262144) throw new InvalidDataException("SOURCE_SIZE_REJECTED");
        var identity = new ImporterProductIdentity(s.SupplierCode,r.Sku,r.Producer,r.Product,r.Vintage,r.VolumeMl,r.Variant);
        if (!SourceIdentityBinding.Verify(identity,r.ExtractedJson)) throw new InvalidDataException("SOURCE_IDENTITY_REJECTED");
        // Preserve the DB-returned string. Parsing is validation only, never normalization.
        return new(s,r.SupplierId,r.EvidenceId,identity,r.ExtractedJson,
            SourceFingerprint.Create(r.SourceId,r.ExtractedJson),bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
    public static async Task<ExportValue> CaptureAsync(Selector s, IReadOnlySourceSnapshot source, CancellationToken ct)
    {
        Validate(s);
        return Bind(s,await source.ReadAsync(s,ct));
    }
    public static string SaveNew(string directory, ExportValue value)
    {
        return CatalogSearch.Infrastructure.ProtectedExportFiles.CreateNew(Path.Combine(directory,"source-export.protected.json"),JsonSerializer.SerializeToUtf8Bytes(value),524288);
    }
}

// The caller supplies an already-authorized connection factory. This module never obtains credentials.
public sealed class PostgresSourceSnapshot(Func<CancellationToken,Task<NpgsqlConnection>> open) : IReadOnlySourceSnapshot
{
    public async Task<IReadOnlyList<SnapshotRow>> ReadAsync(Selector s,CancellationToken ct)
    {
        SourceExport.Validate(s);
        await using var c=await open(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using(var mode=new NpgsqlCommand("SET TRANSACTION READ ONLY",c,tx){CommandTimeout=20})
            await mode.ExecuteNonQueryAsync(ct);
        const string sql="""
            SELECT p."Id", p."SupplierId", u."Id", d."SupplierId", p."CurrentSourceDocumentId",
                   d."Id", e."Id", u."Code", p."SupplierProductCode", p."ProducerNameJa",
                   p."SupplierProductName", p."VintageRaw", p."VolumeMl", p."ProductType",
                   CASE WHEN octet_length(e."ExtractedRowJson"::text)<=262144 THEN e."ExtractedRowJson"::text ELSE NULL END
            FROM "SupplierProducts" p JOIN "Suppliers" u ON p."SupplierId"=u."Id"
            JOIN "SourceDocuments" d ON p."CurrentSourceDocumentId"=d."Id"
            JOIN "SourceEvidence" e ON e."SupplierProductId"=p."Id" AND e."SourceDocumentId"=d."Id"
            WHERE p."Id"=@product AND d."Id"=@source AND u."Code"=@supplier AND p."SupplierProductCode"=@sku
            LIMIT 2
            """;
        await using var cmd=new NpgsqlCommand(sql,c,tx){CommandTimeout=20};
        cmd.Parameters.AddWithValue("product",s.ProductId);cmd.Parameters.AddWithValue("source",s.SourceId);
        cmd.Parameters.AddWithValue("supplier",s.SupplierCode);cmd.Parameters.AddWithValue("sku",s.Sku);
        var rows=new List<SnapshotRow>();
        await using(var r=await cmd.ExecuteReaderAsync(ct)) while(await r.ReadAsync(ct))
        {
            if(r.IsDBNull(14)) throw new InvalidDataException("SOURCE_SIZE_REJECTED");
            rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetGuid(4),r.GetGuid(5),r.GetGuid(6),
                r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetInt32(12),r.GetString(13),r.GetString(14)));
        }
        // Validate inside the same transaction; no write/commit or source mutation is performed.
        _=SourceExport.Bind(s,rows);
        await tx.RollbackAsync(ct);
        return rows;
    }
}
