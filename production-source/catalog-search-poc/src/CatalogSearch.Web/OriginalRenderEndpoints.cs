using System.Diagnostics;
using System.Text.Json;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Web;

// Local-only adapter. Render artifacts are disposable, separate from immutable source bytes.
public static class OriginalRenderEndpoints
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void MapOriginalRenderEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("IntakeReview:Enabled")) return;
        app.MapGet("/api/import/reviews/{id:guid}/render-info", async (Guid id, CatalogSearchDbContext db, CancellationToken ct) =>
            await Render(app, db, id, "info", 1, 1, 1, null, ct));
        app.MapGet("/api/import/reviews/{id:guid}/render", async (Guid id, int? sheet, int? row, int? page, CatalogSearchDbContext db, CancellationToken ct) =>
            await Render(app, db, id, "render", sheet ?? 1, row ?? 1, page ?? 1, null, ct));
        app.MapGet("/api/import/reviews/{id:guid}/render-image", async (Guid id, int? sheet, int? row, int? page, bool? highlight, CatalogSearchDbContext db, CancellationToken ct) =>
            await Render(app, db, id, "render", sheet ?? 1, row ?? 1, page ?? 1, highlight ?? false, ct));
    }

    private static async Task<IResult> Render(WebApplication app, CatalogSearchDbContext db, Guid id,
        string mode, int sheet, int row, int page, bool? image, CancellationToken ct)
    {
        if (sheet is < 1 or > 1000 || row is < 1 or > 1048576 || page is < 1 or > 1000)
            return Results.BadRequest(new { error = "シート・行・ページ番号が範囲外です。" });
        var store = new IntakeReviewStore(db.Database.GetConnectionString()!);
        var review = await store.Get(id, ct);
        if (review is null) return Results.NotFound();
        var ext = Path.GetExtension(review.Path).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".pdf")
            return Results.UnprocessableEntity(new { error = "原典画像は.xlsxと.pdfに対応しています。この形式は原典ダウンロードで確認してください。" });
        var python = app.Configuration["OriginalRender:Python"];
        if (string.IsNullOrEmpty(python) || !File.Exists(python))
            return Results.Json(new { error = "原典レンダラーが未設定です。" }, statusCode: 503);
        // Content() checks the persisted source hash before any native renderer is invoked.
        var bytes = await store.Content(review, ct);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var root = app.Configuration["OriginalRender:Cache"] ?? Path.Combine(app.Environment.ContentRootPath, "render-cache");
        var scriptDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "original-render");
        string cacheVersion;
        try { cacheVersion = OriginalRenderCacheVersion.Resolve(ext, OperatingSystem.IsWindows(), scriptDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.Json(new { error = "原典レンダラーの版を確認できません。" }, statusCode: 503);
        }
        var dir = Path.Combine(root, cacheVersion, hash, $"{mode}-{sheet}-{row}-{page}");
        var json = Path.Combine(dir, mode + ".json");
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5), ct))
            return Results.Json(new { error = "別の原典を描画中です。少し待って表示ボタンを押してください。" }, statusCode: 503);
        try
        {
            if (!File.Exists(json))
            {
                Directory.CreateDirectory(dir);
                var input = Path.Combine(dir, "source" + ext);
                await File.WriteAllBytesAsync(input, bytes, ct);
                // The project copies these assets beside the executable for build and publish.
                // dotnet run uses the project directory as ContentRoot, not the output directory.
                var script = Path.Combine(scriptDirectory, "render.py");
                var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in new[] { script, input, dir, "--mode", mode, "--sheet", sheet.ToString(), "--row", row.ToString(), "--page", page.ToString() })
                    start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                // Drain pipes without logging document content. Do not abort native cleanup on client disconnect.
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(85));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    return Results.Json(new { error = "原典描画が時間切れです。自動再試行はしません。" }, statusCode: 504);
                }
                await Task.WhenAll(output, error);
                if (process.ExitCode != 0 || !File.Exists(json))
                {
                    var errorFile = Path.Combine(dir, "error.json");
                    return File.Exists(errorFile)
                        ? Results.Content(await File.ReadAllTextAsync(errorFile), "application/json", statusCode: 422)
                        : Results.Json(new { error = "原典を描画できませんでした。原典ダウンロードで確認してください。" }, statusCode: 422);
                }
            }
            if (image is not null)
            {
                var file = Path.Combine(dir, image.Value && ext == ".xlsx" ? "highlight.png" : "base.png");
                return File.Exists(file) ? Results.File(file, "image/png") : Results.NotFound();
            }
            var data = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(json, ct));
            return Results.Json(data);
        }
        finally { Gate.Release(); }
    }
}
