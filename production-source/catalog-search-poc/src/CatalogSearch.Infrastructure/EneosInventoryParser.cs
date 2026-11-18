using System.Globalization;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

public sealed class EneosInventoryParser : IWorkbookContentParser
{
    public static string? DocumentDate(byte[] bytes)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));var sheets=w.Worksheets.Where(Matches).ToArray();
        if(sheets.Length!=1)throw new InvalidDataException("Unknown ENEOS date layout.");
        var s=sheets[0];var cell=N(s.Cell(30,1))=="品番"?s.Cell(1,1):N(s.Cell(3,4))=="商品名"?s.Cell(3,2):s.Cell(3,3);
        if(cell.HasFormula)throw new InvalidDataException("Formula date is not a confirmed source date.");
        if(cell.DataType==XLDataType.DateTime)return cell.GetDateTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var text=T(cell).Replace('/','-');
        return FolderImportService.TryDate(text,null,out var date)?date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):null;
    }
    private static string T(IXLCell c)=>c.HasFormula?throw new InvalidDataException("Formula in ENEOS value."):c.GetString().Trim();
    private static string N(IXLCell c)=>SupplierExcel.Normalize(T(c));
    public static bool Matches(IXLWorksheet s)=>
        N(s.Cell(3,1))=="品番" && ((N(s.Cell(3,4))=="商品名"&&N(s.Cell(3,13)).StartsWith("参考上代"))||
            (N(s.Cell(3,5))=="品名"&&N(s.Cell(3,12)).StartsWith("参考小売価格"))) ||
        N(s.Cell(30,1))=="品番"&&N(s.Cell(30,2))=="本数"&&N(s.Cell(30,11)).StartsWith("納入価格");
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));
        var sheets=w.Worksheets.Where(Matches).ToArray();if(sheets.Length!=1)throw new InvalidDataException("Ambiguous ENEOS layout.");
        var s=sheets[0];var fine=N(s.Cell(30,1))=="品番";var agri=!fine&&N(s.Cell(3,4))=="商品名";
        int header=fine?30:3,stock=fine||agri?2:3,name=fine||agri?4:5,vt=agri?6:fine?5:6,
            producer=agri?9:fine?7:8,volume=agri?12:fine?9:10,retail=agri?13:12,wholesale=agri?14:fine?11:13;
        var result=new List<SupplierInventoryRow>();
        foreach(var line in s.RowsUsed().Where(r=>r.RowNumber()>header))
        {
            ct.ThrowIfCancellationRequested();string V(int c)=>T(line.Cell(c));
            var sku=V(1);if(sku==""||SupplierExcel.Normalize(sku)=="品番")continue;
            if(!sku.Any(char.IsDigit)||!sku.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-'))continue; // section headings have no supplier item number
            if(V(name)=="")throw new InvalidDataException("Missing product name.");
            var quantityRaw=V(stock);InventoryValue inventory;
            if(int.TryParse(quantityRaw,NumberStyles.None,CultureInfo.InvariantCulture,out var quantity)&&quantity>=0)
                inventory=new(quantity,quantity==0?InventoryStatus.OutOfStock:InventoryStatus.InStock,quantityRaw);
            else if(quantityRaw is "欠品" or "品切" or "品切れ")inventory=new(null,InventoryStatus.OutOfStock,quantityRaw);
            else if(quantityRaw=="")inventory=new(null,InventoryStatus.Unknown,quantityRaw);
            else throw new InvalidDataException("Unknown ENEOS stock notation.");
            var prices=new List<SourcePriceComponent>();
            SourcePriceComponent Price(int col,string kind){var raw=V(col);var h=T(s.Cell(header,col));return new(kind,SupplierExcel.Price(raw),h.Contains("税込"),h.Contains("単品")?"PerBottle":h.Contains("ケース")?"PerCase":"Unknown",raw);}
            var reference=fine?null:Price(retail,"ReferenceRetail");
            var delivery=Price(wholesale,"Wholesale");
            if(reference is not null)prices.Add(reference);prices.Add(delivery);
            var raw=line.CellsUsed().ToDictionary(c=>c.Address.ToString()!,c=>c.HasFormula?"="+c.FormulaA1:c.GetString());
            result.Add(new(sku,V(producer),"",V(name),fine?V(15):"",SupplierExcel.Year(V(vt)),V(vt),SupplierExcel.Positive(V(volume)),
                fine?0:int.TryParse(V(agri?10:11),out var caseSize)?caseSize:0,reference?.Amount,fine?"オープン":V(retail),reference?.TaxIncluded??false,inventory,
                V(agri?5:fine?6:7),agri?V(7):fine?V(13):"",V(agri?8:fine?8:9),[],"",null,"","","","",
                "納入価格原表記: "+delivery.Raw,"","",line.RowNumber(),s.Name){RawCellsJson=JsonSerializer.Serialize(raw),PriceComponents=prices});
        }
        return result;
    }
}
