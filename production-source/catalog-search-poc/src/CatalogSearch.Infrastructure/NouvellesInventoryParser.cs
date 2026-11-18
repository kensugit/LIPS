using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;

public sealed class NouvellesInventoryParser : IWorkbookContentParser
{
    public static byte[] ReadProjection(byte[] bytes)
    {
        using var input=new System.IO.Compression.ZipArchive(new MemoryStream(bytes,false));
        using var output=new MemoryStream();
        using(var target=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create,true))
        foreach(var part in input.Entries) {
            using var source=part.Open();using var dest=target.CreateEntry(part.FullName).Open();
            if(part.FullName=="xl/sharedStrings.xml") {
                using var reader=System.Xml.XmlReader.Create(source,new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null});
                var doc=System.Xml.Linq.XDocument.Load(reader);
                foreach(var node in doc.Descendants().Where(e=>e.Name.LocalName is "rPh" or "phoneticPr").ToList())node.Remove();
                doc.Save(dest);
            } else source.CopyTo(dest);
        }
        return output.ToArray();
    }
    public static string DocumentDate(byte[] bytes)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));
        var s=Sheet(w);var text=Text(s.Cell("A3"));
        var m=Regex.Match(text,@"更新日：\s*(20\d{2})年(\d{1,2})月(\d{1,2})日時点");
        if(!m.Success || !DateTime.TryParseExact($"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[3].Value):00}","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new InvalidDataException("Nouvelles document date missing.");
        return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    }
    private static string Text(IXLCell c) => c.HasFormula ? throw new InvalidDataException("Formula in semantic field.") : c.GetString().Trim();
    private static IXLWorksheet Sheet(XLWorkbook w)
    {
        if(w.Worksheets.Count!=1 || !w.TryGetWorksheet("Sheet1",out var s) || !Text(s.Cell("A1")).StartsWith("ヌーヴェル・セレクション",StringComparison.Ordinal))throw new InvalidDataException("Nouvelles layout changed.");
        SupplierExcel.Header(s,42,(1,"コード"),(2,"作品名"),(4,"年"),(5,"色"),(6,"ml"),(7,"納入価格"),(8,"希望小売価格"),(10,"在庫本数"));
        if(!Text(s.Cell("A4")).Contains("「少」＝24本以上100本未満")||!Text(s.Cell("A4")).Contains("「OK」＝100本以上")||!Text(s.Cell("A5")).Contains("すべて税別価格"))throw new InvalidDataException("Nouvelles price/stock contract changed.");
        return s;
    }
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));var s=Sheet(w);var rows=new List<SupplierInventoryRow>();string producer="";
        foreach(var row in s.RowsUsed().Where(r=>r.RowNumber()>42))
        {
            ct.ThrowIfCancellationRequested();string V(int c)=>Text(row.Cell(c));
            if(V(1)=="コード"){producer="";continue;}
            if(V(2)=="") {
                // Producer markers carry a source hyperlink formula. Never evaluate or fetch it.
                if(row.Cell(12).HasFormula && row.Cell(12).FormulaA1.StartsWith("HYPERLINK(",StringComparison.OrdinalIgnoreCase))producer=V(1);
                continue;
            }
            if(V(1)=="" || producer=="")throw new InvalidDataException("Missing source identity/producer.");
            var stockRaw=V(10);InventoryValue stock;
            if(stockRaw=="OK")stock=new(null,InventoryStatus.InStock,stockRaw);
            else if(stockRaw=="少")stock=new(null,InventoryStatus.LowStock,stockRaw);
            else if(int.TryParse(stockRaw,NumberStyles.None,CultureInfo.InvariantCulture,out var quantity)&&quantity>=0)stock=new(quantity,quantity==0?InventoryStatus.OutOfStock:InventoryStatus.InStock,stockRaw);
            else if(stockRaw=="")stock=new(null,InventoryStatus.Unknown,stockRaw);
            else throw new InvalidDataException("Unknown Nouvelles stock.");
            if(!int.TryParse(V(6),out var volume)||volume<=0||!decimal.TryParse(V(8),NumberStyles.Number,CultureInfo.InvariantCulture,out var price)||price<=0)throw new InvalidDataException("Invalid volume/retail.");
            var raw=new Dictionary<string,string>();foreach(var c in row.CellsUsed())raw[c.Address.ColumnLetter+c.Address.RowNumber]=c.HasFormula?"="+c.FormulaA1:c.GetString();
            var vintage=V(4);var wholesale=row.Cell(7).HasFormula?"="+row.Cell(7).FormulaA1:V(7);
            rows.Add(new(V(1),producer,producer,V(2),V(2),SupplierExcel.Year(vintage),vintage,volume,0,price,V(8),false,stock,V(5),"","",[],"",null,"","",V(3),"","納入価格原表記（未評価）: "+wholesale,"","",row.RowNumber(),s.Name)
                {RawCellsJson=JsonSerializer.Serialize(raw),Description=V(9)});
        }
        return SupplierExcel.Validate(rows);
    }
}
