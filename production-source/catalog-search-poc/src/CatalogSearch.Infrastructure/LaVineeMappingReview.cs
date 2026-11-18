using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

public static class LaVineeMappingReview
{
    private static readonly string[] Headers=["NO.","コ-ド","地域","ワイン名","生産者","ヴィンテージ","色","容量","在庫","納価（税別）","ご注文本数"];
    private static readonly IReadOnlyDictionary<string,string> Regions=new Dictionary<string,string>(StringComparer.Ordinal)
    { ["AL"]="アルザス",["BG"]="ブルゴーニュ",["BX"]="ボルドー",["CP"]="シャンパーニュ",["JS"]="ジュラサヴォワ",["LO"]="ロワール",["LR"]="ラングドックルーション",["PC"]="プロヴァンス",["RH"]="ローヌ",["SO"]="スッドウェスト" };
    // Received 2026-10-13: only the 18 compound spellings present in the confirmed source.
    // Do not infer new categories from an arbitrary recognized prefix.
    private static readonly IReadOnlyDictionary<string,string> CompoundRegions=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["AL発泡性"]="AL",["BG酒精強化"]="BG",["BG蒸留酒"]="BG",["BG発泡性"]="BG",
        ["CP非発泡性"]="CP",["CP酒精強化"]="CP",["CP酒精強化(甘味)"]="CP",["CP蒸留酒"]="CP",
        ["JS酒精強化(甘味果実酒)"]="JS",["JS酒精強化"]="JS",["JS発泡性"]="JS",["JS甘味果実酒"]="JS",
        ["LO発泡性"]="LO",["LO酒精強化"]="LO",["LR酒精強化"]="LR",["LR甘味果実酒"]="LR",
        ["RH酒精強化"]="RH",["SO発泡性"]="SO"
    };
    private static string? ReceivedRegion(string code) => Regions.TryGetValue(code,out var region)?region:
        CompoundRegions.TryGetValue(code,out var prefix)?Regions[prefix]:
        code switch { "CALVADOS"=>"カルヴァドス", "COGNAC"=>"コニャック", _=>null };
    public static string ResolveSourceProductCategory(IntakeMappingQuestion question,IntakeMappingResolution answer)
    {
        ResolveRegion(question,answer);
        // Keep the suffix as written, including its original parentheses and spacing.
        return CompoundRegions.ContainsKey(Clean(question.RawValue))?question.RawValue[2..]:"";
    }
    internal static string Clean(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC),@"\s+","");
    internal static string Text(IXLCell cell)=>cell.HasFormula?"="+cell.FormulaA1:cell.GetFormattedString().Trim();
    public static bool Matches(IXLWorksheet sheet)=>Regex.IsMatch(sheet.Name,@"^\d{6}LaVineeリスト$") &&
        Enumerable.Range(1,11).All(c=>!sheet.Cell(1,c).HasFormula&&sheet.Cell(1,c).GetString()==Headers[c-1]) &&
        !sheet.Row(1).CellsUsed().Any(c=>c.Address.ColumnNumber>11&&!string.IsNullOrWhiteSpace(c.GetString()));
    internal static bool IsNonProduct(IXLRow row)
    {
        var used=Enumerable.Range(1,11).Where(c=>!string.IsNullOrWhiteSpace(Text(row.Cell(c)))).ToArray();
        return used.Length==0 || (used.Length==1&&used[0]==4&&!row.Cell(4).HasFormula);
    }
    internal static string Raw(IXLRow row)=>JsonSerializer.Serialize(Enumerable.Range(1,11).ToDictionary(c=>row.Cell(c).Address.ToString()!,c=>row.Cell(c).HasFormula?"="+row.Cell(c).FormulaA1:row.Cell(c).GetFormattedString()));
    public static IReadOnlyList<IntakeMappingQuestion> Inspect(byte[] bytes)
    {
        using var workbook=new XLWorkbook(new MemoryStream(bytes,false));
        var sheets=workbook.Worksheets.Where(Matches).ToArray();if(sheets.Length!=1)return [];
        var sheet=sheets[0];
        var headers=Enumerable.Range(1,11).Select(c=>new{Column=c,Value=sheet.Cell(1,c).GetString()});
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(headers))));
        var scope=new IntakeRuleScope("LA_VINEE","LaVinee/1",fingerprint);var result=new List<IntakeMappingQuestion>();
        for(var n=2;n<=(sheet.LastRowUsed()?.RowNumber()??1);n++)
        {
            var row=sheet.Row(n);if(IsNonProduct(row))continue;
            var sku=Text(row.Cell(2));
            if(Enumerable.Range(1,11).Any(c=>row.Cell(c).HasFormula)||!Regex.IsMatch(sku,@"^\d+$")||string.IsNullOrWhiteSpace(Text(row.Cell(4))))continue;
            void Add(string kind,string key,int column,string prompt,string[] choices)
            {
                if(row.Cell(column).HasFormula)return;
                var raw=row.Cell(column).GetFormattedString().Trim();
                var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Sheet=sheet.Name,Row=n,SourceKey=key}))));
                result.Add(new(id,scope,kind,key,prompt,sheet.Name,n,sku,raw,choices));
            }
            Add("VolumeUnit","H/容量",8,"受領済み回答：容量列全体の単位はL（リットル）。原典行と保存済み回答を照合してください。",["L"]);
            Add("ColumnMeaning","J/納価単位",10,"納価（税別）は各行の商品1本あたりの金額ですか。確認できる場合のみPerBottleを選択してください。未確認・ケース単位の場合は選択せず保留してください。",["PerBottle"]);
            var code=Clean(Text(row.Cell(3)));
            Add("RegionAbbreviation","C/"+code,3,"受領済み地域対応をこの原略号の行に限定して確認します。未知の略号は保留します。",ReceivedRegion(code) is { } region?[region]:[]);
        }
        return result;
    }
    private static void Check(IntakeMappingQuestion expected,IntakeMappingResolution answer)
    {
        if(JsonSerializer.Serialize(expected)!=JsonSerializer.Serialize(answer.Question)||!expected.Choices.Contains(answer.Meaning)||string.IsNullOrWhiteSpace(answer.Evidence))
            throw new InvalidDataException("LaVinee answer does not match the source question.");
    }
    public static int ResolveVolume(IntakeMappingQuestion question,IntakeMappingResolution answer)
    {
        Check(question,answer);
        if(question.Kind!="VolumeUnit"||question.SourceKey!="H/容量"||answer.Meaning!="L"||!LucVolumeReview.BarePositive(question.RawValue,out var value))throw new InvalidDataException("LaVinee capacity invalid.");
        decimal ml;try{ml=checked(value*1000);}catch(OverflowException){throw new InvalidDataException("LaVinee capacity overflow.");}
        if(ml>int.MaxValue||decimal.Truncate(ml)!=ml)throw new InvalidDataException("LaVinee capacity must be whole mL.");
        return (int)ml;
    }
    public static string ResolveRegion(IntakeMappingQuestion question,IntakeMappingResolution answer)
    {
        Check(question,answer);var code=Clean(question.RawValue);
        if(question.Kind!="RegionAbbreviation"||question.SourceKey!="C/"+code||ReceivedRegion(code) is not { } region||region!=answer.Meaning)
            throw new InvalidDataException("LaVinee region is not in the received mapping.");
        return region;
    }
    public static string ResolvePriceUnit(IntakeMappingQuestion question,IntakeMappingResolution answer)
    {
        Check(question,answer);
        if(question.Kind!="ColumnMeaning"||question.SourceKey!="J/納価単位"||answer.Meaning!="PerBottle")
            throw new InvalidDataException("LaVinee price unit needs an explicit per-bottle answer.");
        return "PerBottle";
    }
}
