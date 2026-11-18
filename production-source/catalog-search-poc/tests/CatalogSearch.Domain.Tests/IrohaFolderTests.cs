using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public sealed class IrohaFolderTests
{
    sealed class Store : IInventoryStore
    {
        public SourceInput? Source;
        public Task<ImportResult> SaveAsync(SourceInput source, string hash, IReadOnlyList<SupplierInventoryRow> rows, CancellationToken ct)
        { Source=source; return Task.FromResult(new ImportResult(Guid.NewGuid(),rows.Count,false)); }
    }
    sealed class Reader(bool valid=true) : IFolderPdfReader
    {
        public Task<FolderPdfResult> ReadAsync(string supplier, byte[] bytes, CancellationToken ct) => Task.FromResult(
            new FolderPdfResult(valid, Rows: new StandardCsvParser().Parse(FolderImportTests.Csv(),default), ObservedAt:"2026-10-01"));
    }
    [Theory]
    [InlineData("いろはわいん", "20261001", null, true)]
    [InlineData("IROHA", "20261001", null, true)]
    [InlineData("いろはわいん", "20261002", null, false)]
    [InlineData("いろはわいん", "20261001", "2026-10-02", false)]
    public async Task XlsUsesSourceDateAndPreservesOriginal(string folder,string date,string? explicitDate,bool save)
    {
        var store=new Store(); byte[] bytes=[1,2,3];
        var result=await new FolderImportService(store,xlsReader:new Reader()).ImportAsync($"取引先別/{folder}/Stocklist_IROHA_{date}.xls",bytes,explicitDate,default);
        Assert.Equal(save,store.Source is not null);
        if(save) { Assert.Equal("Success",result.Status); Assert.Equal("IROHA",result.SupplierCode); Assert.Equal("2026-10-01",result.ObservedAt); Assert.Same(bytes,store.Source!.Content); }
    }
    [Fact]
    public async Task FailedReaderNeverSaves()
    {
        var store=new Store();
        var result=await new FolderImportService(store,xlsReader:new Reader(false)).ImportAsync("いろはわいん/20261001.xls",[1],null,default);
        Assert.Equal("Unsupported",result.Status); Assert.Null(store.Source);
    }
    sealed class Duplicate : IFolderDuplicateLookup
    {
        public Task<FolderDuplicate?> FindCompleted(string supplier,string hash,CancellationToken ct) =>
            Task.FromResult<FolderDuplicate?>(supplier=="IROHA" ? new FolderDuplicate(Guid.NewGuid(),172,"2026-10-01") : null);
    }
    [Fact]
    public async Task ExistingOriginalSkipsBeforeReaderAndDoesNotWrite()
    {
        var store=new Store();
        var result=await new FolderImportService(store,duplicateLookup:new Duplicate()).ImportAsync("いろはわいん/Stocklist_IROHA_20261001.xls",[1,2],null,default);
        Assert.Equal("Success",result.Status); Assert.True(result.AlreadyImported); Assert.Equal(172,result.Products); Assert.Null(store.Source);
    }
}
