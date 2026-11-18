using System.Security.Cryptography;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class LucWorkbookFactAttribute : FactAttribute
{
    public LucWorkbookFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_LUC_WORKBOOK"))||string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_LUC_CONNECTION")))Skip="Set LUC workbook and dedicated DB connection.";}
}
public class LucInventoryParserTests
{
    static byte[] Workbook(string stock="◎",string volume="(750ml)",string price="5000",bool tax=false,bool duplicate=false)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("在庫リスト");s.Cell(4,19).Value="◎＝在庫60本以上";
        string[] headers=["商品コード","JANコード","生産者コード","生産者（アルファベット）","生産者（カタカナ）","ヴィンテージ","商品名（アルファベット）","商品名（カタカナ）","容量","色","地域コード","原産国","生産地方","生産地域","格付コード","格付名",tax?"参考小売価格（税込）":"参考小売価格","商品コメント","在庫"];
        string[] values=["000001","0123456789012","P1","Producer","生産者","NV","Sample","確認ワイン",volume,"白","R1","フランス","ブルゴーニュ地方","ボーヌ","G1","格付",""+price,"特別な香りの説明",stock];
        for(int i=0;i<headers.Length;i++){s.Cell(5,i+1).Value=headers[i];s.Cell(6,i+1).Value=values[i];if(duplicate)s.Cell(7,i+1).Value=values[i];}
        using var stream=new MemoryStream();w.SaveAs(stream);return stream.ToArray();
    }
    [Theory][InlineData("◎",null,InventoryStatus.InStock)][InlineData("60",60,InventoryStatus.InStock)][InlineData("0",0,InventoryStatus.OutOfStock)][InlineData("",null,InventoryStatus.Unknown)][InlineData("入荷未定",null,InventoryStatus.Unknown)]
    public void StockIsLiteralAndEvidencePreserved(string stock,int? quantity,InventoryStatus status)
    {
        var bytes=Workbook(stock);var selected=WorkbookParserRegistry.Detect(bytes);Assert.Equal("LUC",selected.SupplierCode);
        var r=Assert.Single(selected.Parser.Parse(bytes,default));Assert.Equal(quantity,r.Inventory.Quantity);Assert.Equal(status,r.Inventory.Status);Assert.Equal(stock,r.Inventory.RawValue);
        Assert.Equal("000001",r.SupplierProductCode);Assert.Equal("0123456789012",Assert.Single(r.JanCodes));Assert.Equal("NV",r.VintageRaw);Assert.Null(r.Vintage);Assert.Equal(750,r.VolumeMl);Assert.False(r.TaxIncluded);Assert.Equal(6,r.SourceRow);Assert.Contains("R6",r.RawCellsJson);Assert.Equal("特別な香りの説明",r.Description);
    }
    [Fact]public void TaxAndOpenPriceAreExplicit(){var r=Assert.Single(new LucInventoryParser().Parse(Workbook(price:"オープン",tax:true),default));Assert.True(r.TaxIncluded);Assert.Null(r.ReferenceRetailPrice);}
    [Theory][InlineData("750","5000",false)][InlineData("(0ml)","5000",false)][InlineData("(750ml)","-1",false)][InlineData("(750ml)","未定",false)][InlineData("(750ml)","5000",true)]
    public void InvalidOrDuplicateRowsRejectWholeFile(string volume,string price,bool duplicate)=>Assert.Throws<InvalidDataException>(()=>new LucInventoryParser().Parse(Workbook(volume:volume,price:price,duplicate:duplicate),default));
    [Fact]public void UnrecognizedExcelRejected(){using var w=new XLWorkbook();w.AddWorksheet("Unknown");using var m=new MemoryStream();w.SaveAs(m);Assert.Throws<UnsupportedWorkbookException>(()=>WorkbookParserRegistry.Detect(m.ToArray()));}
    [LucWorkbookFact]public async Task RealWorkbookRoundTripAndRepeatedImport()
    {
        var bytes=await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("CATALOG_LUC_WORKBOOK")!);var hash=SHA256.HashData(bytes);
        var rows=new LucInventoryParser().Parse(bytes,default);Assert.Equal(2902,rows.Count);Assert.Equal(1604,rows.Count(x=>x.Inventory.RawValue=="◎"));Assert.Equal(1298,rows.Count(x=>x.Inventory.Quantity.HasValue));Assert.Equal(45,rows.Count(x=>x.VintageRaw=="NV"));Assert.Equal(2,rows.Count(x=>x.JanCodes.Count==0));Assert.All(rows,r=>{Assert.False(r.TaxIncluded);Assert.Equal(InventoryStatus.InStock,r.Inventory.Status);});
        var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_LUC_CONNECTION")).Options;
        Guid source;
        for(var n=0;n<2;n++){
            await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();
            var result=await new FolderImportService(new PostgresInventoryStore(db,true)).ImportAsync("LUC/luc-2026-08-17.xlsx",bytes,null,default);
            Assert.Equal(2902,result.Products);if(n==1)Assert.True(result.AlreadyImported);source=result.SourceDocumentId!.Value;
            var supplier=await db.Suppliers.SingleAsync(x=>x.Code=="LUC");var stored=await db.SupplierProducts.Where(x=>x.SupplierId==supplier.Id).ToDictionaryAsync(x=>x.SupplierProductCode);
            Assert.Equal(2902,stored.Count);foreach(var row in rows){var actual=stored[row.SupplierProductCode];Assert.Equal(row.ProductNameJa,actual.SupplierProductName);Assert.Equal(row.VolumeMl,actual.VolumeMl);Assert.Equal(row.ReferenceRetailPrice,actual.CurrentPrice);Assert.Equal(row.Inventory.Quantity,actual.CurrentInventoryQuantity);Assert.Equal(row.VintageRaw,actual.VintageRaw);Assert.Equal(source,actual.CurrentSourceDocumentId);}
            Assert.Equal(2902,await db.SourceEvidence.CountAsync(x=>x.SourceDocumentId==source));Assert.Equal(bytes,(await db.SourceDocuments.SingleAsync(x=>x.Id==source)).Content);
            var last=Assert.Single(await new CatalogQuery(db).SearchAsync(new(Text:"80101801301")));Assert.Equal(1500,last.VolumeMl);Assert.Equal(30,last.Quantity);
            var hits=await new CatalogQuery(db).SearchAsync(new(Text:"ベルトラン",VolumeMl:750,VintageRaw:"2023",PriceMin:3900,PriceMax:5000));Assert.NotEmpty(hits);Assert.All(hits,x=>Assert.InRange(x.ReferenceRetailPrice!.Value,3900,5000));
        }
        Assert.Equal(hash,SHA256.HashData(await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("CATALOG_LUC_WORKBOOK")!)));
    }
}
