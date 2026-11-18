using System.Security.Cryptography;
using CatalogSearch.Application;
using CatalogSearch.Infrastructure;
using ClosedXML.Excel;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public class LucVolumeReviewTests
{
    private static byte[] Workbook(string raw,string second="",bool changeHeader=false)
    {
        using var w=new XLWorkbook();var s=w.AddWorksheet("在庫リスト");
        string[] headers=["商品コード","JANコード","生産者コード","生産者（アルファベット）","生産者（カタカナ）","ヴィンテージ","商品名（アルファベット）","商品名（カタカナ）","容量","色","地域コード","原産国","生産地方","生産地域","格付コード","格付名","参考小売価格","商品コメント","在庫"];
        for(var i=0;i<headers.Length;i++)s.Cell(5,i+1).Value=headers[i];
        if(changeHeader)s.Cell(5,10).Value="種類";
        for(var n=6;n<=(second.Length>0?7:6);n++)
        {s.Cell(n,1).Value="SYNTH-"+n;s.Cell(n,8).Value="合成商品";s.Cell(n,9).Value=n==6?raw:second;s.Cell(n,17).Value="1000";s.Cell(n,19).Value="◎";}
        using var m=new MemoryStream();w.SaveAs(m);return m.ToArray();
    }
    [Theory][InlineData("750","mL",750)][InlineData("75","cL",750)][InlineData("0.75","L",750)]
    public void ExplicitAnswerConvertsBareValuePreservesSourceAndEvidence(string raw,string unit,int ml)
    {
        var bytes=Workbook(raw);var hash=SHA256.HashData(bytes);var q=Assert.Single(LucVolumeReview.Inspect(bytes));
        Assert.Equal("LUC",q.Scope.SupplierId);Assert.Equal("LUC/1",q.Scope.DocumentKind);Assert.Equal(6,q.Row);Assert.Equal(raw,q.RawValue);
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser().Parse(bytes,default));
        var answer=new IntakeMappingResolution(q,unit,"合成本人確認：この列の単位");
        var row=Assert.Single(new LucInventoryParser([answer]).Parse(bytes,default));
        Assert.Equal(ml,row.VolumeMl);Assert.Equal(answer,Assert.Single(row.MappingEvidence));Assert.Contains(raw,row.RawCellsJson);
        Assert.Equal(hash,SHA256.HashData(bytes));
    }
    [Fact] public void ExplicitMlNeedsNoAnswerAndInvalidLayoutCreatesNoQuestion()
    {
        var bytes=Workbook("(750ml)");Assert.Empty(LucVolumeReview.Inspect(bytes));Assert.Empty(Assert.Single(new LucInventoryParser().Parse(bytes,default)).MappingEvidence);
        Assert.Empty(LucVolumeReview.Inspect(Workbook("0.75",changeHeader:true)));
    }
    [Fact] public void EveryBareRowHasOwnStableLocationAndMustBeAnswered()
    {
        var bytes=Workbook("0.75","1.5");var q=LucVolumeReview.Inspect(bytes);Assert.Equal(2,q.Count);Assert.NotEqual(q[0].Id,q[1].Id);
        Assert.Equal(q[0].Id,LucVolumeReview.Inspect(bytes)[0].Id);Assert.Equal(q[0].Scope,q[1].Scope);
        var answers=q.Select(x=>new IntakeMappingResolution(x,"L","合成列単位確認")).ToArray();
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([answers[0]]).Parse(bytes,default));
        Assert.Equal(new[]{750,1500},new LucInventoryParser(answers).Parse(bytes,default).Select(r=>r.VolumeMl));
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([answers[0],answers[1] with {Meaning="cL"}]).Parse(bytes,default));
    }
    [Theory][InlineData("0.0001","L")][InlineData("2147483648","mL")][InlineData("750","unknown")]
    public void InvalidOrFractionalNormalizedVolumeRejects(string raw,string unit)
    {
        var bytes=Workbook(raw);var q=Assert.Single(LucVolumeReview.Inspect(bytes));
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([new(q,unit,"合成確認")]).Parse(bytes,default));
    }
    [Fact] public void WrongRowRawSupplierFingerprintMissingEvidenceAndDuplicateAnswersReject()
    {
        var bytes=Workbook("0.75");var q=Assert.Single(LucVolumeReview.Inspect(bytes));var a=new IntakeMappingResolution(q,"L","合成確認");
        var changed=new[]{q with {Row=7},q with {RawValue="1"},q with {Scope=q.Scope with {SupplierId="OTHER"}},q with {Scope=q.Scope with {ColumnsFingerprint=new('C',64)}}};
        foreach(var invalid in changed)Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([a with {Question=invalid}]).Parse(bytes,default));
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([a with {Evidence=""}]).Parse(bytes,default));
        Assert.Throws<InvalidDataException>(()=>new LucInventoryParser([a,a]).Parse(bytes,default));
    }
}
