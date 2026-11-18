using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using CatalogSearch.Domain;
using ClosedXML.Excel;
namespace CatalogSearch.Infrastructure;

// Port of the existing source-specific le_caveau_import.py contract, without recalculation.
public sealed class LeCaveauInventoryParser : IWorkbookContentParser
{
    private static readonly Dictionary<string,(string Country,string Region,string Kind)> Sections = new()
    {
        ["Bourgogne rouges"]=("フランス","ブルゴーニュ","赤"), ["Bourgogne blancs"]=("フランス","ブルゴーニュ","白"),
        ["Bordeaux rouges"]=("フランス","ボルドー","赤"), ["Bordeaux blancs & Sauternes"]=("フランス","ボルドー","白"),
        ["Rhone rouges & blancs"]=("フランス","ローヌ",""), ["Jura & Alsace & Loire"]=("フランス","",""),
        ["Champagne"]=("フランス","シャンパーニュ","泡"), ["California & U.S. Red"]=("アメリカ","","赤"),
        ["California & U.S. White"]=("アメリカ","","白"), ["Italia"]=("イタリア","","")
    };
    public IReadOnlyList<SupplierInventoryRow> Parse(byte[] bytes,CancellationToken ct)
    {
        using var w=new XLWorkbook(new MemoryStream(bytes,false));
        if(w.Worksheets.Count!=1 || !w.TryGetWorksheet("Sheet1",out var s))throw new InvalidDataException("Le Caveau sheet changed.");
        if(s.Cell("A1").GetString()!="Le Caveau" || s.Cell("G10").GetString()!="卸価格" || !s.Cell("A4").GetString().StartsWith("価格は消費税別です。",StringComparison.Ordinal))throw new InvalidDataException("Le Caveau price contract changed.");
        // Read the serialized date cache, never cell.Value (which can evaluate TODAY).
        var date=s.Cell("H9");
        if(date.FormulaA1!="TODAY()" || date.CachedValue.Type!=XLDataType.DateTime || date.CachedValue.GetDateTime()!=new DateTime(2026,9,29))throw new FolderImportHeldException("ルカヴォーの保存済み資料日付が対応版と異なります。再計算せず確認してください。");
        if(s.CellsUsed().Any(c=>c.HasFormula && c.Address.ToString()!="H9"))throw new InvalidDataException("Unreviewed formula.");
        var rows=new List<SupplierInventoryRow>();string? section=null;
        foreach(var row in s.RowsUsed().Where(r=>r.RowNumber()>=9))
        {
            ct.ThrowIfCancellationRequested();
            string V(int c)=>row.Cell(c).GetString().Trim();
            if(Sections.ContainsKey(V(1))){section=V(1);continue;}
            if(V(2)=="Bt" || row.IsEmpty())continue;
            int Positive(int c){if(!int.TryParse(V(c),NumberStyles.None,CultureInfo.InvariantCulture,out var n)||n<=0)throw new InvalidDataException("Invalid integer.");return n;}
            if(section is null || V(3)!="" || V(5)=="" || !Regex.IsMatch(V(4),@"^(?:[12][0-9]{3}|NV)$") || V(1) is not ("入荷済" or "10/6頃" or "10/8頃"))throw new InvalidDataException("Unrecognized Le Caveau row.");
            var quantity=Positive(2);var volume=Positive(6);var vintage=V(4);var name=V(5);
            if(!decimal.TryParse(V(7),NumberStyles.Number,CultureInfo.InvariantCulture,out var price)||price<=0)throw new InvalidDataException("Invalid wholesale price.");
            string PythonString(string value) => "\""+string.Concat(value.Select(c=>c switch {
                '"'=>"\\\"", '\\'=>"\\\\", '\b'=>"\\b", '\f'=>"\\f", '\n'=>"\\n", '\r'=>"\\r", '\t'=>"\\t",
                < ' '=>"\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture), _=>c.ToString()
            }))+"\"";
            // Match Python json.dumps(..., ensure_ascii=False) identity used by the existing importer.
            var identity="["+PythonString(name)+", "+PythonString(vintage)+", "+volume.ToString(CultureInfo.InvariantCulture)+"]";
            var sku="LC-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
            var arrived=V(1)=="入荷済";var meta=Sections[section];
            var note=$"卸価格（税別）: {price.ToString(CultureInfo.InvariantCulture)}円。{V(1)}。原本本数: {quantity}本。";
            var raw=new Dictionary<string,object?>();foreach(var c in row.CellsUsed())raw[c.Address.ColumnLetter+c.Address.RowNumber]=c.GetString();
            raw["WholesalePrice"]=price;raw["WholesaleTaxIncluded"]=false;raw["原本区分"]=section;raw["商品識別"]="原本に商品コードなし。商品名・VT・容量から生成。";raw["日付"]="H9のTODAY式は再計算せず保存済み値2026-09-29を採用";
            rows.Add(new(sku,"","",name,name,vintage=="NV"?null:int.Parse(vintage,CultureInfo.InvariantCulture),vintage,volume,0,null,"",false,
                new InventoryValue(arrived?quantity:null,arrived?InventoryStatus.InStock:InventoryStatus.Expected,$"{V(1)} / {quantity}本"),meta.Kind,meta.Country,meta.Region,[],"",null,"","","","",note,"","",row.RowNumber(),s.Name)
                {RawCellsJson=JsonSerializer.Serialize(raw),Description=$"{section}。{note} Parker欄: {V(8)}"});
        }
        return SupplierExcel.Validate(rows);
    }
}

