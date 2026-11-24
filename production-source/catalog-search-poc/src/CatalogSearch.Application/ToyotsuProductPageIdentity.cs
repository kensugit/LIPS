using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

// Reviewed Toyotsu product DOM only; price/stock/region and producer prose are not extraction fields.
public sealed class ToyotsuProductPageIdentity
{
    private readonly string supplier;
    private readonly Dictionary<string,string> producers;
    public string Version { get; }
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Token = new("<(?<end>/)?(?<tag>[a-zA-Z][a-zA-Z0-9:-]*)(?<attrs>(?:\"[^\"]*\"|'[^']*'|[^'\">])*)>", RegexOptions.CultureInvariant, Budget);
    private static readonly Regex Attribute = new("\\G\\s+(?<key>[a-zA-Z_:][a-zA-Z0-9_:.-]*)(?:\\s*=\\s*(?:\"(?<v>[^\"]*)\"|'(?<v>[^']*)'|(?<v>[^\\s\"'=<>`]+)))?", RegexOptions.CultureInvariant, Budget);
    private static readonly HashSet<string> Void = new("area base br col embed hr img input link meta param source track wbr".Split(' '));
    public ToyotsuProductPageIdentity(string reviewedSupplierCode,IReadOnlyDictionary<string,string> reviewedProducers)
    {
        if(!ImporterEvidenceValidator.ValidText(reviewedSupplierCode,100)||reviewedProducers is null||reviewedProducers.Count is 0 or >100)throw new ArgumentException("TOYOTSU_RULES_INVALID");
        supplier=reviewedSupplierCode;producers=new(StringComparer.Ordinal);
        foreach(var pair in reviewedProducers)
            if(!ImporterEvidenceValidator.ValidText(pair.Key,200)||!ImporterEvidenceValidator.ValidText(pair.Value,200)||!producers.TryAdd(pair.Key,pair.Value))throw new ArgumentException("TOYOTSU_RULES_INVALID");
        Version="toyotsu-product/1+"+Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{supplier,rules=producers.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray()})));
    }
    public bool TryExtract(ImporterProductIdentity identity,string finalUrl,ReadOnlyMemory<byte> body,out IReadOnlyList<ImporterGrapeEvidence> grapes)
    {
        grapes=[];
        try
        {
            if(identity.SupplierCode!=supplier||!producers.TryGetValue(identity.Producer,out var producer)||body.Length is 0 or >ImporterEvidenceValidator.MaxBodyBytes||
               !Uri.TryCreate(finalUrl,UriKind.Absolute,out var url)||url.Scheme!="https"||url.IdnHost!="toyotsu-wine.com"||url.Port!=443||url.UserInfo!=""||url.Query!=""||url.Fragment!=""||
               url.AbsolutePath!="/shop/g/g"+identity.Sku+"/"||!Regex.IsMatch(identity.Sku,"^[A-Z0-9]{1,40}$",RegexOptions.CultureInvariant,Budget))return false;
            var raw=new UTF8Encoding(false,true).GetString(body.Span);var root=Parse(raw);
            var detail=All(root).Single(n=>n.Tag=="div"&&n.Class("block-detail"));
            var title=All(detail).Single(n=>n.Tag=="h1"&&n.Class("goods_name_"));
            if(Canonical(StrictText(title))!=Canonical(identity.Product))return false;
            var spec=All(detail).Single(n=>n.Tag=="div"&&n.Class("goodsspec2_")&&n.Class("formdetail_"));
            var fields=new Dictionary<string,Node>(StringComparer.Ordinal);
            foreach(var row in spec.Children)
            {
                if(row.Tag!="div"||!row.Class("flex")||row.Children.Count!=2||row.Content.OfType<string>().Any(s=>!string.IsNullOrWhiteSpace(s)))return false;
                var label=One(row,"div","ttl");var value=One(row,"div","cont");
                if(label.Children.Count!=0||value.Children.Count!=0||!fields.TryAdd(Space(label.Text()),value))return false;
            }
            string Field(string name)=>StrictText(fields[name]);
            if(Canonical(Field("生産者名"))!=Canonical(producer)||Canonical(Field("生産者名"))!=Canonical(identity.Producer)||
               Field("商品コード")!=identity.Sku||Canonical(Field("年号"))!=Canonical(identity.Vintage+"年")||
               Canonical(Field("容量"))!=identity.VolumeMl.ToString(System.Globalization.CultureInfo.InvariantCulture)+"ml"||Field("色")!=identity.Variant)return false;
            var statement=Field("品種");var percent=Regex.Match(statement,@"^(?<name>[^0-9%,;、/<>]{1,100}?)(?<space>\s*)100[%％]$",RegexOptions.CultureInvariant,Budget);
            string name,excerpt;
            if(percent.Success){name=percent.Groups["name"].Value.Trim();excerpt=statement;}
            else
            {
                name=statement;if(GrapeRegistry.Resolve(name)is null)return false;
                var productAbout=All(detail).Single(n=>n.Tag=="div"&&n.Class("goodscomment3_"));
                if(Space(All(productAbout).Single(n=>n.Tag=="h3"&&n.Class("ttl")).Text())!="商品について")return false;
                var description=All(productAbout).Single(n=>n.Tag=="div"&&n.Class("cont"));if(description.Children.Count!=0)return false;
                var first=description.Text().Split('。')[0];
                if(first.Length>300||Regex.IsMatch(first,"ない|なく|ません|ではなく|以外|除く|約|程度|以下|以上|もし|仮|場合|例|他|別|料理|合わせ|[~〜～]",RegexOptions.CultureInvariant,Budget))return false;
                var matches=Regex.Matches(first,"(?<claim>"+Regex.Escape(name)+@"\s*100[%％])による",RegexOptions.CultureInvariant,Budget);
                if(matches.Count!=1||first.Count(c=>c is '%' or '％')!=1)return false;
                excerpt=matches[0].Groups["claim"].Value;
            }
            if(GrapeRegistry.Resolve(name)is null||!raw.Contains(excerpt,StringComparison.Ordinal))return false;
            grapes=Array.AsReadOnly(new[]{new ImporterGrapeEvidence(name,excerpt)});return true;
        }
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or KeyNotFoundException or DecoderFallbackException or RegexMatchTimeoutException){return false;}
    }
    public bool Verify(ImporterProductIdentity identity,IReadOnlyList<ImporterGrapeEvidence> grapes,string finalUrl,ReadOnlyMemory<byte> body)=>TryExtract(identity,finalUrl,body,out var actual)&&actual.SequenceEqual(grapes);
    private static string Canonical(string value)=>Space(value.Normalize(NormalizationForm.FormKC));
    private static string Space(string s)=>Regex.Replace(s,"\\s+"," ",RegexOptions.CultureInvariant,Budget).Trim();
    private sealed class Node(string tag, Dictionary<string,string> attrs)
    {
        public string Tag=tag; public Dictionary<string,string> Attrs=attrs;public List<Node> Children=[];public List<object> Content=[];
        public bool Class(string s)=>Attrs.TryGetValue("class",out var c)&&c.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(s);
        public string Text()=>string.Concat(Content.Select(x=>x is Node n?n.Tag=="br"?"\n":n.Text():(string)x));
    }
    private static IEnumerable<Node> All(Node n){yield return n;foreach(var c in n.Children)foreach(var d in All(c))yield return d;}
    private static Node One(Node n,string tag,string? cls)=>n.Children.Single(x=>x.Tag==tag&&(cls is null||x.Class(cls)));
    private static string StrictText(Node n)
    {if(n.Children.Count!=0)throw new ArgumentException();return Space(n.Text());}
    private static Dictionary<string,Node> Pairs(Node n)
    {
        if(n.Children.Count is 0 or >64 || n.Children.Count%2!=0 || n.Content.OfType<string>().Any(x=>!string.IsNullOrWhiteSpace(x)))throw new ArgumentException();
        var d=new Dictionary<string,Node>(StringComparer.Ordinal);
        for(var i=0;i<n.Children.Count;i+=2){var a=n.Children[i];var b=n.Children[i+1];if(a.Tag!="dt"||b.Tag!="dd"||a.Children.Count!=0||!d.TryAdd(Space(a.Text()),b))throw new ArgumentException();}
        return d;
    }
    private static Node Parse(string raw)
    {
        var s=Regex.Replace(raw,"<!--.*?-->|<script\\b[^>]*>.*?</script\\s*>|<style\\b[^>]*>.*?</style\\s*>","",RegexOptions.IgnoreCase|RegexOptions.Singleline,Budget);
        // Templates remain unsupported. Noscript is parsed with the same strict balanced tree,
        // then removed only after proving it neither contains nor is contained by a detail region.
        if(Regex.IsMatch(s,"</?template\\b",RegexOptions.IgnoreCase,Budget))throw new ArgumentException();
        s=Regex.Replace(s,"<!DOCTYPE[^>]*>","",RegexOptions.IgnoreCase,Budget);
        s=ProductRegion(s);
        var root=new Node("root",new());var stack=new Stack<Node>();stack.Push(root);var at=0;var count=0;
        foreach(Match m in Token.Matches(s))
        {
            if(++count>20000||stack.Count>64)throw new ArgumentException();
            var plain=s[at..m.Index];if(plain.Contains('<'))throw new ArgumentException();stack.Peek().Content.Add(WebUtility.HtmlDecode(plain));at=m.Index+m.Length;
            var tag=m.Groups["tag"].Value.ToLowerInvariant();
            if(m.Groups["end"].Success){if(stack.Count==1||stack.Peek().Tag!=tag)throw new ArgumentException();stack.Pop();continue;}
            var attrs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var a=m.Groups["attrs"].Value;var pos=0;
            while(pos<a.Length&&!string.IsNullOrWhiteSpace(a[pos..])&&a[pos..].Trim()!="/"){var am=Attribute.Match(a,pos);if(!am.Success||am.Index!=pos||!attrs.TryAdd(am.Groups["key"].Value,WebUtility.HtmlDecode(am.Groups["v"].Value)))throw new ArgumentException();pos+=am.Length;}
            var n=new Node(tag,attrs);stack.Peek().Children.Add(n);stack.Peek().Content.Add(n);if(!Void.Contains(tag))stack.Push(n);
        }
        if(stack.Count!=1||s[at..].Contains('<'))throw new ArgumentException();root.Content.Add(WebUtility.HtmlDecode(s[at..]));
        ValidateNoScriptBoundary(root,false,false);
        RemoveOutsideNoScript(root);
        return root;
    }
    private static string ProductRegion(string page)
    {
        // The observed site has an unmatched closing div after its product boundary.
        // Do not repair the product tree or scan unrelated descriptions for facts.
        int body=0,bodies=0,noscript=0,count=0,start=-1,end=-1,regions=0;
        var stack=new Stack<string>();
        foreach(Match match in Token.Matches(page))
        {
            if(++count>20000)throw new ArgumentException();
            var tag=match.Groups["tag"].Value.ToLowerInvariant();var closing=match.Groups["end"].Success;
            if(!closing&&tag=="body"){body++;bodies++;}
            if(closing&&tag=="body")body--;
            if(!closing&&tag=="noscript")noscript++;
            var isRegion=false;
            if(!closing)
            {
                var attributes=match.Groups["attrs"].Value;var at=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while(at<attributes.Length&&!string.IsNullOrWhiteSpace(attributes[at..])&&attributes[at..].Trim()!="/")
                {
                    var attr=Attribute.Match(attributes,at);if(!attr.Success||attr.Index!=at||!seen.Add(attr.Groups["key"].Value))throw new ArgumentException();at+=attr.Length;
                    if(attr.Groups["key"].Value.Equals("class",StringComparison.OrdinalIgnoreCase)&&WebUtility.HtmlDecode(attr.Groups["v"].Value).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains("block-detail"))isRegion=true;
                }
            }
            if(isRegion)
            {
                if(++regions!=1||tag!="div"||body!=1||noscript!=0)throw new ArgumentException();start=match.Index;
            }
            if(start>=0&&end<0)
            {
                if(closing){if(stack.Count==0||stack.Pop()!=tag)throw new ArgumentException();if(stack.Count==0)end=match.Index+match.Length;}
                else if(!Void.Contains(tag)){stack.Push(tag);if(stack.Count>64)throw new ArgumentException();}
            }
            if(closing&&tag=="noscript")noscript--;
            if(body<0||noscript<0)throw new ArgumentException();
        }
        if(bodies!=1||body!=0||noscript!=0||regions!=1||start<0||end<0||stack.Count!=0)throw new ArgumentException();
        return page[start..end];
    }
    private static void ValidateNoScriptBoundary(Node n,bool inDetail,bool inNoScript)
    {
        // Any detail-class marker is a boundary, including unsupported tags; no fallback content
        // can manufacture a second identity or conceal a product-region boundary.
        var detail=inDetail||n.Class("block-detail");
        if(n.Tag=="noscript"&&(detail||inNoScript) || n.Class("block-detail")&&inNoScript)throw new ArgumentException();
        foreach(var child in n.Children)ValidateNoScriptBoundary(child,detail,inNoScript||n.Tag=="noscript");
    }
    private static void RemoveOutsideNoScript(Node n)
    {
        foreach(var child in n.Children.ToArray())
        {
            if(child.Tag=="noscript"){n.Children.Remove(child);n.Content.Remove(child);}
            else RemoveOutsideNoScript(child);
        }
    }
}
