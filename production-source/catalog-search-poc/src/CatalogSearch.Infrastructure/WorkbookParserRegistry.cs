using CatalogSearch.Application;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;
public sealed record WorkbookParserSelection(string SupplierCode,string SupplierName,string Version,IWorkbookContentParser Parser);
public static class WorkbookParserRegistry
{
    public static WorkbookParserSelection Detect(byte[] bytes)
    {
        using var workbook=new XLWorkbook(new MemoryStream(bytes,false));
        var luc=workbook.TryGetWorksheet("在庫リスト",out var l)&&l.Cell(5,1).GetString().Trim()=="商品コード"&&l.Cell(5,2).GetString().Trim()=="JANコード";
        var wts=workbook.TryGetWorksheet("ワインリスト",out var w)&&w.Row(2).CellsUsed().Any(c=>c.GetString().Trim()=="SKU");
        var candidates=new List<WorkbookParserSelection>();
        if(workbook.Worksheets.Any(OrveauxStockParser.Matches))candidates.Add(new("ORVEAUX","オルヴォー",OrveauxExcelPriorityStore.Version,new OrveauxStockParser()));
        if(workbook.Worksheets.Any(LaVineeMappingReview.Matches))candidates.Add(new("LA_VINEE","ラヴィネ","LaVinee/1",new LaVineeInventoryParser()));
        if(workbook.Worksheets.Any(EneosInventoryParser.Matches))candidates.Add(new("ENEOS","ENEOSマテリアルトレーディング株式会社","ENEOS/1",new EneosInventoryParser()));
        if(luc)candidates.Add(new("LUC","LUC","LUC/1",new LucInventoryParser()));
        if(wts)candidates.Add(new("WINE_TO_STYLE","Wine to Style","WineToStyle/2",new WineToStyleInventoryParser()));
        if(workbook.Worksheets.Any(KinoshitaInventoryParser.Matches))candidates.Add(new("KINOSHITA","木下インターナショナル","Kinoshita/2",new KinoshitaInventoryParser()));
        if(workbook.Worksheets.Any(s=>s.Cell(1,1).GetString().Contains("商品価格情報")&&s.Cell(4,8).GetString()=="納入価格"))candidates.Add(new("TOA","東亜商事","Toa/1",new ToaPriceListParser()));
        if(workbook.Worksheets.Any(s=>s.Cell(1,1).GetString().Contains("エイ・エム・ズィー")))candidates.Add(new("AMZ","AMZ","AMZ/1",new AmzInventoryParser()));
        if(workbook.TryGetWorksheet("Sheet1",out var lc)&&lc.Cell("A1").GetString()=="Le Caveau"&&lc.Cell("G10").GetString()=="卸価格")candidates.Add(new("LE_CAVEAU","ルカヴォー","LeCaveau/20260929-v2",new LeCaveauInventoryParser()));
        if(workbook.TryGetWorksheet("Sheet1",out var ns)&&!ns.Cell("A1").HasFormula&&ns.Cell("A1").GetString().StartsWith("ヌーヴェル・セレクション",StringComparison.Ordinal))candidates.Add(new("NOUVELLES_SELECTIONS","ヌーヴェル・セレクション","Nouvelles/1",new NouvellesInventoryParser()));
        if(candidates.Count==0)throw new UnsupportedWorkbookException();
        if(candidates.Count!=1)throw new AmbiguousWorkbookException();
        return candidates[0];
    }
}

public sealed class UnsupportedWorkbookException : Exception;
public sealed class AmbiguousWorkbookException : Exception;
