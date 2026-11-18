using System.Globalization;
using System.Text;
using System.Text.Json;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using Microsoft.VisualBasic.FileIO;
namespace CatalogSearch.Infrastructure;
public sealed class StandardCsvParser : IWorkbookContentParser
{
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] content, CancellationToken ct)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(content); }
        using var parser = new TextFieldParser(new StringReader(text.TrimStart('\uFEFF'))) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("CSV header missing.");
        if (headers.Distinct().Count() != headers.Length || new[] { "SKU", "ProductNameJa", "VolumeMl", "Price", "Inventory" }.Except(headers).Any()) throw new InvalidDataException("CSV header invalid.");
        var rows = new List<SupplierInventoryRow>();
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var line = checked((int)parser.LineNumber);
            var cells = parser.ReadFields()!;
            if (cells.Length != headers.Length) throw new InvalidDataException("CSV column count mismatch.");
            var raw = headers.Zip(cells).ToDictionary(x => x.First, x => x.Second);
            string V(string key) => raw.GetValueOrDefault(key, "").Trim();
            if (V("SKU").Length == 0 || V("ProductNameJa").Length == 0 || !int.TryParse(V("VolumeMl"), out var volume) || volume <= 0) throw new InvalidDataException("CSV product fields invalid.");
            decimal? price = null;
            if (V("Price") is not ("" or "オープン"))
            {
                if (!decimal.TryParse(V("Price"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0) throw new InvalidDataException("CSV price invalid.");
                price = amount;
            }
            var tax = V("Tax");
            if (tax is not ("" or "税抜" or "税込")) throw new InvalidDataException("CSV tax must be 税抜 or 税込.");
            rows.Add(new(V("SKU"), V("ProducerNameJa"), V("ProducerNameEn"), V("ProductNameJa"), V("ProductNameEn"),
                int.TryParse(V("Vintage"), out var year) ? year : null, V("Vintage"), volume, 1, price, V("Price"), tax == "税込",
                InventoryParser.Parse(V("Inventory")), V("Type"), V("Country"), V("Region"),
                V("JAN").Split('|', StringSplitOptions.RemoveEmptyEntries).Where(x => x != "0").ToArray(), V("Grapes"), null, "", "", "", "", "", "", "", line, "CSV") { RawCellsJson = JsonSerializer.Serialize(raw) });
            if (rows.Count > 10000) throw new InvalidDataException("CSV row limit exceeded.");
        }
        return rows;
    }
}
