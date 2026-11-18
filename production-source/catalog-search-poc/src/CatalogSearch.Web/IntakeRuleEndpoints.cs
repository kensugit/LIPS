using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Web;

public static class IntakeRuleEndpoints
{
    public sealed record PublishRequest(Guid OperationId, int Version, string Hash, int AnswerVersion,
        string QuestionId, Guid RuleId, int ExpectedVersion);
    public sealed record RevokeRequest(Guid OperationId, int ExpectedVersion, string Reason);
    public static async Task<FolderImportService> ImportService(WebApplication app, CatalogSearchDbContext db, CancellationToken ct)
    {
        var rules = app.Configuration.GetValue<bool>("IntakeRules:Enabled")
            ? await new IntakeReusableRuleStore(db.Database.GetConnectionString()!).List(ct) : [];
        var python = app.Configuration["PdfImport:Python"] ?? app.Configuration["OriginalRender:Python"];
        return new(new PostgresInventoryStore(db, folderIntake: true), rules,
            new FolderPdfReader(python, Path.Combine(AppContext.BaseDirectory, "tools", "pdf-folder", "parse_folder_pdf.py")),
            new FolderDuplicateLookup(db),
            new FolderXlsReader(python, Path.Combine(AppContext.BaseDirectory, "tools", "xls-folder", "parse_folder_xls.py")));
    }
    public static void MapIntakeRuleEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("IntakeRules:Enabled")) return;
        IntakeReusableRuleStore Rules(CatalogSearchDbContext db) => new(db.Database.GetConnectionString()!);
        app.MapGet("/api/import/rules", async(CatalogSearchDbContext db, CancellationToken ct) => Results.Ok(await Rules(db).List(ct)));
        app.MapGet("/api/import/rules/{id:guid}/history", async(Guid id, CatalogSearchDbContext db, CancellationToken ct) =>
        {
            var history=await Rules(db).History(id,ct);
            return history.Count==0 ? Results.NotFound() : Results.Ok(history);
        });
        app.MapPost("/api/import/rules/{id:guid}/revoke", async(Guid id, RevokeRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
        {
            try { return Results.Ok(await Rules(db).Revoke(id,request.OperationId,request.ExpectedVersion,request.Reason,ct)); }
            catch(KeyNotFoundException) { return Results.NotFound(); }
            catch(IntakeRuleConflictException ex) { return Results.Conflict(new {error=ex.Message}); }
            catch(ArgumentException ex) { return Results.BadRequest(new {error=ex.Message}); }
        });
        app.MapGet("/api/import/rules/operations/{id:guid}", async(Guid id, CatalogSearchDbContext db, CancellationToken ct) =>
            await Rules(db).GetOperation(id, ct) is { } receipt ? Results.Ok(receipt) : Results.NotFound());
        app.MapPost("/api/import/reviews/{id:guid}/reuse", async(Guid id, PublishRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
        {
            try
            {
                var reviewStore = new IntakeReviewStore(db.Database.GetConnectionString()!);
                var review = await reviewStore.Get(id, ct);
                if (review is null) return Results.NotFound();
                if (review.Hash != request.Hash || review.Version != request.Version || review.State is "Unknown" or "Running")
                    return Results.Conflict(new { error = "原典・回答版を再取得してください。" });
                if (!string.Equals(Path.GetExtension(review.Path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = "この原典形式の再利用には未対応です。" });
                var original = await reviewStore.Content(review, ct);
                var originalQuestions = WorkbookMappingReview.Inspect(original);
                var (answer, resolution) = IntakeRulePublication.Validate(review, originalQuestions, request.QuestionId, request.AnswerVersion);
                var q = resolution.Question;
                var rule = new IntakeReusableRule(request.RuleId, checked(request.ExpectedVersion + 1), q.Scope, q.Kind,
                    q.SourceKey, resolution.Meaning, review.Id, answer!.Version, review.Hash,
                    System.Text.RegularExpressions.Regex.Replace($"{q.Sheet} 行{q.Row} 商品{q.Sku}: {answer.Reason}", @"\s+", " "));
                return Results.Ok(await Rules(db).Save(new(request.OperationId, request.ExpectedVersion, rule), ct,
                    new(review.Id, request.Version, request.Hash)));
            }
            catch (IntakeRuleConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}
