using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogSearch.Application;

// Unregistered candidate. Supplier/producer mappings require separate reviewed configuration.
// Parses one bounded DOM tree; no global-text identity, JSON-LD repair, price or inventory fields.
public sealed class YourCellarProductPageIdentity
{
    public sealed record ProducerRule(string DisplayName, string CategoryName);
    private readonly string supplier;
    private readonly Dictionary<string, ProducerRule> producers;
    public string Version { get; }
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Token = new("<(?<end>/)?(?<tag>[a-zA-Z][a-zA-Z0-9:-]*)(?<attrs>(?:\"[^\"]*\"|'[^']*'|[^'\">])*)>", RegexOptions.CultureInvariant, Budget);
    private static readonly Regex Attribute = new("\\G\\s+(?<key>[a-zA-Z_:][a-zA-Z0-9_:.-]*)(?:\\s*=\\s*(?:\"(?<v>[^\"]*)\"|'(?<v>[^']*)'|(?<v>[^\\s\"'=<>`]+)))?", RegexOptions.CultureInvariant, Budget);
    private static readonly HashSet<string> Void = new("area base br col embed hr img input link meta param source track wbr".Split(' '));

    public YourCellarProductPageIdentity(string reviewedSupplierCode, IReadOnlyDictionary<string, ProducerRule> reviewedProducers)
    {
        if (!ImporterEvidenceValidator.ValidText(reviewedSupplierCode, 100) || reviewedProducers.Count is 0 or > 100) throw new ArgumentException("YOURCELLAR_RULES_INVALID");
        supplier=reviewedSupplierCode; producers=new(StringComparer.Ordinal);
        foreach (var (key, rule) in reviewedProducers)
            if (!ImporterEvidenceValidator.ValidText(key,200) || rule is null || !ImporterEvidenceValidator.ValidText(rule.DisplayName,200) ||
                !ImporterEvidenceValidator.ValidText(rule.CategoryName,300) || !producers.TryAdd(key,rule)) throw new ArgumentException("YOURCELLAR_RULES_INVALID");
        Version="yourcellar-product/3+"+Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { supplier, rules=producers.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray() }))).ToLowerInvariant();
    }

    public sealed record ExtractionDiagnostic(bool Accepted, string Stage, IReadOnlyDictionary<string,bool?> Predicates);
    public bool TryExtract(ImporterProductIdentity identity, string finalUrl, ReadOnlyMemory<byte> body, out IReadOnlyList<ImporterGrapeEvidence> grapes)
        => TryExtractDiagnosed(identity, finalUrl, body, out grapes, out _);

    // The public diagnostic projects only fixed predicate names and booleans from the actual extractor.
    // Null means not evaluable after a prerequisite failed; it never means passed.
    public bool TryExtractDiagnosed(ImporterProductIdentity identity, string finalUrl, ReadOnlyMemory<byte> body,
        out IReadOnlyList<ImporterGrapeEvidence> grapes, out ExtractionDiagnostic diagnostic)
    {
        grapes=Array.Empty<ImporterGrapeEvidence>();
        var checks=new Dictionary<string,bool?>(StringComparer.Ordinal);
        foreach(var key in new[]{"INPUT","DOM","DETAIL","TITLE","CATEGORY","SKU_DL_SHAPE","SKU_VALUE","DESCRIPTION_TITLE","SPEC_DL_SHAPE","GRAPE_DD","TYPE_DD","GRAPE_VALUE","TYPE_VALUE"})checks[key]=null;
        bool Check(string key,Func<bool> test)
        {
            try {return (checks[key]=test())==true;}
            catch(Exception e) when(e is ArgumentException or InvalidOperationException or DecoderFallbackException or RegexMatchTimeoutException){checks[key]=false;return false;}
        }
        ProducerRule? rule=null; Node? root=null; Node? detail=null; Node? title=null; Node? info=null; Node? wrap=null;
        string raw="",expected=""; Dictionary<string,Node>? spec=null; Dictionary<string,Node>[]? codes=null;
        Check("INPUT",()=> identity.SupplierCode==supplier && producers.TryGetValue(identity.Producer,out rule) && body.Length is >0 and <=ImporterEvidenceValidator.MaxBodyBytes &&
            Uri.TryCreate(finalUrl,UriKind.Absolute,out var url) && url.Scheme=="https" && url.IdnHost=="www.yourcellar.jp" && url.Port==443 && url.UserInfo=="" && url.Fragment=="" &&
            Regex.IsMatch(url.AbsolutePath,"^/view/item/[0-9]{12}$",RegexOptions.CultureInvariant,Budget) &&
            (url.Query=="" || Regex.IsMatch(url.Query,"^\\?category_page_id=(?:ct[0-9]{1,6}|sample[0-9]{1,3})$",RegexOptions.CultureInvariant,Budget)));
        if(checks["INPUT"]==true)Check("DOM",()=>{raw=new UTF8Encoding(false,true).GetString(body.Span);root=Parse(raw);return true;});
        if(root is not null)Check("DETAIL",()=>{detail=All(root).Single(n=>n.Tag=="div"&&n.Class("detail"));var item=One(detail,"div","item-detail");title=One(item,"div","item-title");info=One(item,"ul","item-info-list");wrap=One(One(One(detail,"div","item-description-cont"),"div","item-description"),"div","description-wrap");return true;});
        if(checks["DETAIL"]==true)
        {
            expected=Space(rule!.DisplayName+" "+identity.Product+" "+identity.Vintage+" "+identity.VolumeMl.ToString(System.Globalization.CultureInfo.InvariantCulture)+"ML");
            Check("TITLE",()=>Space(One(title!,"p","item-name").Text())==expected);
            Check("CATEGORY",()=>Space(One(title!,"p","item-category-name").Text())==Space(rule.CategoryName));
            Check("SKU_DL_SHAPE",()=>{codes=All(info!).Where(n=>n.Tag=="dl").Select(Pairs).Where(d=>d.ContainsKey("独自商品コード")).ToArray();return codes.Length==1;});
            if(checks["SKU_DL_SHAPE"]==true)Check("SKU_VALUE",()=>StrictText(codes![0]["独自商品コード"])==identity.Sku);
            Check("DESCRIPTION_TITLE",()=>{
                var descriptions=wrap!.Children.Where(n=>n.Tag=="div"&&n.Class("desc-content")).ToArray();if(descriptions.Length!=2)return false;
                var name=One(descriptions[0],"p",null).Text().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                return name.Length is >=1 and <=2 && Space(name[0])==expected && (name.Length!=2 || Regex.IsMatch(name[1],"^[A-Za-z0-9 ,.'’&()/-]{1,300}$",RegexOptions.CultureInvariant,Budget));});
            Check("SPEC_DL_SHAPE",()=>{spec=Pairs(One(wrap!,"dl","desc-spec"));return true;});
            if(spec is not null)
            {
                Check("GRAPE_DD",()=>spec.TryGetValue("品種",out var n)&&n.Children.Count==0);
                Check("TYPE_DD",()=>spec.TryGetValue("タイプ",out var n)&&n.Children.Count==0);
                if(checks["TYPE_DD"]==true)Check("TYPE_VALUE",()=>{var expectedType=identity.Variant switch{"赤"=>"赤ワイン","白"=>"白ワイン","ロゼ"=>"ロゼワイン",_=>null};return expectedType is not null && StrictText(spec["タイプ"])==expectedType;});
            }
        }
        ImporterGrapeEvidence? evidence=null;
        if(checks["GRAPE_DD"]==true)Check("GRAPE_VALUE",()=>{
            var excerpt=StrictText(spec!["品種"]);
            if(!ImporterEvidenceValidator.ValidText(excerpt,1000)||!raw.Contains(excerpt,StringComparison.Ordinal))return false;
            // A registered name in the product's grape field is an explicit presence claim.
            // Preserve it verbatim so the predominant matcher still requires a share or a main-grape statement.
            if(GrapeRegistry.Names.Any(name=>GrapeRegistry.TryGetAliases(name,out var aliases)&&aliases.Contains(excerpt,StringComparer.OrdinalIgnoreCase)))
            {
                evidence=new(excerpt,excerpt);
                return true;
            }
            var match=Regex.Match(excerpt,"^(?<name>[^0-9%,;、/<>]{1,100}?)\\s+(?<pct>(?:100|[0-9]{1,2})(?:\\.[0-9]{1,6})?)%$",RegexOptions.CultureInvariant,Budget);
            if(!match.Success || !decimal.TryParse(match.Groups["pct"].Value,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var pct)||pct>100)return false;
            evidence=new(match.Groups["name"].Value.Trim(),excerpt);return true;});
        var accepted=checks.Values.All(v=>v==true);
        diagnostic=new(accepted,accepted?"PASSED":checks.First(x=>x.Value==false).Key,new System.Collections.ObjectModel.ReadOnlyDictionary<string,bool?>(checks));
        if(accepted)grapes=Array.AsReadOnly(new[]{evidence!});
        return accepted;
    }
    public bool Verify(ImporterProductIdentity identity,IReadOnlyList<ImporterGrapeEvidence> claims,string finalUrl,ReadOnlyMemory<byte> body) => TryExtract(identity,finalUrl,body,out var actual)&&actual.SequenceEqual(claims);
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
    private static void ValidateNoScriptBoundary(Node n,bool inDetail,bool inNoScript)
    {
        // Any detail-class marker is a boundary, including unsupported tags; no fallback content
        // can manufacture a second identity or conceal a product-region boundary.
        var detail=inDetail||n.Class("detail");
        if(n.Tag=="noscript"&&(detail||inNoScript) || n.Class("detail")&&inNoScript)throw new ArgumentException();
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
