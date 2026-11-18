using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.EntityFrameworkCore;
namespace CatalogSearch.Infrastructure;
public sealed class InternalSyncService(CatalogSearchDbContext db)
{
    public async Task<int> SyncAsync(IInternalProductSource adapter, CancellationToken ct)
    {
        var rows = new List<InternalProductSourceRow>();
        await foreach (var row in adapter.ReadChangedAsync(null, ct)) rows.Add(row);
        if (rows.Select(x=>x.SourceKey).Distinct().Count()!=rows.Count) throw new InvalidDataException("Duplicate source keys.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190218)",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({adapter.SourceSystemCode},0))",ct);
        var source=await db.SourceSystems.SingleOrDefaultAsync(x=>x.Code==adapter.SourceSystemCode,ct);
        if(source is null){source=new SourceSystem {Code=adapter.SourceSystemCode,Name="酒道楽（匿名サンプル）",Kind=SourceSystemKind.Shudoraku};db.SourceSystems.Add(source);}
        var records=await db.InternalProductRecords.Where(x=>x.SourceSystemId==source.Id).ToDictionaryAsync(x=>x.SourceKey,ct);
        var changed=0;
        foreach(var row in rows)
        {
            var payload=JsonSerializer.Serialize(row);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            if(!records.TryGetValue(row.SourceKey,out var record))
            {record=new InternalProductRecord{SourceSystemId=source.Id,SourceKey=row.SourceKey,ProductName=row.ProductName};db.InternalProductRecords.Add(record);}
            if(record.RecordHash==hash)continue;
            if(!await db.InternalSnapshots.AnyAsync(x=>x.InternalProductRecordId==record.Id&&x.Hash==hash,ct))
                db.InternalSnapshots.Add(new InternalSnapshot{InternalProductRecordId=record.Id,Hash=hash,PayloadJson=payload,ObservedAt=row.ObservedAt.ToUniversalTime()});
            if(record.ObservedAt>row.ObservedAt)continue;
            record.ProductName=row.ProductName;record.ProductNameKana=row.ProductNameKana;record.JanCode=row.JanCode;
            record.SupplierCode=row.SupplierCode;record.VolumeMl=row.VolumeMl;record.CategoryCode=row.CategoryCode;
            record.Notes=row.Notes;record.IsActive=row.IsActive;record.ObservedAt=row.ObservedAt.ToUniversalTime();record.SourcePayloadJson=row.SourcePayloadJson;record.RecordHash=hash;
            var doc=await db.ProductSearchDocuments.SingleOrDefaultAsync(x=>x.EntityType==SearchEntityType.InternalProduct&&x.SourceRecordId==record.Id,ct);
            if(doc is null){doc=new ProductSearchDocument{EntityType=SearchEntityType.InternalProduct,SourceRecordId=record.Id,SearchText=""};db.ProductSearchDocuments.Add(doc);}
            doc.SearchText=string.Join(" ",row.ProductName,row.ProductNameKana,row.Notes);doc.UpdatedAt=DateTimeOffset.UtcNow;
            changed++;
        }
        db.OperationLogs.Add(new OperationLog{Action="InternalSync",Target=adapter.SourceSystemCode,Status="Completed",RowCount=changed,CreatedAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return changed;
    }
}
