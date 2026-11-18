using System.Globalization;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;
public sealed class KinoshitaInventoryParser:IWorkbookContentParser
{
    public static bool Matches(IXLWorksheet s) => SupplierExcel.Normalize(s.Cell(1,1).GetString())=="商品コード" &&
        (SupplierExcel.Normalize(s.Cell(1,3).GetString())=="商品名称1" || SupplierExcel.Normalize(s.Cell(1,2).GetString())=="商品名称1");
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));var s=w.Worksheets.Single(Matches);
        var october=SupplierExcel.Normalize(s.Cell(1,2).GetString())=="商品名称1";
        if(october) SupplierExcel.Header(s,1,(1,"商品コード"),(2,"商品名称１"),(3,"容量"),(4,"入数"),(5,"在庫数"),(6,"納入価格"),(7,"小売価格"),(8,"ヴィンテージ"),(9,"アルコール度数"),(10,"JANコード"),(11,"国名"));
        else SupplierExcel.Header(s,1,(1,"商品コード"),(2,"JANコード"),(3,"商品名称１"),(4,"ヴィンテージ"),(5,"国名"),(6,"容量"),(7,"入数"),(8,"小売価格"),(9,"アルコール度数"),(10,"在庫数"));
        var rows=new List<SupplierInventoryRow>();
        for(int n=2;n<=(s.LastRowUsed()?.RowNumber()??1);n++){
            ct.ThrowIfCancellationRequested();var row=s.Row(n);if(!row.CellsUsed().Any())continue;string V(int c)=>SupplierExcel.Text(row,c);
            var raw=V(october?5:10);var cases=Regex.IsMatch(raw,@"^\d+CS$",RegexOptions.IgnoreCase);var stock=cases?new InventoryValue(null,InventoryStatus.InStock,raw):InventoryParser.Parse(raw);
            var vintage=V(october?8:4);var price=V(october?7:8);
            rows.Add(new(V(1),"","",V(october?2:3),"",SupplierExcel.Year(vintage),vintage,SupplierExcel.Positive(V(october?3:6)),SupplierExcel.Positive(V(october?4:7)),SupplierExcel.Price(price),price,s.Cell(1,october?7:8).GetString().Contains("税込")||price.Contains("税込"),stock,"",V(october?11:5),"",WineToStyleInventoryParser.ParseJanCodes(V(october?10:2)),"",decimal.TryParse(V(9),NumberStyles.Number,CultureInfo.InvariantCulture,out var alcohol)?alcohol:null,"","","","",cases?"在庫はCS表記。正確な本数への換算は未実施。":"","","",n,s.Name){RawCellsJson=SupplierExcel.Raw(row),
                PriceComponents=october?[new("PurchasePrice",SupplierExcel.Price(V(6)),s.Cell(1,6).GetString().Contains("税込"),"Unknown",V(6))]:[]});
        }return SupplierExcel.Validate(rows);
    }
}
