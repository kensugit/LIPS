using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Infrastructure;

// Accepted evidence is an append-only per-source/product ledger. No same-day source merging.
public sealed class LaVineeInventoryStore(CatalogSearchDbContext db):IInventoryStore
{
    public async Task<ImportResult> SaveAsync(SourceInput input,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
    {
        if(input.SupplierCode!="LA_VINEE")throw new ArgumentException("LaVinee policy is supplier scoped.");
        if(!string.Equals(Convert.ToHexString(SHA256.HashData(input.Content)),hash,StringComparison.OrdinalIgnoreCase))
            throw new FolderImportHeldException("原典hashが一致しません。");
        hash=hash.ToUpperInvariant();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({input.SupplierCode},0))",ct);
        var supplier=await db.Suppliers.SingleOrDefaultAsync(s=>s.Code==input.SupplierCode,ct);
        if(supplier is null){supplier=new(){Code=input.SupplierCode,Name=input.SupplierName};db.Suppliers.Add(supplier);}
        var source=await db.SourceDocuments.SingleOrDefaultAsync(s=>s.SupplierId==supplier.Id&&s.FileHash==hash,ct);
        var repeated=source is not null;
        if(source is not null&&(source.ObservedAt!=input.ObservedAt||!source.Content.SequenceEqual(input.Content)))
            throw new FolderImportHeldException("保存済み原本の日付または内容と一致しません。");
        if(await db.SourceDocuments.AnyAsync(s=>s.SupplierId==supplier.Id&&s.FileHash!=hash&&s.ObservedAt>=input.ObservedAt,ct))
            throw new FolderImportHeldException("同日別内容または新しい資料が登録済みです。資料日付と版を確認してください。");
        if(source is null){source=new(){SupplierId=supplier.Id,FileName=input.FileName,FileHash=hash,Content=input.Content,ObservedAt=input.ObservedAt,ImportedAt=DateTimeOffset.UtcNow};db.SourceDocuments.Add(source);}
        var job=await db.ExtractionJobs.SingleOrDefaultAsync(j=>j.SourceDocumentId==source.Id,ct);
        if(job is null){job=new(){SourceDocumentId=source.Id,ParserVersion=input.ParserVersion};db.ExtractionJobs.Add(job);}
        var snapshot=await db.InventorySnapshots.SingleOrDefaultAsync(s=>s.SourceDocumentId==source.Id,ct);
        if(snapshot is null){snapshot=new(){SourceDocumentId=source.Id,ObservedAt=input.ObservedAt};db.InventorySnapshots.Add(snapshot);}
        var products=await db.SupplierProducts.Where(p=>p.SupplierId==supplier.Id).ToDictionaryAsync(p=>p.SupplierProductCode,ct);
        var accepted=(await db.SourceEvidence.Where(e=>e.SourceDocumentId==source.Id).Select(e=>e.SupplierProductId).ToListAsync(ct)).ToHashSet();
        bool AlreadyAccepted(string sku)=>products.TryGetValue(sku,out var p)&&accepted.Contains(p.Id);
        var holds=(input.PendingHolds??[]).Where(h=>!AlreadyAccepted(h.Sku)).ToList();
        var blocked=holds.Select(h=>h.Sku).ToHashSet(StringComparer.Ordinal);int added=0;
        foreach(var group in rows.GroupBy(r=>r.SupplierProductCode,StringComparer.Ordinal))
        {
            if(AlreadyAccepted(group.Key)||blocked.Contains(group.Key))continue;
            var row=group.First();var occurrences=group.SelectMany(EneosRowPolicy.Sources).ToArray();
            string? reason=group.Count()!=1?"同じ商品コードが複数行にあります。商品同一性は未確定です。":
                input.SelectedSkus is not null&&!input.SelectedSkus.Contains(group.Key,StringComparer.Ordinal)?"今回の明示再開対象外です。":null;
            if(string.IsNullOrWhiteSpace(row.SupplierProductCode)||string.IsNullOrWhiteSpace(row.ProductNameJa)||row.VolumeMl<=0||string.IsNullOrWhiteSpace(row.Region))
                reason="商品識別・容量・地域の必須情報を確認できません。";
            if(row.Inventory.Quantity is null or <0)reason="在庫数量を整数本として確認できません。";
            if(row.ReferenceRetailPrice is not null||row.PriceRaw.Length!=0||row.TaxIncluded||row.PriceComponents.Count!=1||
                row.PriceComponents[0] is not {Kind:"Wholesale",TaxIncluded:false,Unit:"PerBottle",Amount:>=0})
                reason="税別納価と1本単位の明示確認が必要です。希望小売価格へ転記しません。";
            products.TryGetValue(group.Key,out var product);
            if(product?.CurrentObservedAt>input.ObservedAt)reason="より新しい資料があるため保留します。";
            if(reason is not null){holds.Add(new(group.Key,reason,occurrences));continue;}
            if(product is null){product=new(){SupplierId=supplier.Id,SupplierProductCode=row.SupplierProductCode,SupplierProductName=row.ProductNameJa};db.SupplierProducts.Add(product);products.Add(group.Key,product);db.ReviewTasks.Add(new(){SupplierProductId=product.Id});}
            // Wholesale amount/unit remain in full extracted evidence, never retail fields.
            db.PriceHistories.Add(new(){SupplierProductId=product.Id,SourceDocumentId=source.Id,Amount=null,RawValue="",TaxIncluded=false,ObservedAt=input.ObservedAt});
            db.InventoryItems.Add(new(){SupplierProductId=product.Id,InventorySnapshotId=snapshot.Id,Quantity=row.Inventory.Quantity,Status=row.Inventory.Status,RawValue=row.Inventory.RawValue});
            db.SourceEvidence.Add(new(){SourceDocumentId=source.Id,SupplierProductId=product.Id,SheetName=row.SourceSheet,RowNumber=row.SourceRow,RawCellsJson=row.RawCellsJson,ExtractedRowJson=JsonSerializer.Serialize(row)});
            product.SupplierProductName=row.ProductNameJa;product.ProductNameEn=row.ProductNameEn;product.ProducerNameJa=row.ProducerNameJa;product.ProducerNameEn=row.ProducerNameEn;
            product.Country=row.Country;product.Region=row.Region;product.ProductType=row.ProductType;product.Vintage=row.Vintage;product.VintageRaw=row.VintageRaw;product.VolumeMl=row.VolumeMl;
            product.CurrentPrice=null;product.TaxIncluded=false;product.CurrentInventoryQuantity=row.Inventory.Quantity;product.CurrentInventoryStatus=row.Inventory.Status;product.CurrentObservedAt=input.ObservedAt;product.CurrentSourceDocumentId=source.Id;
            var doc=await db.ProductSearchDocuments.SingleOrDefaultAsync(d=>d.EntityType==SearchEntityType.SupplierProduct&&d.SourceRecordId==product.Id,ct);
            if(doc is null){doc=new(){EntityType=SearchEntityType.SupplierProduct,SourceRecordId=product.Id,SearchText=""};db.ProductSearchDocuments.Add(doc);}
            doc.SearchText=string.Join(" ",row.ProductNameJa,row.ProductNameEn,row.ProducerNameJa,row.ProducerNameEn,row.VintageRaw,row.ProductType,row.Country,row.Region,row.Grapes,row.AppellationJa,row.AppellationEn,row.OriginalSupplierProductCode);
            doc.StructuredDataJson=JsonSerializer.Serialize(new{row.SupplierProductCode,row.JanCodes});doc.UpdatedAt=DateTimeOffset.UtcNow;
            accepted.Add(product.Id);added++;
        }
        job.RowCount=accepted.Count;job.Status=holds.Count>0?"Partial":"Completed";
        db.OperationLogs.Add(new(){SourceDocumentId=source.Id,Status=job.Status,Action="LaVineeRowImport",RowCount=added,CreatedAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return new(source.Id,accepted.Count,repeated&&added==0&&holds.Count==0,holds){AddedProducts=added};
    }
}
