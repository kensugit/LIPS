using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public class LaVineeMappingTests
{
    private sealed class NoSaveStore:IInventoryStore
    {
        public int Calls;
        public Task<ImportResult> SaveAsync(SourceInput input,string hash,IReadOnlyList<SupplierInventoryRow> rows,CancellationToken ct)
        {Calls++;throw new InvalidOperationException("Unresolved rows must not reach business persistence.");}
    }
    [Fact] public async Task FolderFlowQuestionsThenAnsweredRowsHoldWithoutSaving()
    {
        var store=new NoSaveStore();var service=new FolderImportService(store);var bytes=Workbook(duplicate:true);
        var initial=await service.ImportAsync("親/ラヴィネ/2026-10-01.xlsx",bytes,null,default);
        Assert.Equal("Held",initial.Status);Assert.Equal("Mapping",initial.QuestionKind);Assert.Equal(6,initial.Questions!.Count);
        var answered=await service.ImportAsync("親/ラヴィネ/2026-10-01.xlsx",bytes,null,default,mappingAnswers:Answers(bytes,true));
        Assert.Equal("Held",answered.Status);Assert.Equal("Rows",answered.QuestionKind);Assert.Equal(2,answered.Holds!.Count);Assert.Equal(0,store.Calls);
    }
    public static byte[] Workbook(string region="AL",string volume="0.75",bool duplicate=false,bool changed=false)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("202610LaVineeリスト");
        string[] headers=["NO.","コ-ド","地域","ワイン名","生産者","ヴィンテージ","色","容量","在庫","納価（税別）","ご注文本数"];
        for(var c=0;c<11;c++)s.Cell(1,c+1).Value=headers[c];if(changed)s.Cell(1,12).Value="追加列";
        for(var n=2;n<=3;n++){s.Cell(n,1).Value=n;s.Cell(n,2).Value=duplicate?"100":"10"+n;s.Cell(n,3).Value=n==2?region:"BG";s.Cell(n,4).Value="合成ワイン";s.Cell(n,5).Value="合成生産者";s.Cell(n,6).Value=2024+n;s.Cell(n,7).Value="白";s.Cell(n,8).Value=volume;s.Cell(n,9).Value=12;s.Cell(n,10).Value=2300;s.Cell(n,11).Value=999;}
        s.Cell(4,4).Value="見出し";s.Cell(8,13).FormulaA1="SUM(J2:J3)";
        using var m=new MemoryStream();w.SaveAs(m);return m.ToArray();
    }
    private static IntakeMappingResolution[] Answers(byte[] bytes,bool priceUnit=false)=>LaVineeMappingReview.Inspect(bytes).Where(q=>q.Choices.Count>0&&(priceUnit||q.Kind!="ColumnMeaning")).Select(q=>new IntakeMappingResolution(q,q.Choices[0],"合成試験専用の明示回答")).ToArray();
    [Fact] public void RegistryAndQuestionsAreLimitedToExactLayoutAndRawRegion()
    {
        var bytes=Workbook();Assert.Equal("LA_VINEE",WorkbookParserRegistry.Detect(bytes).SupplierCode);
        var q=LaVineeMappingReview.Inspect(bytes);Assert.Equal(6,q.Count);
        Assert.Contains(q,x=>x.SourceKey=="C/AL"&&x.Choices.SequenceEqual(new[]{"アルザス"}));
        Assert.Contains(q,x=>x.SourceKey=="C/BG"&&x.Choices.SequenceEqual(new[]{"ブルゴーニュ"}));
        Assert.All(q,x=>Assert.Equal("LA_VINEE",x.Scope.SupplierId));Assert.Empty(LaVineeMappingReview.Inspect(Workbook(changed:true)));
    }
    [Fact] public void KnownAnswersNormalizeButPriceUnitStillHoldsAndOriginalIsUnchanged()
    {
        var bytes=Workbook();var before=bytes.ToArray();var parser=new LaVineeInventoryParser(Answers(bytes));var parsed=parser.ParseWithHolds(bytes,default);
        Assert.Equal(2,parsed.Rows.Count);Assert.Equal(2,parsed.Holds.Count);Assert.Equal(before,bytes);
        var row=parsed.Rows[0];Assert.Equal(750,row.VolumeMl);Assert.Equal("アルザス",row.Region);Assert.Equal(12,row.Inventory.Quantity);
        Assert.Null(row.ReferenceRetailPrice);Assert.Equal("",row.PriceRaw);var price=Assert.Single(row.PriceComponents);Assert.Equal("Wholesale",price.Kind);Assert.Equal(2300,price.Amount);Assert.False(price.TaxIncluded);Assert.Equal("Unknown",price.Unit);
        Assert.Equal(2,row.MappingEvidence.Count);Assert.Contains("K2",row.RawCellsJson);Assert.Contains("999",row.RawCellsJson);
        Assert.All(parsed.Holds,h=>Assert.Contains("価格単位",h.Reason));Assert.Throws<FolderImportHeldException>(()=>parser.Parse(bytes,default));
    }
    [Fact] public void DuplicateCodesRemainSeparateHeldSourceRows()
    {var b=Workbook(duplicate:true);var p=new LaVineeInventoryParser(Answers(b)).ParseWithHolds(b,default);Assert.Equal(2,p.Rows.Count);Assert.All(p.Holds,h=>Assert.Contains("同じ商品コード",h.Reason));Assert.Equal(new[]{2,3},p.Holds.SelectMany(h=>h.Sources).Select(s=>s.Row));}
    [Fact] public void UnknownRegionCannotBeGuessedOrBorrowAnotherAbbreviation()
    {
        var b=Workbook(region:"ZZ");var q=LaVineeMappingReview.Inspect(b).Single(q=>q.SourceKey=="C/ZZ");Assert.Empty(q.Choices);
        Assert.Throws<InvalidDataException>(()=>LaVineeMappingReview.ResolveRegion(q,new(q,"アルザス","guess")));
        var p=new LaVineeInventoryParser(Answers(b)).ParseWithHolds(b,default);Assert.Contains(p.Holds,h=>h.Reason.Contains("地域略号"));
    }
    [Theory][InlineData("0.0001")][InlineData("-1")][InlineData("9999999999999999999999999999")]
    public void InvalidCapacityCannotResolve(string raw)
    {var b=Workbook(volume:raw);var q=LaVineeMappingReview.Inspect(b).First(q=>q.Kind=="VolumeUnit");Assert.Throws<InvalidDataException>(()=>LaVineeMappingReview.ResolveVolume(q,new(q,"L","confirmed")));}
    [Fact] public void UnansweredCapacityAndRegionRemainHeld()
    {var p=new LaVineeInventoryParser().ParseWithHolds(Workbook(),default);Assert.All(p.Holds,h=>{Assert.Contains("容量単位",h.Reason);Assert.Contains("地域略号",h.Reason);});}
    [Theory][InlineData("AL","アルザス")][InlineData("BG","ブルゴーニュ")][InlineData("BX","ボルドー")][InlineData("CP","シャンパーニュ")][InlineData("JS","ジュラサヴォワ")][InlineData("LO","ロワール")][InlineData("LR","ラングドックルーション")][InlineData("PC","プロヴァンス")][InlineData("RH","ローヌ")][InlineData("SO","スッドウェスト")]
    public void OnlyReceivedRegionalMeaningsResolve(string code,string expected)
    {var q=LaVineeMappingReview.Inspect(Workbook(region:code)).Single(q=>q.Row==2&&q.Kind=="RegionAbbreviation");Assert.Equal(expected,LaVineeMappingReview.ResolveRegion(q,new(q,expected,"受領回答")));Assert.Throws<InvalidDataException>(()=>LaVineeMappingReview.ResolveRegion(q,new(q,"未承認地域","guess")));}
    [Fact] public void FormulaInBusinessCellIsHeldWithoutEvaluation()
    {
        using var w=new XLWorkbook(new MemoryStream(Workbook()));w.Worksheet(1).Cell(2,9).FormulaA1="1/0";
        using var m=new MemoryStream();w.SaveAs(m);var b=m.ToArray();var parsed=new LaVineeInventoryParser().ParseWithHolds(b,default);
        Assert.Contains(parsed.Holds,h=>h.Reason.Contains("数式"));Assert.DoesNotContain(LaVineeMappingReview.Inspect(b),q=>q.Row==2);
    }
    [Fact] public void ExplicitPerBottleAnswerPreservesWholesaleTaxAndOriginalEvidence()
    {
        var b=Workbook();var original=b.ToArray();var parsed=new LaVineeInventoryParser(Answers(b,true)).ParseWithHolds(b,default);
        Assert.Empty(parsed.Holds);Assert.Equal(original,b);
        Assert.All(parsed.Rows,r=>{var price=Assert.Single(r.PriceComponents);Assert.Equal("PerBottle",price.Unit);Assert.Equal("Wholesale",price.Kind);Assert.False(price.TaxIncluded);Assert.Equal(2300,price.Amount);Assert.Null(r.ReferenceRetailPrice);Assert.Equal(3,r.MappingEvidence.Count);});
    }
    [Theory][InlineData("PerCase")][InlineData("Unknown")][InlineData("PerBottle ")]
    public void OtherPriceUnitMeaningsCannotBeAdopted(string meaning)
    {
        var b=Workbook();var q=LaVineeMappingReview.Inspect(b).First(q=>q.Kind=="ColumnMeaning");
        Assert.Throws<InvalidDataException>(()=>LaVineeMappingReview.ResolvePriceUnit(q,new(q,meaning,"合成回答")));
        var answers=Answers(b,true).Select(a=>a.Question.Id==q.Id?new IntakeMappingResolution(q,meaning,"合成回答"):a).ToArray();
        var parsed=new LaVineeInventoryParser(answers).ParseWithHolds(b,default);
        Assert.Contains(parsed.Holds,h=>h.Sku==q.Sku);Assert.Equal("Unknown",parsed.Rows[0].PriceComponents[0].Unit);
    }
    [Fact] public void PriceAnswerForAnotherRowCannotSatisfyMissingRow()
    {
        var b=Workbook();var answers=Answers(b,true).Where(a=>a.Question.Kind!="ColumnMeaning"||a.Question.Row==2).ToArray();
        var parsed=new LaVineeInventoryParser(answers).ParseWithHolds(b,default);
        Assert.Single(parsed.Holds);Assert.Equal("PerBottle",parsed.Rows[0].PriceComponents[0].Unit);Assert.Equal("Unknown",parsed.Rows[1].PriceComponents[0].Unit);
    }
}
