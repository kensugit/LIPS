using System.Text.Json;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public sealed class SupplierFilesFactAttribute:FactAttribute{public SupplierFilesFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_SUPPLIER_DIR"))||string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATALOG_MULTI_CONNECTION")))Skip="Set supplier fixture folder and test database.";}}
public class AdditionalSupplierTests
{
    static byte[] Toa(bool malformed=false){using var w=new XLWorkbook();var s=w.AddWorksheet("9月");s.Cell(1,1).Value="2026.9月度商品価格情報（外税価格）";foreach(var (col,name) in new[]{(1,"商品コード"),(2,"商品名"),(6,"規格"),(7,"小売価格"),(8,"納入価格"),(13,"在庫")})s.Cell(4,col).Value=name;
        for(var n=6;n<9;n++){s.Cell(n,1).Value=n<8?"123":"";s.Cell(n,2).Value="確認商品";s.Cell(n,6).Value=n==8?"200X3/8":"1L/12";s.Cell(n,7).Value=5000;s.Cell(n,8).Value=2500;s.Cell(n,13).Value=n==6?"★":n==7?"":"0";s.Cell(n,11).Value=n==7?"26.10月より価格変更":"";}if(malformed)s.Cell(6,6).Value="不明";using var m=new MemoryStream();w.SaveAs(m);return m.ToArray();}
    [Fact]public void ToaRetainsEveryOfferAndSeparatesPriceKinds(){var b=Toa();var r=new ToaPriceListParser().Parse(b,default);Assert.Equal(3,r.Count);Assert.Equal(3,r.Select(x=>x.SupplierProductCode).Distinct().Count());Assert.All(r,x=>Assert.StartsWith("TOA-",x.SupplierProductCode));Assert.Equal(5000,r[0].ReferenceRetailPrice);Assert.Equal(1000,r[0].VolumeMl);Assert.Equal(12,r[0].CaseSize);Assert.Equal(InventoryStatus.Expected,r[0].Inventory.Status);Assert.Equal(InventoryStatus.Unknown,r[1].Inventory.Status);Assert.Equal(InventoryStatus.OutOfStock,r[2].Inventory.Status);Assert.Contains("10月",r[1].PriceNote);Assert.Equal(0,r[2].VolumeMl);Assert.Contains("セット",r[2].PriceNote);Assert.Equal(r.Select(x=>x.SupplierProductCode),new ToaPriceListParser().Parse(b,default).Select(x=>x.SupplierProductCode));}
    [Fact]public void ToaBadPackRejectsFile()=>Assert.Throws<InvalidDataException>(()=>new ToaPriceListParser().Parse(Toa(true),default));
    [SupplierFilesFact]public async Task AllRealSupplierFilesRoundTripWithoutDuplicateOrMissingRows()
    {
        var root=Environment.GetEnvironmentVariable("CATALOG_SUPPLIER_DIR")!;var options=new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_MULTI_CONNECTION")).Options;
        foreach(var (file,code,count,date) in new[]{("木下在庫リスト20260724.xlsx","KINOSHITA",558,new DateTimeOffset(2026,7,24,0,0,0,TimeSpan.Zero)),("【東亜商事】2026年9月総合価格表.xlsx","TOA",1509,new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero)),("StockList20260819.xlsx","AMZ",196,new DateTimeOffset(2026,8,19,0,0,0,TimeSpan.Zero))}){
            var bytes=await File.ReadAllBytesAsync(Path.Combine(root,file));var selected=WorkbookParserRegistry.Detect(bytes);Assert.Equal(code,selected.SupplierCode);var rows=selected.Parser.Parse(bytes,default);Assert.Equal(count,rows.Count);
            for(var attempt=0;attempt<2;attempt++){await using var db=new CatalogSearchDbContext(options);await db.Database.MigrateAsync();var imported=await new FolderImportService(new PostgresInventoryStore(db,true)).ImportAsync(code+"/"+file,bytes,date.ToString("yyyy-MM-dd"),default);if(attempt==1)Assert.True(imported.AlreadyImported);
                var supplier=await db.Suppliers.SingleAsync(x=>x.Code==code);var stored=await db.SupplierProducts.Where(x=>x.SupplierId==supplier.Id).ToDictionaryAsync(x=>x.SupplierProductCode);Assert.Equal(count,stored.Count);Assert.Equal(count,await db.SourceEvidence.CountAsync(x=>x.SourceDocumentId==imported.SourceDocumentId));
                foreach(var row in rows){var v=stored[row.SupplierProductCode];Assert.Equal(row.ProductNameJa,v.SupplierProductName);Assert.Equal(row.ReferenceRetailPrice,v.CurrentPrice);Assert.Equal(row.VolumeMl,v.VolumeMl);Assert.Equal(row.Inventory.Status,v.CurrentInventoryStatus);Assert.Equal(row.Inventory.Quantity,v.CurrentInventoryQuantity);Assert.False(v.TaxIncluded);}
                Assert.Equal(bytes,(await db.SourceDocuments.SingleAsync(x=>x.Id==imported.SourceDocumentId)).Content);
                var last=await new CatalogQuery(db).SearchAsync(new(Text:rows[^1].SupplierProductCode));Assert.Contains(last,x=>x.Sku==rows[^1].SupplierProductCode);
            }
            if(code=="KINOSHITA"){Assert.Equal(3,rows.Count(x=>x.Inventory.RawValue.EndsWith("CS")));Assert.All(rows.Where(x=>x.Inventory.RawValue.EndsWith("CS")),x=>Assert.Null(x.Inventory.Quantity));Assert.All(rows.Where(x=>x.VintageRaw is "0" or "20"),x=>Assert.Null(x.Vintage));}
            if(code=="TOA"){Assert.Equal(1142,rows.Count(x=>x.Inventory.Status==InventoryStatus.Expected));Assert.Equal(352,rows.Count(x=>x.Inventory.Status==InventoryStatus.Unknown));Assert.Equal(5,rows.Count(x=>x.Inventory.Status==InventoryStatus.OutOfStock));Assert.Equal(2,rows.Count(x=>x.VolumeMl==0));}
            if(code=="AMZ"){Assert.Equal(23,rows.Count(x=>x.Inventory.Status==InventoryStatus.LowStock));Assert.All(rows,x=>Assert.Null(x.Inventory.Quantity));Assert.All(rows,x=>Assert.Equal("在庫表 (JAN情報)",x.SourceSheet));Assert.DoesNotContain(rows,x=>x.ProductNameJa=="-");
                using var w=new XLWorkbook(new MemoryStream(bytes));w.Worksheet("在庫表").Cell(4,19).Value=1;using var m=new MemoryStream();w.SaveAs(m);Assert.Throws<InvalidDataException>(()=>new AmzInventoryParser().Parse(m.ToArray(),default));}
        }
    }
}
