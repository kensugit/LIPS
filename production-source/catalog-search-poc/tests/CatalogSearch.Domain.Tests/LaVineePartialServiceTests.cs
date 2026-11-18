using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public sealed class LaVineePartialServiceTests
{
    private const string Path = "parent/LA_VINEE/2026-10-01.xlsx";
    private sealed class RecordingStore : IInventoryStore
    {
        public int Calls;
        public SourceInput? Input;
        public string? Hash;
        public IReadOnlyList<SupplierInventoryRow> Rows = [];
        public bool Fail;
        private readonly HashSet<string> saved = [];
        public Task<ImportResult> SaveAsync(SourceInput input,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
        {
            Calls++;Input=input;Hash=hash;Rows=rows;
            if(Fail)throw new TimeoutException("Synthetic lost storage response");
            var added=rows.Count(r=>saved.Add(r.SupplierProductCode));
            return Task.FromResult(new ImportResult(Guid.NewGuid(),saved.Count,false,input.PendingHolds){AddedProducts=added});
        }
    }
    private static IntakeMappingResolution[] Answers(byte[] b,Func<IntakeMappingQuestion,bool>? include=null)
        =>LaVineeMappingReview.Inspect(b).Where(q=>q.Choices.Count>0&&(include?.Invoke(q)??true)).Select(q=>new IntakeMappingResolution(q,q.Choices[0],"Synthetic explicit answer")).ToArray();

    [Fact] public async Task OneResolvedRowSavesWhileUnknownRegionHoldsWholeOtherSku()
    {
        var bytes=LaVineeMappingTests.Workbook(region:"ZZ");var original=bytes.ToArray();var store=new RecordingStore();
        var result=await new FolderImportService(store).ImportAsync(Path,bytes,null,default,mappingAnswers:Answers(bytes),allowPartial:true);
        Assert.Equal("Partial",result.Status);Assert.Equal("Mapping",result.QuestionKind);Assert.Equal(1,result.Products);Assert.Equal(1,result.AddedProducts);
        Assert.Equal("103",Assert.Single(store.Rows).SupplierProductCode);Assert.Equal("102",Assert.Single(store.Input!.PendingHolds!).Sku);
        Assert.Equal(6,result.Questions!.Count);Assert.Contains(result.Questions,q=>q.SourceKey=="C/ZZ"&&q.Choices.Count==0);
        Assert.Equal(original,bytes);Assert.Equal(bytes,store.Input.Content);Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),store.Hash);
        Assert.Equal(new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero),store.Input.ObservedAt);
        var price=Assert.Single(store.Rows[0].PriceComponents);Assert.Equal("PerBottle",price.Unit);Assert.Null(store.Rows[0].ReferenceRetailPrice);
    }
    [Fact] public async Task PriceMissingForOneRowNeverSavesThatSku()
    {
        var b=LaVineeMappingTests.Workbook();var store=new RecordingStore();var answers=Answers(b,q=>q.Row==2||q.Kind!="ColumnMeaning");
        var r=await new FolderImportService(store).ImportAsync(Path,b,null,default,mappingAnswers:answers,allowPartial:true);
        Assert.Equal("Partial",r.Status);Assert.Equal("102",Assert.Single(store.Rows).SupplierProductCode);Assert.Equal("103",Assert.Single(r.Holds!).Sku);
        Assert.Contains("価格単位",r.Holds![0].Reason);Assert.Equal(6,r.Questions!.Count);
    }
    [Fact] public async Task NoResolvedRowsDoesNotCallStore()
    {
        var store=new RecordingStore();var r=await new FolderImportService(store).ImportAsync(Path,LaVineeMappingTests.Workbook(),null,default,allowPartial:true);
        Assert.Equal("Held",r.Status);Assert.Equal(0,store.Calls);Assert.Equal(0,r.Products);Assert.Equal(2,r.Holds!.Count);Assert.Equal(6,r.Questions!.Count);
    }
    [Fact] public async Task DuplicateSkuExcludesAllOccurrencesEvenWithAllAnswers()
    {
        var b=LaVineeMappingTests.Workbook(duplicate:true);var store=new RecordingStore();
        var r=await new FolderImportService(store).ImportAsync(Path,b,null,default,mappingAnswers:Answers(b),allowPartial:true);
        Assert.Equal("Held",r.Status);Assert.Equal(0,store.Calls);Assert.Equal(2,r.Holds!.Count);Assert.All(r.Holds,h=>Assert.Equal("100",h.Sku));
        Assert.Equal(new[]{2,3},r.Holds.SelectMany(h=>h.Sources).Select(s=>s.Row));Assert.Equal(6,r.Questions!.Count);
    }
    [Fact] public async Task RowsHoldCanExplicitlySaveNormalSkuWithoutCombiningDuplicateOccurrences()
    {
        using var workbook=new ClosedXML.Excel.XLWorkbook(new MemoryStream(LaVineeMappingTests.Workbook(duplicate:true)));
        var sheet=workbook.Worksheet(1);sheet.Row(2).CopyTo(sheet.Row(4));sheet.Cell(4,2).Value="104";
        sheet.Cell(2,6).Value="N.V";sheet.Cell(3,6).Value="S.A.";
        sheet.Cell(2,9).Value=7;sheet.Cell(3,9).Value=13;
        using var stream=new MemoryStream();workbook.SaveAs(stream);var bytes=stream.ToArray();var original=bytes.ToArray();
        var store=new RecordingStore();var service=new FolderImportService(store);var answers=Answers(bytes);
        var held=await service.ImportAsync(Path,bytes,null,default,mappingAnswers:answers);
        Assert.Equal("Held",held.Status);Assert.Equal("Rows",held.QuestionKind);Assert.Equal(0,store.Calls);
        var review=new IntakeReview{State="Held",Result=held};Assert.True(review.CanResumeResolved);
        var result=await service.ImportAsync(Path,bytes,null,default,mappingAnswers:answers,allowPartial:true);
        Assert.Equal("Partial",result.Status);Assert.Equal("Rows",result.QuestionKind);Assert.Equal(1,result.AddedProducts);
        Assert.Equal("104",Assert.Single(store.Rows).SupplierProductCode);
        Assert.Equal(2,result.Holds!.Count);Assert.All(result.Holds,h=>Assert.Equal("100",h.Sku));
        Assert.Equal(new int?[]{7,13},result.Holds.SelectMany(h=>h.Sources).Select(s=>s.Inventory.Quantity));
        Assert.Equal(9,result.Questions!.Count);Assert.Equal(original,bytes);
        var repeated=await service.ImportAsync(Path,bytes,null,default,mappingAnswers:answers,allowPartial:true);
        Assert.Equal(0,repeated.AddedProducts);Assert.Equal(1,repeated.Products);Assert.Equal(2,repeated.Holds!.Count);
    }
    [Theory]
    [InlineData("Held","LA_VINEE","Rows",true,true)]
    [InlineData("Ready","LA_VINEE","Rows",true,true)]
    [InlineData("Partial","LA_VINEE","Rows",true,true)]
    [InlineData("Unknown","LA_VINEE","Rows",true,false)]
    [InlineData("Running","LA_VINEE","Rows",true,false)]
    [InlineData("Completed","LA_VINEE","Rows",true,false)]
    [InlineData("Held","LUC","Rows",true,false)]
    [InlineData("Held","LA_VINEE","Rows",false,false)]
    [InlineData("Held","LA_VINEE","Date",true,false)]
    public void PartialRowsActionIsLimitedToEligibleReviewState(string state,string supplier,string kind,bool hasHolds,bool expected)
    {
        var review=new IntakeReview{State=state,Result=new FolderImportResult(Path,"Held","test",SupplierCode:supplier,
            QuestionKind:kind,Holds:hasHolds?[new ImportRowHold("100","duplicate",[])]:[])};
        Assert.Equal(expected,review.CanResumeResolved);
    }
    [Fact] public async Task LaterAnswersSendRemainingEligibleAndUseCumulativeStoreCount()
    {
        var b=LaVineeMappingTests.Workbook();var store=new RecordingStore();var service=new FolderImportService(store);
        var first=await service.ImportAsync(Path,b,null,default,mappingAnswers:Answers(b,q=>q.Row==2),allowPartial:true);
        Assert.Equal(1,first.Products);Assert.Equal(1,first.AddedProducts);Assert.Equal("Partial",first.Status);
        var second=await service.ImportAsync(Path,b,null,default,mappingAnswers:Answers(b),allowPartial:true);
        Assert.Equal("Success",second.Status);Assert.Equal(2,second.Products);Assert.Equal(1,second.AddedProducts);Assert.Empty(store.Input!.PendingHolds!);Assert.Equal(6,second.Questions!.Count);
        var repeat=await service.ImportAsync(Path,b,null,default,mappingAnswers:Answers(b),allowPartial:true);
        Assert.Equal(2,repeat.Products);Assert.Equal(0,repeat.AddedProducts);
    }
    [Fact] public async Task LostStorageResponseStopsQueueWithoutClaimingSuccess()
    {
        var b=LaVineeMappingTests.Workbook();var store=new RecordingStore{Fail=true};
        var r=await new FolderImportService(store).ImportAsync(Path,b,null,default,mappingAnswers:Answers(b,q=>q.Row==2),allowPartial:true);
        Assert.Equal("Held",r.Status);Assert.True(r.StopQueue);Assert.Equal(1,store.Calls);Assert.Equal(6,r.Questions!.Count);Assert.Single(r.Holds!);
    }
    [Fact] public async Task DefaultPathStillRefusesAnyUnansweredRowAndAllowsFullyResolvedWorkbook()
    {
        var b=LaVineeMappingTests.Workbook();var store=new RecordingStore();var service=new FolderImportService(store);
        var held=await service.ImportAsync(Path,b,null,default,mappingAnswers:Answers(b,q=>q.Row==2));
        Assert.Equal("Held",held.Status);Assert.Equal(0,store.Calls);
        var completed=await service.ImportAsync(Path,b,null,default,mappingAnswers:Answers(b));
        Assert.Equal("Success",completed.Status);Assert.Equal(2,completed.Products);Assert.Equal(2,completed.AddedProducts);Assert.Null(store.Input!.PendingHolds);
    }
    [Fact] public async Task PartialRejectsOtherSupplierAndKeepsSelectionAndExplicitDate()
    {
        var store=new RecordingStore();var service=new FolderImportService(store);
        var rejected=await service.ImportAsync("root/LUC/2026-10-01.xlsx",FolderImportTests.Workbook(luc:true),null,default,allowPartial:true);
        Assert.Equal("Held",rejected.Status);Assert.Equal(0,store.Calls);
        var b=LaVineeMappingTests.Workbook();string[] selected=["102"];
        await service.ImportAsync("root/LA_VINEE/synthetic.xlsx",b,"2026-10-01",default,selected,Answers(b,q=>q.Row==2),allowPartial:true);
        Assert.Equal(selected,store.Input!.SelectedSkus);Assert.Equal(new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero),store.Input.ObservedAt);
    }
}
