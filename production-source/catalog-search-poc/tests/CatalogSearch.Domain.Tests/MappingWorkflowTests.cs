using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;
namespace CatalogSearch.Domain.Tests;

public class MappingWorkflowTests
{
    private sealed class Store:IInventoryStore
    {
        public int Calls;public SourceInput? Source;public IReadOnlyList<SupplierInventoryRow>? Rows;public string? Hash;
        public Task<ImportResult> SaveAsync(SourceInput source,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
        {Calls++;Source=source;Rows=rows;Hash=hash;return Task.FromResult(new ImportResult(Guid.NewGuid(),rows.Count,false));}
    }
    private static byte[] Workbook(string volume="0.75",int stock=4,int price=1000,bool second=false,bool changedFormat=false)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("在庫リスト");
        string[] headers=["商品コード","JANコード","生産者コード","生産者（アルファベット）","生産者（カタカナ）","ヴィンテージ","商品名（アルファベット）","商品名（カタカナ）","容量","色","地域コード","原産国","生産地方","生産地域","格付コード","格付名","参考小売価格","商品コメント","在庫"];
        for(var c=0;c<headers.Length;c++)s.Cell(5,c+1).Value=headers[c];
        if(changedFormat)s.Cell(5,20).Value="新しい列";
        for(var r=6;r<=(second?7:6);r++)
        {s.Cell(r,1).Value="SYNTH-"+r;s.Cell(r,8).Value="合成確認商品";s.Cell(r,9).Value=volume;s.Cell(r,17).Value=price;s.Cell(r,19).Value=stock;}
        using var m=new MemoryStream();w.SaveAs(m);return m.ToArray();
    }
    private static IntakeReview Review(FolderImportResult result,byte[] bytes)=>new(){State="Held",Path="LUC/list.xlsx",Hash=Convert.ToHexString(SHA256.HashData(bytes)),Result=result,Date="2026-10-01"};
    private static IntakeAnswer Answer(IntakeReview r,params IntakeMappingAnswer[] answers)=>new(r.Version,r.Hash,"mapping",null,"合成本人確認：容量列の単位はL",MappingAnswers:answers);
    private static IntakeReusableRule Rule(IntakeMappingQuestion q)=>new(Guid.NewGuid(),1,q.Scope,q.Kind,q.SourceKey,"L",Guid.NewGuid(),1,new('A',64),"合成本人確認");

    [Fact] public async Task HeldAnswerReadyThenExplicitImportPreservesOriginalAndRowEvidence()
    {
        var bytes=Workbook();var store=new Store();var service=new FolderImportService(store);
        var held=await service.ImportAsync("LUC/list.xlsx",bytes,"2026-10-01",default);
        Assert.Equal("Held",held.Status);Assert.Equal("Mapping",held.QuestionKind);Assert.Equal(0,store.Calls);
        var q=Assert.Single(held.Questions!);var r=Review(held,bytes);
        IntakeReviewStore.ApplyAnswer(r,Answer(r,new IntakeMappingAnswer(q.Id,"L")));
        Assert.True(r.CanResume);Assert.Equal("Ready",r.State);Assert.Equal(0,store.Calls);
        Assert.Single(r.MappingResolutions);Assert.Single(Assert.Single(r.History).MappingResolutions!);
        // Static answer validation performs no business I/O. This explicit operation is the resume boundary.
        var saved=await service.ImportAsync(r.Path,bytes,r.Date,default,selectedSkus:null,mappingAnswers:r.MappingResolutions);
        Assert.Equal("Success",saved.Status);Assert.Equal(1,store.Calls);Assert.Equal(bytes,store.Source!.Content);
        Assert.Equal(r.Hash,store.Hash);Assert.Equal(750,Assert.Single(store.Rows!).VolumeMl);
        Assert.Equal("L",Assert.Single(store.Rows![0].MappingEvidence).Meaning);
    }
    [Fact] public async Task NextDocumentReusesOnlyUnitNotPreviousVolumePriceStockOrDate()
    {
        var first=Workbook();var q=Assert.Single(LucVolumeReview.Inspect(first));var rule=Rule(q);
        var next=Workbook("1.5",stock:9,price:2400);var store=new Store();
        var result=await new FolderImportService(store,[rule]).ImportAsync("LUC/next.xlsx",next,"2026-10-02",default);
        Assert.Equal("Success",result.Status);var row=Assert.Single(store.Rows!);
        Assert.Equal(1500,row.VolumeMl);Assert.Equal(9,row.Inventory.Quantity);Assert.Equal(2400m,row.ReferenceRetailPrice);
        Assert.Equal(new DateTimeOffset(2026,10,2,0,0,0,TimeSpan.Zero),store.Source!.ObservedAt);
        Assert.Equal(next,store.Source.Content);Assert.Contains(rule.Id.ToString(),Assert.Single(row.MappingEvidence).Evidence);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(first)),store.Hash);
    }
    [Theory][InlineData("supplier")][InlineData("format")][InlineData("revoked")][InlineData("conflict")][InlineData("document")]
    public async Task IneligibleRulesHoldWithoutSaving(string scenario)
    {
        var original=Workbook();var rule=Rule(Assert.Single(LucVolumeReview.Inspect(original)));var bytes=original;
        IReadOnlyList<IntakeReusableRule> rules=scenario switch {
            "supplier"=>[rule with {Scope=rule.Scope with {SupplierId="OTHER"}}],
            "document"=>[rule with {Scope=rule.Scope with {DocumentKind="Other/1"}}],
            "revoked"=>[rule,rule with {Version=2,Revoked=true}],
            "conflict"=>[rule,rule with {Id=Guid.NewGuid(),Meaning="mL"}],_=>[rule]};
        if(scenario=="format")bytes=Workbook(changedFormat:true);
        var store=new Store();var result=await new FolderImportService(store,rules).ImportAsync("LUC/list.xlsx",bytes,"2026-10-01",default);
        Assert.Equal("Held",result.Status);Assert.Equal("Mapping",result.QuestionKind);Assert.Equal(0,store.Calls);
    }
    [Fact] public async Task OtherSupplierFolderCannotUseLucRule()
    {
        var bytes=Workbook();var rule=Rule(Assert.Single(LucVolumeReview.Inspect(bytes)));var store=new Store();
        Assert.Equal("Held",(await new FolderImportService(store,[rule]).ImportAsync("KINOSHITA/list.xlsx",bytes,"2026-10-01",default)).Status);
        Assert.Equal(0,store.Calls);
    }
    [Fact] public async Task PartialAnswersStayHeldAndCanBeCompletedWithoutLosingHistory()
    {
        var bytes=Workbook(second:true);var store=new Store();var service=new FolderImportService(store);
        var held=await service.ImportAsync("LUC/list.xlsx",bytes,"2026-10-01",default);var r=Review(held,bytes);var q=held.Questions!;
        IntakeReviewStore.ApplyAnswer(r,Answer(r,new IntakeMappingAnswer(q[0].Id,"L")));Assert.Equal("Held",r.State);Assert.False(r.CanResume);
        var premature=await service.ImportAsync(r.Path,bytes,r.Date,default,mappingAnswers:r.MappingResolutions);
        Assert.Equal("Held",premature.Status);Assert.Single(premature.Questions!);Assert.Equal(0,store.Calls);
        r.Version++; // Database CAS increments the saved answer version; this test exercises the pure transition.
        IntakeReviewStore.ApplyAnswer(r,Answer(r,new IntakeMappingAnswer(q[1].Id,"L")));Assert.True(r.CanResume);
        Assert.Equal(2,r.MappingResolutions.Count);Assert.Equal(2,r.History.Count);Assert.All(r.History,h=>Assert.Single(h.MappingResolutions!));
        Assert.Equal("Success",(await service.ImportAsync(r.Path,bytes,r.Date,default,mappingAnswers:r.MappingResolutions)).Status);
        Assert.Equal(2,store.Rows!.Count);
    }
    [Theory][InlineData("choice")][InlineData("question")][InlineData("missing")][InlineData("duplicate")][InlineData("version")][InlineData("hash")]
    public async Task InvalidAnswersCannotMakeReady(string scenario)
    {
        var bytes=Workbook();var held=await new FolderImportService(new Store()).ImportAsync("LUC/list.xlsx",bytes,"2026-10-01",default);
        var r=Review(held,bytes);var q=Assert.Single(held.Questions!);var a=Answer(r,new IntakeMappingAnswer(q.Id,"L"));
        a=scenario switch {"choice"=>a with {MappingAnswers=[new(q.Id,"g")]},"question"=>a with {MappingAnswers=[new("missing","L")]},
            "missing"=>a with {MappingAnswers=[]},"duplicate"=>a with {MappingAnswers=[new(q.Id,"L"),new(q.Id,"L")]},
            "version"=>a with {Version=99},_=>a with {Hash="wrong"}};
        if(scenario is "version" or "hash")Assert.Throws<IntakeConflictException>(()=>IntakeReviewStore.ApplyAnswer(r,a));
        else Assert.Throws<ArgumentException>(()=>IntakeReviewStore.ApplyAnswer(r,a));
        Assert.Equal("Held",r.State);Assert.Empty(r.MappingResolutions);Assert.Empty(r.History);
    }
}
