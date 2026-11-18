using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using System.Text.Json;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class ArcanOriginalFactAttribute : FactAttribute
{
    public ArcanOriginalFactAttribute()
    {
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCAN_TEST_PDF")))
            Skip="Set read-only ARCAN_TEST_PDF, PDF_TEST_PYTHON and PDF_TEST_SCRIPT.";
    }
}
public class ArcanRetailFolderTests
{
    private sealed class Store : IInventoryStore
    {
        public SourceInput? Source; public IReadOnlyList<SupplierInventoryRow>? Rows;
        public Task<ImportResult> SaveAsync(SourceInput s,string h,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
        {Source=s;Rows=rows;return Task.FromResult(new ImportResult(Guid.NewGuid(),rows.Count,false));}
    }
    [ArcanOriginalFact]
    public async Task ActualArcanRetailPdfUsesBulkFolderPathAndPreservesIdentityPriceAndStock()
    {
        var bytes=await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("ARCAN_TEST_PDF")!);
        var reader=new FolderPdfReader(Environment.GetEnvironmentVariable("PDF_TEST_PYTHON"),Environment.GetEnvironmentVariable("PDF_TEST_SCRIPT")!);
        var store=new Store();
        var result=await new FolderImportService(store,pdfReader:reader).ImportAsync("取引先別/アルカン/ワイン価格表_2026.10.pdf",bytes,"2026-10-17",default);
        Assert.Equal("Success",result.Status);Assert.Equal(392,result.Products);
        Assert.Equal(bytes,store.Source!.Content);Assert.Equal("ARCAN",store.Source.SupplierCode);
        Assert.Equal("FolderPdf/2/arcan",store.Source.ParserVersion);
        var rows=store.Rows!;Assert.Equal(392,rows.Select(r=>r.SupplierProductCode).Distinct().Count());
        Assert.All(rows,r=>Assert.Null(r.Inventory.Quantity));
        var duplicates=rows.Where(r=>r.OriginalSupplierProductCode=="606020718").ToArray();
        Assert.Equal(2,duplicates.Length);Assert.Equal(new decimal?[]{15000,20000},duplicates.Select(r=>r.ReferenceRetailPrice).Order().ToArray());
        Assert.All(duplicates,r=>Assert.Contains("同じ品番が別商品",r.PriceNote));
        var shortYear=Assert.Single(rows,r=>r.SupplierProductCode=="07438700");
        Assert.Null(shortYear.Vintage);Assert.Equal("10",shortYear.VintageRaw);
        Assert.Equal(394,rows.Sum(r=>JsonDocument.Parse(r.RawCellsJson).RootElement.GetProperty("掲載箇所").GetArrayLength()));
    }
}
