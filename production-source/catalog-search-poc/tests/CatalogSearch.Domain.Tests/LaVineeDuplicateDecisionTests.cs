using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;

namespace CatalogSearch.Domain.Tests;
public class LaVineeDuplicateDecisionTests
{
    private static LaVineeParseResult Parse(Action<IXLWorksheet>? change=null,bool missingPrice=false)
    {
        using var w=new XLWorkbook(new MemoryStream(LaVineeMappingTests.Workbook(duplicate:true)));
        var s=w.Worksheet(1);s.Cell(3,3).Value="AL";s.Cell(2,6).Value="2024";s.Cell(3,6).Value="2024";
        s.Cell(2,9).Value=7;s.Cell(3,9).Value=13;change?.Invoke(s);
        using var m=new MemoryStream();w.SaveAs(m);var bytes=m.ToArray();
        var answers=LaVineeMappingReview.Inspect(bytes).Where(q=>q.Choices.Count>0&&!(missingPrice&&q.Row==3&&q.Kind=="ColumnMeaning"))
            .Select(q=>new IntakeMappingResolution(q,q.Choices[0],"Synthetic explicit answer")).ToArray();
        return new LaVineeInventoryParser(answers).ParseWithHolds(bytes,default);
    }
    [Fact] public void UniqueSmallerStockAdoptsOneRowAndPreservesBothOriginalsAndAnswers()
    {
        var result=Parse();Assert.Empty(result.Holds);var row=Assert.Single(result.Rows);
        Assert.True(row.DuplicateMinimumSelected);Assert.Equal(7,row.Inventory.Quantity);Assert.Equal(2,row.SourceRow);
        Assert.Equal(new int?[]{7,13},row.Occurrences.Select(o=>o.Inventory.Quantity));Assert.Equal(6,row.MappingEvidence.Count);
        Assert.Contains("C2",row.Occurrences[0].RawCellsJson);Assert.Contains("C3",row.Occurrences[1].RawCellsJson);
    }
    [Fact] public void ZeroIsAdoptedWithoutAddingOtherStock()
    {var r=Parse(s=>s.Cell(3,9).Value=0);Assert.Empty(r.Holds);Assert.Equal(0,Assert.Single(r.Rows).Inventory.Quantity);}
    [Fact] public void EqualQuantityRemainsHeld()
    {var r=Parse(s=>s.Cell(3,9).Value=7);Assert.Equal(2,r.Holds.Count);Assert.All(r.Rows,x=>Assert.False(x.DuplicateMinimumSelected));}
    [Fact] public void UnknownQuantityRemainsHeld()
    {var r=Parse(s=>s.Cell(3,9).Value="不明");Assert.Equal(2,r.Holds.Count);}
    [Theory][InlineData(4,"別名")][InlineData(5,"別生産者")][InlineData(6,"2023")][InlineData(8,"1.5")][InlineData(10,"9999")]
    public void OtherUnresolvedDifferenceIsNotResolvedByLowerStock(int column,string value)
    {var r=Parse(s=>s.Cell(3,column).Value=value);Assert.Equal(2,r.Holds.Count);Assert.All(r.Rows,x=>Assert.False(x.DuplicateMinimumSelected));}
    [Fact] public void NvSaExceptionIsNotGeneralizedToOtherBytesOrSku()
    {var r=Parse(s=>{s.Cell(2,6).Value="N.V";s.Cell(3,6).Value="S.A.";});Assert.Equal(2,r.Holds.Count);}
    [Fact] public void MissingOtherRowPriceAnswerHoldsWholeSku()
    {var r=Parse(missingPrice:true);Assert.Equal(2,r.Holds.Count);Assert.All(r.Rows,x=>Assert.False(x.DuplicateMinimumSelected));}
}
