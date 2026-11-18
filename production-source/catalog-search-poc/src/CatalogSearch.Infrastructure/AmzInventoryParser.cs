using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;
public sealed class AmzInventoryParser:IWorkbookContentParser
{
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));var hasJan=w.TryGetWorksheet("在庫表 (JAN情報)",out var s);s=hasJan?s:w.Worksheet("在庫表");
        // Accept only the two explicit JAN placements; worksheet names alone do not define column offsets.
        int Layout(IXLWorksheet sheet) => SupplierExcel.Normalize(sheet.Cell(2,18).GetString())=="JAN1" && SupplierExcel.Normalize(sheet.Cell(2,19).GetString())=="JAN2" ? 17
            : SupplierExcel.Normalize(sheet.Cell(2,11).GetString())=="JAN1" && SupplierExcel.Normalize(sheet.Cell(2,12).GetString())=="JAN2" ? 10 : 0;
        int Column(int c,int layout) => layout>0 && c>layout ? c+2 : c;
        var layout=Layout(s); int C(int c)=>Column(c,layout);
        SupplierExcel.Header(s,2,(3,"商品"),(4,"ビンテージ"),(5,"商品名"),(6,"生産者名"),(7,"商品名(カナ)"),(8,"生産者名(カナ)"),(9,"内容量"),(10,"色"),(C(15),"原産国"),(C(16),"生産地方"),(C(19),"参考上代価格"));
        if(!s.Cell(2,C(20)).GetString().Contains("在庫"))throw new InvalidDataException("AMZ stock column missing.");
        // Two presentations of the same products: verify common fields before choosing JAN evidence.
        if(hasJan&&w.TryGetWorksheet("在庫表",out var standard)){
            var a=standard.RowsUsed().Where(r=>!string.IsNullOrWhiteSpace(r.Cell(3).GetString())&&r.RowNumber()>2).ToDictionary(r=>r.Cell(3).GetString());
            var b=s.RowsUsed().Where(r=>!string.IsNullOrWhiteSpace(r.Cell(3).GetString())&&r.RowNumber()>2).ToDictionary(r=>r.Cell(3).GetString());
            if(!a.Keys.ToHashSet().SetEquals(b.Keys))throw new InvalidDataException("AMZ sheets disagree.");
            foreach(var key in a.Keys)for(var c=1;c<=20;c++)if(SupplierExcel.Text(a[key],Column(c,Layout(standard)))!=SupplierExcel.Text(b[key],C(c)))throw new InvalidDataException("AMZ sheets disagree.");
        }
        var rows=new List<SupplierInventoryRow>();for(var n=3;n<=(s.LastRowUsed()?.RowNumber()??2);n++){
            ct.ThrowIfCancellationRequested();var row=s.Row(n);string V(int c)=>SupplierExcel.Text(row,c);if(V(3)=="")continue;
            var stockRaw=V(C(20));var stock=Regex.IsMatch(stockRaw,@"^\d+↑$")?new InventoryValue(null,InventoryStatus.InStock,stockRaw):Regex.IsMatch(stockRaw,@"^\d+↓$")?new InventoryValue(null,InventoryStatus.LowStock,stockRaw):InventoryParser.Parse(stockRaw);
            string Fallback(string value,string fallback)=>value is "" or "-"?fallback:value;
            var vintage=V(4);var price=V(C(19));var jan=layout>0?WineToStyleInventoryParser.ParseJanCodes(V(layout+1)+" "+V(layout+2)):[];
            rows.Add(new(V(3),Fallback(V(8),V(6)),V(6),Fallback(V(7),V(5)),V(5),SupplierExcel.Year(vintage),vintage,SupplierExcel.Positive(V(9)),0,SupplierExcel.Price(price),price,s.Cell(2,C(19)).GetString().Contains("税込")||price.Contains("税込"),stock,V(10),V(C(15)),V(C(16)),jan,"",null,V(C(17)),"","","",V(2),"","",n,s.Name){RawCellsJson=SupplierExcel.Raw(row)});
        }return SupplierExcel.Validate(rows);
    }
}
