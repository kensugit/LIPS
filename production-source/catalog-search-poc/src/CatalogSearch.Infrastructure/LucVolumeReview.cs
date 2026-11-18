using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatalogSearch.Application;
using ClosedXML.Excel;

namespace CatalogSearch.Infrastructure;

public static class LucVolumeReview
{
    private static readonly string[] Headers=["商品コード","JANコード","生産者コード","生産者(アルファベット)","生産者(カタカナ)","ヴィンテージ","商品名(アルファベット)","商品名(カタカナ)","容量","色","地域コード","原産国","生産地方","生産地域","格付コード","格付名","参考小売価格","商品コメント","在庫"];
    private static string Clean(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC),@"\s+","");
    public static bool BarePositive(string raw,out decimal value)
    {
        var clean=Clean(raw);
        value=0;
        return Regex.IsMatch(clean,@"^[0-9]+(?:\.[0-9]+)?$") &&
            decimal.TryParse(clean,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value) && value>0;
    }
    public static IReadOnlyList<IntakeMappingQuestion> Inspect(byte[] content)
    {
        using var w=new XLWorkbook(new MemoryStream(content,false));
        if(!w.TryGetWorksheet("在庫リスト",out var sheet))return [];
        // Only the known 19-column inventory layout. Reservation and changed layouts are not guessed.
        for(var c=1;c<=Headers.Length;c++)
            if(sheet.Cell(5,c).HasFormula || Clean(sheet.Cell(5,c).GetString()).Replace("(税込)","")!=Headers[c-1])return [];
        var headerCells=sheet.Row(5).CellsUsed().Select(c=>new{Column=c.Address.ColumnNumber,Value=c.GetString()}).OrderBy(c=>c.Column).ToArray();
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(headerCells))));
        var scope=new IntakeRuleScope("LUC","LUC/1",fingerprint);
        var result=new List<IntakeMappingQuestion>();
        for(var n=6;n<=(sheet.LastRowUsed()?.RowNumber()??5);n++)
        {
            var cell=sheet.Cell(n,9);var raw=cell.GetFormattedString().Trim();
            var sku=sheet.Cell(n,1).GetFormattedString().Trim();
            if(sku.Length==0||sheet.Cell(n,8).GetFormattedString().Trim().Length==0||cell.HasFormula||!BarePositive(raw,out _))continue;
            var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Sheet=sheet.Name,Row=n,SourceKey="容量"}))));
            result.Add(new(id,scope,"VolumeUnit","容量",
                "容量に単位がありません。この取引先・帳票形式の容量列全体で使う単位を原典に基づき確認してください。今回の行にも確認結果を記録します。分からなければ保留してください。",
                sheet.Name,n,sku,raw,["mL","cL","L"]));
        }
        return result;
    }
    public static int Resolve(IntakeMappingQuestion question, IntakeMappingResolution resolution)
    {
        var q=resolution.Question;
        if(q is null || q.Id!=question.Id || q.Scope!=question.Scope || q.Kind!=question.Kind || q.SourceKey!=question.SourceKey ||
            q.Sheet!=question.Sheet || q.Row!=question.Row || q.Sku!=question.Sku || q.RawValue!=question.RawValue ||
            q.Prompt!=question.Prompt || q.Choices is null || !q.Choices.SequenceEqual(question.Choices) ||
            string.IsNullOrWhiteSpace(resolution.Evidence) || resolution.Evidence.Length>2000 ||
            !BarePositive(question.RawValue,out var value))throw new InvalidDataException("LUC volume answer does not match current source question.");
        var factor=resolution.Meaning switch {"mL"=>1m,"cL"=>10m,"L"=>1000m,_=>throw new InvalidDataException("LUC volume unit answer invalid.")};
        decimal ml;try{ml=checked(value*factor);}catch(OverflowException){throw new InvalidDataException("LUC volume exceeds integer range.");}
        if(ml<=0 || ml>int.MaxValue || decimal.Truncate(ml)!=ml)throw new InvalidDataException("LUC volume must resolve to positive whole mL.");
        return (int)ml;
    }
}
