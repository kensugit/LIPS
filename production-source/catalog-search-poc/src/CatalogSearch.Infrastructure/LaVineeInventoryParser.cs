using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

// Rows are extraction candidates; Holds must be resolved before any business save.
public sealed record LaVineeParseResult(IReadOnlyList<SupplierInventoryRow> Rows,IReadOnlyList<ImportRowHold> Holds);
public sealed class LaVineeInventoryParser(IReadOnlyList<IntakeMappingResolution>? resolutions=null):IWorkbookContentParser
{
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        var parsed=ParseWithHolds(bytes,ct);
        if(parsed.Holds.Count>0)throw new FolderImportHeldException("ラヴィネ資料に未確定の価格単位・商品識別・回答があります。対象行を確認してください。");
        return parsed.Rows;
    }
    public LaVineeParseResult ParseWithHolds(byte[] bytes,CancellationToken ct)
    {
        var questions=LaVineeMappingReview.Inspect(bytes);
        using var workbook=new XLWorkbook(new MemoryStream(bytes,false));
        var sheets=workbook.Worksheets.Where(LaVineeMappingReview.Matches).ToArray();
        if(sheets.Length!=1)throw new InvalidDataException("LaVinee exact layout required.");
        var sheet=sheets[0];var data=sheet.RowsUsed().Where(r=>r.RowNumber()>1&&!LaVineeMappingReview.IsNonProduct(r)).ToArray();
        var duplicates=data.GroupBy(r=>LaVineeMappingReview.Text(r.Cell(2))).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var rows=new List<SupplierInventoryRow>();var holds=new List<ImportRowHold>();
        foreach(var row in data)
        {
            ct.ThrowIfCancellationRequested();string V(int c)=>LaVineeMappingReview.Text(row.Cell(c));
            var sku=V(2);var raw=LaVineeMappingReview.Raw(row);var stockRaw=V(9);
            var stock=InventoryParser.Parse(stockRaw);
            var occurrence=new SourceRowOccurrence(sheet.Name,row.RowNumber(),raw,stock);
            var reasons=new List<string>();
            if(Enumerable.Range(1,11).Any(c=>row.Cell(c).HasFormula)){holds.Add(new(sku,"業務列に数式があります。値を推測せず保留します。",[occurrence]));continue;}
            if(!Regex.IsMatch(sku,@"^\d+$")||V(4).Length==0){holds.Add(new(sku,"商品コードまたは商品名が未確定です。",[occurrence]));continue;}
            if(duplicates.Contains(sku))reasons.Add("同じ商品コードが複数行にあります。別VT等の商品同一性は未確定です。");
            var evidence=new List<IntakeMappingResolution>();int volume=0;var region=V(3);var priceUnit="Unknown";var sourceCategory="";
            foreach(var q in questions.Where(q=>q.Row==row.RowNumber()))
            {
                var answers=(resolutions??[]).Where(a=>a.Question.Id==q.Id).ToArray();
                if(answers.Length!=1){reasons.Add(q.Kind switch {"VolumeUnit"=>"容量単位の保存済み回答が必要です。","ColumnMeaning"=>"納価（税別）の価格単位が未確定です。希望小売価格へ転記しません。",_=>"地域略号の保存済み対応が必要です。"});continue;}
                try
                {
                    if(q.Kind=="VolumeUnit")volume=LaVineeMappingReview.ResolveVolume(q,answers[0]);
                    else if(q.Kind=="ColumnMeaning")priceUnit=LaVineeMappingReview.ResolvePriceUnit(q,answers[0]);
                    else { region=LaVineeMappingReview.ResolveRegion(q,answers[0]);sourceCategory=LaVineeMappingReview.ResolveSourceProductCategory(q,answers[0]); }
                    evidence.Add(answers[0]);
                }
                catch(InvalidDataException){reasons.Add("原典と対応する有効な回答がありません。");}
            }
            if(!int.TryParse(stockRaw,out var quantity)||quantity<0)reasons.Add("在庫数量を整数本として確認できません。");
            var price=WineToStyleInventoryParser.ParseMoney(V(10));
            if(price is null or <0)reasons.Add("納価を確認できません。");
            rows.Add(new(sku,V(5),"",V(4),"",SupplierExcel.Year(V(6)),V(6),volume,0,null,"",false,stock,V(7),"",region,[],"",null,"","","","",priceUnit=="Unknown"?"納価税別・価格単位未確定":"納価税別・1本単位確認済み","","",row.RowNumber(),sheet.Name)
            {RawCellsJson=raw,SourceRegionRaw=V(3),SourceProductCategoryRaw=sourceCategory,OriginalSupplierProductCode=sku,PriceComponents=[new("Wholesale",price,false,priceUnit,V(10))],MappingEvidence=evidence,Occurrences=[occurrence]});
            if(reasons.Count>0)holds.Add(new(sku,string.Join(" / ",reasons),[occurrence]));
        }
        if(rows.Count==0&&holds.Count==0)throw new InvalidDataException("LaVinee has no product rows.");
        LaVineeDuplicatePolicy.Apply(bytes,rows,holds);
        return new(rows,holds);
    }
}
