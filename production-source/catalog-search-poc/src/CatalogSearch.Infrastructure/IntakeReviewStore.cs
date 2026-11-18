using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using CatalogSearch.Application;

namespace CatalogSearch.Infrastructure;

public sealed record IntakeSourceReference(string Kind, string? Sheet = null, int? Row = null, int? Page = null,
    double? X = null, double? Y = null, double? Right = null, double? Bottom = null, int? Width = null, int? Height = null)
{
    public void Validate()
    {
        if (Kind == "xlsx" && !string.IsNullOrWhiteSpace(Sheet) && Sheet.Length <= 31 &&
            !Sheet.Any(char.IsControl) && Row is >= 1 and <= 1048576 && Page is null &&
            X is null && Y is null && Right is null && Bottom is null && Width is null && Height is null) return;
        if (Kind == "pdf" && Sheet is null && Row is null && Page is >= 1 and <= 1000 &&
            Width is > 0 and <= 2400 && Height is > 0 and <= 2400 &&
            X is >= 0 && Y is >= 0 && Right > X && Bottom > Y && Right <= Width && Bottom <= Height) return;
        throw new ArgumentException("原典の参照位置が不正です。原典画像で確認対象を選び直してください。");
    }
}
public sealed record IntakeAnswer(int Version, string Hash, string Action, string? Date, string Reason,
    IntakeSourceReference? SourceReference = null, IReadOnlyList<string>? SelectedSkus = null,
    IReadOnlyList<IntakeMappingAnswer>? MappingAnswers = null);
public sealed record IntakeEvent(DateTimeOffset At, string Method, string Action, string Reason, string? Date, int Version,
    IntakeSourceReference? SourceReference = null, IReadOnlyList<string>? SelectedSkus = null,
    IReadOnlyList<IntakeMappingResolution>? MappingResolutions = null)
{
    public Guid? OperationId { get; init; }
}
public sealed class IntakeReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Hash { get; set; } = "";
    public string Path { get; set; } = "";
    public int Version { get; set; } = 1;
    public string State { get; set; } = "Created";
    public string? Date { get; set; }
    public IReadOnlyList<string>? SelectedSkus { get; set; }
    public List<IntakeMappingResolution> MappingResolutions { get; set; } = [];
    public FolderImportResult? Result { get; set; }
    public List<IntakeEvent> History { get; set; } = [];
    public string Location => "原典ファイル全体（疑義のページ・シート・行は未特定）";
    public bool CanAnswer => State is "Held" or "Ready" or "Partial";
    public bool CanResume => State == "Ready";
    public bool CanResumeResolved => State is "Held" or "Ready" or "Partial"
        && Result?.SupplierCode == "LA_VINEE"
        && (Result.QuestionKind == "Mapping" || Result.QuestionKind == "Rows" && Result.Holds is { Count: > 0 });
}
public sealed class IntakeConflictException : Exception;

// Compare-and-swap transitions persist before business I/O. A crashed Running state
// is deliberately not retried: the commit outcome may be unknown.
public sealed class IntakeReviewStore(string connectionString)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    { var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct); return c; }

    public async Task<IntakeReview> Receive(string path, byte[] bytes, string? date, CancellationToken ct)
    {
        if (bytes.Length is 0 or > FolderImportService.MaxFileBytes || path.Length is 0 or > 500 ||
            path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split('/').Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".."))
            throw new ArgumentException("原典のパスまたはサイズが不正です。");
        var r = new IntakeReview { Path = path, Hash = Convert.ToHexString(SHA256.HashData(bytes)), Date = date };
        r.History.Add(new(DateTimeOffset.UtcNow, "Rule", "Received", "原典を変更せず受付保存", date, 1));
        await using var c = await Open(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO "IntakeReviews" ("Id","Hash","Path","Content","Version","Body")
            VALUES (@id,@hash,@path,@content,1,@body) ON CONFLICT ("Hash","Path") DO NOTHING;
            SELECT "Body"::text FROM "IntakeReviews" WHERE "Hash"=@hash AND "Path"=@path;
            """, c);
        cmd.Parameters.AddWithValue("id", r.Id); cmd.Parameters.AddWithValue("hash", r.Hash);
        cmd.Parameters.AddWithValue("path", path); cmd.Parameters.AddWithValue("content", bytes);
        cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(r, Json));
        return JsonSerializer.Deserialize<IntakeReview>((string)(await cmd.ExecuteScalarAsync(ct))!, Json)!;
    }
    public async Task<IntakeReview?> Get(Guid id, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReviews\" WHERE \"Id\"=@id", c);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync(ct) is string s ? JsonSerializer.Deserialize<IntakeReview>(s, Json) : null;
    }
    public async Task<List<IntakeReview>> List(CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = new NpgsqlCommand("SELECT \"Body\"::text FROM \"IntakeReviews\" ORDER BY \"UpdatedAt\" DESC LIMIT 100", c);
        await using var reader = await cmd.ExecuteReaderAsync(ct); var items = new List<IntakeReview>();
        while (await reader.ReadAsync(ct)) items.Add(JsonSerializer.Deserialize<IntakeReview>(reader.GetString(0), Json)!);
        return items;
    }
    public async Task<byte[]> Content(IntakeReview r, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = new NpgsqlCommand("SELECT \"Content\" FROM \"IntakeReviews\" WHERE \"Id\"=@id", c);
        cmd.Parameters.AddWithValue("id", r.Id);
        var bytes = (byte[])(await cmd.ExecuteScalarAsync(ct))!;
        if (Convert.ToHexString(SHA256.HashData(bytes)) != r.Hash) throw new InvalidDataException("原典hash不一致");
        return bytes;
    }
    private async Task Save(IntakeReview r, int expected, CancellationToken ct)
    {
        r.Version = expected + 1;
        await using var c = await Open(ct);
        await using var cmd = new NpgsqlCommand("UPDATE \"IntakeReviews\" SET \"Version\"=@v,\"Body\"=@body,\"UpdatedAt\"=now() WHERE \"Id\"=@id AND \"Version\"=@expected", c);
        cmd.Parameters.AddWithValue("id",r.Id); cmd.Parameters.AddWithValue("v",r.Version);
        cmd.Parameters.AddWithValue("expected",expected); cmd.Parameters.AddWithValue("body",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(r,Json));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new IntakeConflictException();
    }
    public static void ApplyAnswer(IntakeReview r, IntakeAnswer a)
    {
        if (r.Version != a.Version || r.Hash != a.Hash || !r.CanAnswer) throw new IntakeConflictException();
        if (string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 2000) throw new ArgumentException("回答根拠を1～2000文字で入力してください。");
        a.SourceReference?.Validate();
        if (a.SourceReference is { } reference && Path.GetExtension(r.Path).ToLowerInvariant() != "." + reference.Kind)
            throw new ArgumentException("回答と原典形式が一致しません。");
        if (a.Action == "date")
        {
            if (r.Result?.QuestionKind != "Date" || !DateOnly.TryParseExact(a.Date,"yyyy-MM-dd",out _))
                throw new ArgumentException("資料日付の疑義に有効な日付を指定してください。");
            r.Date = a.Date; r.State = "Ready";
        }
        else if (a.Action == "recheck-rows")
        {
            var held=r.Result?.Holds;
            if(r.Result?.QuestionKind!="Rows" || held is null || a.SelectedSkus is null || a.SelectedSkus.Count is <1 or >10000 ||
                a.SelectedSkus.Distinct().Count()!=a.SelectedSkus.Count || a.SelectedSkus.Any(s=>!held.Any(h=>h.Sku==s)))
                throw new ArgumentException("未保存の確認対象商品を選択してください。");
            r.SelectedSkus=a.SelectedSkus.ToArray();r.State="Ready";
        }
        else if (a.Action == "mapping")
        {
            var questions = r.Result?.Questions;
            if (r.Result?.QuestionKind != "Mapping" || questions is null || a.MappingAnswers is not { Count: > 0 and <= 10000 } ||
                a.MappingAnswers.Select(x => x.QuestionId).Distinct().Count() != a.MappingAnswers.Count)
                throw new ArgumentException("確認対象の質問と選択回答を指定してください。");
            var resolved = new List<IntakeMappingResolution>();
            foreach (var answer in a.MappingAnswers)
            {
                var q = questions.SingleOrDefault(q => q.Id == answer.QuestionId);
                if (q is null || !q.Choices.Contains(answer.Meaning)) throw new ArgumentException("質問または回答の選択肢が一致しません。");
                resolved.Add(new(q, answer.Meaning, a.Reason));
            }
            var combined = r.MappingResolutions.Where(x => !resolved.Any(v => v.Question.Id == x.Question.Id)).Concat(resolved).ToArray();
            if (combined.GroupBy(x => (x.Question.Scope, x.Question.Kind, x.Question.SourceKey)).Any(g => g.Select(x => x.Meaning).Distinct().Count() > 1))
                throw new ArgumentException("同じ対象条件の回答が食い違っています。同じ列・原表記の対象行について回答を確認してください。");
            foreach (var resolution in resolved)
                if (resolution.Question.Kind == "VolumeUnit")
                    try { LucVolumeReview.Resolve(resolution.Question, resolution); }
                    catch (InvalidDataException) { throw new ArgumentException("選んだ単位では正しい整数mLに換算できません。原典を確認してください。"); }
            foreach (var answer in resolved)
            {
                r.MappingResolutions.RemoveAll(x => x.Question.Id == answer.Question.Id);
                r.MappingResolutions.Add(answer);
            }
            r.State = questions.All(q => r.MappingResolutions.Any(x => x.Question.Id == q.Id)) ? "Ready" : r.Result.Products > 0 ? "Partial" : "Held";
        }
        else if (a.Action == "hold") r.State = r.Result?.Products>0 ? "Partial" : "Held";
        else throw new ArgumentException("未対応の回答方式です。任意の指示文は実行できません。");
        r.History.Add(new(DateTimeOffset.UtcNow,"Human",a.Action,a.Reason,a.Action=="date"?a.Date:null,r.Version+1,a.SourceReference,a.Action=="recheck-rows"?r.SelectedSkus:null,
            a.Action == "mapping" ? r.MappingResolutions.Where(x => a.MappingAnswers!.Any(v => v.QuestionId == x.Question.Id)).ToArray() : null));
    }
    public async Task<IntakeReview> Answer(Guid id, IntakeAnswer a, CancellationToken ct)
    {
        var r = await Get(id,ct) ?? throw new KeyNotFoundException(); var version=r.Version;
        ApplyAnswer(r,a); await Save(r,version,ct); return r;
    }
    public static bool ApplyQuestionRefresh(IntakeReview r,int expected,string hash,Guid operationId,byte[] content)
    {
        if(r.Version!=expected||r.Hash!=hash||r.State is not ("Held" or "Ready")||r.Result?.QuestionKind!="Mapping")
            throw new IntakeConflictException();
        if(operationId==Guid.Empty||r.History.Any(h=>h.OperationId==operationId))
            throw new ArgumentException("質問更新の操作IDが空または使用済みです。再送せず履歴を確認してください。");
        if(Convert.ToHexString(SHA256.HashData(content))!=hash)throw new InvalidDataException("原典hash不一致");
        var questions=WorkbookMappingReview.Inspect(content);
        if(questions.Count==0||questions.Select(q=>q.Id).Distinct().Count()!=questions.Count)
            throw new ArgumentException("現在の原典形式から有効な質問を生成できません。原典を確認してください。");
        var previous=r.MappingResolutions.ToArray();
        var current=questions.ToDictionary(q=>q.Id,q=>(Question:q,Serialized:JsonSerializer.Serialize(q,Json)));
        var kept=previous.Where(a=>current.TryGetValue(a.Question.Id,out var entry)
            &&entry.Serialized==JsonSerializer.Serialize(a.Question,Json)&&entry.Question.Choices.Contains(a.Meaning)).ToList();
        if(kept.Select(a=>a.Question.Id).Distinct().Count()!=kept.Count)
            throw new ArgumentException("保存回答が重複しています。履歴を確認してください。");
        var answeredIds=kept.Select(a=>a.Question.Id).ToHashSet();
        var state=questions.All(q=>answeredIds.Contains(q.Id))?"Ready":"Held";
        if(JsonSerializer.Serialize(r.Result.Questions,Json)==JsonSerializer.Serialize(questions,Json)
            &&kept.Count==previous.Length&&r.State==state)return false;
        r.MappingResolutions=kept;
        r.Result=r.Result with {Questions=questions,Message="原典から質問を更新しました。保存済みの一致する回答は保持しています。未回答を確認してください。"};
        r.State=state;
        // Snapshot preserves removed active answers without altering prior Human events.
        r.History.Add(new(DateTimeOffset.UtcNow,"Rule","RefreshQuestions",
            $"質問更新。質問{questions.Count}件、保持回答{kept.Count}件、再確認回答{previous.Length-kept.Count}件。商品保存・再開なし。",
            r.Date,expected+1,MappingResolutions:previous){OperationId=operationId});
        return true;
    }
    public async Task<IntakeReview> RefreshQuestions(Guid id,int expected,string hash,Guid operationId,CancellationToken ct)
    {
        var r=await Get(id,ct)??throw new KeyNotFoundException();
        // Reject stale or unsafe states before reading/re-inspecting the workbook.
        if(r.Version!=expected||r.Hash!=hash||r.State is not ("Held" or "Ready")||r.Result?.QuestionKind!="Mapping")
            throw new IntakeConflictException();
        var content=await Content(r,ct);
        if(ApplyQuestionRefresh(r,expected,hash,operationId,content))await Save(r,expected,ct);
        return r;
    }
    public async Task<IntakeReview> Run(IntakeReview r, int expected, string hash, bool initial,
        Func<byte[],string?,CancellationToken,Task<FolderImportResult>> import, CancellationToken ct, bool resolvedOnly = false)
    {
        if (r.Version != expected || r.Hash != hash || (resolvedOnly ? initial || !r.CanResumeResolved : initial ? r.State!="Created" : r.State!="Ready")) throw new IntakeConflictException();
        r.State="Running";
        r.History.Add(new(DateTimeOffset.UtcNow,"Rule","Processing","保存開始。応答不明時は自動再送しない",r.Date,expected+1));
        await Save(r,expected,ct);
        try
        {
            var bytes=await Content(r,ct);
            r.Result=PreserveConfirmedProgress(r.Result,await import(bytes,r.Date,ct)) with { ReviewId=r.Id };
            r.State=r.Result.StopQueue ? "Unknown" : r.Result.Status=="Success" ? "Completed" : r.Result.Status=="Partial" ? "Partial" : "Held";
        }
        catch (Exception)
        {
            r.State="Unknown";
            r.Result=PreserveConfirmedProgress(r.Result,new(r.Path,"Held","処理結果を確認できません。履歴照合が必要です。自動再開しません。",StopQueue:true,ReviewId:r.Id));
        }
        r.History.Add(new(DateTimeOffset.UtcNow,"Rule",r.State,r.Result.Message,r.Date,r.Version+1));
        // Cancellation may mean the browser disconnected after the business commit.
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Save(r,r.Version,timeout.Token); return r;
    }
    public static void PrepareUnsupportedRecheck(IntakeReview r, int expected, string hash)
    {
        var partialOrveaux = r.Hash == OrveauxStockParser.ApprovedOriginalHash && r.State == "Partial" &&
            r.Result is { SupplierCode: "ORVEAUX", Status: "Partial", StopQueue: false, Products: 160, SourceDocumentId: not null };
        var orveaux = partialOrveaux || r.Hash == OrveauxStockParser.ApprovedOriginalHash && r.Result is { SupplierCode: "ORVEAUX", Status: "Held", StopQueue: false, Products: 0, SourceDocumentId: null } &&
            r.Result.Message.Contains("同日別内容", StringComparison.Ordinal);
        if (r.Version != expected || r.Hash != hash || r.State != "Held" && !partialOrveaux ||
            !orveaux && r.Result is not { Status: "Unsupported", StopQueue: false, Products: 0, SourceDocumentId: null })
            throw new IntakeConflictException();
        r.State = "Ready";
        r.History.Add(new(DateTimeOffset.UtcNow, "Human", "RecheckUnsupported",
            orveaux ? "本人指定の今回Excel優先・確定商品照合を、未保存の同一原本に適用する明示操作。" :
            "未対応として未登録の同一原本を、現在の読取り処理で再判定する明示操作。", r.Date, expected + 1));
    }
    public async Task<IntakeReview> RecheckUnsupported(Guid id, int expected, string hash,
        Func<byte[],string?,CancellationToken,Task<FolderImportResult>> import, CancellationToken ct)
    {
        var r = await Get(id, ct) ?? throw new KeyNotFoundException();
        PrepareUnsupportedRecheck(r, expected, hash);
        await Save(r, expected, ct);
        return await Run(r, r.Version, hash, false, import, ct);
    }
    // Run reprocesses this review's immutable original; a parser/validation failure
    // cannot undo an earlier confirmed source/product acceptance.
    public static FolderImportResult PreserveConfirmedProgress(FolderImportResult? previous,FolderImportResult next)
    {
        if(previous is not {Products:>0,SourceDocumentId:not null})return next;
        var mismatch=next.SourceDocumentId is not null&&next.SourceDocumentId!=previous.SourceDocumentId;
        if(next.StopQueue||mismatch)
            return next with {Status="Held",StopQueue=true,Products=previous.Products,SourceDocumentId=previous.SourceDocumentId,
                AlreadyImported=false,AddedProducts=0,Message=next.Message+" 既確認の保存済み件数は保持しています。今回の追加保存は未確認です。"};
        var confirmed=next.SourceDocumentId==previous.SourceDocumentId&&next.Products>=previous.Products;
        if(confirmed)return next;
        return next with {Status="Partial",Products=previous.Products,SourceDocumentId=previous.SourceDocumentId,
            AlreadyImported=false,AddedProducts=0,Message=next.Message+" 以前の保存済み商品は保持しています。資料全体は未完了です。"};
    }
    // Only the upload endpoint sets this after Receive, before any first Run.
    // Return an adjusted receipt without rewriting the original result or history.
    public static FolderImportResult Receipt(IntakeReview r, bool completedAtReceive = false) => ReceiptCore(r, completedAtReceive) with { Hash = r.Hash };
    private static FolderImportResult ReceiptCore(IntakeReview r, bool completedAtReceive) => r.State is "Running" or "Unknown"
        ? (r.Result??new(r.Path,"Held","")) with {Status="Held",Message="保存結果確認中または不明です。自動再開しません。",StopQueue=true,ReviewId=r.Id,AlreadyImported=false,AddedProducts=0}
        : completedAtReceive && r.State == "Completed" && r.Result is { Status: "Success" }
        ? r.Result with { ReviewId=r.Id, AlreadyImported=true, AddedProducts=0, Message="取込済み。同じ内容のため重複登録しませんでした。" }
        : (r.Result ?? new(r.Path,"Held","受付済み。確認画面で状態を確認してください。")) with { ReviewId=r.Id };
}
