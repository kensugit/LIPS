using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using Npgsql;
using NpgsqlTypes;

namespace CatalogSearch.Infrastructure;

public sealed class IntakeRuleConflictException : Exception
{ public IntakeRuleConflictException(string message) : base(message) { } }
public sealed record IntakeRuleSaveRequest(Guid OperationId, int ExpectedVersion, IntakeReusableRule Rule);
public sealed record IntakeRuleReceipt(Guid OperationId, int ExpectedVersion, string RequestHash, IntakeReusableRule Rule);
public sealed record IntakeRuleReviewGuard(Guid ReviewId, int Version, string Hash);

// Append-only rule history, separate from source and business tables. No import side effects.
// If Save loses its response, GET the operation before deciding whether to retry the exact request.
public sealed class IntakeReusableRuleStore(string connectionString)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    { var c=new NpgsqlConnection(connectionString); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }
    private static IntakeReusableRule Decode(string body) => JsonSerializer.Deserialize<IntakeReusableRule>(body,Json)!;

    public async Task<IntakeReusableRule?> Get(Guid id, CancellationToken ct = default)
    {
        await using var c=await Open(ct);
        await using var cmd=new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReusableRuleVersions\" WHERE \"RuleId\"=@id ORDER BY \"Version\" DESC LIMIT 1",c);
        cmd.Parameters.AddWithValue("id",id);
        return await cmd.ExecuteScalarAsync(ct) is string body ? Decode(body) : null;
    }
    public async Task<IReadOnlyList<IntakeReusableRule>> History(Guid id, CancellationToken ct = default)
    {
        await using var c=await Open(ct);
        await using var cmd=new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReusableRuleVersions\" WHERE \"RuleId\"=@id ORDER BY \"Version\"",c);
        cmd.Parameters.AddWithValue("id",id); await using var reader=await cmd.ExecuteReaderAsync(ct);
        var result=new List<IntakeReusableRule>(); while(await reader.ReadAsync(ct))result.Add(Decode(reader.GetString(0))); return result;
    }
    // Includes revoked latest versions: callers must not hide revocation and revive old rules.
    public async Task<IReadOnlyList<IntakeReusableRule>> List(CancellationToken ct = default)
    {
        await using var c=await Open(ct);
        await using var cmd=new NpgsqlCommand("SELECT DISTINCT ON (\"RuleId\") \"Body\"::text FROM \"IntakeReusableRuleVersions\" ORDER BY \"RuleId\",\"Version\" DESC",c);
        await using var reader=await cmd.ExecuteReaderAsync(ct); var result=new List<IntakeReusableRule>();
        while(await reader.ReadAsync(ct))result.Add(Decode(reader.GetString(0)));return result;
    }
    private static async Task<IntakeRuleReceipt?> ReadOperation(NpgsqlConnection c, NpgsqlTransaction? tx, Guid id, CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("SELECT \"ExpectedVersion\",\"RequestHash\",\"Body\"::text FROM \"IntakeReusableRuleVersions\" WHERE \"OperationId\"=@id",c,tx);
        cmd.Parameters.AddWithValue("id",id);await using var reader=await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)?new(id,reader.GetInt32(0),reader.GetString(1),Decode(reader.GetString(2))):null;
    }
    public async Task<IntakeRuleReceipt?> GetOperation(Guid operationId, CancellationToken ct = default)
    { await using var c=await Open(ct);return await ReadOperation(c,null,operationId,ct); }

    public async Task<IntakeRuleReceipt> Revoke(Guid id, Guid operationId, int expectedVersion, string reason, CancellationToken ct = default)
    {
        if(id==Guid.Empty || operationId==Guid.Empty || expectedVersion<1 || string.IsNullOrWhiteSpace(reason) || reason.Length>2000 || reason.Any(char.IsControl))
            throw new ArgumentException("取消対象・版・操作IDと1～2000文字の取消理由を指定してください。");
        // Resolve a lost response before reading the current (possibly newer) version.
        IntakeRuleReceipt Match(IntakeRuleReceipt receipt)
        {
            if(receipt.Rule.Id!=id || receipt.ExpectedVersion!=expectedVersion || !receipt.Rule.Revoked || receipt.Rule.CancellationReason!=reason)
                throw new IntakeRuleConflictException("Operation ID already belongs to a different payload.");
            return receipt;
        }
        if(await GetOperation(operationId,ct) is { } receipt) return Match(receipt);
        var current=await Get(id,ct) ?? throw new KeyNotFoundException();
        if(current.Version!=expectedVersion || current.Revoked)
        {
            // The same operation may have committed between the two reads.
            if(await GetOperation(operationId,ct) is { } concurrentReceipt) return Match(concurrentReceipt);
            throw new IntakeRuleConflictException("再利用ルールの最新版を再取得してください。変更済みまたは取消済みです。");
        }
        var next=current with {Version=checked(expectedVersion+1),Revoked=true,CancellationReason=reason};
        return await Save(new(operationId,expectedVersion,next),ct);
    }

    public async Task<IntakeRuleReceipt> Save(IntakeRuleSaveRequest request, CancellationToken ct = default, IntakeRuleReviewGuard? reviewGuard = null)
    {
        if(request is null || request.OperationId==Guid.Empty || request.ExpectedVersion<0)
            throw new ArgumentException("Missing operation identity or expected version.");
        IntakeReusableRules.Validate(request.Rule);
        if((long)request.Rule.Version != (long)request.ExpectedVersion+1)
            throw new ArgumentException("Rule version must follow expected version.");
        var body=JsonSerializer.Serialize(request.Rule,Json);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,Json))));
        await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);
        // Distinct RuleIds for the same scope must not both win an initial save.
        // Include product/vintage boundaries, and retain the operation and RuleId locks.
        var scopeKey=JsonSerializer.Serialize(new { request.Rule.Scope, request.Rule.Kind, request.Rule.SourceKey },Json);
        foreach(var key in new[]{"lips-rule-operation:"+request.OperationId,"lips-rule-scope:"+scopeKey,"lips-rule:"+request.Rule.Id})
        {
            await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",c,tx);
            gate.Parameters.AddWithValue("key",key);await gate.ExecuteNonQueryAsync(ct);
        }
        if(await ReadOperation(c,tx,request.OperationId,ct) is { } previous)
        {
            if(previous.RequestHash!=hash)throw new IntakeRuleConflictException("Operation ID already belongs to a different payload.");
            await tx.CommitAsync(ct);return previous;
        }
        if (reviewGuard is not null)
        {
            // Hold the source review lock through rule commit. Answer and Run use UPDATE
            // on this same row, so neither can invalidate the checked answer in between.
            await using var reviewCommand = new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReviews\" WHERE \"Id\"=@id FOR UPDATE", c, tx);
            reviewCommand.Parameters.AddWithValue("id", reviewGuard.ReviewId);
            var reviewBody = await reviewCommand.ExecuteScalarAsync(ct) as string;
            var review = reviewBody is null ? null : JsonSerializer.Deserialize<IntakeReview>(reviewBody, Json);
            if (review is null || review.Version != reviewGuard.Version || review.Hash != reviewGuard.Hash ||
                review.State is "Running" or "Unknown" || request.Rule.AnswerId != review.Id || request.Rule.SourceHash != review.Hash)
                throw new IntakeRuleConflictException("原典・回答版を再取得してください。回答更新または処理開始後の旧回答は再利用保存できません。");
        }
        await using var latest=new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReusableRuleVersions\" WHERE \"RuleId\"=@id ORDER BY \"Version\" DESC LIMIT 1",c,tx);
        latest.Parameters.AddWithValue("id",request.Rule.Id);
        var current=await latest.ExecuteScalarAsync(ct) is string s?Decode(s):null;
        if((current?.Version??0)!=request.ExpectedVersion)throw new IntakeRuleConflictException("Rule changed; reload before saving.");
        if(current is not null && (current.Scope!=request.Rule.Scope || current.Kind!=request.Rule.Kind || current.SourceKey!=request.Rule.SourceKey))
            throw new IntakeRuleConflictException("A new scope requires a new rule identity.");
        if(current is null)
        {
            // Revoked identities still own their scope. Do not create a replacement
            // identity that hides revocation, or merge/delete existing conflicts.
            await using var sameScope=new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM (
                        SELECT DISTINCT ON ("RuleId") "Body" FROM "IntakeReusableRuleVersions"
                        ORDER BY "RuleId", "Version" DESC
                    ) latest
                    WHERE "Body"->'scope'=@scope AND "Body"->>'kind'=@kind AND "Body"->>'sourceKey'=@sourceKey
                )
                """,c,tx);
            sameScope.Parameters.AddWithValue("scope",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(request.Rule.Scope,Json));
            sameScope.Parameters.AddWithValue("kind",request.Rule.Kind);
            sameScope.Parameters.AddWithValue("sourceKey",request.Rule.SourceKey);
            if((bool)(await sameScope.ExecuteScalarAsync(ct))!)
                throw new IntakeRuleConflictException("同じ対象の再利用ルールが既にあります。既存のIDと最新版を再取得してください。");
        }
        await using var insert=new NpgsqlCommand("INSERT INTO \"IntakeReusableRuleVersions\" (\"RuleId\",\"Version\",\"OperationId\",\"RequestHash\",\"ExpectedVersion\",\"Body\") VALUES (@id,@v,@op,@hash,@expected,@body)",c,tx);
        insert.Parameters.AddWithValue("id",request.Rule.Id);insert.Parameters.AddWithValue("v",request.Rule.Version);
        insert.Parameters.AddWithValue("op",request.OperationId);insert.Parameters.AddWithValue("hash",hash);
        insert.Parameters.AddWithValue("expected",request.ExpectedVersion);insert.Parameters.AddWithValue("body",NpgsqlDbType.Jsonb,body);
        await insert.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        return new(request.OperationId,request.ExpectedVersion,hash,request.Rule);
    }
}
