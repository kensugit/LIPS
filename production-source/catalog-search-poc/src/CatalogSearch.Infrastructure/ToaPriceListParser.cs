using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;
public sealed class ToaPriceListParser:IWorkbookContentParser
{
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));var s=w.Worksheets.Single(x=>x.Cell(1,1).GetString().Contains("商品価格情報"));
        SupplierExcel.Header(s,4,(1,"商品コード"),(2,"商品名"),(6,"規格"),(7,"小売価格"),(8,"納入価格"),(13,"在庫"));
        var data=s.RowsUsed().Where(r=>r.RowNumber()>5&&SupplierExcel.Text(r,2)!=""&&SupplierExcel.Text(r,6) is not ("" or "規格")).ToList();
        var counts=data.GroupBy(r=>SupplierExcel.Text(r,1)).ToDictionary(g=>g.Key,g=>g.Count());
        var hash=Convert.ToHexString(SHA256.HashData(bytes));var result=new List<SupplierInventoryRow>();
        foreach(var row in data){ct.ThrowIfCancellationRequested();string V(int c)=>SupplierExcel.Text(row,c);var original=V(1);
            // Duplicate codes can represent different effective dates. Preserve each offer row without inventing a supplier identity.
            var provisional=!Regex.IsMatch(original,@"^\d+$")||counts[original]>1;
            var sku=provisional?$"TOA-{hash}-{row.RowNumber()}":original;
            var spec=SupplierExcel.Normalize(V(6)).Replace(",","").ToUpperInvariant();
            var match=Regex.Match(spec,@"^(\d+)(ML|L)?(?:[X×](\d+))?(?:/(\d+))?$");
            if(!match.Success)throw new InvalidDataException($"TOA unsupported pack at row {row.RowNumber()}.");
            var volume=checked(SupplierExcel.Positive(match.Groups[1].Value)*(match.Groups[2].Value=="L"?1000:1));
            var pack=match.Groups[3].Success?SupplierExcel.Positive(match.Groups[3].Value):1;
            var caseSize=match.Groups[4].Success?SupplierExcel.Positive(match.Groups[4].Value):0;
            // For a set, retain component capacity in evidence; don't let a multi-bottle set pass single-bottle volume filters.
            if(pack>1)volume=0;
            var raw=V(13);var stock=raw=="★"?new InventoryValue(null,InventoryStatus.Expected,raw):InventoryParser.Parse(raw);
            string Merged(int c){var cell=row.Cell(c);return cell.IsMerged()?cell.MergedRange().FirstCell().GetFormattedString().Trim():V(c);}
            var note=Merged(11);var price=V(7);
            result.Add(new(sku,"","",V(2),"",null,"",volume,caseSize,SupplierExcel.Price(price),price,price.Contains("税込"),stock,"","","",[],"",null,"","","","",string.Join(" / ",new[]{provisional?"商品コード未確定・価格条件別の原本行":"",note,pack>1?"複数本セット：容量は原本規格を確認":""}.Where(x=>x!="")),"","",row.RowNumber(),s.Name){RawCellsJson=SupplierExcel.Raw(row),OriginalSupplierProductCode=original,Description=note});
        }return SupplierExcel.Validate(result);
    }
}
