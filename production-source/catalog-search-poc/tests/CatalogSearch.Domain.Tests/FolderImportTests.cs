using System.Text;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class FolderImportTests
{
    private sealed class Store : IInventoryStore
    {
        public SourceInput? Source; public IReadOnlyList<SupplierInventoryRow>? Rows; public int Calls;
        public Task<ImportResult> SaveAsync(SourceInput source, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
        { Calls++; Source=source; Rows=rows; return Task.FromResult(new ImportResult(Guid.NewGuid(), rows.Count, false)); }
    }
    internal static byte[] Csv(string sku="FOLDER-TEST", string price="1000") => Encoding.UTF8.GetBytes(
        "SKU,ProductNameJa,Vintage,VolumeMl,Price,Tax,Inventory\n"+$"{sku},確認用商品,NV,750,{price},,\n");
    internal static byte[] Workbook(bool luc=false, bool ambiguous=false)
    {
        using var w=new XLWorkbook();
        if (!luc || ambiguous) {
            var s=w.AddWorksheet("ワインリスト");
            string[] headers=["SKU","生産者名／ブランド名","Producer / Brand","商品名","Wine","VTG","容量","入数","希望小売","在庫","タイプ","国","産地","JAN"];
            string[] values=["FOLDER-XLSX","生産者","Producer","試験ワイン","Test","NV","750","6","1200","","赤","フランス","ロワール","0"];
            for(var i=0;i<headers.Length;i++){s.Cell(2,i+1).Value=headers[i];s.Cell(3,i+1).Value=values[i];}
        }
        if (luc || ambiguous) {
            var s=w.AddWorksheet("在庫リスト");
            string[] headers=["商品コード","JANコード","生産者コード","生産者(アルファベット)","生産者(カタカナ)","ヴィンテージ","商品名(アルファベット)","商品名(カタカナ)","容量","色","原産国","生産地方","生産地域","格付名","参考小売価格","商品コメント","在庫"];
            string[] values=["LUC-TEST","0","P","Producer","生産者","NV","Test","試験ワイン","750ml","赤","フランス","ロワール","地域","","1200","コメント","◎"];
            for(var i=0;i<headers.Length;i++){s.Cell(5,i+1).Value=headers[i];s.Cell(6,i+1).Value=values[i];}
        }
        using var stream=new MemoryStream(); w.SaveAs(stream); return stream.ToArray();
    }
    [Theory]
    [InlineData("StockList20261007.xlsx",null,true)]
    [InlineData("LUC最新在庫リスト＿2026.8.17.xlsx",null,true)]
    [InlineData("在庫2026-10-09.csv",null,true)]
    [InlineData("在庫2026年10月9日.csv",null,true)]
    [InlineData("sample.csv","2026-10-09",true)]
    [InlineData("sample.csv",null,false)]
    [InlineData("予約案内2026年10月①.xlsx",null,false)]
    [InlineData("予約案内2026年10月①.xlsx","2026-10-15",true)]
    [InlineData("在庫２０２６年１０月０６日.xlsx",null,true)]
    [InlineData("在庫2026年10月①_2026-10-06.xlsx",null,true)]
    [InlineData("2026-02-30.csv",null,false)]
    [InlineData("2026-10-08.csv","2026-10-09",false)]
    [InlineData("2026-10-08_2026-10-09.csv",null,false)]
    public void DocumentDateIsExplicitAndNotFileMtime(string name,string? date,bool valid)
        => Assert.Equal(valid,FolderImportService.TryDate(name,date,out _));

    [Theory]
    [InlineData("root/LUC/2026-10-09.pdf","Unsupported")]
    [InlineData("root/LUC/2026-10-09.xls","Unsupported")]
    [InlineData("root/unknown/2026-10-09.csv","Held")]
    [InlineData("root/LUC/AMZ/2026-10-09.csv","Held")]
    [InlineData("root/LUC/undated.csv","Held")]
    [InlineData("root/LUC/~$2026-10-09.xlsx","Held")]
    [InlineData("root/../LUC/2026-10-09.csv","Failed")]
    [InlineData("/LUC/2026-10-09.csv","Failed")]
    [InlineData("C:/LUC/2026-10-09.csv","Failed")]
    [InlineData("root\\LUC\\2026-10-09.csv","Failed")]
    [InlineData("2026-10-09.csv","Failed")]
    public async Task InvalidOrUnknownInputsNeverCallPersistence(string path,string status)
    {
        var store=new Store(); var r=await new FolderImportService(store).ImportAsync(path,Csv(),null,default);
        Assert.Equal(status,r.Status);Assert.False(r.StopQueue);Assert.Equal(0,store.Calls);
    }
    [Theory]
    [InlineData("取引先別/LUC/在庫リスト/2026-10-11.csv", "LUC")]
    [InlineData("取引先別/Wine to Style/カタログ/2026/2026-10-11.csv", "WINE_TO_STYLE")]
    [InlineData("取引先別/木下インターナショナル/在庫/2026-10-11.csv", "KINOSHITA")]
    [InlineData("取引先別/東亜商事/在庫/2026-10-11.csv", "TOA")]
    [InlineData("取引先別/AMZ/在庫/2026-10-11.csv", "AMZ")]
    public async Task ParentFolderAndNestedDocumentsResolveSupplier(string path, string code)
    {
        var store = new Store();
        var result = await new FolderImportService(store).ImportAsync(path, Csv(), null, default);
        Assert.Equal("Success", result.Status);
        Assert.Equal(code, result.SupplierCode);
        Assert.Equal(1, store.Calls);
        Assert.Equal("2026-10-11", result.ObservedAt);
    }

    [Fact]
    public async Task FolderAndWorkbookMustAgreeAndAmbiguityIsHeld()
    {
        var store=new Store(); var service=new FolderImportService(store);
        Assert.Equal("Held",(await service.ImportAsync("LUC/2026-10-09.xlsx",Workbook(),null,default)).Status);
        Assert.Equal("Held",(await service.ImportAsync("LUC/2026-10-09.xlsx",Workbook(ambiguous:true),null,default)).Status);
        Assert.Equal(0,store.Calls);
    }
    [Fact]
    public async Task UnknownWorkbookAndMalformedCsvNeverSave()
    {
        var store=new Store();var service=new FolderImportService(store);
        using var w=new XLWorkbook();w.AddWorksheet("other");using var b=new MemoryStream();w.SaveAs(b);
        Assert.Equal("Unsupported",(await service.ImportAsync("LUC/2026-10-09.xlsx",b.ToArray(),null,default)).Status);
        Assert.Equal("Held",(await service.ImportAsync("LUC/2026-10-09.csv",Encoding.UTF8.GetBytes("bad\n1"),null,default)).Status);
        Assert.Equal("Failed",(await service.ImportAsync("LUC/2026-10-09.csv",[],null,default)).Status);
        Assert.Equal(0,store.Calls);
    }
    [Fact]
    public async Task LucWorkbookPreservesOriginalEvidenceAndLowerBound()
    {
        var store=new Store(); var bytes=Workbook(luc:true);
        var r=await new FolderImportService(store).ImportAsync("取引先別/LUC/2026-10-09.xlsx",bytes,null,default);
        Assert.Equal("Success",r.Status);Assert.Equal("LUC",r.SupplierCode);Assert.Equal(bytes,store.Source!.Content);
        var row=Assert.Single(store.Rows!);Assert.Null(row.Inventory.Quantity);Assert.Equal(InventoryStatus.InStock,row.Inventory.Status);
        Assert.Equal("◎",row.Inventory.RawValue);Assert.False(row.TaxIncluded);Assert.Equal(6,row.SourceRow);Assert.Equal("◎", System.Text.Json.JsonDocument.Parse(row.RawCellsJson).RootElement.GetProperty("Q6").GetString());
    }
    [Fact]
    public async Task UnknownStockAndDateAreNotInvented()
    {
        var store=new Store(); var service=new FolderImportService(store);
        var result=await service.ImportAsync("Wine to Style/2026-10-09.csv",Csv(),null,default);
        Assert.Equal("Success",result.Status);Assert.Equal("WINE_TO_STYLE",result.SupplierCode);
        Assert.Equal(InventoryStatus.Unknown,Assert.Single(store.Rows!).Inventory.Status);
        Assert.Equal(TimeSpan.Zero,store.Source!.ObservedAt.Offset);
    }
    [Fact]
    public async Task WtsWorkbookIsDetectedAndSavedThroughExistingParser()
    {
        var store=new Store();var bytes=Workbook();
        var result=await new FolderImportService(store).ImportAsync("取引先別/Wine to Style/2026-10-09.xlsx",bytes,null,default);
        Assert.Equal("Success",result.Status);Assert.Equal("WineToStyle/2",store.Source!.ParserVersion);
        Assert.Equal(bytes,store.Source.Content);Assert.Equal(1200,Assert.Single(store.Rows!).ReferenceRetailPrice);
    }
    private sealed class UncertainStore : IInventoryStore
    {
        public int Calls;
        public Task<ImportResult> SaveAsync(SourceInput source,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
        {Calls++;throw new TimeoutException();}
    }
    [Theory]
    [InlineData("AMZ")]
    [InlineData("KINOSHITA")]
    [InlineData("TOA")]
    public async Task AdditionalSupplierLayoutsUseTheirOwnParser(string code)
    {
        using var w=new XLWorkbook();
        if(code=="AMZ")
        {
            var s=w.AddWorksheet("在庫表");s.Cell(1,1).Value="エイ・エム・ズィー";
            foreach(var (c,h) in new[]{(3,"商品"),(4,"ビンテージ"),(5,"商品名"),(6,"生産者名"),(7,"商品名(カナ)"),(8,"生産者名(カナ)"),(9,"内容量"),(10,"色"),(15,"原産国"),(16,"生産地方"),(19,"参考上代価格"),(20,"10/07在庫")})s.Cell(2,c).Value=h;
            s.Cell(3,3).Value="A1";s.Cell(3,5).Value="Test";s.Cell(3,7).Value="確認ワイン";s.Cell(3,9).Value="750";s.Cell(3,19).Value="2000";s.Cell(3,20).Value="24↑";
        }
        else if(code=="KINOSHITA")
        {
            var s=w.AddWorksheet("商品");string[] headers=["商品コード","JANコード","商品名称１","ヴィンテージ","国名","容量","入数","小売価格","アルコール度数","在庫数"];
            string[] values=["K1","0","確認ワイン","2023","フランス","750","6","2000","13","3CS"];
            for(var i=0;i<headers.Length;i++){s.Cell(1,i+1).Value=headers[i];s.Cell(2,i+1).Value=values[i];}
        }
        else
        {
            var s=w.AddWorksheet("価格表");s.Cell(1,1).Value="商品価格情報";
            foreach(var (c,h) in new[]{(1,"商品コード"),(2,"商品名"),(6,"規格"),(7,"小売価格"),(8,"納入価格"),(13,"在庫")})s.Cell(4,c).Value=h;
            s.Cell(6,1).Value="101";s.Cell(6,2).Value="確認ワイン";s.Cell(6,6).Value="750/6";s.Cell(6,7).Value="2000";s.Cell(6,8).Value="1000";s.Cell(6,13).Value="★";
        }
        using var b=new MemoryStream();w.SaveAs(b);var store=new Store();
        var result=await new FolderImportService(store).ImportAsync($"取引先別/{code}/2026-10-07.xlsx",b.ToArray(),null,default);
        Assert.Equal("Success",result.Status);Assert.Equal(code,result.SupplierCode);var row=Assert.Single(store.Rows!);
        Assert.Equal(2000,row.ReferenceRetailPrice);Assert.Null(row.Inventory.Quantity);
        Assert.Equal(code=="TOA"?InventoryStatus.Expected:InventoryStatus.InStock,row.Inventory.Status);
    }
    [Fact]
    public void AmzJanAfterRegionDoesNotShiftCountryOrRetailIntoWholesale()
    {
        using var workbook = new XLWorkbook();
        foreach (var name in new[] { "在庫表", "在庫表 (JAN情報)" }) {
            var sheet = workbook.AddWorksheet(name);
            foreach(var (c,h) in new[]{(3,"商品"),(4,"ビンテージ"),(5,"商品名"),(6,"生産者名"),(7,"商品名(カナ)"),(8,"生産者名(カナ)"),(9,"内容量"),(10,"色"),(15,"原産国"),(16,"生産地方"),(18,"JAN1"),(19,"JAN2"),(21,"参考上代価格"),(22,"10/07在庫")}) sheet.Cell(2,c).Value=h;
            sheet.Cell(3,3).Value="AMZ-JAN";sheet.Cell(3,5).Value="Test";sheet.Cell(3,7).Value="試験";
            sheet.Cell(3,9).Value="750";sheet.Cell(3,15).Value="フランス";sheet.Cell(3,16).Value="ロワール";
            sheet.Cell(3,18).Value="4901234567894";sheet.Cell(3,20).Value="1000";sheet.Cell(3,21).Value="2000";sheet.Cell(3,22).Value="24↑";
        }
        using var stream=new MemoryStream();workbook.SaveAs(stream);
        var row=Assert.Single(new AmzInventoryParser().Parse(stream.ToArray(),default));
        Assert.Equal("フランス",row.Country);Assert.Equal("ロワール",row.Region);Assert.Equal(2000,row.ReferenceRetailPrice);
        Assert.Equal("24↑",row.Inventory.RawValue);Assert.Null(row.Inventory.Quantity);
    }

    [Theory]
    [InlineData(false, "WINE_TO_STYLE", "Success")]
    [InlineData(true, "WINE_TO_STYLE", "Held")]
    [InlineData(false, "LUC", "Held")]
    public async Task WtsStaticProjectionKeepsOriginalAndRejectsFormulaOrOtherSupplier(bool formula, string supplier, string status)
    {
        using var memory=new MemoryStream();memory.Write(Workbook());
        using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Update,true)) {
            using(var writer=new StreamWriter(zip.CreateEntry("xl/externalLinks/externalLink1.xml").Open()))
                writer.Write("<externalLink xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>");
            using(var writer=new StreamWriter(zip.CreateEntry("xl/externalLinks/_rels/externalLink1.xml.rels").Open()))
                writer.Write("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"externalLinkPath\" TargetMode=\"External\" Target=\"http://127.0.0.1:1/must-not-fetch\"/></Relationships>");
            using(var binary=zip.CreateEntry("xl/printerSettings/printerSettings1.bin").Open()) binary.Write(new byte[]{0,255,42});
            var workbookEntry=zip.GetEntry("xl/workbook.xml")!;System.Xml.Linq.XDocument workbookDoc;
            using(var input=workbookEntry.Open())workbookDoc=System.Xml.Linq.XDocument.Load(input);
            var ns=workbookDoc.Root!.Name.Namespace;
            workbookDoc.Root.Add(new System.Xml.Linq.XElement(ns+"externalReferences",new System.Xml.Linq.XElement(ns+"externalReference",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships")+"id","externalTest"))));
            workbookEntry.Delete();using(var output=zip.CreateEntry("xl/workbook.xml").Open())workbookDoc.Save(output);
            var relations=zip.GetEntry("xl/_rels/workbook.xml.rels")!;System.Xml.Linq.XDocument relationDoc;
            using(var input=relations.Open())relationDoc=System.Xml.Linq.XDocument.Load(input);
            relationDoc.Root!.Add(new System.Xml.Linq.XElement(relationDoc.Root.Name.Namespace+"Relationship",new System.Xml.Linq.XAttribute("Id","externalTest"),new System.Xml.Linq.XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),new System.Xml.Linq.XAttribute("Target","externalLinks/externalLink1.xml")));
            relations.Delete();using(var output=zip.CreateEntry("xl/_rels/workbook.xml.rels").Open())relationDoc.Save(output);
            if(formula) {
                var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;System.Xml.Linq.XDocument doc;
                using(var input=entry.Open())doc=System.Xml.Linq.XDocument.Load(input);
                var cell=doc.Descendants().First(e=>e.Name.LocalName=="c");cell.Add(new System.Xml.Linq.XElement(cell.Name.Namespace+"f","[1]Other!A1"));
                entry.Delete();using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();doc.Save(output);
            }
        }
        var bytes=memory.ToArray();var before=bytes.ToArray();var store=new Store();
        var result=await new FolderImportService(store).ImportAsync($"{supplier}/2026-10-11.xlsx",bytes,null,default);
        Assert.True(status==result.Status,result.Message);Assert.Equal(before,bytes);
        if(status=="Success") {
            Assert.Equal(before,store.Source!.Content);Assert.EndsWith("/StaticValues/1",store.Source.ParserVersion);
            Assert.Equal(1200,Assert.Single(store.Rows!).ReferenceRetailPrice);
        }else Assert.Equal(0,store.Calls);
    }

    [Fact]
    public async Task LostCommitResponseIsHeldWithoutAutomaticRetry()
    {
        var store=new UncertainStore();var result=await new FolderImportService(store).ImportAsync("LUC/2026-10-09.csv",Csv(),null,default);
        Assert.Equal("Held",result.Status);Assert.Contains("保存結果を確認できません",result.Message);Assert.Equal(1,store.Calls);Assert.True(result.StopQueue);
    }
    [Fact]
    public async Task DuplicateSkuRejectsWholeFileAndCancelledRequestNeverSaves()
    {
        var store=new Store();var service=new FolderImportService(store);
        var duplicate=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Csv())+"FOLDER-TEST,重複,NV,750,2000,,\n");
        Assert.Equal("Held",(await service.ImportAsync("LUC/2026-10-09.csv",duplicate,null,default)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ImportAsync("LUC/2026-10-09.csv",Csv(),null,new CancellationToken(true)));
        Assert.Equal(0,store.Calls);
    }

    [Theory]
    [InlineData("入荷済", "卸価格", "2026-09-29", "Success")]
    [InlineData("10/6頃", "卸価格", "2026-09-29", "Success")]
    [InlineData("不明", "卸価格", "2026-09-29", "Held")]
    [InlineData("入荷済", "参考上代", "2026-09-29", "Unsupported")]
    [InlineData("入荷済", "卸価格", "2026-09-30", "Held")]
    public async Task LeCaveauKeepsWholesaleAndExpectedQuantitySeparate(string arrival,string header,string date,string status)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("Sheet1");
        s.Cell("A1").Value="Le Caveau";s.Cell("A4").Value="価格は消費税別です。";s.Cell("G10").Value=header;
        s.Cell("H9").Value=new DateTime(2026,9,29);s.Cell("A9").Value="Bourgogne rouges";s.Cell("B10").Value="Bt";
        s.Cell("A11").Value=arrival;s.Cell("B11").Value=6;s.Cell("D11").Value="2020";s.Cell("E11").Value="テスト　Wine";s.Cell("F11").Value=750;s.Cell("G11").Value=1234;
        using var memory=new MemoryStream();w.SaveAs(memory);
        using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Update,true)) {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;System.Xml.Linq.XDocument doc;
            using(var input=entry.Open())doc=System.Xml.Linq.XDocument.Load(input);
            var cell=doc.Descendants().Single(e=>e.Name.LocalName=="c"&&(string?)e.Attribute("r")=="H9");
            cell.AddFirst(new System.Xml.Linq.XElement(cell.Name.Namespace+"f","TODAY()"));
            entry.Delete();using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();doc.Save(output);
        }
        var bytes=memory.ToArray();var store=new Store();var result=await new FolderImportService(store).ImportAsync($"ルカヴォー/list-{date}.xlsx",bytes,null,default);
        Assert.True(status==result.Status,result.Message);
        if(status=="Success") {
            var row=Assert.Single(store.Rows!);Assert.Equal("LC-CA3FBDF36C65C86DC72F6A09",row.SupplierProductCode);Assert.Null(row.ReferenceRetailPrice);Assert.False(row.TaxIncluded);Assert.Contains("1234",row.PriceNote);
            Assert.Equal(arrival=="入荷済"?6:null,row.Inventory.Quantity);Assert.Equal(arrival=="入荷済"?InventoryStatus.InStock:InventoryStatus.Expected,row.Inventory.Status);
            Assert.Contains("WholesalePrice",row.RawCellsJson);Assert.Equal(bytes,store.Source!.Content);Assert.Equal(11,row.SourceRow);
        }else Assert.Equal(0,store.Calls);
    }

    [Theory]
    [InlineData("OK", null, false, "Success")]
    [InlineData("少", null, false, "Success")]
    [InlineData("7", null, false, "Success")]
    [InlineData("", null, false, "Success")]
    [InlineData("OK", "2026-10-31", false, "Held")]
    [InlineData("OK", null, true, "Held")]
    public async Task NouvellesUsesUpdateDateAndStaticRetailWithoutEvaluatingWholesale(string stock,string? date,bool retailFormula,string status)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("Sheet1");
        s.Cell("A1").Value="ヌーヴェル・セレクション 最新在庫表";
        s.Cell("A3").Value="★ 更新日： 2026年9月30日時点";s.Cell("A6").Value="★ 見積期限：2026年10月31日";
        s.Cell("A4").Value="「少」＝24本以上100本未満、「OK」＝100本以上";s.Cell("A5").Value="価格はすべて税別価格です。";
        string[] header=["コード","作品名","栽培","年","色","ml","納入価格","希望小売価格","備考","在庫本数"];
        for(var n=0;n<header.Length;n++)s.Cell(42,n+1).Value=header[n];
        s.Cell("A43").Value="Test Producer";s.Cell("L43").FormulaA1="HYPERLINK(\"http://127.0.0.1:1/must-not-fetch\",\"detail\")";
        s.Cell("A44").Value="NS-TEST";s.Cell("B44").Value="Test Wine";s.Cell("D44").Value="NV";s.Cell("E44").Value="白";s.Cell("F44").Value=750;
        s.Cell("G44").FormulaA1="1/0";s.Cell("H44").Value=3800;s.Cell("J44").Value=stock;
        if(retailFormula)s.Cell("H44").FormulaA1="1/0";
        using var memory=new MemoryStream();w.SaveAs(memory);var bytes=memory.ToArray();var store=new Store();
        var result=await new FolderImportService(store).ImportAsync("ヌーヴェルセレクション/latest.xlsx",bytes,date,default);
        Assert.True(status==result.Status,result.Message);
        if(status=="Success"){
            Assert.Equal("2026-09-30",result.ObservedAt);var row=Assert.Single(store.Rows!);Assert.Equal(3800,row.ReferenceRetailPrice);Assert.False(row.TaxIncluded);
            Assert.Equal(stock=="7"?7:null,row.Inventory.Quantity);Assert.Equal(stock switch {"OK"=>InventoryStatus.InStock,"少"=>InventoryStatus.LowStock,"7"=>InventoryStatus.InStock,_=>InventoryStatus.Unknown},row.Inventory.Status);
            Assert.Contains("=1/0",row.RawCellsJson);Assert.Equal(bytes,store.Source!.Content);Assert.Equal("Test Producer",row.ProducerNameEn);
        }else Assert.Equal(0,store.Calls);
    }

    [PostgresFact]
    public async Task FolderPersistenceSerializesDuplicatesConflictsAndKeepsSourceAndSupplierIdentity()
    {
        CatalogSearchDbContext Db()=>new(new DbContextOptionsBuilder<CatalogSearchDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("CATALOG_TEST_CONNECTION")).Options);
        await using(var setup=Db())await setup.Database.MigrateAsync();
        async Task<FolderImportResult> Import(string path,byte[] bytes){await using var db=Db();return await new FolderImportService(new PostgresInventoryStore(db,true)).ImportAsync(path,bytes,null,default);}
        var bytes=Csv();
        var results=await Task.WhenAll(Import("LUC/2026-10-09.csv",bytes),Import("LUC/duplicate-2026-10-09.csv",bytes));
        Assert.All(results,r=>Assert.Equal("Success",r.Status));Assert.Single(results,r=>r.AlreadyImported);
        Assert.Equal(results[0].SourceDocumentId,results[1].SourceDocumentId);
        Assert.Equal("Held",(await Import("LUC/2026-10-09.csv",Csv(price:"9000"))).Status);
        Assert.Equal("Held",(await Import("LUC/2026-10-08.csv",Csv(price:"8000"))).Status);
        Assert.Equal("Success",(await Import("AMZ/2026-10-09.csv",bytes)).Status);
        Assert.Equal("Success",(await Import("LUC/2026-10-10.csv",Csv(price:"2000"))).Status);
        await using var check=Db();
        Assert.Equal(2,await check.SupplierProducts.CountAsync(x=>x.SupplierProductCode=="FOLDER-TEST"));
        var luc=await check.Suppliers.SingleAsync(x=>x.Code=="LUC");
        var product=await check.SupplierProducts.SingleAsync(x=>x.SupplierId==luc.Id);
        Assert.Equal(2000,product.CurrentPrice);Assert.Equal(InventoryStatus.Unknown,product.CurrentInventoryStatus);
        Assert.Equal(2,await check.SourceDocuments.CountAsync(x=>x.SupplierId==luc.Id));
        Assert.Equal(bytes,(await check.SourceDocuments.FindAsync(results[0].SourceDocumentId))!.Content);
        Assert.Equal(2,await check.SourceEvidence.CountAsync(x=>x.SupplierProductId==product.Id));
    }
}

