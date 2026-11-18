using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Npgsql;
using Xunit;

public sealed class OrveauxExactPairStoreTests
{
    public sealed class ExactPairDbFactAttribute : FactAttribute
    {
        public ExactPairDbFactAttribute()
        {
            if (new[] { "LIPS_AOC_CONNECTION", "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS", "LIPS_AOC_MIGRATION" }.Any(k => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k))))
                Skip = "Requires the dedicated isolated identity database and original files.";
        }
    }

    [ExactPairDbFact]
    public async Task FixedPairPersistsOneIdentityAndOperationReceiptsWithoutBusinessImports()
    {
        var connection = Environment.GetEnvironmentVariable("LIPS_AOC_CONNECTION")!;
        var builder = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("lips_aoc_identity_20261013", builder.Database);
        Assert.Equal(55993, builder.Port); Assert.Contains(builder.Host, new[] { "127.0.0.1", "localhost" });
        var paths = new[] { "LIPS_AOC_EXCEL", "LIPS_AOC_PDF", "LIPS_AOC_ANSWERS" }.Select(k => Environment.GetEnvironmentVariable(k)!).ToArray();
        var bytes = paths.Select(File.ReadAllBytes).ToArray();
        static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
        var before = bytes.Select(Hash).ToArray();
        var candidate = new OrveauxPairCandidate("ORVEAUX",
            new(before[0], "xlsx", "新着 /66", "Château Cesseras Cuvée Olric AOC Minervois", "シャトー・セスラ　キュヴェ・オルリック　ミネルヴォワ", 2024, null, 60),
            new(before[1], "pdf", "15/Château Cesseras Cuvée Orlic Minervois/2024/750/60本", "Château Cesseras Cuvée Orlic Minervois", "シャトー・セスラス　キュヴェ・オルリック　ミネルヴォワ", 2024, 750, 60),
            1980, 3300, 3630, 1880, @"12本価格\1,880");
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        var sql = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("LIPS_AOC_MIGRATION")!);
        for (var n = 0; n < 2; n++) { await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
        var store = new OrveauxExactPairStore(connection);
        var op1 = Guid.NewGuid(); var op2 = Guid.NewGuid();
        var receipts = await Task.WhenAll(
            store.Save(op1, 0, candidate, bytes[0], bytes[1], bytes[2]),
            store.Save(op2, 0, candidate, bytes[0], bytes[1], bytes[2]));
        Assert.Equal(receipts[0].Link.ReservedProductId, receipts[1].Link.ReservedProductId);
        var again = await store.Save(op1, 0, candidate, bytes[0], bytes[1], bytes[2]);
        Assert.Equal(receipts[0].Link.ReservedProductId, again.Link.ReservedProductId);
        var read = await store.Get(); var operation = await store.GetOperation(op1);
        Assert.NotNull(read); Assert.NotNull(operation);
        Assert.Equal(candidate, read.Evidence.Pair); Assert.Equal(read.ReservedProductId, operation.Link.ReservedProductId);
        Assert.Equal("IdentityReservedOnly", read.State); Assert.False(read.BusinessImported);
        await Assert.ThrowsAsync<ExactPairConflictException>(() => store.Save(op1, 1, candidate, bytes[0], bytes[1], bytes[2]));
        var different = candidate with { Excel = candidate.Excel with { Hash = before[0].ToUpperInvariant() } };
        await Assert.ThrowsAsync<ExactPairConflictException>(() => store.Save(op1, 0, different, bytes[0], bytes[1], bytes[2]));
        await Assert.ThrowsAsync<ExactPairConflictException>(() => store.Save(Guid.NewGuid(), 0, different, bytes[0], bytes[1], bytes[2]));
        await Assert.ThrowsAsync<ExactPairConflictException>(() => store.Save(Guid.NewGuid(), 0, candidate with { Pdf = candidate.Pdf with { Vintage = 2023 } }, bytes[0], bytes[1], bytes[2]));
        Assert.Null(await store.GetOperation(Guid.NewGuid()));
        await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM \"ExactSourcePairLinks\"", c)) Assert.Equal(1L, await cmd.ExecuteScalarAsync());
        await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_name IN ('Products','SupplierProducts','SourceDocuments','PriceHistories','InventoryItems')", c)) Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        Assert.Equal(before, paths.Select(p => Hash(File.ReadAllBytes(p))));
        if (Environment.GetEnvironmentVariable("LIPS_AOC_PROOF") is string proof)
            await File.WriteAllTextAsync(proof, JsonSerializer.Serialize(new { schema = "lips-aoc-identity-proof/v1", verifiedAt = DateTimeOffset.UtcNow,
                reservedProductId = read.ReservedProductId, pairKey = read.PairKey, version = read.Version, operationId = op1,
                twoSourcePositionsReadBack = true, concurrentSameIdentity = true, retrySameIdentity = true,
                differentPayloadRejected = true, otherVintageRejected = true, originalHashes = before, originalsUnchanged = true,
                businessTablesCreated = 0, state = read.State, searchImportConnected = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
