using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogSearch.Infrastructure;

public static class EneosRowPolicy
{
    public static IReadOnlyList<SourceRowOccurrence> Sources(SupplierInventoryRow r) => r.Occurrences.Count > 0
        ? r.Occurrences : [new(r.SourceSheet,r.SourceRow,r.RawCellsJson,r.Inventory)];
    public static bool SameIdentity(SupplierInventoryRow a, SupplierInventoryRow b) =>
        a.SupplierProductCode==b.SupplierProductCode && a.ProductNameJa==b.ProductNameJa &&
        a.ProducerNameJa==b.ProducerNameJa && a.VolumeMl==b.VolumeMl && a.VintageRaw==b.VintageRaw &&
        a.Country==b.Country && a.Region==b.Region && a.ProductType==b.ProductType;
    public static bool SamePrices(SupplierInventoryRow a, SupplierInventoryRow b)
    {
        if (a.PriceComponents.Count==0 || a.PriceComponents.Count!=b.PriceComponents.Count || a.CaseSize!=b.CaseSize) return false;
        return a.PriceComponents.All(p => p.Amount is >= 0 && p.Unit is "PerBottle" or "PerCase" &&
            b.PriceComponents.Count(q=>q.Kind==p.Kind && q.Amount==p.Amount && q.TaxIncluded==p.TaxIncluded && q.Unit==p.Unit)==1);
    }
    public static InventoryValue Inventory(IEnumerable<InventoryValue> values)
    {
        var all=values.ToArray();var quantities=all.Select(v=>v.Quantity).Distinct().ToArray();
        var statuses=all.Select(v=>v.Status).Distinct().ToArray();
        return new(quantities.Length==1?quantities[0]:null,statuses.Length==1?statuses[0]:InventoryStatus.Unknown,
            string.Join(" / ",all.Select(v=>v.RawValue).Distinct()));
    }
}

// SourceDocument+product uniqueness is the accepted-row ledger. Pending rows stay in the
// immutable original and IntakeReview receipt; retry never adds evidence for accepted groups.
public sealed class EneosInventoryStore(CatalogSearchDbContext db) : IInventoryStore
{
    public async Task<ImportResult> SaveAsync(SourceInput input,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
    {
        if(input.SupplierCode!="ENEOS")throw new ArgumentException("ENEOS policy is supplier scoped.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({input.SupplierCode},0))",ct);
        var supplier=await db.Suppliers.SingleOrDefaultAsync(s=>s.Code==input.SupplierCode,ct);
        if(supplier is null){supplier=new(){Code=input.SupplierCode,Name=input.SupplierName};db.Suppliers.Add(supplier);}
        var source=await db.SourceDocuments.SingleOrDefaultAsync(s=>s.SupplierId==supplier.Id&&s.FileHash==hash,ct);
        var repeated=source is not null;
        var newer=await db.SourceDocuments.AnyAsync(s=>s.SupplierId==supplier.Id&&s.ObservedAt>input.ObservedAt,ct);
        if(source is not null && (source.ObservedAt!=input.ObservedAt || !source.Content.SequenceEqual(input.Content)))
            throw new FolderImportHeldException("保存済み原本の日付または内容と一致しません。");
        if(source is null && newer)
            throw new FolderImportHeldException("新しい資料が登録済みです。古い資料は保留します。");
        if(source is null){source=new(){SupplierId=supplier.Id,FileName=input.FileName,FileHash=hash,Content=input.Content,ObservedAt=input.ObservedAt,ImportedAt=DateTimeOffset.UtcNow};db.SourceDocuments.Add(source);}
        var job=await db.ExtractionJobs.SingleOrDefaultAsync(j=>j.SourceDocumentId==source.Id,ct);
        if(job is null){job=new(){SourceDocumentId=source.Id,ParserVersion=input.ParserVersion};db.ExtractionJobs.Add(job);}
        var snapshot=await db.InventorySnapshots.SingleOrDefaultAsync(s=>s.SourceDocumentId==source.Id,ct);
        if(snapshot is null){snapshot=new(){SourceDocumentId=source.Id,ObservedAt=input.ObservedAt};db.InventorySnapshots.Add(snapshot);}
        var products=await db.SupplierProducts.Where(p=>p.SupplierId==supplier.Id).ToDictionaryAsync(p=>p.SupplierProductCode,ct);
        var accepted=(await db.SourceEvidence.Where(e=>e.SourceDocumentId==source.Id).Select(e=>e.SupplierProductId).ToListAsync(ct)).ToHashSet();
        var sameDay=await (from e in db.SourceEvidence join s in db.SourceDocuments on e.SourceDocumentId equals s.Id
            where s.SupplierId==supplier.Id&&s.ObservedAt==input.ObservedAt select e).ToListAsync(ct);
        var holds=new List<ImportRowHold>();int added=0;
        foreach(var group in rows.GroupBy(r=>r.SupplierProductCode,StringComparer.Ordinal))
        {
            var row=group.First();products.TryGetValue(group.Key,out var product);
            if(product is not null&&accepted.Contains(product.Id))continue;
            var occurrences=group.SelectMany(EneosRowPolicy.Sources).ToArray();
            if(input.SelectedSkus is not null&&!input.SelectedSkus.Contains(group.Key,StringComparer.Ordinal))
            {holds.Add(new(group.Key,"今回の回答・再開対象外のため保留しています。",occurrences));continue;}
            var peers=product is null?[]:sameDay.Where(e=>e.SupplierProductId==product.Id)
                .Select(e=>JsonSerializer.Deserialize<SupplierInventoryRow>(e.ExtractedRowJson)!).ToArray();
            var internalConflict=group.Skip(1).Any(r=>!EneosRowPolicy.SameIdentity(row,r)||
                row.ReferenceRetailPrice!=r.ReferenceRetailPrice||row.TaxIncluded!=r.TaxIncluded||
                !row.PriceComponents.SequenceEqual(r.PriceComponents));
            string? reason=internalConflict?"同じ品番の名称・容量・年・価格に相違があります。":
                peers.Any(p=>!EneosRowPolicy.SameIdentity(row,p))?"同日資料の同じ品番で商品属性が一致しません。":
                peers.Any(p=>!EneosRowPolicy.SamePrices(row,p))?"上代/納入価格・税区分・単品/ケースの一致を確認できません。":null;
            if(newer || product?.CurrentObservedAt>input.ObservedAt)reason="より新しい資料があるため保留します。";
            if(reason is not null){holds.Add(new(group.Key,reason,occurrences));continue;}
            var aggregate=EneosRowPolicy.Inventory(occurrences.Select(o=>o.Inventory));
            row=row with{Inventory=aggregate,Occurrences=occurrences};
            if(product is null){product=new(){SupplierId=supplier.Id,SupplierProductCode=row.SupplierProductCode,SupplierProductName=row.ProductNameJa};db.SupplierProducts.Add(product);products.Add(group.Key,product);db.ReviewTasks.Add(new(){SupplierProductId=product.Id});}
            db.PriceHistories.Add(new(){SupplierProductId=product.Id,SourceDocumentId=source.Id,Amount=row.ReferenceRetailPrice,RawValue=row.PriceRaw,TaxIncluded=row.TaxIncluded,ObservedAt=input.ObservedAt});
            db.InventoryItems.Add(new(){SupplierProductId=product.Id,InventorySnapshotId=snapshot.Id,Quantity=aggregate.Quantity,Status=aggregate.Status,RawValue=aggregate.RawValue});
            db.SourceEvidence.Add(new(){SourceDocumentId=source.Id,SupplierProductId=product.Id,SheetName=row.SourceSheet,RowNumber=row.SourceRow,RawCellsJson=row.RawCellsJson,ExtractedRowJson=JsonSerializer.Serialize(row)});
            var current=EneosRowPolicy.Inventory(peers.SelectMany(EneosRowPolicy.Sources).Concat(occurrences).Select(o=>o.Inventory));
            product.SupplierProductName=row.ProductNameJa;product.ProductNameEn=row.ProductNameEn;product.ProducerNameJa=row.ProducerNameJa;product.ProducerNameEn=row.ProducerNameEn;
            product.Country=row.Country;product.Region=row.Region;product.ProductType=row.ProductType;product.Vintage=row.Vintage;product.VintageRaw=row.VintageRaw;product.VolumeMl=row.VolumeMl;
            product.CurrentPrice=row.ReferenceRetailPrice;product.TaxIncluded=row.TaxIncluded;product.CurrentInventoryQuantity=current.Quantity;product.CurrentInventoryStatus=current.Status;product.CurrentObservedAt=input.ObservedAt;product.CurrentSourceDocumentId=source.Id;
            var doc=await db.ProductSearchDocuments.SingleOrDefaultAsync(d=>d.EntityType==SearchEntityType.SupplierProduct&&d.SourceRecordId==product.Id,ct);
            if(doc is null){doc=new(){EntityType=SearchEntityType.SupplierProduct,SourceRecordId=product.Id,SearchText=""};db.ProductSearchDocuments.Add(doc);}
            doc.SearchText=string.Join(" ",row.ProductNameJa,row.ProductNameEn,row.ProducerNameJa,row.ProducerNameEn,row.VintageRaw,row.ProductType,row.Country,row.Region);
            doc.StructuredDataJson=JsonSerializer.Serialize(new{row.SupplierProductCode,row.JanCodes});doc.UpdatedAt=DateTimeOffset.UtcNow;
            accepted.Add(product.Id);added++;
        }
        job.RowCount=accepted.Count;job.Status=holds.Count>0?"Partial":"Completed";
        db.OperationLogs.Add(new(){SourceDocumentId=source.Id,Status=job.Status,Action="EneosRowImport",RowCount=added,CreatedAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return new(source.Id,accepted.Count,repeated&&added==0&&holds.Count==0,holds);
    }
}
