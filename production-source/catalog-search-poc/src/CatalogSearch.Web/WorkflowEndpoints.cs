using System.Globalization;
using System.Text.Json;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Web;
public sealed record CatalogEntry(string Kind, Guid Id, string Code, string Name, string Source, int? VolumeMl, string Vintage, bool Active, Guid? ProductId, string ReviewStatus, decimal? Price, bool? TaxIncluded, int? Quantity, string Inventory)
{
    public string CodeKind { get; init; } = "SupplierSku";
    public string? OriginalSupplierProductCode { get; init; }
}
public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapPost("/api/import",async(HttpRequest request,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            if(!request.HasFormContentType)return Results.BadRequest(new{error="ファイルを選択してください。"});
            var form=await request.ReadFormAsync(ct);var file=form.Files.GetFile("file");
            if(file is null||file.Length>20_000_000)return Results.BadRequest(new{error="20MB以下のファイルを選択してください。"});
            if(!DateTime.TryParseExact(form["observedAt"],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))return Results.BadRequest(new{error="在庫表の日付を入力してください。"});
            using var stream=new MemoryStream();await file.CopyToAsync(stream,ct);
            try{return Results.Ok(await new ContentImportService(db).ImportAsync(file.FileName,stream.ToArray(),new DateTimeOffset(date,TimeSpan.Zero),form["supplierCode"].ToString(),form["supplierName"].ToString(),ct));}
            catch(Exception ex) when(ex is InvalidDataException or ArgumentException or FormatException or Microsoft.VisualBasic.FileIO.MalformedLineException or ClosedXML.Excel.Exceptions.ClosedXMLException)
            {
                db.ChangeTracker.Clear();db.OperationLogs.Add(new OperationLog{Action="FileImport",Status="Failed",CreatedAt=DateTimeOffset.UtcNow,Details="形式・必須列・SKU・値を確認してください。"});await db.SaveChangesAsync(ct);
                return Results.BadRequest(new{error="取込できませんでした。対応形式・必須列・SKU重複・入力値を確認してください。登録は行っていません。"});
            }
        });
        app.MapPost("/api/fixtures/internal",async(CatalogSearchDbContext db,CancellationToken ct)=>Results.Ok(new{changed=await new InternalSyncService(db).SyncAsync(new FixtureInternalProductSource(),ct)}));
        app.MapGet("/api/catalog",async(string? text,string? source,bool? includeInactive,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            if(text?.Length>200)return Results.BadRequest(new{error="検索語は200文字以内にしてください。"});
            var result=new List<CatalogEntry>();
            if(source!="internal")
            {
                var supplierQuery=from p in db.SupplierProducts.AsNoTracking() join s in db.Suppliers on p.SupplierId equals s.Id join t in db.ReviewTasks on p.Id equals t.SupplierProductId select new{p,s,t};
                foreach(var term in SearchTerms.Parse(text))
                {var pattern=SearchTerms.Pattern(term);supplierQuery=supplierQuery.Where(x=>EF.Functions.ILike(x.p.SupplierProductName,pattern,"\\")||EF.Functions.ILike(x.p.SupplierProductCode,pattern,"\\")||EF.Functions.ILike(x.p.ProductNameEn,pattern,"\\")||EF.Functions.ILike(x.p.ProducerNameJa,pattern,"\\")||EF.Functions.ILike(x.p.ProducerNameEn,pattern,"\\"));}
                var suppliers=await supplierQuery.OrderBy(x=>x.p.SupplierProductCode).Take(2000).ToListAsync(ct);
                result.AddRange(suppliers.Select(x=>new CatalogEntry("supplier",x.p.Id,x.p.SupplierProductCode,x.p.SupplierProductName,x.s.Name,x.p.VolumeMl,x.p.VintageRaw,true,x.p.ProductId,x.t.Status,x.p.CurrentPrice,x.p.TaxIncluded,x.p.CurrentInventoryQuantity,x.p.CurrentInventoryStatus.ToString()) { CodeKind = x.p.CodeKind, OriginalSupplierProductCode = x.p.OriginalSupplierProductCode }));
            }
            if(source!="supplier")
            {
                var query=db.InternalProductRecords.AsNoTracking().Where(x=>includeInactive==true||x.IsActive);
                foreach(var term in SearchTerms.Parse(text))
                {var pattern=SearchTerms.Pattern(term);query=query.Where(x=>EF.Functions.ILike(x.ProductName,pattern,"\\")||EF.Functions.ILike(x.SourceKey,pattern,"\\")||(x.ProductNameKana!=null&&EF.Functions.ILike(x.ProductNameKana,pattern,"\\"))||(x.Notes!=null&&EF.Functions.ILike(x.Notes,pattern,"\\")));}
                var internals=await query.OrderBy(x=>x.SourceKey).Take(2000).ToListAsync(ct);
                var links=await db.InternalProductLinks.AsNoTracking().ToDictionaryAsync(x=>x.InternalProductRecordId,ct);
                result.AddRange(internals.Select(x=>new CatalogEntry("internal",x.Id,x.SourceKey,x.ProductName,"酒道楽（匿名サンプル）",x.VolumeMl,ReviewService.Vintage(x),x.IsActive,links.GetValueOrDefault(x.Id)?.ProductId,links.GetValueOrDefault(x.Id)?.Status.ToString()??"Unmatched",null,null,null,"Unknown")));
            }
            return Results.Ok(new{rows=result,limit=2000});
        });
        app.MapGet("/api/internal/{id:guid}",async(Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            var row=await db.InternalProductRecords.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(row is null)return Results.NotFound();
            return Results.Ok(new{row,history=await db.InternalSnapshots.AsNoTracking().Where(x=>x.InternalProductRecordId==id).OrderByDescending(x=>x.ObservedAt).Select(x=>new{x.ObservedAt,x.Hash,x.PayloadJson}).ToListAsync(ct)});
        });
        app.MapGet("/api/review/{type}/{id:guid}",async(string type,Guid id,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            if(type is not("internal" or "supplier"))return Results.BadRequest();
            if(type=="internal"?!await db.InternalProductRecords.AnyAsync(x=>x.Id==id,ct):!await db.SupplierProducts.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            return Results.Ok(await new ReviewService(db).CandidatesAsync(type,id,ct));
        });
        app.MapPost("/api/review",async(ReviewRequest request,CatalogSearchDbContext db,CancellationToken ct)=>
        {
            if(request.Action=="link"&&string.IsNullOrEmpty(request.ExpectedRecordVersion))return Results.BadRequest(new{error="候補を再読み込みしてから紐付けてください。"});
            try{return Results.Ok(new{productId=await new ReviewService(db).ApplyAsync(request,ct)});}
            catch(ReviewConflictException){return Results.Conflict(new{error="状態または候補が変わっています。再読み込みして確認してください。"});}
            catch(Exception ex)when(ex is ArgumentException or InvalidOperationException){return Results.BadRequest(new{error="対象または操作を確認してください。"});}
        });
        app.MapGet("/api/activity",async(CatalogSearchDbContext db,CancellationToken ct)=>Results.Ok(new{
            logs=await db.OperationLogs.AsNoTracking().OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync(ct),
            reviews=await db.ReviewActions.AsNoTracking().OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync(ct),
            watch=await db.WatchFileStates.AsNoTracking().OrderByDescending(x=>x.NextAttemptAt).Take(100).ToListAsync(ct)}));
        app.MapGet("/api/watch/info",()=>Results.Ok(new{inbox=app.Configuration["CatalogDemo:Inbox"],enabled=app.Configuration.GetValue<bool>("CatalogDemo:WatchEnabled")}));
        app.MapPost("/api/watch/retry",async(CatalogSearchDbContext db,CancellationToken ct)=>
        {var items=await db.WatchFileStates.Where(x=>x.Status=="Failed").ToListAsync(ct);foreach(var item in items){item.Status="Pending";item.Attempts=0;item.NextAttemptAt=DateTimeOffset.UtcNow;}await db.SaveChangesAsync(ct);return Results.Ok(new{retry=items.Count});});
    }
}
