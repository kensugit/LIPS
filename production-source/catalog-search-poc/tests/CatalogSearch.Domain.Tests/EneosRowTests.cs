using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public class EneosRowTests
{
    [EneosSourceFact] public void ActualWorkbooksReadOnlyLayoutAndHashCheck()
    {
        foreach(var (name,count) in new[]{("Agri_QB3.xlsx",48),("regularwine50.xlsx",139),("finewine50.xlsx",371)})
        {
            var path=Path.Combine(Environment.GetEnvironmentVariable("ENEOS_SOURCE_FOLDER")!,name);
            var bytes=File.ReadAllBytes(path);var hash=SHA256.HashData(bytes);
            var rows=new EneosInventoryParser().Parse(bytes,default);
            Assert.Equal(count,rows.Count);Assert.Equal("2026-10-01",EneosInventoryParser.DocumentDate(bytes));
            if(name=="finewine50.xlsx"){Assert.Equal(366,rows.Select(r=>r.SupplierProductCode).Distinct().Count());Assert.All(rows,r=>Assert.Null(r.ReferenceRetailPrice));}
            Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(path)));
        }
    }
    public static byte[] Workbook(string sku="1001",int qty=12,int price=1000,bool units=true,bool duplicate=false,string? second=null)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("ENEOS合成");
        var headers=new[]{"品番","2026-10-01","ﾘﾏｰｸ","商品名","色","VT","原産国","地域/産地（格付）","生産者","入数","JANコード単品","容量(ml)","参考上代","お見積価格"};
        for(int i=0;i<headers.Length;i++)s.Cell(3,i+1).Value=headers[i]+(units&&i>=12?"（税別/単品）":"");
        void Add(int n,string code){s.Cell(n,1).Value=code;s.Cell(n,2).Value=qty;s.Cell(n,4).Value="合成商品"+code;s.Cell(n,5).Value="赤";s.Cell(n,6).Value="2020";s.Cell(n,7).Value="日本";s.Cell(n,8).Value="合成産地";s.Cell(n,9).Value="合成生産者";s.Cell(n,10).Value=6;s.Cell(n,12).Value=750;s.Cell(n,13).Value=price;s.Cell(n,14).Value=700;}
        Add(4,sku);if(duplicate)Add(5,sku);if(second is not null)Add(6,second);
        using var stream=new MemoryStream();w.SaveAs(stream);return stream.ToArray();
    }
    private static SupplierInventoryRow Row()=>new EneosInventoryParser().Parse(Workbook(),default)[0];
    [Fact] public void ParserKeepsPriceTypesAndDoesNotInferUnit()
    {var r=new EneosInventoryParser().Parse(Workbook(units:false),default)[0];Assert.Equal(1000,r.ReferenceRetailPrice);Assert.Equal(700,r.PriceComponents[1].Amount);Assert.All(r.PriceComponents,p=>Assert.Equal("Unknown",p.Unit));}
    [Fact] public void ComparisonRequiresAllPriceContracts()
    {
        var r=Row();Assert.True(EneosRowPolicy.SamePrices(r,r));
        Assert.False(EneosRowPolicy.SamePrices(r,r with{PriceComponents=[r.PriceComponents[0] with{TaxIncluded=true},r.PriceComponents[1]]}));
        Assert.False(EneosRowPolicy.SamePrices(r,r with{PriceComponents=[r.PriceComponents[0] with{Unit="PerCase"},r.PriceComponents[1]]}));
        Assert.False(EneosRowPolicy.SamePrices(r,r with{PriceComponents=[r.PriceComponents[0] with{Amount=null},r.PriceComponents[1]]}));
    }
    [Fact] public void QuantitiesNeverSumOrRecoverFromThirdSource()
    {
        var a=new InventoryValue(12,InventoryStatus.InStock,"12");var b=new InventoryValue(8,InventoryStatus.InStock,"8");
        Assert.Equal(12,EneosRowPolicy.Inventory([a,a]).Quantity);Assert.Null(EneosRowPolicy.Inventory([a,b,a]).Quantity);
        Assert.Equal(InventoryStatus.Unknown,EneosRowPolicy.Inventory([a,new(null,InventoryStatus.OutOfStock,"欠品")]).Status);
    }
    [Fact] public void TypedRowAnswerOnlyTargetsPendingSkusAndCannotBypassUnknown()
    {
        var r=new IntakeReview{Hash="H",State="Partial",Path="ENEOS/test.xlsx",Result=new("ENEOS/test.xlsx","Partial","保留",Products:1,QuestionKind:"Rows",Holds:[new("A","価格",[])])};
        Assert.Throws<ArgumentException>(()=>IntakeReviewStore.ApplyAnswer(r,new(1,"H","recheck-rows",null,"確認",SelectedSkus:["SAVED"])));
        IntakeReviewStore.ApplyAnswer(r,new(1,"H","recheck-rows",null,"再判定",SelectedSkus:["A"]));Assert.Equal("Ready",r.State);Assert.Equal("A",Assert.Single(r.SelectedSkus!));
        r.State="Unknown";Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyAnswer(r,new(1,"H","recheck-rows",null,"再判定",SelectedSkus:["A"])));
    }
    [PostgresFact] public async Task PartialIdempotentMultiSourceEvidenceAndThirdQuantityConflict()
    {
        var connection=Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")!;
        CatalogSearchDbContext Db()=>new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(connection).Options);
        await using(var init=Db())await init.Database.MigrateAsync();
        var sku=Random.Shared.Next(10000000,99999999).ToString();var second=sku+"2";var date=new DateTimeOffset(DateTime.UtcNow.Date.AddDays(200),TimeSpan.Zero);
        async Task<ImportResult> Save(byte[] bytes,string name,IReadOnlyList<string>? selected=null)
        {await using var db=Db();return await new EneosInventoryStore(db).SaveAsync(new(name,bytes,date,"ENEOS","ENEOS合成","test"){SelectedSkus=selected},Convert.ToHexString(SHA256.HashData(bytes)),new EneosInventoryParser().Parse(bytes,default),default);}
        var first=Workbook(sku,duplicate:true);var one=await Save(first,"one.xlsx");var again=await Save(first,"one.xlsx");Assert.True(again.AlreadyImported);Assert.Equal(one.SourceDocumentId,again.SourceDocumentId);
        await Save(Workbook(sku,8),"two.xlsx");await Save(Workbook(sku,12),"three.xlsx");
        var conflict=Workbook(sku,9,1100,second:second);var partial=await Save(conflict,"four.xlsx");Assert.Equal(1,partial.Products);Assert.Equal(sku,Assert.Single(partial.Holds!).Sku);
        var retry=await Save(conflict,"four.xlsx",[sku]);Assert.Equal(partial.SourceDocumentId,retry.SourceDocumentId);Assert.Single(retry.Holds!);Assert.Equal(1,retry.Products);
        await using var check=Db();var product=await check.SupplierProducts.SingleAsync(p=>p.SupplierProductCode==sku);
        Assert.Null(product.CurrentInventoryQuantity);Assert.Equal(1000,product.CurrentPrice);Assert.Equal(InventoryStatus.InStock,product.CurrentInventoryStatus);
        var evidence=await check.SourceEvidence.Where(e=>e.SupplierProductId==product.Id).ToListAsync();Assert.Equal(3,evidence.Count);
        Assert.Equal(2,JsonSerializer.Deserialize<SupplierInventoryRow>(evidence.Single(e=>e.SourceDocumentId==one.SourceDocumentId).ExtractedRowJson)!.Occurrences.Count);
        Assert.Equal(1,await check.SourceEvidence.CountAsync(e=>e.SourceDocumentId==partial.SourceDocumentId));
        Assert.Equal(first,(await check.SourceDocuments.FindAsync(one.SourceDocumentId))!.Content);
        await using var old=Db();await Assert.ThrowsAsync<FolderImportHeldException>(()=>new EneosInventoryStore(old).SaveAsync(new("old",[1],date.AddDays(-1),"ENEOS","ENEOS","test"),"old-"+sku,[Row()],default));
    }
}
public sealed class EneosSourceFactAttribute : FactAttribute
{public EneosSourceFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENEOS_SOURCE_FOLDER")))Skip="Set ENEOS_SOURCE_FOLDER for read-only real-source verification.";}}
