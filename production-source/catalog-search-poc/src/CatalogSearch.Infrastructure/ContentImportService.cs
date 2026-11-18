using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
namespace CatalogSearch.Infrastructure;
public sealed class ContentImportService(CatalogSearchDbContext db)
{
    public async Task<ImportResult> ImportAsync(string fileName, byte[] bytes, DateTimeOffset observedAt, string supplierCode, string supplierName, CancellationToken ct)
    {
        if (bytes.Length is 0 or > 20_000_000) throw new InvalidDataException("ファイルは20MB以下にしてください。");
        var excel = Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
        if (!excel && !Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("対応形式はxlsxとcsvです。");
        if (excel) { supplierCode = "WINE_TO_STYLE"; supplierName = "Wine to Style"; }
        if (!Regex.IsMatch(supplierCode, "^[A-Z0-9_]{1,50}$") || string.IsNullOrWhiteSpace(supplierName) || supplierName.Length > 100) throw new InvalidDataException("仕入先コードと名称を確認してください。");
        IWorkbookContentParser parser = excel ? new WineToStyleInventoryParser() : new StandardCsvParser();
        var rows = parser.Parse(bytes, ct);
        if (rows.Count == 0 || rows.Select(x => x.SupplierProductCode).Distinct().Count() != rows.Count) throw new InvalidDataException("商品が0件、またはSKUが重複しています。");
        return await new PostgresInventoryStore(db).SaveAsync(new(Path.GetFileName(fileName), bytes, observedAt.ToUniversalTime(), supplierCode, supplierName, excel ? "WineToStyle/2" : "StandardCsv/1"), Convert.ToHexString(SHA256.HashData(bytes)), rows, ct);
    }
}
