using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Web;

public static class FolderImportEndpoints
{
    public static void MapFolderImportEndpoints(this WebApplication app)
    {
        app.MapIntakeReviewEndpoints();
        // One file per request keeps the existing 21MB gateway limit and isolates failures.
        app.MapPost("/api/import/folder", async (HttpRequest request, CatalogSearchDbContext db, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "フォルダーからファイルを選択してください。" });
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form["relativePath"].Count != 1 || form["observedAt"].Count > 1)
                return Results.BadRequest(new { error = "1リクエストに1ファイルと相対パスが必要です。" });
            var file = form.Files[0]; var path = form["relativePath"].ToString();
            if (file.Length is 0 or > FolderImportService.MaxFileBytes)
                return Results.Ok(new FolderImportResult(path, "Failed", "空ファイル、または20MBを超えています。"));
            try
            {
                await using var input = file.OpenReadStream(); using var buffer = new MemoryStream();
                var chunk = new byte[81920]; int read;
                while ((read = await input.ReadAsync(chunk, ct)) > 0)
                {
                    if (buffer.Length + read > FolderImportService.MaxFileBytes)
                        return Results.Ok(new FolderImportResult(path, "Failed", "20MBを超えています。"));
                    buffer.Write(chunk, 0, read);
                }
                var service = await IntakeRuleEndpoints.ImportService(app, db, ct);
                if (app.Configuration.GetValue<bool>("IntakeReview:Enabled"))
                {
                    var ledger = new IntakeReviewStore(db.Database.GetConnectionString()!);
                    var review = await ledger.Receive(path,buffer.ToArray(),form["observedAt"].ToString(),ct);
                    var completedAtReceive = review.State == "Completed";
                    if (review.State == "Created")
                        review = await ledger.Run(review,review.Version,review.Hash,true,
                            (bytes,date,token)=>service.ImportAsync(path,bytes,date,token),ct);
                    return Results.Ok(IntakeReviewStore.Receipt(review, completedAtReceive));
                }
                var content = buffer.ToArray();
                return Results.Ok((await service.ImportAsync(path, content, form["observedAt"].ToString(), ct)) with
                    { Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)) });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (IntakeConflictException)
            { return Results.Ok(new FolderImportResult(path,"Held","同じ原典が別の操作で処理中です。確認履歴を再取得してください。",StopQueue:true)); }
            catch (Exception)
            {
                return Results.Ok(new FolderImportResult(path, "Held", "処理を完了できませんでした。取込履歴と資料を確認してください。自動再試行はしません。",StopQueue:true));
            }
        });
    }
}
