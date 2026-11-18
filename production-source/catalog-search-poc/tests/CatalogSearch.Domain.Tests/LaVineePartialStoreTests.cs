using System.Security.Cryptography;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public sealed class LaVineePartialDbFactAttribute:FactAttribute
{public LaVineePartialDbFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIPS_LAVINEE_PARTIAL_CONNECTION")))Skip="Dedicated synthetic LaVinee partial database required.";}}
public class LaVineePartialStoreTests
{
    [LaVineePartialDbFact] public async Task PartialResumeLedgerAndConflictsRemainSupplierScoped()
    {
        var connection=Environment.GetEnvironmentVariable("LIPS_LAVINEE_PARTIAL_CONNECTION")!;var config=new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("lips_partial_lavinee_20261012",config.Database);Assert.Equal("127.0.0.1",config.Host);Assert.Equal(55993,config.Port);
        CatalogSearchDbContext Db()=>new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
        var stamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        using var workbook=new XLWorkbook(new MemoryStream(LaVineeMappingTests.Workbook()));workbook.Worksheet(1).Cell(2,2).Value=stamp+"1";workbook.Worksheet(1).Cell(3,2).Value=stamp+"2";
        using var stream=new MemoryStream();workbook.SaveAs(stream);var bytes=stream.ToArray();
        var answers=LaVineeMappingReview.Inspect(bytes).Select(q=>new IntakeMappingResolution(q,q.Choices.Single(),"合成DB試験のみ" )).ToArray();
        var rows=new LaVineeInventoryParser(answers).Parse(bytes,default);var a=rows[0].SupplierProductCode;var b=rows[1].SupplierProductCode;
        var day=new DateTimeOffset(1900,1,1,0,0,0,TimeSpan.Zero).AddMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()%86400000);
        async Task<ImportResult> Save(byte[] content,DateTimeOffset date,IReadOnlyList<SupplierInventoryRow> data,IReadOnlyList<ImportRowHold>? holds=null,IReadOnlyList<string>? selected=null)
        {await using var db=Db();return await new PostgresInventoryStore(db,true).SaveAsync(new("synthetic.xlsx",content,date,"LA_VINEE","ラヴィネ合成","test"){PendingHolds=holds,SelectedSkus=selected},Convert.ToHexString(SHA256.HashData(content)),data,default);}
        var pending=new ImportRowHold(b,"地域確認保留",EneosRowPolicy.Sources(rows[1]));
        var first=await Save(bytes,day,rows,[pending]);Assert.Equal(1,first.Products);Assert.Equal(1,first.AddedProducts);Assert.Equal(b,Assert.Single(first.Holds!).Sku);
        var selected=await Save(bytes,day,rows,selected:[a]);Assert.Equal(1,selected.Products);Assert.Equal(0,selected.AddedProducts);Assert.Equal(b,Assert.Single(selected.Holds!).Sku);
        var parallel=await Task.WhenAll(Save(bytes,day,rows,selected:[b]),Save(bytes,day,rows,selected:[b]));
        Assert.Equal(1,parallel.Sum(r=>r.AddedProducts));Assert.All(parallel,r=>{Assert.Equal(2,r.Products);Assert.Empty(r.Holds!);Assert.Equal(first.SourceDocumentId,r.SourceDocumentId);});
        var repeat=await Save(bytes,day,rows);Assert.True(repeat.AlreadyImported);Assert.Equal(0,repeat.AddedProducts);
        await using(var check=Db())
        {
            Assert.Equal(2,await check.SourceEvidence.CountAsync(e=>e.SourceDocumentId==first.SourceDocumentId));
            Assert.Equal(2,await check.PriceHistories.CountAsync(e=>e.SourceDocumentId==first.SourceDocumentId));
            var savedSource=await check.SourceDocuments.SingleAsync(s=>s.Id==first.SourceDocumentId);Assert.Equal(bytes,savedSource.Content);
            var evidence=await check.SourceEvidence.Where(e=>e.SourceDocumentId==first.SourceDocumentId).ToListAsync();
            Assert.All(evidence,e=>{var row=JsonSerializer.Deserialize<SupplierInventoryRow>(e.ExtractedRowJson)!;Assert.Equal("PerBottle",row.PriceComponents.Single().Unit);Assert.Equal("Wholesale",row.PriceComponents.Single().Kind);Assert.False(row.PriceComponents.Single().TaxIncluded);Assert.Null(row.ReferenceRetailPrice);Assert.Equal(3,row.MappingEvidence.Count);});
            Assert.All(await check.SupplierProducts.Where(p=>p.SupplierProductCode==a||p.SupplierProductCode==b).ToListAsync(),p=>Assert.Null(p.CurrentPrice));
        }
        await Assert.ThrowsAsync<FolderImportHeldException>(()=>Save(bytes,day.AddDays(1),rows));
        await Assert.ThrowsAsync<FolderImportHeldException>(()=>Save([1,2,3],day,rows));
        // A new source with a duplicate SKU must hold the complete group, even if values agree.
        var duplicate=await Save([4,5,6],day.AddDays(1),[rows[0],rows[0]]);Assert.Equal(0,duplicate.Products);Assert.Single(duplicate.Holds!);
        await Assert.ThrowsAsync<FolderImportHeldException>(()=>Save(bytes,day,rows));
        var proof=Environment.GetEnvironmentVariable("LIPS_LAVINEE_PARTIAL_PROOF");
        if(!string.IsNullOrEmpty(proof))await File.WriteAllTextAsync(proof,JsonSerializer.Serialize(new{sourceId=first.SourceDocumentId,syntheticSkus=new[]{a,b},firstSaved=1,resumedTotal=2,concurrentAdded=1,repeatedAdded=0,evidenceCount=2,originalUnchanged=true,retailNotReplaced=true,sameDayConflictRejected=true,sameHashDateConflictRejected=true,newerSourceRejected=true,duplicateGroupHeld=true}));
    }
}
