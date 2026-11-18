using System.Globalization;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

public sealed partial class WineToStyleInventoryParser : ISupplierInventoryWorkbookParser, IWorkbookContentParser
{
    public string SupplierCode => "WINE_TO_STYLE";

    public Task<IReadOnlyList<SupplierInventoryRow>> ParseAsync(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(Parse(File.ReadAllBytes(path), cancellationToken));
    }

    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("ワインリスト");
        var header = sheet.Row(2).CellsUsed().ToDictionary(
            cell => NormalizeHeader(cell.GetString()), cell => cell.Address.ColumnNumber,
            StringComparer.OrdinalIgnoreCase);

        var required = new[] { "SKU", "生産者名／ブランド名", "Producer / Brand", "商品名", "Wine", "VTG", "容量", "入数", "希望小売", "在庫", "タイプ", "国", "産地", "JAN" };
        var missing = required.Where(name => !header.ContainsKey(NormalizeHeader(name))).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"Required columns missing: {string.Join(", ", missing)}");

        var rows = new List<SupplierInventoryRow>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 2;
        for (var rowNumber = 3; rowNumber <= lastRow; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = sheet.Row(rowNumber);
            var sku = Text(row, header, "SKU");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var vintageRaw = Text(row, header, "VTG");
            var priceRaw = Text(row, header, "希望小売");
            var inventoryRaw = Text(row, header, "在庫");

            rows.Add(new SupplierInventoryRow(
                sku,
                Text(row, header, "生産者名／ブランド名"),
                Text(row, header, "Producer / Brand"),
                Text(row, header, "商品名"),
                Text(row, header, "Wine"),
                ParseVintage(vintageRaw),
                vintageRaw,
                RequiredInt(row, header, "容量"),
                RequiredInt(row, header, "入数"),
                row.Cell(header[NormalizeHeader("希望小売")]).DataType == XLDataType.Number
                    ? row.Cell(header[NormalizeHeader("希望小売")]).GetValue<decimal>() : ParseMoney(priceRaw),
                priceRaw,
                sheet.Row(2).CellsUsed().Any(cell => NormalizeHeader(cell.GetString()) == "希望小売" && cell.GetString().Contains("税込", StringComparison.Ordinal))
                    || priceRaw.Contains("税込", StringComparison.Ordinal),
                InventoryParser.Parse(inventoryRaw),
                Text(row, header, "タイプ"),
                Text(row, header, "国"),
                Text(row, header, "産地"),
                ParseJanCodes(Text(row, header, "JAN")),
                Text(row, header, "品種（英）"),
                ParseDecimal(Text(row, header, "ｱﾙｺｰﾙ度数")),
                Text(row, header, "原産地呼称JP"),
                Text(row, header, "原産地呼称EN"),
                Text(row, header, "有機区分 カテゴリー （カタログ・TS表記）"),
                Text(row, header, "有機区分 認証団体"),
                Text(row, header, "メモ （価格関係）"),
                Text(row, header, "ｽｸﾘｭｰ"),
                Text(row, header, "旧"),
                rowNumber,
                sheet.Name)
            {
                RawCellsJson = System.Text.Json.JsonSerializer.Serialize(row.CellsUsed().ToDictionary(
                    cell => cell.Address.ToString()!, cell => cell.GetFormattedString()))
            });
        }

        return rows;
    }

    internal static int? ParseVintage(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;

    internal static decimal? ParseMoney(string value) =>
        decimal.TryParse(value.Replace(",", string.Empty).Replace("¥", "").Replace("￥", "").Replace("円", "").Replace("（税込）", "").Replace("(税込)", "").Replace("税込", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    internal static IReadOnlyList<string> ParseJanCodes(string value) => JanRegex()
        .Matches(value)
        .Select(match => match.Value)
        .Where(code => code != "0")
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int RequiredInt(IXLRow row, IReadOnlyDictionary<string, int> header, string name)
    {
        var value = Text(row, header, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidDataException($"{name} is not an integer at {row.Worksheet.Name}!{row.RowNumber()}: '{value}'");
    }

    private static string Text(IXLRow row, IReadOnlyDictionary<string, int> header, string name)
    {
        var key = NormalizeHeader(name);
        return header.TryGetValue(key, out var column) ? row.Cell(column).GetFormattedString().Trim() : string.Empty;
    }

    private static string NormalizeHeader(string value) => WhitespaceRegex().Replace(value.Replace("（税込）", "").Replace("(税込)", "").Replace("税込", ""), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<!\d)\d{8,14}(?!\d)")]
    private static partial Regex JanRegex();
}
