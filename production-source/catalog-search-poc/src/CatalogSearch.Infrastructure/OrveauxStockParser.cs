using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

// Stock tables only. New-arrival order forms have no volume column and are not stock tables.
public sealed class OrveauxStockParser : IWorkbookContentParser
{
    public const string ApprovedOriginalHash = "901FAB1EF4940521203D3CBAB3A4EBDA1E82F48C835669D867109DF3D62B4527";
    public static bool Matches(IXLWorksheet s) => s.Name == "定番品" &&
        s.Cell("N6").GetString() == "株式会社オルヴォー" && Header(s.Row(11));
    private static bool Header(IXLRow r) => SupplierExcel.Normalize(r.Cell(7).GetString()) == "容量" &&
        r.Cell(9).GetString() == "納入価格" && r.Cell(11).GetString() == "参考上代" && r.Cell(14).GetString() == "在庫";
    private static string Value(IXLCell c) => c.HasFormula
        ? throw new InvalidDataException("Formula in Orveaux semantic input.") : c.GetString().Trim();
    private static string Key(string producer, string name, string vintage, int volume) =>
        JsonSerializer.Serialize(new[] { SupplierExcel.Normalize(producer), SupplierExcel.Normalize(name), SupplierExcel.Normalize(vintage), volume.ToString(CultureInfo.InvariantCulture) });
    public static string DocumentDate(byte[] bytes)
    {
        using var w = new XLWorkbook(new MemoryStream(bytes, false));
        var s = w.Worksheets.SingleOrDefault(s => s.Name.Trim() == "新着") ?? throw new InvalidDataException("Orveaux date sheet missing.");
        if (s.Cell("G1").HasFormula || !s.Cell("G1").TryGetValue<DateTime>(out var date)) throw new InvalidDataException("Orveaux source date missing.");
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public static IReadOnlyList<ImportRowHold> NewArrivalHolds(byte[] bytes)
    {
        using var w = new XLWorkbook(new MemoryStream(bytes, false));
        var s = w.Worksheets.Single(s => s.Name.Trim() == "新着");
        if (Value(s.Cell("A11")) != "商品名" || Value(s.Cell("D11")) != "在庫数" || Value(s.Cell("G11")) != "ご注文本数")
            throw new InvalidDataException("Orveaux new-arrival layout changed.");
        var result = new List<ImportRowHold>();
        foreach (var r in s.RowsUsed(XLCellsUsedOptions.Contents))
        {
            if (Value(r.Cell(7)) != "本") continue;
            var raw = JsonSerializer.Serialize(r.CellsUsed().ToDictionary(c => c.Address.ToString()!, c => c.HasFormula ? "=" + c.FormulaA1 : c.GetString()));
            var stock = Value(r.Cell(4));
            result.Add(new("ORV-NEW-ROW-" + r.RowNumber(), "新着案内の容量・在庫表との商品対応が未確定です。容量を推測せず確認対象として残しています。",
                [new(s.Name, r.RowNumber(), raw, Stock(int.TryParse(stock, out _) ? stock + "本" : stock))]));
        }
        if (result.Count == 0) throw new InvalidDataException("Orveaux new-arrival rows missing.");
        return result;
    }
    public static InventoryValue Stock(string raw)
    {
        if (raw.Length == 0) return new(null, InventoryStatus.Unknown, raw);
        if (raw is "欠品中" or "欠品" or "×" or "✖" or "完売") return new(null, InventoryStatus.OutOfStock, raw);
        var m = Regex.Match(raw, @"^(\d+)(本|個)$");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n)) throw new InvalidDataException("Unknown Orveaux stock.");
        return new(n, n == 0 ? InventoryStatus.OutOfStock : InventoryStatus.InStock, raw);
    }
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes, CancellationToken ct)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes, false));
        if (!workbook.Worksheets.Any(Matches)) throw new InvalidDataException("Orveaux stock layout changed.");
        var groups = new Dictionary<string, List<SupplierInventoryRow>>(StringComparer.Ordinal);
        foreach (var s in workbook.Worksheets.Where(s => s.Name.Trim() != "新着"))
        {
            var headerSeen = false;
            foreach (var r in s.RowsUsed(XLCellsUsedOptions.Contents))
            {
                ct.ThrowIfCancellationRequested();
                if (Header(r)) { headerSeen = true; continue; }
                if (Value(r.Cell(3)).Length == 0) continue;
                if (!headerSeen) throw new InvalidDataException("Orveaux table header missing.");
                string V(int c) => Value(r.Cell(c));
                var name = V(1); var ja = V(2); var producer = V(3); var vintage = V(6);
                if (name.Length == 0 || ja.Length == 0 || vintage.Length == 0) throw new InvalidDataException("Orveaux identity missing.");
                var volume = SupplierExcel.Positive(V(7)); var caseSize = SupplierExcel.Positive(V(8));
                var retail = SupplierExcel.Price(V(11)); var stock = Stock(V(14));
                var raw = JsonSerializer.Serialize(r.CellsUsed().ToDictionary(c => c.Address.ColumnLetter + c.Address.RowNumber,
                    c => c.HasFormula ? "=" + c.FormulaA1 : c.GetString()));
                var key = Key(producer, name, vintage, volume);
                // Source-derived ID: never presented as an original supplier SKU.
                var sku = "ORV-XLS-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
                var row = new SupplierInventoryRow(sku, V(4), producer, ja, name, SupplierExcel.Year(vintage), vintage, volume, caseSize,
                    retail, V(11), false, stock, V(5), "", s.Name == "定番品" ? "" : s.Name, [], "", null, "", "", "", "",
                    "納入価格原表記: " + V(9) + "。12本価格（条件付き）: " + V(10), "", "", r.RowNumber(), s.Name)
                { RawCellsJson = raw, Description = V(15), Occurrences = [new(s.Name, r.RowNumber(), raw, stock)] };
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
                group.Add(row);
            }
            if (!headerSeen) throw new InvalidDataException("Unexpected Orveaux worksheet.");
        }
        var approved = Convert.ToHexString(SHA256.HashData(bytes)) == ApprovedOriginalHash;
        var result = new List<SupplierInventoryRow>();
        foreach (var group in groups.Values)
        {
            var first = group[0];
            var facts = group.Select(r => (r.ReferenceRetailPrice, r.Inventory.Quantity, r.Inventory.Status)).Distinct().Count();
            var decision = "";
            if (facts > 1)
            {
                if (!approved || group.Count != 2 || !group.Any(r => r.SourceSheet == "定番品" && r.SourceRow == 95 && r.Inventory.Quantity == 126) ||
                    !group.Any(r => r.SourceSheet == "スペイン" && r.SourceRow == 9 && r.Inventory.Quantity == 90) ||
                    group.Any(r => r.ProductNameEn != "Tinto Velasco Vino de la Tierra de Castilla" || r.ProducerNameEn != "Finca la Escatada" || r.VintageRaw != "2023" || r.VolumeMl != 750) ||
                    group.Select(r => r.ReferenceRetailPrice).Distinct().Count() != 1)
                    throw new FolderImportHeldException("オルヴォーの重複行で価格または在庫が相違しています。原典の確認が必要です。");
                first = group.Single(r => r.SourceSheet == "定番品");
                decision = "2026-10-17本人明示回答：定番品95行126本を採用。スペイン9行90本は原典Evidenceとして保持。";
            }
            var occurrences = group.SelectMany(r => r.Occurrences).ToArray();
            result.Add(first with { Occurrences = occurrences,
                RawCellsJson = JsonSerializer.Serialize(new { Sources = occurrences, Decision = decision }),
                PriceNote = string.Join(" / ", group.Select(r => r.PriceNote).Distinct()),
                Description = string.Join(" / ", group.Select(r => r.Description).Where(x => x.Length > 0).Distinct()) });
        }
        return SupplierExcel.Validate(result);
    }
}
