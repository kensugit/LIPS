using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class RealPdfFactAttribute : FactAttribute
{
    public RealPdfFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KINOSHITA_TEST_PDF"))) Skip="Set isolated read-only PDF/Excel test paths."; }
}
public class PdfFolderTests
{
    [RealPdfFact]
    public async Task RealPdfAndCurrentWorkbookAgreeOnEveryProductPriceVolumeStockAndVintage()
    {
        var pdf=File.ReadAllBytes(Environment.GetEnvironmentVariable("KINOSHITA_TEST_PDF")!);
        var excel=File.ReadAllBytes(Environment.GetEnvironmentVariable("KINOSHITA_TEST_XLSX")!);
        var reader=new FolderPdfReader(Environment.GetEnvironmentVariable("PDF_TEST_PYTHON"),Environment.GetEnvironmentVariable("PDF_TEST_SCRIPT")!);
        var parsed=await reader.ReadAsync("KINOSHITA",pdf,default);Assert.True(parsed.Ok,parsed.Message);Assert.Equal(523,parsed.Rows!.Count);
        var rows=new KinoshitaInventoryParser().Parse(excel,default).ToDictionary(r=>r.SupplierProductCode);
        Assert.Equal(523,rows.Count);
        foreach(var p in parsed.Rows){var x=rows[p.SupplierProductCode];Assert.Equal(x.ReferenceRetailPrice,p.ReferenceRetailPrice);
            Assert.Equal(x.VolumeMl,p.VolumeMl);Assert.Equal(x.CaseSize,p.CaseSize);Assert.Equal(x.Inventory,p.Inventory);
            Assert.Equal(x.VintageRaw,p.VintageRaw);Assert.Equal(x.AlcoholPercent,p.AlcoholPercent);Assert.Equal(x.Country,p.Country);}
    }
    [Theory]
    [InlineData("〇")][InlineData("○")][InlineData("◯")][InlineData("◎")]
    public void AbundantStockKeepsOriginalMarkAndNeverInventsQuantity(string raw)
    {
        var stock=InventoryParser.Parse(raw);Assert.Equal(InventoryStatus.InStock,stock.Status);
        Assert.Null(stock.Quantity);Assert.Equal(raw,stock.RawValue);Assert.True(InventoryValue.IsAbundant(raw));
    }
    private sealed class Store : IInventoryStore
    {
        public SourceInput? Source; public IReadOnlyList<SupplierInventoryRow>? Rows;
        public Task<ImportResult> SaveAsync(SourceInput s, string h, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
        { Source=s; Rows=rows; return Task.FromResult(new ImportResult(Guid.NewGuid(),rows.Count,false)); }
    }
    private static SupplierInventoryRow Row() => new("46803202","","","原典商品","",null,"0",750,12,7200,"7,200",false,
        new(145,InventoryStatus.InStock,"145"),"","Portugal","",["5601519335000"],"",19,"","","","","","","",2,"PDF p.1");
    private sealed class Reader : IFolderPdfReader
    {
        public int Calls; public FolderPdfResult Result=new(true,Version:"Fixture/1",Rows:[Row()]);
        public Task<FolderPdfResult> ReadAsync(string s, byte[] bytes, CancellationToken ct) { Calls++;return Task.FromResult(Result); }
    }
    private sealed class Duplicates(FolderDuplicate? value) : IFolderDuplicateLookup
    {
        public Task<FolderDuplicate?> FindCompleted(string s,string h,CancellationToken ct) => Task.FromResult(value);
    }
    [Fact]
    public async Task ParentNestedPdfUsesParserAndPreservesOriginalBytesAndPrice()
    {
        var store=new Store();var reader=new Reader();byte[] bytes="%PDF-original"u8.ToArray();
        var result=await new FolderImportService(store,pdfReader:reader).ImportAsync("取引先別/木下インターナショナル/202610/source.pdf",bytes,"2026-10-16",default);
        Assert.Equal("Success",result.Status);Assert.Equal(1,reader.Calls);Assert.Equal(bytes,store.Source!.Content);
        Assert.Equal("KINOSHITA",store.Source.SupplierCode);Assert.Equal("Fixture/1",store.Source.ParserVersion);
        Assert.Equal(7200,Assert.Single(store.Rows!).ReferenceRetailPrice);
    }
    [Fact]
    public async Task DuplicateIsCheckedAfterByteUploadBeforeDateOrReader()
    {
        var store=new Store();var reader=new Reader();var id=Guid.NewGuid();var bytes="%PDF-original"u8.ToArray();
        var result=await new FolderImportService(store,pdfReader:reader,duplicateLookup:new Duplicates(new(id,523,"2026-10-01")))
            .ImportAsync("取引先別/木下インターナショナル/renamed.pdf",bytes,null,default);
        Assert.True(result.AlreadyImported);Assert.Equal(id,result.SourceDocumentId);Assert.Equal(523,result.Products);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),result.Hash);Assert.Equal(0,reader.Calls);Assert.Null(store.Source);
    }
    [Fact]
    public async Task CompletedLegacyFormatDuplicateSkipsBeforeUnsupportedFormatCheck()
    {
        var store=new Store();var id=Guid.NewGuid();
        var result=await new FolderImportService(store,duplicateLookup:new Duplicates(new(id,10,"2026-10-01")))
            .ImportAsync("取引先別/ファインズ/renamed.xls","original workbook"u8.ToArray(),null,default);
        Assert.True(result.AlreadyImported);Assert.Equal(id,result.SourceDocumentId);Assert.Null(store.Source);Assert.NotNull(result.Hash);
    }
    [Fact]
    public async Task UnsupportedPdfExplainsRemedyWithoutPersistence()
    {
        var store=new Store();var reader=new Reader { Result=new(false,"PdfExtractionFailed","列が変わりました","原本対応を依頼してください") };
        var result=await new FolderImportService(store,pdfReader:reader).ImportAsync("取引先別/豊通/source.pdf","%PDF-original"u8.ToArray(),"2026-10-16",default);
        Assert.Equal("Unsupported",result.Status);Assert.Equal("PdfExtractionFailed",result.FailureCode);
        Assert.Equal("原本対応を依頼してください",result.Remedy);Assert.Null(store.Source);
    }
    [Fact]
    public async Task ReadablePdfWithoutDateAsksForDateAndNeverAssumesMonthStart()
    {
        var store=new Store();var reader=new Reader();
        var result=await new FolderImportService(store,pdfReader:reader).ImportAsync("取引先別/豊通/202610.pdf","%PDF-original"u8.ToArray(),null,default);
        Assert.Equal("Date",result.QuestionKind);Assert.Null(store.Source);Assert.Equal(1,reader.Calls);
    }
    [Fact]
    public void OctoberKinoshitaColumnsKeepPurchaseSeparateFromRetail()
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("在庫表");
        string[] h=["商品ｺｰﾄﾞ","商品名称１","容量","入数","在庫数","納入価格","小売価格","ヴィンテージ","アルコール度数","JANコード","国名"];
        string[] v=["46803202","原典商品","750","12","145","4320","7200","0","19","5601519335000","Portugal"];
        for(int i=0;i<h.Length;i++){s.Cell(1,i+1).Value=h[i];s.Cell(2,i+1).Value=v[i];}
        using var buffer=new MemoryStream();w.SaveAs(buffer);var bytes=buffer.ToArray();
        Assert.Equal("KINOSHITA",WorkbookParserRegistry.Detect(bytes).SupplierCode);
        var row=Assert.Single(new KinoshitaInventoryParser().Parse(bytes,default));
        Assert.Equal(7200,row.ReferenceRetailPrice);Assert.Equal(4320,Assert.Single(row.PriceComponents).Amount);
        Assert.Equal("PurchasePrice",row.PriceComponents[0].Kind);Assert.Equal(145,row.Inventory.Quantity);Assert.Null(row.Vintage);Assert.Equal("0",row.VintageRaw);
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Running")][InlineData("Partial")][InlineData("Completed")][InlineData("Ready")]
    public void RecheckRejectsUnsafeOrCompletedStates(string state)
    {
        var r=new IntakeReview {State=state,Hash=new('A',64),Result=new("x.pdf","Unsupported","未対応")};
        Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.PrepareUnsupportedRecheck(r,r.Version,r.Hash));
        Assert.Equal(state,r.State);Assert.Empty(r.History);
    }
    [Fact]
    public void RecheckKeepsHistoricalReasonAndClaimsOnlyUnregisteredUnsupportedSource()
    {
        var r=new IntakeReview {State="Held",Hash=new('A',64),Result=new("x.pdf","Unsupported","以前の理由")};
        IntakeReviewStore.PrepareUnsupportedRecheck(r,1,r.Hash);
        Assert.Equal("Ready",r.State);Assert.Equal("以前の理由",r.Result.Message);
        Assert.Equal("RecheckUnsupported",Assert.Single(r.History).Action);
        Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.PrepareUnsupportedRecheck(r,1,r.Hash));
    }
    [Fact]
    public void EveryReviewReceiptContainsUploadedHashWithoutChangingStoredResult()
    {
        var r=new IntakeReview {State="Held",Hash=new('A',64),Result=new("x.pdf","Unsupported","未対応")};
        var receipt=IntakeReviewStore.Receipt(r);Assert.Equal(r.Hash,receipt.Hash);Assert.Null(r.Result.Hash);
    }
}
