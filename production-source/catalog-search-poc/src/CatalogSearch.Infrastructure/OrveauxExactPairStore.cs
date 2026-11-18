using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using Npgsql;
using NpgsqlTypes;

namespace CatalogSearch.Infrastructure;

public sealed record ExactPairLink(string PairKey, Guid ReservedProductId, int Version,
    string RequestHash, OrveauxPairResolution Evidence)
{
    public string State => "IdentityReservedOnly";
    public bool BusinessImported => false;
}
public sealed record ExactPairReceipt(Guid OperationId, ExactPairLink Link);
public sealed class ExactPairConflictException(string message) : Exception(message);

/// <summary>Append-only reservation of one approved pair's internal ID, not a catalog import.</summary>
public sealed class OrveauxExactPairStore(string connectionString)
{
    public const string PairKey = "ORVEAUX/source-17a7690c75c1a61f/2024/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExactPairLink?> Get(CancellationToken ct = default)
    {
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT \"Body\"::text FROM \"ExactSourcePairLinks\" WHERE \"PairKey\"=@key", c);
        cmd.Parameters.AddWithValue("key", PairKey);
        return await cmd.ExecuteScalarAsync(ct) is string body ? JsonSerializer.Deserialize<ExactPairLink>(body, Json) : null;
    }

    public async Task<ExactPairReceipt?> GetOperation(Guid operationId, CancellationToken ct = default)
    {
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT \"Body\"::text FROM \"ExactSourcePairOperations\" WHERE \"OperationId\"=@id", c);
        cmd.Parameters.AddWithValue("id", operationId);
        return await cmd.ExecuteScalarAsync(ct) is string body ? JsonSerializer.Deserialize<ExactPairReceipt>(body, Json) : null;
    }

    // On a lost response, call GetOperation; callers must not assume rollback or automatically resend.
    public async Task<ExactPairReceipt> Save(Guid operationId, int expectedVersion, OrveauxPairCandidate candidate,
        byte[] excelOriginal, byte[] pdfOriginal, byte[] answerExport, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty || expectedVersion != 0) throw new ExactPairConflictException("Only a new immutable reservation is supported.");
        var evidence = OrveauxAocPairPolicy.Resolve(candidate, excelOriginal, pdfOriginal, answerExport);
        if (evidence.Status != "ResolvedEvidenceOnly") throw new ExactPairConflictException(evidence.Reason);
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { expectedVersion, evidence }, Json)));
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", c, tx))
        { cmd.Parameters.AddWithValue("key", "exact-source-pair:" + PairKey); await cmd.ExecuteNonQueryAsync(ct); }
        await using (var cmd = new NpgsqlCommand("SELECT \"RequestHash\", \"Body\"::text FROM \"ExactSourcePairOperations\" WHERE \"OperationId\"=@id", c, tx))
        {
            cmd.Parameters.AddWithValue("id", operationId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) != requestHash) throw new ExactPairConflictException("Operation payload differs.");
                var receipt = JsonSerializer.Deserialize<ExactPairReceipt>(reader.GetString(1), Json)!;
                await reader.DisposeAsync(); await tx.CommitAsync(ct); return receipt;
            }
        }
        ExactPairLink? link = null;
        await using (var cmd = new NpgsqlCommand("SELECT \"RequestHash\", \"Body\"::text FROM \"ExactSourcePairLinks\" WHERE \"PairKey\"=@key FOR UPDATE", c, tx))
        {
            cmd.Parameters.AddWithValue("key", PairKey);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) != requestHash) throw new ExactPairConflictException("Pair reservation differs; no automatic merge.");
                link = JsonSerializer.Deserialize<ExactPairLink>(reader.GetString(1), Json)!;
            }
        }
        if (link is null)
        {
            link = new(PairKey, Guid.NewGuid(), 1, requestHash, evidence);
            await using var cmd = new NpgsqlCommand("INSERT INTO \"ExactSourcePairLinks\" (\"PairKey\",\"ReservedProductId\",\"Version\",\"RequestHash\",\"Body\") VALUES (@key,@id,1,@hash,@body)", c, tx);
            cmd.Parameters.AddWithValue("key", PairKey); cmd.Parameters.AddWithValue("id", link.ReservedProductId);
            cmd.Parameters.AddWithValue("hash", requestHash); cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(link, Json));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var result = new ExactPairReceipt(operationId, link);
        await using (var cmd = new NpgsqlCommand("INSERT INTO \"ExactSourcePairOperations\" (\"OperationId\",\"PairKey\",\"RequestHash\",\"Body\") VALUES (@id,@key,@hash,@body)", c, tx))
        {
            cmd.Parameters.AddWithValue("id", operationId); cmd.Parameters.AddWithValue("key", PairKey);
            cmd.Parameters.AddWithValue("hash", requestHash); cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, Json));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return result;
    }
}
