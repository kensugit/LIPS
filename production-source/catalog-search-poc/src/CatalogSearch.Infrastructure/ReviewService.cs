using System.Text;
using System.Text.Json;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Infrastructure;
public sealed record ReviewRequest(string EntityType, Guid RecordId, Guid? CandidateSupplierId, Guid? ExpectedProductId, string Action, Guid? ExpectedCandidateSourceDocumentId = null, string? ExpectedRecordVersion = null);
public sealed class ReviewConflictException : Exception;
public sealed class ReviewService(CatalogSearchDbContext db)
{
    public static string Normalize(string text)=>new(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    public static string Vintage(InternalProductRecord record)
    {using var json=JsonDocument.Parse(record.SourcePayloadJson);return json.RootElement.TryGetProperty("Vintage",out var v)?v.GetString()??"":"";}
    public async Task<object> CandidatesAsync(string type, Guid id, CancellationToken ct)
    {
        string name,vintage,producerJa="",producerEn="",version;int? volume;Guid? current;
        if(type=="internal")
        {var r=await db.InternalProductRecords.SingleAsync(x=>x.Id==id,ct);name=r.ProductName;version=r.RecordHash;volume=r.VolumeMl;vintage=Vintage(r);current=(await db.InternalProductLinks.FindAsync([id],ct))?.ProductId;}
        else if(type=="supplier")
        {var r=await db.SupplierProducts.SingleAsync(x=>x.Id==id,ct);name=r.SupplierProductName;producerJa=r.ProducerNameJa;producerEn=r.ProducerNameEn;version=r.CurrentSourceDocumentId?.ToString()??"";volume=r.VolumeMl;vintage=r.VintageRaw;current=r.ProductId;}
        else throw new ArgumentException("Invalid entity type.");
        var pool=await db.SupplierProducts.AsNoTracking().Where(x=>x.VolumeMl==volume&&x.VintageRaw==vintage&&x.Id!=id).ToListAsync(ct);
        var candidates=pool.Where(x=>CandidatePolicy.Matches(name,volume,vintage,producerJa,producerEn,x.SupplierProductName,x.VolumeMl,x.VintageRaw,x.ProducerNameJa,x.ProducerNameEn)).OrderBy(x=>x.SupplierProductCode).Select(x=>new{x.Id,x.SupplierId,x.SupplierProductCode,x.CodeKind,x.OriginalSupplierProductCode,x.SupplierProductName,x.ProducerNameJa,x.ProducerNameEn,x.VintageRaw,x.VolumeMl,x.CurrentPrice,x.TaxIncluded,x.ProductId,x.CurrentSourceDocumentId,Reason="名称・容量・ヴィンテージ一致。生産者は両側に同言語の記載がある場合に照合。JAN・箱違い等は元資料で要確認。"}).ToArray();
        return new {entityType=type,recordId=id,name,volumeMl=volume,vintage,currentProductId=current,recordVersion=version,producerNameJa=producerJa,producerNameEn=producerEn,candidates};
    }
    public async Task<Guid?> ApplyAsync(ReviewRequest request,CancellationToken ct)
    {
        if(request.EntityType is not ("supplier" or "internal") || request.Action is not ("link" or "unlink" or "reject"))throw new ArgumentException("Invalid review action.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)",ct);
        SupplierProduct? supplier=null;InternalProductRecord? internalRow=null;InternalProductLink? link=null;Guid? before;
        if(request.EntityType=="supplier") {supplier=await db.SupplierProducts.SingleAsync(x=>x.Id==request.RecordId,ct);before=supplier.ProductId;}
        else{internalRow=await db.InternalProductRecords.SingleAsync(x=>x.Id==request.RecordId,ct);link=await db.InternalProductLinks.FindAsync([request.RecordId],ct);before=link?.ProductId;}
        var recordVersion=supplier is not null?supplier.CurrentSourceDocumentId?.ToString()??"":internalRow!.RecordHash;
        if(request.ExpectedRecordVersion is not null && request.ExpectedRecordVersion!=recordVersion)throw new ReviewConflictException();
        if(before!=request.ExpectedProductId)throw new ReviewConflictException();
        if(request.Action=="reject"&&before is not null)throw new ReviewConflictException();
        Guid? after=before;
        if(request.Action=="unlink")after=null;
        if(request.Action=="link")
        {
            if(request.CandidateSupplierId is null || request.CandidateSupplierId==request.RecordId)throw new ArgumentException("Choose a candidate.");
            var candidate=await db.SupplierProducts.SingleAsync(x=>x.Id==request.CandidateSupplierId,ct);
            var name=supplier?.SupplierProductName??internalRow!.ProductName;
            var volume=supplier?.VolumeMl??internalRow!.VolumeMl;
            var vintage=supplier?.VintageRaw??Vintage(internalRow!);
            if(request.ExpectedRecordVersion is not null && request.ExpectedCandidateSourceDocumentId!=candidate.CurrentSourceDocumentId)throw new ReviewConflictException();
            if(!CandidatePolicy.Matches(name,volume,vintage,supplier?.ProducerNameJa??"",supplier?.ProducerNameEn??"",candidate.SupplierProductName,candidate.VolumeMl,candidate.VintageRaw,candidate.ProducerNameJa,candidate.ProducerNameEn))throw new ReviewConflictException();
            if(candidate.ProductId is null)
            {
                var product=new Product{CanonicalName=candidate.SupplierProductName,Category="WINE",Vintage=candidate.Vintage,VolumeMl=candidate.VolumeMl,Region=candidate.Region};
                db.Products.Add(product);candidate.ProductId=product.Id;
                db.ReviewActions.Add(new ReviewAction{EntityType="supplier",RecordId=candidate.Id,Action="CreateCanonical",AfterProductId=product.Id});
                var candidateDoc=await db.ProductSearchDocuments.SingleOrDefaultAsync(x=>x.EntityType==SearchEntityType.SupplierProduct&&x.SourceRecordId==candidate.Id,ct);
                if(candidateDoc is not null)candidateDoc.ProductId=product.Id;
                var candidateTask=await db.ReviewTasks.SingleOrDefaultAsync(x=>x.SupplierProductId==candidate.Id,ct);
                if(candidateTask is not null)candidateTask.Status="Confirmed";
            }
            after=candidate.ProductId;
        }
        if(supplier is not null)
        {supplier.ProductId=after;var task=await db.ReviewTasks.SingleAsync(x=>x.SupplierProductId==supplier.Id,ct);task.Status=request.Action=="reject"?"Rejected":after.HasValue?"Confirmed":"Pending";}
        else
        {
            if(link is null){link=new InternalProductLink{InternalProductRecordId=request.RecordId};db.InternalProductLinks.Add(link);}
            link.ProductId=after;link.Status=request.Action=="reject"?LinkStatus.Rejected:after.HasValue?LinkStatus.Confirmed:LinkStatus.Unmatched;
            link.ReviewedAt=DateTimeOffset.UtcNow;link.ReviewedBy="Local demo operator";
        }
        var docType=supplier is null?SearchEntityType.InternalProduct:SearchEntityType.SupplierProduct;
        var document=await db.ProductSearchDocuments.SingleOrDefaultAsync(x=>x.EntityType==docType&&x.SourceRecordId==request.RecordId,ct);
        if(document is not null)document.ProductId=after;
        db.ReviewActions.Add(new ReviewAction{EntityType=request.EntityType,RecordId=request.RecordId,BeforeProductId=before,AfterProductId=after,Action=request.Action});
        db.OperationLogs.Add(new OperationLog{Action="Review:"+request.Action,Target=request.RecordId.ToString(),Status="Completed",CreatedAt=DateTimeOffset.UtcNow,RowCount=1});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return after;
    }
}
