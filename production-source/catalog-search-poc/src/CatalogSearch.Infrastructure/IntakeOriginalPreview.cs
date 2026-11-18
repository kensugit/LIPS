using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace CatalogSearch.Infrastructure;

public sealed record OriginalLine(string Sheet,string Row,string Text);
public sealed record OriginalPreview(string Notice,IReadOnlyList<OriginalLine> Lines);
public static class IntakeOriginalPreview
{
    public static OriginalPreview Read(string path,byte[] bytes)
    {
        try
        {
            if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);string s;
                try{s=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){s=Encoding.GetEncoding(932).GetString(bytes);}
                return new("先頭100物理行・各2000文字。複数行セルは物理行で表示します。",s.Split('\n').Take(100).Select((x,i)=>new OriginalLine("CSV",(i+1).ToString(),x[..Math.Min(x.Length,2000)])).ToArray());
            }
            if(!Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))return new("この形式の画面プレビューは未対応です。原典をダウンロードして参照してください。",[]);
            using var zip=new ZipArchive(new MemoryStream(bytes,false));
            if(zip.Entries.Count>2000 || zip.Entries.Sum(x=>x.Length)>100_000_000)throw new InvalidDataException();
            XDocument Xml(string name){using var stream=zip.GetEntry(name)!.Open();using var reader=XmlReader.Create(stream,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=5_000_000});return XDocument.Load(reader);}
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var strings=zip.GetEntry("xl/sharedStrings.xml") is null?[]:Xml("xl/sharedStrings.xml").Descendants(ns+"si").Take(20000).Select(x=>string.Concat(x.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
            var relationships=Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Where(x=>(string?)x.Attribute("TargetMode")!="External").ToDictionary(x=>(string)x.Attribute("Id")!,x=>(string)x.Attribute("Target")!);
            var lines=new List<OriginalLine>();
            foreach(var sheet in Xml("xl/workbook.xml").Descendants(ns+"sheet").Take(5))
            {
                if(!relationships.TryGetValue((string)sheet.Attribute(rel+"id")!,out var target))continue;
                var entry=target.StartsWith('/')?target.TrimStart('/') : "xl/"+target;
                if(entry.Contains("..")||zip.GetEntry(entry) is null)continue;
                foreach(var row in Xml(entry).Descendants(ns+"row").Take(50))
                {
                    var cells=row.Elements(ns+"c").Take(25).Select(c=>{
                        var value=(string?)c.Element(ns+"v")??string.Concat(c.Descendants(ns+"t").Select(t=>t.Value));
                        if((string?)c.Attribute("t")=="s"&&int.TryParse(value,out var i)&&i>=0&&i<strings.Length)value=strings[i];
                        if(c.Element(ns+"f") is { } f)value="[数式原文: "+f.Value+"] [保存済値: "+value+"]";
                        return (string?)c.Attribute("r")+": "+value[..Math.Min(value.Length,2000)];});
                    lines.Add(new((string?)sheet.Attribute("name")??"",(string?)row.Attribute("r")??"未特定",string.Join(" | ",cells)));
                }
            }
            return new("先頭5シート・各50行・25列の保存済みセル値。数式・外部リンクを評価/取得しません。省略部分は原典で確認してください。",lines);
        }
        catch(Exception ex) when(ex is InvalidDataException or XmlException or ArgumentException or NullReferenceException)
        {return new("安全にプレビューできません。原典ファイルをダウンロードして参照してください。",[]);}
    }
}
