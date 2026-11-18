using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Web;

public static class IntakeReviewEndpoints
{
    public sealed record ResumeRequest(int Version,string Hash);
    public sealed record RefreshQuestionsRequest(int Version,string Hash,Guid OperationId);
    public static void MapIntakeReviewEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("IntakeReview:Enabled")) return;
        app.MapIntakeRuleEndpoints();
        app.MapOriginalRenderEndpoints();
        app.MapOrveauxPairEndpoints();
        app.MapGet("/api/products/{id:guid}/source-inventory",async(Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var product=await db.SupplierProducts.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id,ct);
            if(product is null)return Results.NotFound();
            var evidence=await (from e in db.SourceEvidence.AsNoTracking()
                join s in db.SourceDocuments.AsNoTracking() on e.SourceDocumentId equals s.Id
                where e.SupplierProductId==id&&s.ObservedAt==product.CurrentObservedAt
                orderby s.FileName select new{s.Id,s.FileName,s.FileHash,s.ObservedAt,e.ExtractedRowJson}).ToListAsync(ct);
            return Results.Ok(new{product.SupplierProductCode,Quantity=product.CurrentInventoryQuantity,
                Sources=evidence.Select(e=>new{SourceDocumentId=e.Id,e.FileName,e.FileHash,e.ObservedAt,
                    Rows=EneosRowPolicy.Sources(System.Text.Json.JsonSerializer.Deserialize<CatalogSearch.Application.SupplierInventoryRow>(e.ExtractedRowJson)!)})});
        });
        IntakeReviewStore Store(CatalogSearchDbContext db)=>new(db.Database.GetConnectionString()!);
        app.MapGet("/api/import/reviews",async(CatalogSearchDbContext db,CancellationToken ct)=>
            Results.Ok(await Store(db).List(ct)));
        app.MapPost("/api/import/reviews/{id:guid}/recheck", async(Guid id, ResumeRequest a, CatalogSearchDbContext db, CancellationToken ct) =>
        {
            var store = Store(db); var r = await store.Get(id, ct); if(r is null) return Results.NotFound();
            try
            {
                var service = await IntakeRuleEndpoints.ImportService(app, db, ct);
                return Results.Ok(await store.RecheckUnsupported(id, a.Version, a.Hash,
                    (bytes,date,token) => service.ImportAsync(r.Path, bytes, date, token), ct));
            }
            catch(IntakeConflictException) { return Results.Conflict(new {error="未登録の未対応資料だけ再読取りできます。結果不明・部分保存・古い版は履歴を確認してください。"}); }
        });
        app.MapGet("/api/import/reviews/{id:guid}",async(Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
            await Store(db).Get(id,ct) is { } r ? Results.Ok(r) : Results.NotFound());
        app.MapGet("/api/import/reviews/{id:guid}/original",async(Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var store=Store(db); var r=await store.Get(id,ct); if(r is null)return Results.NotFound();
            return Results.File(await store.Content(r,ct),"application/octet-stream",r.Path.Split('/')[^1]);
        });
        app.MapGet("/api/import/reviews/{id:guid}/preview",async(Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var store=Store(db);var r=await store.Get(id,ct);if(r is null)return Results.NotFound();
            return Results.Ok(IntakeOriginalPreview.Read(r.Path,await store.Content(r,ct)));
        });
        app.MapPost("/api/import/reviews/{id:guid}/answer",async(Guid id,IntakeAnswer a,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            try { return Results.Ok(await Store(db).Answer(id,a,ct)); }
            catch(IntakeConflictException){return Results.Conflict(new{error="状態または回答版が変わりました。再取得してください。"});}
            catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
            catch(KeyNotFoundException){return Results.NotFound();}
        });
        app.MapPost("/api/import/reviews/{id:guid}/refresh-questions",async(Guid id,RefreshQuestionsRequest a,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            try { return Results.Ok(await Store(db).RefreshQuestions(id,a.Version,a.Hash,a.OperationId,ct)); }
            catch(IntakeConflictException){return Results.Conflict(new{error="質問更新の対象状態または版が変わりました。再送せず再取得してください。"});}
            catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
            catch(InvalidDataException){return Results.BadRequest(new{error="原典または質問形式を確認できません。更新していません。"});}
            catch(KeyNotFoundException){return Results.NotFound();}
        });
        app.MapPost("/api/import/reviews/{id:guid}/resume-resolved",async(Guid id,ResumeRequest a,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var store=Store(db);var r=await store.Get(id,ct);if(r is null)return Results.NotFound();
            try
            {
                var service=await IntakeRuleEndpoints.ImportService(app,db,ct);
                return Results.Ok(await store.Run(r,a.Version,a.Hash,false,
                    (bytes,date,token)=>service.ImportAsync(r.Path,bytes,date,token,r.SelectedSkus,r.MappingResolutions,allowPartial:true),ct,resolvedOnly:true));
            }
            catch(IntakeConflictException){return Results.Conflict(new{error="回答済み行の取込対象ではない、保存結果不明、または古い版です。再取得してください。"});}
        });
        app.MapPost("/api/import/reviews/{id:guid}/resume",async(Guid id,ResumeRequest a,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var store=Store(db);var r=await store.Get(id,ct);if(r is null)return Results.NotFound();
            try
            {
                var service=await IntakeRuleEndpoints.ImportService(app,db,ct);
                return Results.Ok(await store.Run(r,a.Version,a.Hash,false,
                    (bytes,date,token)=>service.ImportAsync(r.Path,bytes,date,token,r.SelectedSkus,r.MappingResolutions),ct));
            }
            catch(IntakeConflictException){return Results.Conflict(new{error="回答未確定、保存結果不明、または古い版です。再取得してください。"});}
        });
    }
}
