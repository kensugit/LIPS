using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;
internal static class SupplierExcel
{
    public static string Text(IXLRow row,int column)=>row.Cell(column).GetFormattedString().Trim();
    public static string Normalize(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC),@"\s+","");
    public static int Positive(string text){if(!int.TryParse(Normalize(text),out var n)||n<=0)throw new InvalidDataException("Positive integer required.");return n;}
    public static int? Year(string text)=>Regex.IsMatch(text,@"^[12]\d{3}$")?int.Parse(text,CultureInfo.InvariantCulture):null;
    public static decimal? Price(string text){if(text.Trim().ToUpperInvariant() is "" or "-" or "OPEN" or "オープン")return null;var n=WineToStyleInventoryParser.ParseMoney(text);if(n is null or <0)throw new InvalidDataException("Invalid retail price.");return n;}
    public static string Raw(IXLRow row)=>JsonSerializer.Serialize(row.CellsUsed().ToDictionary(c=>c.Address.ToString()!,c=>c.GetFormattedString()));
    public static void Header(IXLWorksheet sheet,int row,params (int Col,string Name)[] headers){foreach(var h in headers)if(Normalize(sheet.Cell(row,h.Col).GetString()).Replace("(税込)","")!=Normalize(h.Name))throw new InvalidDataException("Workbook headers changed.");}
    public static IReadOnlyList<SupplierInventoryRow> Validate(List<SupplierInventoryRow> rows){if(rows.Count==0||rows.Any(x=>string.IsNullOrWhiteSpace(x.SupplierProductCode)||string.IsNullOrWhiteSpace(x.ProductNameJa))||rows.Select(x=>x.SupplierProductCode).Distinct().Count()!=rows.Count)throw new InvalidDataException("Invalid or duplicate supplier identities.");return rows;}
}
