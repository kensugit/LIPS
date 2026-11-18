using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;

public sealed class LucInventoryParser(IReadOnlyList<IntakeMappingResolution>? resolutions = null) : ISupplierInventoryWorkbookParser, IWorkbookContentParser
{
    public string SupplierCode => "LUC";
    public Task<IReadOnlyList<SupplierInventoryRow>> ParseAsync(string path, CancellationToken ct) => Task.FromResult(Parse(File.ReadAllBytes(path), ct));
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] content, CancellationToken ct)
    {
        var questions=resolutions is {Count:>0}?LucVolumeReview.Inspect(content):[];
        using var workbook = new XLWorkbook(new MemoryStream(content, false));
        if (!workbook.TryGetWorksheet("在庫リスト", out var sheet)) throw new InvalidDataException("LUC sheet missing.");
        string Clean(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "");
        var headers = sheet.Row(5).CellsUsed().ToDictionary(c => Clean(c.GetString()).Replace("(税込)", ""), c => c.Address.ColumnNumber);
        string[] required = ["商品コード","JANコード","生産者コード","生産者(アルファベット)","生産者(カタカナ)","ヴィンテージ","商品名(アルファベット)","商品名(カタカナ)","容量","色","原産国","生産地方","生産地域","格付名","参考小売価格","商品コメント","在庫"];
        if(required.Any(x => !headers.ContainsKey(x))) throw new InvalidDataException("LUC required columns missing.");
        var result = new List<SupplierInventoryRow>();
        var skus = new HashSet<string>(StringComparer.Ordinal);
        for(var n=6;n<=(sheet.LastRowUsed()?.RowNumber()??5);n++)
        {
            ct.ThrowIfCancellationRequested();var row=sheet.Row(n);
            string V(string key) => row.Cell(headers[key]).GetFormattedString().Trim();
            if(row.CellsUsed().All(c=>string.IsNullOrWhiteSpace(c.GetFormattedString())))continue;
            var sku=V("商品コード");var name=V("商品名(カタカナ)");
            if(sku.Length==0||name.Length==0||!skus.Add(sku))throw new InvalidDataException($"LUC identity invalid at row {n}.");
            var volumeMatch=Regex.Match(Clean(V("容量")),@"^\(?(\d+)ml\)?$",RegexOptions.IgnoreCase);
            var volume=0;IntakeMappingResolution? mapping=null;
            if(!volumeMatch.Success||!int.TryParse(volumeMatch.Groups[1].Value,out volume)||volume<=0)
            {
                var question=questions.SingleOrDefault(q=>q.Row==n && q.Sheet==sheet.Name);
                var answers=question is null?[]:resolutions!.Where(r=>r.Question?.Id==question.Id).ToArray();
                if(question is null||answers.Length!=1)throw new InvalidDataException($"LUC volume invalid or unanswered at row {n}.");
                mapping=answers[0];volume=LucVolumeReview.Resolve(question,mapping);
            }
            var priceRaw=V("参考小売価格");var price=WineToStyleInventoryParser.ParseMoney(priceRaw);
            if(price<0||(price is null&&priceRaw is not ("" or "オープン")))throw new InvalidDataException($"LUC price invalid at row {n}.");
            var rawStock=V("在庫");
            // ◎ is a lower bound (60+), never an exact quantity of 60.
            var stock=rawStock=="◎"?new InventoryValue(null,InventoryStatus.InStock,rawStock):InventoryParser.Parse(rawStock);
            var vintage=V("ヴィンテージ");
            var tax=sheet.Cell(5,headers["参考小売価格"]).GetString().Contains("税込",StringComparison.Ordinal)||priceRaw.Contains("税込",StringComparison.Ordinal);
            result.Add(new(sku,V("生産者(カタカナ)"),V("生産者(アルファベット)"),name,V("商品名(アルファベット)"),
                int.TryParse(vintage,out var year)?year:null,vintage,volume,0,price,priceRaw,tax,stock,V("色"),V("原産国"),V("生産地方"),
                WineToStyleInventoryParser.ParseJanCodes(V("JANコード")),"",null,V("生産地域"),"","","","","", "",n,sheet.Name)
            { Description=V("商品コメント"),RawCellsJson=JsonSerializer.Serialize(row.CellsUsed().ToDictionary(c=>c.Address.ToString()!,c=>c.GetFormattedString())),MappingEvidence=mapping is null?[]:[mapping] });
        }
        if(result.Count==0)throw new InvalidDataException("LUC contains no products.");
        if(result.SelectMany(r=>r.MappingEvidence).Select(r=>r.Meaning).Distinct().Count()>1)
            throw new InvalidDataException("LUC volume column answers conflict.");
        return result;
    }
}
