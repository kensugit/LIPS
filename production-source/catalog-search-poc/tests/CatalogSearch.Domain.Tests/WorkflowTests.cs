using System.Text;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class WorkflowTests
{
    const string Header="SKU,ProductNameJa,VolumeMl,Price,Inventory,Vintage,Tax\n";
    [Theory] [InlineData(65001)] [InlineData(932)]
    public void CsvEncodingQuotedFieldsAndTax(int codepage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var rows=new StandardCsvParser().Parse(Encoding.GetEncoding(codepage).GetBytes(Header+"A,\"日本語,ワイン\",750,4800,12,NV,\nB,白,375,オープン,,MV,税込\n"),default);
        Assert.Equal(2,rows.Count); Assert.Equal("日本語,ワイン",rows[0].ProductNameJa);
        Assert.False(rows[0].TaxIncluded);Assert.True(rows[1].TaxIncluded);
        Assert.Equal(12,rows[0].Inventory.Quantity);Assert.Equal(InventoryStatus.Unknown,rows[1].Inventory.Status);
        Assert.Null(rows[1].ReferenceRetailPrice);Assert.Equal("MV",rows[1].VintageRaw);
        Assert.Equal(2,rows[0].SourceRow);Assert.Contains("ProductNameJa",rows[0].RawCellsJson);
    }
    [Theory] [InlineData("A,白,0,100,1,NV,")] [InlineData("A,白,750,-1,1,NV,")] [InlineData("A,白,750,100,1,NV,不明")] [InlineData("A,白,750")]
    public void InvalidCsvRejected(string line)=>Assert.Throws<InvalidDataException>(()=>new StandardCsvParser().Parse(Encoding.UTF8.GetBytes(Header+line),default));
    [Theory] [InlineData("2026/10入荷")] [InlineData("-3")] [InlineData("約10本")]
    public void AmbiguousStockIsNotQuantity(string raw){var v=InventoryParser.Parse(raw);Assert.Null(v.Quantity);Assert.Equal(InventoryStatus.Unknown,v.Status);}
    [PostgresFact]
    public async Task CsvSupplierIdentitySyncAndReviewArePersisted()
    {
        var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options;
        await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();
        var code="CSV_"+Guid.NewGuid().ToString("N").ToUpperInvariant();
        var csv=Encoding.UTF8.GetBytes(Header+"SAME,確認用サンプル ピノ・ノワール,750,4800,12,2023,");
        var service=new ContentImportService(db);var date=DateTimeOffset.UtcNow;
        var first=await service.ImportAsync("a.csv",csv,date,code,code,default);
        Assert.True((await service.ImportAsync("a.csv",csv,date,code,code,default)).AlreadyImported);
        await service.ImportAsync("a.csv",csv,date,code+"B",code+"B",default);
        var rows=await (from r in db.SupplierProducts join s in db.Suppliers on r.SupplierId equals s.Id where s.Code==code||s.Code==code+"B" select r).ToListAsync();
        Assert.Equal(2,rows.Count);Assert.All(rows,r=>Assert.Null(r.ProductId));
        var sync=new InternalSyncService(db);await sync.SyncAsync(new FixtureInternalProductSource(),default);
        Assert.Equal(0,await sync.SyncAsync(new FixtureInternalProductSource(),default));
        var source=await db.SourceSystems.SingleAsync(x=>x.Code=="SHUDORAKU_FIXTURE");
        var records=await db.InternalProductRecords.Where(x=>x.SourceSystemId==source.Id).OrderBy(x=>x.SourceKey).ToListAsync();
        Assert.Equal(4,records.Count);Assert.Equal(3,records.Count(x=>x.IsActive));
        Assert.Equal(4,await db.InternalSnapshots.CountAsync(x=>records.Select(r=>r.Id).Contains(x.InternalProductRecordId)));
        var target=records[0];var review=new ReviewService(db);
        var product=await review.ApplyAsync(new("internal",target.Id,rows[0].Id,null,"link"),default);Assert.NotNull(product);
        await using(var stale=new CatalogSearchDbContext(options))await Assert.ThrowsAsync<ReviewConflictException>(()=>new ReviewService(stale).ApplyAsync(new("internal",target.Id,null,null,"unlink"),default));
        Assert.Null(await review.ApplyAsync(new("internal",target.Id,null,product,"unlink"),default));
        await review.ApplyAsync(new("internal",target.Id,null,null,"reject"),default);
        Assert.Equal(LinkStatus.Rejected,(await db.InternalProductLinks.FindAsync(target.Id))!.Status);
        Assert.Equal(4800,rows[0].CurrentPrice);Assert.Equal(12,rows[0].CurrentInventoryQuantity);
        Assert.Equal(csv,(await db.SourceDocuments.FindAsync(first.SourceDocumentId))!.Content);
    }
    sealed class SnapshotSource(string code, InternalProductSourceRow row):IInternalProductSource
    {
        public string SourceSystemCode=>code;
        public async IAsyncEnumerable<InternalProductSourceRow> ReadChangedAsync(DateTimeOffset? since,[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken){await Task.CompletedTask;yield return row;}
    }
    [PostgresFact]
    public async Task OldInternalSnapshotIsPreservedWithoutReplacingCurrent()
    {
        var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options;
        await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();
        var code="HISTORY_"+Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow;
        var row=new InternalProductSourceRow("ONE","現在の商品",null,null,null,750,null,null,true,now,"{}");
        var sync=new InternalSyncService(db);Assert.Equal(1,await sync.SyncAsync(new SnapshotSource(code,row),default));
        Assert.Equal(0,await sync.SyncAsync(new SnapshotSource(code,row with {ProductName="古い商品",ObservedAt=now.AddDays(-1)}),default));
        var source=await db.SourceSystems.SingleAsync(x=>x.Code==code);var record=await db.InternalProductRecords.SingleAsync(x=>x.SourceSystemId==source.Id);
        Assert.Equal("現在の商品",record.ProductName);Assert.Equal(2,await db.InternalSnapshots.CountAsync(x=>x.InternalProductRecordId==record.Id));
    }
}
