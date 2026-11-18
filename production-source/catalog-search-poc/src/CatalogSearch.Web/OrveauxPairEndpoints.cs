using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Web;

// Explicit, fixed-source acceptance. This never marks either whole workbook imported.
public static class OrveauxPairEndpoints
{
    public static void MapOrveauxPairEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("OrveauxPair:Enabled")) return;
        app.MapGet("/api/import/orveaux-pair/review-candidates", async (CatalogSearchDbContext db, CancellationToken ct) =>
        {
            var reviews = await OrveauxPairReviewService.Discover(db.Database.GetConnectionString()!, ct);
            var excel = reviews.Where(r => OrveauxPairReviewService.Eligible(r, true)).ToArray();
            var pdf = reviews.Where(r => OrveauxPairReviewService.Eligible(r, false)).ToArray();
            object Reference(IntakeReview r) => new { r.Id, r.Version, r.Hash, r.Path, r.State };
            var candidates = excel.Length != 1 || pdf.Length != 1 ? Array.Empty<object>() : new[] { new { Excel = Reference(excel[0]), Pdf = Reference(pdf[0]) } };
            return Results.Ok(new { candidates, excelReviews = excel.Select(Reference), pdfReviews = pdf.Select(Reference),
                requiresReviewSelection = excel.Length > 1 || pdf.Length > 1, wholeSourceImported = false });
        });
        foreach (var mode in new[] { "preview", "commit" })
        {
            var commit = mode == "commit";
            app.MapPost("/api/import/orveaux-pair/reviews/" + mode, async (HttpRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest();
                try
                {
                    var form = await request.ReadFormAsync(ct);
                    if (form.Files.Count != 1 || form.Files[0].Name != "answers" || form.Files[0].Length is <= 0 or > 100_000
                        || form["reviews"].Count != 1 || form["reviews"].ToString().Length > 4000) return Results.BadRequest();
                    var selection = JsonSerializer.Deserialize<ExactPairReviewSelection>(form["reviews"].ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (selection?.Excel is null || selection.Pdf is null) return Results.BadRequest();
                    var prepared = await OrveauxPairReviewService.Prepare(new IntakeReviewStore(db.Database.GetConnectionString()!), selection, ct);
                    await using var input = form.Files[0].OpenReadStream(); using var buffer = new MemoryStream(); await input.CopyToAsync(buffer, ct);
                    var answers = buffer.ToArray();
                    var resolved = OrveauxAocPairPolicy.Resolve(prepared.Candidate, prepared.Excel, prepared.Pdf, answers);
                    if (resolved.Status != "ResolvedEvidenceOnly") return Results.Conflict(new { error = resolved.Reason });
                    if (!commit) return Results.Ok(new { evidence = resolved, reviews = selection, requiresExplicitCommit = true, wholeSourceImported = false });
                    if (form["operationId"].Count != 1 || !Guid.TryParse(form["operationId"], out var operation) || operation == Guid.Empty) return Results.BadRequest();
                    return Results.Ok(await new OrveauxExactPairImportStore(db).Import(operation, prepared.Candidate, prepared.Excel, prepared.Pdf, answers,
                        new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), ct, selection));
                }
                catch (IntakeConflictException) { return Results.Conflict(new { error = "原典レビューの状態・版・hashが変わりました。再取得してください。" }); }
                catch (ExactPairConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException)
                { return Results.BadRequest(new { error = "受付済み原典または回答JSONを確認できません。" }); }
            });
        }
        app.MapGet("/api/import/orveaux-pair", async (CatalogSearchDbContext db, CancellationToken ct) =>
            await new OrveauxExactPairImportStore(db).Get(ct) is { } r ? Results.Ok(r) : Results.NotFound());
        app.MapGet("/api/import/orveaux-pair/operations/{id:guid}", async (Guid id, CatalogSearchDbContext db, CancellationToken ct) =>
            await new OrveauxExactPairImportStore(db).GetOperation(id, ct) is { } r ? Results.Ok(r) : Results.NotFound());
        foreach (var mode in new[] { "preview", "commit" })
        {
            var commit = mode == "commit";
            app.MapPost("/api/import/orveaux-pair/" + mode, async (HttpRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest(new { error = "原典2件と受領回答JSONが必要です。" });
                try
                {
                    var form = await request.ReadFormAsync(ct);
                    if (form.Files.Count != 3 || form["candidate"].Count != 1 || form["candidate"].ToString().Length > 8000
                        || form.Files.Sum(f => f.Length) > 20_000_000)
                        return Results.BadRequest(new { error = "対象ファイルまたは候補の形式が不正です。" });
                    async Task<byte[]> Read(string name, long limit)
                    {
                        var files = form.Files.GetFiles(name);
                        if (files.Count != 1 || files[0].Length <= 0 || files[0].Length > limit) throw new ArgumentException("原典と回答JSONの数・サイズを確認してください。");
                        await using var input = files[0].OpenReadStream();
                        using var output = new MemoryStream();
                        await input.CopyToAsync(output, ct);
                        return output.ToArray();
                    }
                    var excel = await Read("excel", 20_000_000); var pdf = await Read("pdf", 20_000_000); var answers = await Read("answers", 100_000);
                    var candidate = JsonSerializer.Deserialize<OrveauxPairCandidate>(form["candidate"].ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (candidate is null) return Results.BadRequest();
                    var resolved = OrveauxAocPairPolicy.Resolve(candidate, excel, pdf, answers);
                    if (resolved.Status != "ResolvedEvidenceOnly") return Results.Conflict(new { error = resolved.Reason });
                    if (!commit) return Results.Ok(new { evidence = resolved, requiresExplicitCommit = true, wholeSourceImported = false });
                    if (form["operationId"].Count != 1 || !Guid.TryParse(form["operationId"], out var operation) || operation == Guid.Empty)
                        return Results.BadRequest(new { error = "保存結果照合用の操作IDが必要です。" });
                    return Results.Ok(await new OrveauxExactPairImportStore(db).Import(operation, candidate, excel, pdf, answers,
                        new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), ct));
                }
                catch (ExactPairConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
                catch (JsonException) { return Results.BadRequest(new { error = "候補JSONが不正です。" }); }
                catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            });
        }
    }
}
