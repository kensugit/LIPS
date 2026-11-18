using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public class LaVineeReceivedDecisionTests
{
    public static TheoryData<string,string,string> CompoundCases => new()
    {
        {"AL発泡性","アルザス","発泡性"},{"BG酒精強化","ブルゴーニュ","酒精強化"},
        {"BG蒸留酒","ブルゴーニュ","蒸留酒"},{"BG発泡性","ブルゴーニュ","発泡性"},
        {"CP非発泡性","シャンパーニュ","非発泡性"},{"CP酒精強化","シャンパーニュ","酒精強化"},
        {"CP酒精強化(甘味）","シャンパーニュ","酒精強化(甘味）"},{"CP蒸留酒","シャンパーニュ","蒸留酒"},
        {"JS酒精強化(甘味果実酒）","ジュラサヴォワ","酒精強化(甘味果実酒）"},{"JS酒精強化","ジュラサヴォワ","酒精強化"},
        {"JS発泡性","ジュラサヴォワ","発泡性"},{"JS甘味果実酒","ジュラサヴォワ","甘味果実酒"},
        {"LO発泡性","ロワール","発泡性"},{"LO酒精強化","ロワール","酒精強化"},
        {"LR酒精強化","ラングドックルーション","酒精強化"},{"LR甘味果実酒","ラングドックルーション","甘味果実酒"},
        {"RH酒精強化","ローヌ","酒精強化"},{"SO発泡性","スッドウェスト","発泡性"}
    };
    private static IntakeMappingResolution[] Answers(byte[] bytes)=>LaVineeMappingReview.Inspect(bytes)
        .Where(q=>q.Choices.Count>0).Select(q=>new IntakeMappingResolution(q,q.Choices[0],"合成試験：受領方針")).ToArray();
    [Theory][MemberData(nameof(CompoundCases))]
    public void ConfirmedCompoundSeparatesRegionAndVerbatimSuffixWithoutReclassifying(string raw,string region,string category)
    {
        var bytes=LaVineeMappingTests.Workbook(region:raw);var before=bytes.ToArray();
        var result=new LaVineeInventoryParser(Answers(bytes)).ParseWithHolds(bytes,default);
        Assert.Empty(result.Holds);Assert.Equal(before,bytes);
        var row=result.Rows[0];Assert.Equal(region,row.Region);Assert.Equal(raw,row.SourceRegionRaw);
        Assert.Equal(category,row.SourceProductCategoryRaw);Assert.Equal("白",row.ProductType);
        using var original=JsonDocument.Parse(row.RawCellsJson);Assert.Equal(raw,original.RootElement.GetProperty("C2").GetString());
        var restored=JsonSerializer.Deserialize<SupplierInventoryRow>(JsonSerializer.Serialize(row))!;
        Assert.Equal(category,restored.SourceProductCategoryRaw);Assert.Equal(raw,restored.SourceRegionRaw);
        Assert.Equal(raw,Assert.Single(row.MappingEvidence,a=>a.Question.Kind=="RegionAbbreviation").Question.RawValue);
        var price=Assert.Single(row.PriceComponents);Assert.Equal("PerBottle",price.Unit);Assert.False(price.TaxIncluded);Assert.Equal("Wholesale",price.Kind);Assert.Null(row.ReferenceRetailPrice);
    }
    [Theory][InlineData("CALVADOS","カルヴァドス")][InlineData("COGNAC","コニャック")]
    public void ReceivedJapaneseLabelDoesNotInventGeographyOrClassification(string raw,string expected)
    {
        var bytes=LaVineeMappingTests.Workbook(region:raw);var result=new LaVineeInventoryParser(Answers(bytes)).ParseWithHolds(bytes,default);
        Assert.Empty(result.Holds);var row=result.Rows[0];Assert.Equal(expected,row.Region);Assert.Equal(raw,row.SourceRegionRaw);
        Assert.Empty(row.SourceProductCategoryRaw);Assert.Empty(row.Country);Assert.Equal("白",row.ProductType);
    }
    [Theory][InlineData("AL未確認区分")][InlineData("PC発泡性")][InlineData("ZZ酒精強化")]
    public void UnconfirmedCompoundIsHeldEvenWithKnownPrefixOrCategory(string raw)
    {
        var bytes=LaVineeMappingTests.Workbook(region:raw);var question=LaVineeMappingReview.Inspect(bytes).Single(q=>q.Row==2&&q.Kind=="RegionAbbreviation");
        Assert.Empty(question.Choices);var result=new LaVineeInventoryParser(Answers(bytes)).ParseWithHolds(bytes,default);
        Assert.Single(result.Holds);Assert.Empty(result.Rows[0].SourceProductCategoryRaw);
    }
    [Fact] public void NewSpellingStillRequiresExplicitRowAndPriceAnswers()
    {
        var bytes=LaVineeMappingTests.Workbook(region:"AL発泡性");
        var noRegion=Answers(bytes).Where(a=>!(a.Question.Kind=="RegionAbbreviation"&&a.Question.Row==2)).ToArray();
        var result=new LaVineeInventoryParser(noRegion).ParseWithHolds(bytes,default);
        Assert.Single(result.Holds);Assert.Empty(result.Rows[0].SourceProductCategoryRaw);
        result=new LaVineeInventoryParser(Answers(bytes).Where(a=>a.Question.Kind!="ColumnMeaning").ToArray()).ParseWithHolds(bytes,default);
        Assert.Equal(2,result.Holds.Count);Assert.All(result.Rows,r=>Assert.Equal("Unknown",r.PriceComponents[0].Unit));
    }
}
